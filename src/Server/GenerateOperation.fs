module GenerateOperation

open System
open System.IO
open FsToolkit.ErrorHandling
open Microsoft.Data.Sqlite
open Shared
open TsebtConfig
open Bootstrap

/// Represents one planned generated run before writing files.
type PlannedGeneratedRun = {
    RunId: string
    Seed: string
    NodeDigit: string
    NodeName: string
    PhaseSpaceIndex: string
    PhaseSpaceFile: string
    InputFilePath: string
    OutputFilePath: string
    RunFolder: string
    FinalText: string
}

/// Replaces a configured placeholder token in template text.
let private replaceToken (token: string) (value: string) (text: string) : string = text.Replace(token, value)

/// Builds a seed from seed base and node digit.
let buildSeed (seedBase: string) (nodeDigit: string) : string = $"{seedBase}{nodeDigit}"

/// Builds a run id from phase-space index and seed.
let buildRunId (phaseSpaceIndex: string) (seed: string) : string = $"phsp{phaseSpaceIndex}_seed{seed}"

/// Builds the generated input file name for a run.
let buildInputFileName (seed: string) (phaseSpaceIndex: string) (nodeDigit: string) : string =
    $"input_sd{seed}_ps{phaseSpaceIndex}_n{nodeDigit}.txt"

/// Reads and stitches selected template files from templates root.
let private readAndStitchTemplates (templatesRoot: string) (relativeTemplatePaths: string list) : Result<string, string> =
    try
        relativeTemplatePaths
        |> List.map (fun relativePath ->
            let normalizedPath = relativePath.Replace('/', Path.DirectorySeparatorChar)
            let fullPath = Path.Combine(templatesRoot, normalizedPath)

            if File.Exists fullPath then
                Ok(File.ReadAllText(fullPath))
            else
                Error $"Template file not found: {relativePath}")
        |> List.sequenceResultM
        |> Result.map (String.concat $"{Environment.NewLine}{Environment.NewLine}")
    with ex ->
        Error $"Failed reading template files: {ex.Message}"

/// Finds a configured node by its node digit.
let private tryFindNode (settings: TsebtSettings) (nodeDigit: string) : Result<TsebtNode, string> =
    settings.Nodes
    |> List.tryFind (fun node -> node.Digit = nodeDigit)
    |> Result.requireSome $"Node digit not found in configuration: {nodeDigit}"

/// Finds a configured phase-space file by its phase-space index.
let private tryFindPhaseSpaceFile (settings: TsebtSettings) (phaseSpaceIndex: string) : Result<TsebtPhaseSpaceFile, string> =
    settings.PhaseSpaceFiles
    |> List.tryFind (fun file -> file.Index = phaseSpaceIndex)
    |> Result.requireSome $"Phase-space index not found in configuration: {phaseSpaceIndex}"

/// Creates a UTC timestamp string for persistence fields.
let private utcNowString () : string = DateTime.UtcNow.ToString("O")

/// Checks whether a folder exists and contains at least one file.
let private folderContainsFiles (folderPath: string) : bool =
    Directory.Exists folderPath && Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories) |> Seq.isEmpty |> not

/// Builds one planned generated run from selected node and phase-space values.
let private planGeneratedRun (settings: TsebtSettings) (seedBase: string) (stitchedTemplateText: string) (inputFolder: string) (node: TsebtNode) (phaseSpace: TsebtPhaseSpaceFile) : PlannedGeneratedRun =
    let seed = buildSeed seedBase node.Digit
    let runId = buildRunId phaseSpace.Index seed
    let runFolder = combineAppRoot settings.AppRoot (Path.Combine(settings.Paths.Runs, runId))
    let outputFilePath = Path.Combine(runFolder, "dose")
    let inputFileName = buildInputFileName seed phaseSpace.Index node.Digit
    let inputFilePath = Path.Combine(inputFolder, inputFileName)

    let finalText =
        stitchedTemplateText
        |> replaceToken settings.Placeholders.PhaseSpaceFile phaseSpace.Value
        |> replaceToken settings.Placeholders.OutputFile outputFilePath
        |> replaceToken settings.Placeholders.Seed seed

    {
        RunId = runId
        Seed = seed
        NodeDigit = node.Digit
        NodeName = node.Name
        PhaseSpaceIndex = phaseSpace.Index
        PhaseSpaceFile = phaseSpace.Value
        InputFilePath = inputFilePath
        OutputFilePath = outputFilePath
        RunFolder = runFolder
        FinalText = finalText
    }

/// Plans all generated runs before file-system and database effects.
let planGeneratedRuns (settings: TsebtSettings) (seedBase: string) (stitchedTemplateText: string) (selectedNodes: TsebtNode list) (selectedPhaseSpaces: TsebtPhaseSpaceFile list) : PlannedGeneratedRun list =
    let inputFolder = combineAppRoot settings.AppRoot (Path.Combine(settings.Paths.Inputs, seedBase))

    selectedPhaseSpaces
    |> List.collect (fun phaseSpace ->
        selectedNodes
        |> List.map (fun node -> planGeneratedRun settings seedBase stitchedTemplateText inputFolder node phaseSpace))

/// Returns duplicate run ids that already exist in SQLite generated_runs.
let private findExistingRunIds (connection: SqliteConnection) (runIds: string list) : Result<string list, string> =
    try
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT run_id FROM generated_runs WHERE run_id = $runId LIMIT 1;"

        runIds
        |> List.distinct
        |> List.choose (fun runId ->
            command.Parameters.Clear()
            command.Parameters.AddWithValue("$runId", runId) |> ignore

            if isNull (command.ExecuteScalar()) then
                None
            else
                Some runId)
        |> Ok
    with ex ->
        Error $"Failed checking existing generated runs: {ex.Message}"

/// Validates planned output collisions before writing files or inserting metadata.
let private validateNoCollisions (settings: TsebtSettings) (seedBase: string) (plannedRuns: PlannedGeneratedRun list) (connection: SqliteConnection) : Result<unit, string> =
    result {
        let inputFolder = combineAppRoot settings.AppRoot (Path.Combine(settings.Paths.Inputs, seedBase))

        if folderContainsFiles inputFolder then
            return!
                Error
                    $"Input folder already contains generated files for seed base {seedBase}: {inputFolder}"

        match plannedRuns |> List.tryFind (fun run -> File.Exists run.InputFilePath) with
        | Some conflictingRun -> return! Error $"Generated input file already exists: {conflictingRun.InputFilePath}"
        | None -> ()

        match plannedRuns |> List.tryFind (fun run -> Directory.Exists run.RunFolder) with
        | Some conflictingRun -> return! Error $"Run folder already exists: {conflictingRun.RunFolder}"
        | None -> ()

        let! existingRunIds = plannedRuns |> List.map _.RunId |> findExistingRunIds connection

        match existingRunIds with
        | firstConflict :: _ -> return! Error $"Run id already exists in database: {firstConflict}"
        | [] -> return ()
    }

/// Inserts a generated batch row and returns the new batch id.
let private insertBatch (connection: SqliteConnection) (seedBase: string) : Result<int64, string> =
    try
        use command = connection.CreateCommand()
        command.CommandText <- "INSERT INTO generated_batches (seed_base, created_at) VALUES ($seedBase, $createdAt); SELECT last_insert_rowid();"
        command.Parameters.AddWithValue("$seedBase", seedBase) |> ignore
        command.Parameters.AddWithValue("$createdAt", utcNowString ()) |> ignore
        Ok(Convert.ToInt64(command.ExecuteScalar()))
    with ex ->
        Error $"Failed inserting generated batch: {ex.Message}"

/// Inserts one generated run row for metadata persistence.
let private insertGeneratedRun (connection: SqliteConnection) (batchId: int64) (run: PlannedGeneratedRun) : Result<unit, string> =
    try
        use command = connection.CreateCommand()

        command.CommandText <-
            """INSERT INTO generated_runs (
  batch_id, run_id, phase_space_index, phase_space_file, node_name, node_digit,
  seed, input_file_path, output_file_path, run_folder, status, created_at
) VALUES (
  $batchId, $runId, $phaseSpaceIndex, $phaseSpaceFile, $nodeName, $nodeDigit,
  $seed, $inputFilePath, $outputFilePath, $runFolder, $status, $createdAt
);"""

        command.Parameters.AddWithValue("$batchId", batchId) |> ignore
        command.Parameters.AddWithValue("$runId", run.RunId) |> ignore
        command.Parameters.AddWithValue("$phaseSpaceIndex", run.PhaseSpaceIndex) |> ignore
        command.Parameters.AddWithValue("$phaseSpaceFile", run.PhaseSpaceFile) |> ignore
        command.Parameters.AddWithValue("$nodeName", run.NodeName) |> ignore
        command.Parameters.AddWithValue("$nodeDigit", run.NodeDigit) |> ignore
        command.Parameters.AddWithValue("$seed", run.Seed) |> ignore
        command.Parameters.AddWithValue("$inputFilePath", run.InputFilePath) |> ignore
        command.Parameters.AddWithValue("$outputFilePath", run.OutputFilePath) |> ignore
        command.Parameters.AddWithValue("$runFolder", run.RunFolder) |> ignore
        command.Parameters.AddWithValue("$status", "Generated") |> ignore
        command.Parameters.AddWithValue("$createdAt", utcNowString ()) |> ignore
        command.ExecuteNonQuery() |> ignore
        Ok ()
    with ex ->
        Error $"Failed inserting generated run metadata: {ex.Message}"

/// Writes one planned generated input file.
let private writePlannedGeneratedInput (run: PlannedGeneratedRun) : Result<unit, string> =
    try
        Directory.CreateDirectory(run.RunFolder) |> ignore
        let inputFolder = Path.GetDirectoryName(run.InputFilePath)

        if String.IsNullOrWhiteSpace inputFolder then
            Error $"Input folder path is invalid: {run.InputFilePath}"
        else
            Directory.CreateDirectory(inputFolder) |> ignore
            File.WriteAllText(run.InputFilePath, run.FinalText)
            Ok ()
    with ex ->
        Error $"Failed generating input file: {ex.Message}"

/// Executes real generate and persists generated metadata in SQLite.
let generate (settings: TsebtSettings) (seedBase: string) (request: GenerateRequest) : Result<GenerateResult, string> =
    result {
        let templatesRoot = combineAppRoot settings.AppRoot settings.Paths.Templates
        let! stitchedTemplateText = readAndStitchTemplates templatesRoot request.SelectedTemplatePaths

        let! selectedNodes = request.SelectedNodeDigits |> List.map (tryFindNode settings) |> List.sequenceResultM
        let! selectedPhaseSpaces = request.SelectedPhaseSpaceIndexes |> List.map (tryFindPhaseSpaceFile settings) |> List.sequenceResultM

        let plannedRuns = planGeneratedRuns settings seedBase stitchedTemplateText selectedNodes selectedPhaseSpaces

        let databasePath = combineAppRoot settings.AppRoot settings.Paths.Database
        let connectionStringBuilder = SqliteConnectionStringBuilder()
        connectionStringBuilder.DataSource <- databasePath
        use connection = new SqliteConnection(connectionStringBuilder.ConnectionString)
        connection.Open()
        do! validateNoCollisions settings seedBase plannedRuns connection
        do! plannedRuns |> List.map writePlannedGeneratedInput |> List.sequenceResultM |> Result.map ignore
        let! batchId = insertBatch connection seedBase

        do!
            plannedRuns
            |> List.map (insertGeneratedRun connection batchId)
            |> List.sequenceResultM
            |> Result.map ignore

        let inputFolder = combineAppRoot settings.AppRoot (Path.Combine(settings.Paths.Inputs, seedBase))
        let generatedRuns =
            plannedRuns
            |> List.map (fun run -> {
                RunId = run.RunId
                InputFilePath = run.InputFilePath
                Seed = run.Seed
                NodeDigit = run.NodeDigit
                PhaseSpaceIndex = run.PhaseSpaceIndex
            })

        return {
            SeedBase = seedBase
            GeneratedInputCount = generatedRuns.Length
            NodeCount = selectedNodes.Length
            PhaseSpaceCount = selectedPhaseSpaces.Length
            InputFolder = inputFolder
            GeneratedRuns = generatedRuns
        }
    }
