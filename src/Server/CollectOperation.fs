module CollectOperation

open System
open FsToolkit.ErrorHandling
open Microsoft.Data.Sqlite
open Shared
open TsebtConfig
open Bootstrap
open CollectPlanning
open CollectPreflight

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

/// Returns not-implemented response for collect operation during foundation phase.
let collectBatch (_settings: TsebtSettings) (_request: CollectRequest) : Result<CollectResult, string> =
    Error "Collect operation is not implemented yet."
