module CollectOperation

open System
open FsToolkit.ErrorHandling
open Microsoft.Data.Sqlite
open Shared
open TsebtConfig
open Bootstrap
open CollectPlanning
open CollectPreflight
open CollectCsvMerge
open CollectStatistics
open System.IO

/// Returns nullable database values as optional strings.
let private asOptionalString (value: obj) : string option =
    if isNull value || Convert.IsDBNull value then None else Some(string value)

/// Returns nullable database values as optional integers.
let private asOptionalInt (value: obj) : int option =
    if isNull value || Convert.IsDBNull value then None else Some(Convert.ToInt32 value)

/// Builds sqlite connection string from configured database path.
let private toConnectionString (settings: TsebtSettings) : string =
    let databasePath = combineAppRoot settings.AppRoot settings.Paths.Database
    let builder = SqliteConnectionStringBuilder()
    builder.DataSource <- databasePath
    builder.ConnectionString

/// Reads collect batch summaries from generated batch and run metadata.
let private readCollectBatchSummaries (connection: SqliteConnection) : Result<CollectBatchSummary list, string> =
    try
        use command = connection.CreateCommand()

        command.CommandText <-
            """SELECT
  b.seed_base,
  b.created_at,
  COUNT(r.id) AS generated_run_count,
  COUNT(DISTINCT r.node_digit) AS node_count,
  COUNT(DISTINCT r.phase_space_index) AS phase_space_count,
  b.run_status,
  COALESCE(b.collect_status, 'NotCollected') AS collect_status,
  b.collect_summary_path
FROM generated_batches b
LEFT JOIN generated_runs r ON r.batch_id = b.id
GROUP BY b.id, b.seed_base, b.created_at, b.run_status, b.collect_status, b.collect_summary_path
ORDER BY b.created_at DESC;"""

        use reader = command.ExecuteReader()
        let rows = ResizeArray<CollectBatchSummary>()

        while reader.Read() do
            rows.Add(
                {
                    SeedBase = reader.GetString(0)
                    CreatedAt = reader.GetString(1)
                    GeneratedRunCount = reader.GetInt32(2)
                    NodeCount = reader.GetInt32(3)
                    PhaseSpaceCount = reader.GetInt32(4)
                    RunStatus = asOptionalString (reader.GetValue(5))
                    CollectStatus = reader.GetString(6)
                    CollectSummaryPath = asOptionalString (reader.GetValue(7))
                }
            )

        Ok(List.ofSeq rows)
    with ex ->
        Error $"Failed reading collect batch summaries: {ex.Message}"

/// Returns collect batch summaries for all generated batches.
let getCollectBatches (settings: TsebtSettings) : Result<CollectBatchSummary list, string> =
    try
        use connection = new SqliteConnection(toConnectionString settings)
        connection.Open()
        readCollectBatchSummaries connection
    with ex ->
        Error $"Failed loading collect batches: {ex.Message}"

/// Reads one collect batch details row by seed base.
let private readCollectBatchDetailsRow
    (connection: SqliteConnection)
    (seedBase: string)
    : Result<CollectBatchDetails, string> =
    try
        use command = connection.CreateCommand()

        command.CommandText <-
            """SELECT
  b.seed_base,
  b.created_at,
  COUNT(r.id) AS generated_run_count,
  COUNT(DISTINCT r.node_digit) AS node_count,
  COUNT(DISTINCT r.phase_space_index) AS phase_space_count,
  b.run_status,
  COALESCE(b.collect_status, 'NotCollected') AS collect_status,
  b.collected_at,
  b.collect_output_folder,
  b.collect_summary_path,
  b.collect_csv_found_count,
  b.collect_csv_missing_count,
  b.collect_log_found_count,
  b.collect_log_missing_count
FROM generated_batches b
LEFT JOIN generated_runs r ON r.batch_id = b.id
WHERE b.seed_base = $seedBase
GROUP BY b.id, b.seed_base, b.created_at, b.run_status, b.collect_status, b.collected_at, b.collect_output_folder, b.collect_summary_path, b.collect_csv_found_count, b.collect_csv_missing_count, b.collect_log_found_count, b.collect_log_missing_count
LIMIT 1;"""

        command.Parameters.AddWithValue("$seedBase", seedBase) |> ignore
        use reader = command.ExecuteReader()

        if not (reader.Read()) then
            Error $"Collect batch not found for seed base: {seedBase}"
        else
            Ok
                {
                    SeedBase = reader.GetString(0)
                    CreatedAt = reader.GetString(1)
                    GeneratedRunCount = reader.GetInt32(2)
                    NodeCount = reader.GetInt32(3)
                    PhaseSpaceCount = reader.GetInt32(4)
                    RunStatus = asOptionalString (reader.GetValue(5))
                    CollectStatus = reader.GetString(6)
                    CollectedAt = asOptionalString (reader.GetValue(7))
                    CollectOutputFolder = asOptionalString (reader.GetValue(8))
                    CollectSummaryPath = asOptionalString (reader.GetValue(9))
                    CollectCsvFoundCount = asOptionalInt (reader.GetValue(10))
                    CollectCsvMissingCount = asOptionalInt (reader.GetValue(11))
                    CollectLogFoundCount = asOptionalInt (reader.GetValue(12))
                    CollectLogMissingCount = asOptionalInt (reader.GetValue(13))
                }
    with ex ->
        Error $"Failed reading collect batch details: {ex.Message}"

/// Returns collect batch details by seed base.
let getCollectBatchDetails (settings: TsebtSettings) (seedBase: string) : Result<CollectBatchDetails, string> =
    try
        use connection = new SqliteConnection(toConnectionString settings)
        connection.Open()
        readCollectBatchDetailsRow connection seedBase
    with ex ->
        Error $"Failed loading collect batch details: {ex.Message}"

/// Reads generated run rows needed for collect preflight and planning.
let private readCollectRunRows
    (connection: SqliteConnection)
    (seedBase: string)
    : Result<CollectRunRow list, string> =
    try
        use command = connection.CreateCommand()

        command.CommandText <-
            """SELECT
  r.run_id,
  r.phase_space_index,
  r.node_digit,
  r.output_file_path
FROM generated_runs r
INNER JOIN generated_batches b ON b.id = r.batch_id
WHERE b.seed_base = $seedBase
ORDER BY r.id;"""

        command.Parameters.AddWithValue("$seedBase", seedBase) |> ignore
        use reader = command.ExecuteReader()
        let rows = ResizeArray<CollectRunRow>()

        while reader.Read() do
            rows.Add(
                {
                    RunId = reader.GetString(0)
                    PhaseSpaceIndex = reader.GetString(1)
                    NodeDigit = reader.GetString(2)
                    OutputFilePath = reader.GetString(3)
                }
            )

        Ok(List.ofSeq rows)
    with ex ->
        Error $"Failed reading collect run rows: {ex.Message}"

/// Runs collect preflight checks for one seed base using generated run metadata.
let preflightCollect (settings: TsebtSettings) (seedBase: string) : Result<CollectPreflightResult, string> =
    try
        use connection = new SqliteConnection(toConnectionString settings)
        connection.Open()

        result {
            let! rows = readCollectRunRows connection seedBase
            return buildCollectPreflightResult settings seedBase rows
        }
    with ex ->
        Error $"Failed running collect preflight: {ex.Message}"

/// Builds collect preview including planned output paths and preflight details.
let previewCollect (settings: TsebtSettings) (request: CollectPreviewRequest) : Result<CollectPreviewResult, string> =
    try
        use connection = new SqliteConnection(toConnectionString settings)
        connection.Open()

        result {
            let! rows = readCollectRunRows connection request.SeedBase
            let preflight = buildCollectPreflightResult settings request.SeedBase rows
            let outputFolder = outputFolderPath settings request.SeedBase

            return
                {
                    SeedBase = request.SeedBase
                    ExpectedRunCount = preflight.ExpectedRunCount
                    PhaseSpaceCount = rows |> List.map _.PhaseSpaceIndex |> List.distinct |> List.length
                    NodeCount = rows |> List.map _.NodeDigit |> List.distinct |> List.length
                    OutputFolder = outputFolder
                    PlannedMergedFiles = plannedMergedFiles settings request.SeedBase rows
                    FinalSummaryPath = plannedSummaryPath settings request.SeedBase
                    ManifestPath = plannedManifestPath settings request.SeedBase
                    Preflight = preflight
                }
        }
    with ex ->
        Error $"Failed building collect preview: {ex.Message}"

/// Builds collect manifest lines from generated run rows.
let private buildCollectManifestLines (rows: CollectRunRow list) : string list =
    let header = "runId\tphaseSpaceIndex\tnodeDigit\tcsvPath\tlogPath"

    let lines =
        rows
        |> List.map (fun row ->
            let csvPath, logPath = expectedCsvAndLogPaths row
            $"{row.RunId}\t{row.PhaseSpaceIndex}\t{row.NodeDigit}\t{csvPath}\t{logPath}")

    header :: lines

/// Returns true when folder exists and contains at least one file.
let private folderHasFiles (folderPath: string) : bool =
    Directory.Exists folderPath
    && Directory.EnumerateFileSystemEntries(folderPath, "*", SearchOption.TopDirectoryOnly)
        |> Seq.isEmpty
        |> not

/// Validates output collision rules before collect writes any files.
let private validateCollectOutputCollisions
    (outputFolder: string)
    (plannedMergedFilesList: string list)
    (summaryPath: string)
    (manifestPath: string)
    : Result<unit, string> =
    if folderHasFiles outputFolder then
        Error $"Collect output folder already exists and is not empty: {outputFolder}"
    elif plannedMergedFilesList |> List.exists File.Exists then
        Error "One or more planned merged csv files already exist."
    elif File.Exists summaryPath then
        Error $"Summary output file already exists: {summaryPath}"
    elif File.Exists manifestPath then
        Error $"Collect manifest file already exists: {manifestPath}"
    else
        Ok()

/// Writes collect manifest file for one seed base.
let private writeCollectManifest (manifestPath: string) (manifestLines: string list) : Result<unit, string> =
    try
        let parent = Path.GetDirectoryName manifestPath

        if not (String.IsNullOrWhiteSpace parent) then
            Directory.CreateDirectory(parent) |> ignore

        File.WriteAllLines(manifestPath, manifestLines)
        Ok()
    with ex ->
        Error $"Failed writing collect manifest: {ex.Message}"

/// Merges node csv files grouped by phase-space index.
let private mergePhaseSpaceCsvFiles
    (settings: TsebtSettings)
    (seedBase: string)
    (rows: CollectRunRow list)
    : Result<CollectedPhaseSpaceResult list, string> =
    rows
    |> List.groupBy _.PhaseSpaceIndex
    |> List.sortBy fst
    |> List.map (fun (phaseSpaceIndex, phaseRows) ->
        let csvInputPaths =
            phaseRows
            |> List.map expectedCsvAndLogPaths
            |> List.map fst
            |> List.distinct

        let outputPath = Path.Combine(outputFolderPath settings seedBase, $"phsp{phaseSpaceIndex}_merged.csv")

        result {
            do! mergeNodeCsvFilesForPhaseSpace csvInputPaths outputPath

            return
                {
                    PhaseSpaceIndex = phaseSpaceIndex
                    MergedFilePath = outputPath
                    SourceCsvCount = csvInputPaths.Length
                }
        })
    |> List.sequenceResultM

/// Updates collect metadata columns in generated_batches after successful collect.
let private updateCollectedBatchMetadata
    (connection: SqliteConnection)
    (seedBase: string)
    (outputFolder: string)
    (summaryPath: string)
    (preflight: CollectPreflightResult)
    : Result<string, string> =
    try
        let collectedAt = DateTime.UtcNow.ToString("O")
        use command = connection.CreateCommand()

        command.CommandText <-
            "UPDATE generated_batches SET collect_status = $collectStatus, collected_at = $collectedAt, collect_output_folder = $collectOutputFolder, collect_summary_path = $collectSummaryPath, collect_csv_found_count = $collectCsvFoundCount, collect_csv_missing_count = $collectCsvMissingCount, collect_log_found_count = $collectLogFoundCount, collect_log_missing_count = $collectLogMissingCount WHERE seed_base = $seedBase;"

        command.Parameters.AddWithValue("$collectStatus", "Collected") |> ignore
        command.Parameters.AddWithValue("$collectedAt", collectedAt) |> ignore
        command.Parameters.AddWithValue("$collectOutputFolder", outputFolder) |> ignore
        command.Parameters.AddWithValue("$collectSummaryPath", summaryPath) |> ignore
        command.Parameters.AddWithValue("$collectCsvFoundCount", preflight.FoundCsvCount) |> ignore
        command.Parameters.AddWithValue("$collectCsvMissingCount", preflight.MissingCsvCount) |> ignore
        command.Parameters.AddWithValue("$collectLogFoundCount", preflight.FoundLogCount) |> ignore
        command.Parameters.AddWithValue("$collectLogMissingCount", preflight.MissingLogCount) |> ignore
        command.Parameters.AddWithValue("$seedBase", seedBase) |> ignore

        let affected = command.ExecuteNonQuery()

        if affected = 0 then
            Error $"Collect batch not found for seed base: {seedBase}"
        else
            Ok collectedAt
    with ex ->
        Error $"Failed updating collect metadata: {ex.Message}"

/// Executes full collect operation for one seed base.
let collectBatch (settings: TsebtSettings) (request: CollectRequest) : Result<CollectResult, string> =
    try
        use connection = new SqliteConnection(toConnectionString settings)
        connection.Open()

        result {
            let! details = getCollectBatchDetails settings request.SeedBase

            if String.Equals(details.CollectStatus, "Collected", StringComparison.OrdinalIgnoreCase) then
                return! Error $"Batch {request.SeedBase} has already been collected."

            let! rows = readCollectRunRows connection request.SeedBase
            let preflight = buildCollectPreflightResult settings request.SeedBase rows

            if not preflight.CanCollect then
                return! Error $"Collect preflight failed for seed base: {request.SeedBase}"

            let outputFolder = outputFolderPath settings request.SeedBase
            let summaryPath = plannedSummaryPath settings request.SeedBase
            let manifestPath = plannedManifestPath settings request.SeedBase
            let plannedMergedFilesList = plannedMergedFiles settings request.SeedBase rows

            do! validateCollectOutputCollisions outputFolder plannedMergedFilesList summaryPath manifestPath

            let manifestLines = buildCollectManifestLines rows
            do! writeCollectManifest manifestPath manifestLines

            let! mergedFiles = mergePhaseSpaceCsvFiles settings request.SeedBase rows
            let mergedPaths = mergedFiles |> List.map _.MergedFilePath
            do! computeDoseSummary mergedPaths summaryPath

            let! _collectedAt =
                updateCollectedBatchMetadata connection request.SeedBase outputFolder summaryPath preflight

            return
                {
                    SeedBase = request.SeedBase
                    ExpectedRunCount = preflight.ExpectedRunCount
                    CsvReadCount = preflight.FoundCsvCount
                    LogFoundCount = preflight.FoundLogCount
                    MergedPhaseSpaceCount = mergedFiles.Length
                    OutputFolder = outputFolder
                    SummaryPath = summaryPath
                    MergedFiles = mergedFiles
                    ManifestPath = manifestPath
                    Status = "Collected"
                }
        }
    with ex ->
        Error $"Failed executing collect operation: {ex.Message}"
