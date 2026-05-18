module GenerateOperation

open System
open System.IO
open FsToolkit.ErrorHandling
open Microsoft.Data.Sqlite
open Shared
open TsebtConfig
open Bootstrap

/// Replaces a configured placeholder token in template text.
let private replaceToken (token: string) (value: string) (text: string) : string = text.Replace(token, value)

/// Builds a run id from phase-space index and seed.
let private buildRunId (phaseSpaceIndex: string) (seed: string) : string = $"phsp{phaseSpaceIndex}_seed{seed}"

/// Builds the generated input file name for a run.
let private buildInputFileName (seed: string) (phaseSpaceIndex: string) (nodeDigit: string) : string =
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
let private insertGeneratedRun (settings: TsebtSettings) (connection: SqliteConnection) (batchId: int64) (run: GeneratedRunInfo) (phaseSpaceFile: string) (nodeName: string) : Result<unit, string> =
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

        let runFolder = combineAppRoot settings.AppRoot (Path.Combine(settings.Paths.Runs, run.RunId))
        let outputFilePath = Path.Combine(runFolder, "dose")
        command.Parameters.AddWithValue("$batchId", batchId) |> ignore
        command.Parameters.AddWithValue("$runId", run.RunId) |> ignore
        command.Parameters.AddWithValue("$phaseSpaceIndex", run.PhaseSpaceIndex) |> ignore
        command.Parameters.AddWithValue("$phaseSpaceFile", phaseSpaceFile) |> ignore
        command.Parameters.AddWithValue("$nodeName", nodeName) |> ignore
        command.Parameters.AddWithValue("$nodeDigit", run.NodeDigit) |> ignore
        command.Parameters.AddWithValue("$seed", run.Seed) |> ignore
        command.Parameters.AddWithValue("$inputFilePath", run.InputFilePath) |> ignore
        command.Parameters.AddWithValue("$outputFilePath", outputFilePath) |> ignore
        command.Parameters.AddWithValue("$runFolder", runFolder) |> ignore
        command.Parameters.AddWithValue("$status", "Generated") |> ignore
        command.Parameters.AddWithValue("$createdAt", utcNowString ()) |> ignore
        command.ExecuteNonQuery() |> ignore
        Ok ()
    with ex ->
        Error $"Failed inserting generated run metadata: {ex.Message}"

/// Writes one generated input file and returns generated metadata.
let private writeGeneratedInput (settings: TsebtSettings) (seedBase: string) (stitchedTemplateText: string) (node: TsebtNode) (phaseSpace: TsebtPhaseSpaceFile) : Result<GeneratedRunInfo, string> =
    try
        let seed = $"{seedBase}{node.Digit}"
        let runId = buildRunId phaseSpace.Index seed
        let runFolder = combineAppRoot settings.AppRoot (Path.Combine(settings.Paths.Runs, runId))
        let outputFilePath = Path.Combine(runFolder, "dose")
        let inputFolder = combineAppRoot settings.AppRoot (Path.Combine(settings.Paths.Inputs, seedBase))
        let inputFileName = buildInputFileName seed phaseSpace.Index node.Digit
        let inputFilePath = Path.Combine(inputFolder, inputFileName)

        let finalText =
            stitchedTemplateText
            |> replaceToken settings.Placeholders.PhaseSpaceFile phaseSpace.Value
            |> replaceToken settings.Placeholders.OutputFile outputFilePath
            |> replaceToken settings.Placeholders.Seed seed

        Directory.CreateDirectory(runFolder) |> ignore
        Directory.CreateDirectory(inputFolder) |> ignore
        File.WriteAllText(inputFilePath, finalText)

        Ok {
            RunId = runId
            InputFilePath = inputFilePath
            Seed = seed
            NodeDigit = node.Digit
            PhaseSpaceIndex = phaseSpace.Index
        }
    with ex ->
        Error $"Failed generating input file: {ex.Message}"

/// Executes real generate and persists generated metadata in SQLite.
let generate (settings: TsebtSettings) (seedBase: string) (request: GenerateRequest) : Result<GenerateResult, string> =
    result {
        let templatesRoot = combineAppRoot settings.AppRoot settings.Paths.Templates
        let! stitchedTemplateText = readAndStitchTemplates templatesRoot request.SelectedTemplatePaths

        let! selectedNodes = request.SelectedNodeDigits |> List.map (tryFindNode settings) |> List.sequenceResultM
        let! selectedPhaseSpaces = request.SelectedPhaseSpaceIndexes |> List.map (tryFindPhaseSpaceFile settings) |> List.sequenceResultM

        let generatedRunsResult =
            selectedPhaseSpaces
            |> List.collect (fun phaseSpace ->
                selectedNodes
                |> List.map (fun node -> writeGeneratedInput settings seedBase stitchedTemplateText node phaseSpace))
            |> List.sequenceResultM

        let! generatedRuns = generatedRunsResult

        let databasePath = combineAppRoot settings.AppRoot settings.Paths.Database
        let connectionStringBuilder = SqliteConnectionStringBuilder()
        connectionStringBuilder.DataSource <- databasePath
        use connection = new SqliteConnection(connectionStringBuilder.ConnectionString)
        connection.Open()
        let! batchId = insertBatch connection seedBase

        do!
            generatedRuns
            |> List.map (fun run ->
                let phaseValue =
                    selectedPhaseSpaces
                    |> List.tryFind (fun phase -> phase.Index = run.PhaseSpaceIndex)
                    |> Option.map _.Value
                    |> Option.defaultValue ""

                let nodeName =
                    selectedNodes
                    |> List.tryFind (fun node -> node.Digit = run.NodeDigit)
                    |> Option.map _.Name
                    |> Option.defaultValue ""

                insertGeneratedRun settings connection batchId run phaseValue nodeName)
            |> List.sequenceResultM
            |> Result.map ignore

        let inputFolder = combineAppRoot settings.AppRoot (Path.Combine(settings.Paths.Inputs, seedBase))

        return {
            SeedBase = seedBase
            GeneratedInputCount = generatedRuns.Length
            NodeCount = selectedNodes.Length
            PhaseSpaceCount = selectedPhaseSpaces.Length
            InputFolder = inputFolder
            GeneratedRuns = generatedRuns
        }
    }
