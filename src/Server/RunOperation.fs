module RunOperation

open System
open System.IO
open FsToolkit.ErrorHandling
open Microsoft.Data.Sqlite
open Shared
open TsebtConfig
open Bootstrap

/// Returns nullable database values as option strings.
let private asOptionalString (value: obj) : string option =
    if isNull value || Convert.IsDBNull value then None else Some(string value)

/// Resolves the sqlite connection string from configured app root.
let private toConnectionString (settings: TsebtSettings) : string =
    let databasePath = combineAppRoot settings.AppRoot settings.Paths.Database
    let builder = SqliteConnectionStringBuilder()
    builder.DataSource <- databasePath
    builder.ConnectionString

/// Resolves the absolute run batch folder path for a seed base.
let private runBatchFolderPath (settings: TsebtSettings) (seedBase: string) : string =
    Path.Combine(settings.AppRoot, settings.Paths.Runs, seedBase)

/// Reads run batch summary rows from generated batch and run metadata.
let private readRunBatchSummaries (connection: SqliteConnection) : Result<RunBatchSummary list, string> =
    try
        use command = connection.CreateCommand()

        command.CommandText <-
            """SELECT
  b.seed_base,
  b.created_at,
  COALESCE(b.run_status, 'Generated') AS run_status,
  b.slurm_job_id,
  COUNT(r.id) AS generated_input_count,
  COUNT(DISTINCT r.node_digit) AS node_count,
  COUNT(DISTINCT r.phase_space_index) AS phase_space_count
FROM generated_batches b
LEFT JOIN generated_runs r ON r.batch_id = b.id
GROUP BY b.id, b.seed_base, b.created_at, b.run_status, b.slurm_job_id
ORDER BY b.created_at DESC;"""

        use reader = command.ExecuteReader()
        let results = ResizeArray<RunBatchSummary>()

        while reader.Read() do
            results.Add(
                {
                    SeedBase = reader.GetString(0)
                    CreatedAt = reader.GetString(1)
                    RunStatus = reader.GetString(2)
                    SlurmJobId = asOptionalString (reader.GetValue(3))
                    GeneratedInputCount = reader.GetInt32(4)
                    NodeCount = reader.GetInt32(5)
                    PhaseSpaceCount = reader.GetInt32(6)
                }
            )

        Ok(List.ofSeq results)
    with ex ->
        Error $"Failed reading run batch summaries: {ex.Message}"

/// Returns run batch summaries for all generated seed bases.
let getRunBatches (settings: TsebtSettings) : Result<RunBatchSummary list, string> =
    try
        use connection = new SqliteConnection(toConnectionString settings)
        connection.Open()
        readRunBatchSummaries connection
    with ex ->
        Error $"Failed loading run batches: {ex.Message}"

/// Reads one run batch details row by seed base.
let private readRunBatchDetailsHeader
    (connection: SqliteConnection)
    (seedBase: string)
    : Result<RunBatchDetails, string> =
    try
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT id, seed_base, created_at, COALESCE(run_status, 'Generated'), slurm_job_id, manifest_path, script_path, submitted_at FROM generated_batches WHERE seed_base = $seedBase LIMIT 1;"
        command.Parameters.AddWithValue("$seedBase", seedBase) |> ignore
        use reader = command.ExecuteReader()

        if not (reader.Read()) then
            Error $"Run batch not found for seed base: {seedBase}"
        else
            let batchId = reader.GetInt64(0)

            use countCommand = connection.CreateCommand()
            countCommand.CommandText <- "SELECT COUNT(id), COUNT(DISTINCT node_digit), COUNT(DISTINCT phase_space_index) FROM generated_runs WHERE batch_id = $batchId;"
            countCommand.Parameters.AddWithValue("$batchId", batchId) |> ignore
            use countReader = countCommand.ExecuteReader()
            countReader.Read() |> ignore

            Ok
                {
                    SeedBase = reader.GetString(1)
                    CreatedAt = reader.GetString(2)
                    RunStatus = reader.GetString(3)
                    SlurmJobId = asOptionalString (reader.GetValue(4))
                    ManifestPath = asOptionalString (reader.GetValue(5))
                    ScriptPath = asOptionalString (reader.GetValue(6))
                    SubmittedAt = asOptionalString (reader.GetValue(7))
                    GeneratedInputCount = countReader.GetInt32(0)
                    NodeCount = countReader.GetInt32(1)
                    PhaseSpaceCount = countReader.GetInt32(2)
                    Rows = []
                }
    with ex ->
        Error $"Failed reading run batch details: {ex.Message}"

/// Reads per-run manifest rows for one seed base.
let private readRunManifestRows
    (connection: SqliteConnection)
    (seedBase: string)
    : Result<RunManifestRow list, string> =
    try
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT r.run_id, r.input_file_path, r.output_file_path, r.run_folder, r.seed, r.node_digit, r.phase_space_index FROM generated_runs r INNER JOIN generated_batches b ON b.id = r.batch_id WHERE b.seed_base = $seedBase ORDER BY r.id;"
        command.Parameters.AddWithValue("$seedBase", seedBase) |> ignore
        use reader = command.ExecuteReader()
        let rows = ResizeArray<RunManifestRow>()

        while reader.Read() do
            rows.Add(
                {
                    RunId = reader.GetString(0)
                    InputFilePath = reader.GetString(1)
                    OutputFilePath = reader.GetString(2)
                    RunFolder = reader.GetString(3)
                    Seed = reader.GetString(4)
                    NodeDigit = reader.GetString(5)
                    PhaseSpaceIndex = reader.GetString(6)
                }
            )

        Ok(List.ofSeq rows)
    with ex ->
        Error $"Failed reading run manifest rows: {ex.Message}"

/// Returns detailed run batch data by seed base.
let getRunBatchDetails (settings: TsebtSettings) (seedBase: string) : Result<RunBatchDetails, string> =
    try
        use connection = new SqliteConnection(toConnectionString settings)
        connection.Open()

        result {
            let! header = readRunBatchDetailsHeader connection seedBase
            let! rows = readRunManifestRows connection seedBase
            return { header with Rows = rows }
        }
    with ex ->
        Error $"Failed loading run batch details: {ex.Message}"

/// Builds one preflight check result value.
let private runCheck (name: string) (ok: bool) (message: string option) : RunPreflightCheck =
    {
        Name = name
        Ok = ok
        Message = if ok then None else message
    }

/// Returns true when all generated input files exist on disk.
let private allInputFilesExist (rows: RunManifestRow list) : bool =
    rows |> List.forall (fun row -> File.Exists row.InputFilePath)

/// Returns true when no output base path exists yet.
let private noOutputPathCollisions (rows: RunManifestRow list) : bool =
    rows
    |> List.exists (fun row -> File.Exists row.OutputFilePath || Directory.Exists row.OutputFilePath)
    |> not

/// Returns true when no output csv path exists yet.
let private noOutputCsvCollisions (rows: RunManifestRow list) : bool =
    rows |> List.exists (fun row -> File.Exists(row.OutputFilePath + ".csv")) |> not

/// Returns true when no output log path exists yet.
let private noOutputLogCollisions (rows: RunManifestRow list) : bool =
    rows |> List.exists (fun row -> File.Exists(row.OutputFilePath + ".log")) |> not

/// Evaluates Run preflight checks from batch metadata and generated rows.
let private evaluatePreflight
    (settings: TsebtSettings)
    (details: RunBatchDetails)
    : RunPreflightResult =
    let hasRows = details.Rows |> List.isEmpty |> not
    let isGeneratedStatus = String.Equals(details.RunStatus, "Generated", StringComparison.OrdinalIgnoreCase)
    let noPreviousSlurmJob = details.SlurmJobId |> Option.isNone
    let inputFilesOk = hasRows && allInputFilesExist details.Rows
    let runFolderExists = Directory.Exists(runBatchFolderPath settings details.SeedBase)
    let noOutputBaseCollisions = noOutputPathCollisions details.Rows
    let noOutputCsv = noOutputCsvCollisions details.Rows
    let noOutputLog = noOutputLogCollisions details.Rows

    let checks =
        [
            runCheck
                "Generated runs found"
                hasRows
                (Some $"No generated runs were found for seed base: {details.SeedBase}")
            runCheck
                "Batch status is Generated"
                isGeneratedStatus
                (Some $"Run batch status must be Generated but was {details.RunStatus}.")
            runCheck
                "No previous Slurm job"
                noPreviousSlurmJob
                (Some "This run batch already has a Slurm job id.")
            runCheck
                "Input files exist"
                inputFilesOk
                (Some "One or more generated input files are missing.")
            runCheck
                "Run folder exists"
                runFolderExists
                (Some $"Run folder does not exist: {runBatchFolderPath settings details.SeedBase}")
            runCheck
                "No output collisions"
                noOutputBaseCollisions
                (Some "One or more output base paths already exist.")
            runCheck
                "No output CSV collisions"
                noOutputCsv
                (Some "One or more output csv files already exist.")
            runCheck
                "No log collisions"
                noOutputLog
                (Some "One or more output log files already exist.")
        ]

    {
        SeedBase = details.SeedBase
        CanSubmit = checks |> List.forall _.Ok
        Checks = checks
    }

/// Runs Run preflight checks by seed base.
let preflightRun (settings: TsebtSettings) (seedBase: string) : Result<RunPreflightResult, string> =
    result {
        let! details = getRunBatchDetails settings seedBase
        return evaluatePreflight settings details
    }

/// Builds Slurm script text for a run batch manifest.
let private buildSlurmScriptText
    (settings: TsebtSettings)
    (seedBase: string)
    (manifestPath: string)
    (rowCount: int)
    : string =
    let slurmOutputPath = Path.Combine(settings.AppRoot, settings.Paths.Runs, seedBase, "slurm-%A_%a.out")

    [
        "#!/usr/bin/env bash"
        $"#SBATCH --job-name=tsebt-{seedBase}"
        $"#SBATCH --partition={settings.Slurm.Partition}"
        $"#SBATCH --cpus-per-task={settings.Slurm.CpusPerTask}"
        $"#SBATCH --array=1-{rowCount}"
        $"#SBATCH --output={slurmOutputPath}"
        ""
        "set -euo pipefail"
        ""
        $"MANIFEST=\"{manifestPath}\""
        "ROW=$(awk -F '\\t' -v task_id=\"$SLURM_ARRAY_TASK_ID\" '$1 == task_id { print; exit }' \"$MANIFEST\")"
        "if [ -z \"$ROW\" ]; then"
        "  echo \"Manifest row not found for task id $SLURM_ARRAY_TASK_ID\" >&2"
        "  exit 1"
        "fi"
        "IFS=$'\\t' read -r TASK_ID NODE_NAME RUN_ID INPUT_FILE LOG_FILE <<< \"$ROW\""
        $"\"{settings.Topas.Executable}\" \"$INPUT_FILE\" > \"$LOG_FILE\" 2>&1"
    ]
    |> String.concat "\n"

/// Builds a run script preview payload from existing generated run metadata.
let previewRun (settings: TsebtSettings) (seedBase: string) : Result<RunScriptPreview, string> =
    result {
        let! details = getRunBatchDetails settings seedBase
        let! preflight = preflightRun settings seedBase
        let manifestPath = Path.Combine(settings.AppRoot, settings.Paths.Runs, seedBase, "run_manifest.tsv")
        let scriptPath = Path.Combine(settings.AppRoot, settings.Paths.Runs, seedBase, "run_batch.slurm")

        let manifestRows =
            details.Rows
            |> List.mapi (fun index row ->
                {
                    TaskId = index + 1
                    NodeName = $"node{row.NodeDigit.PadLeft(2, '0')}"
                    RunId = row.RunId
                    InputFilePath = row.InputFilePath
                    LogFilePath = Path.Combine(settings.AppRoot, settings.Paths.Runs, seedBase, $"{row.RunId}.log")
                })

        let manifestRowsPreview = manifestRows |> List.truncate 20
        let scriptText = buildSlurmScriptText settings seedBase manifestPath manifestRows.Length

        return
            {
                SeedBase = seedBase
                ManifestPath = manifestPath
                ScriptPath = scriptPath
                ScriptText = scriptText
                RunCount = details.GeneratedInputCount
                ManifestRowsPreview = manifestRowsPreview
                Preflight = preflight
            }
    }

/// Updates generated batch status metadata for foundation-only submit behavior.
let private updateRunSubmissionStatus
    (connection: SqliteConnection)
    (seedBase: string)
    (newStatus: string)
    (manifestPath: string)
    (scriptPath: string)
    : Result<SubmitRunResult, string> =
    try
        let submittedAt = DateTime.UtcNow.ToString("O")
        use command = connection.CreateCommand()
        command.CommandText <- "UPDATE generated_batches SET run_status = $runStatus, submitted_at = $submittedAt, manifest_path = $manifestPath, script_path = $scriptPath WHERE seed_base = $seedBase;"
        command.Parameters.AddWithValue("$runStatus", newStatus) |> ignore
        command.Parameters.AddWithValue("$submittedAt", submittedAt) |> ignore
        command.Parameters.AddWithValue("$manifestPath", manifestPath) |> ignore
        command.Parameters.AddWithValue("$scriptPath", scriptPath) |> ignore
        command.Parameters.AddWithValue("$seedBase", seedBase) |> ignore

        let affected = command.ExecuteNonQuery()

        if affected = 0 then
            Error $"Run batch not found for seed base: {seedBase}"
        else
            Ok
                {
                    SeedBase = seedBase
                    RunStatus = newStatus
                    SlurmJobId = None
                    ManifestPath = Some manifestPath
                    ScriptPath = Some scriptPath
                    SubmittedAt = Some submittedAt
                }
    with ex ->
        Error $"Failed updating run submission status: {ex.Message}"

/// Stores submit foundation metadata without performing Slurm submission.
let submitRun (settings: TsebtSettings) (request: SubmitRunRequest) : Result<SubmitRunResult, string> =
    result {
        let! preview = previewRun settings request.SeedBase

        if not preview.Preflight.CanSubmit then
            return! Error $"Run preflight failed for seed base: {request.SeedBase}"

        try
            use connection = new SqliteConnection(toConnectionString settings)
            connection.Open()

            return!
                updateRunSubmissionStatus
                    connection
                    request.SeedBase
                    "Submitted"
                    preview.ManifestPath
                    preview.ScriptPath
        with ex ->
            return! Error $"Failed submitting run batch: {ex.Message}"
    }
