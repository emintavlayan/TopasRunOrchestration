module Server.XUnit.Tests

open System
open System.IO
open Microsoft.Data.Sqlite
open Microsoft.Extensions.Configuration
open Xunit

open Shared
open Server
open TsebtConfig
open SqliteInit
open GeneratePlanning
open GenerateOperation
open RunOperation
open CollectOperation
open CollectCsvMerge
open CollectStatistics

/// Asserts that a Result is Ok and returns the value.
let private assertOk result =
    match result with
    | Ok value -> value
    | Error message -> failwith $"Expected Ok but got Error: {message}"

/// Asserts that a Result is Error and returns the message.
let private assertError result =
    match result with
    | Ok _ -> failwith "Expected Error but got Ok"
    | Error message -> message

/// Asserts that an error Result contains expected text.
let private assertErrorContains (expected: string) result =
    let message: string = assertError result
    Assert.Contains(expected, message)

/// Deletes a temporary directory after clearing SQLite pools.
let private cleanupTestDirectory (path: string) =
    if Directory.Exists path then
        SqliteConnection.ClearAllPools()
        GC.Collect()
        GC.WaitForPendingFinalizers()
        Directory.Delete(path, true)

/// Builds a minimal valid settings record for server tests.
let private buildSettings (appRoot: string) =
    {
        AppRoot = appRoot
        Paths = {
            Templates = "templates"
            Inputs = "inputs"
            Runs = "runs"
            Outputs = "outputs"
            Database = "database\\app.db"
            Logs = "logs"
        }
        Placeholders = {
            PhaseSpaceFile = "__PHSP_FILE__"
            OutputFile = "__OUTPUT_FILE__"
            Seed = "__SEED__"
        }
        Seed = { CurrentBase = "1001" }
        Topas = { Executable = "topas" }
        Slurm = {
            Partition = "compute"
            CpusPerTask = 1
        }
        Nodes = [
            { Name = "monte-carlo-01"; Digit = "1" }
            { Name = "monte-carlo-02"; Digit = "2" }
        ]
        PhaseSpaceFiles = [
            { Index = "01"; Value = "ps01.IAEAphsp" }
            { Index = "02"; Value = "ps02.IAEAphsp" }
        ]
    }

/// Builds a minimal valid IConfiguration for config loading tests.
let private buildValidConfig () =
    let values =
        dict [
            "Tsebt:AppRoot", @"C:\app-root"
            "Tsebt:Paths:Templates", "templates"
            "Tsebt:Paths:Inputs", "inputs"
            "Tsebt:Paths:Runs", "runs"
            "Tsebt:Paths:Outputs", "outputs"
            "Tsebt:Paths:Database", "database\\app.db"
            "Tsebt:Paths:Logs", "logs"
            "Tsebt:Placeholders:PhaseSpaceFile", "__PHSP_FILE__"
            "Tsebt:Placeholders:OutputFile", "__OUTPUT_FILE__"
            "Tsebt:Placeholders:Seed", "__SEED__"
            "Tsebt:Seed:CurrentBase", "1001"
            "Tsebt:Nodes:0:Name", "node01"
            "Tsebt:Nodes:0:Digit", "1"
            "Tsebt:Nodes:1:Name", "node02"
            "Tsebt:Nodes:1:Digit", "2"
            "Tsebt:PhaseSpaceFiles:0:Index", "01"
            "Tsebt:PhaseSpaceFiles:0:Value", "ps01.IAEAphsp"
            "Tsebt:PhaseSpaceFiles:1:Index", "02"
            "Tsebt:PhaseSpaceFiles:1:Value", "ps02.IAEAphsp"
        ]

    ConfigurationBuilder().AddInMemoryCollection(values).Build() :> IConfiguration

/// Seeds a generated batch and generated runs in SQLite.
let private seedGeneratedBatch
    (connection: SqliteConnection)
    (seedBase: string)
    (rows: (string * string * string * string * string) list)
    =
    use batchInsert = connection.CreateCommand()
    batchInsert.CommandText <- "INSERT INTO generated_batches(seed_base, created_at, run_status, collect_status) VALUES($seedBase, '2026-01-01T00:00:00Z', 'Generated', 'NotCollected');"
    batchInsert.Parameters.AddWithValue("$seedBase", seedBase) |> ignore
    batchInsert.ExecuteNonQuery() |> ignore

    rows
    |> List.iter (fun (runId, phaseIndex, nodeDigit, inputPath, outputBase) ->
        use runInsert = connection.CreateCommand()
        runInsert.CommandText <- "INSERT INTO generated_runs(batch_id, run_id, phase_space_index, phase_space_file, node_name, node_digit, seed, input_file_path, output_file_path, run_folder, status, created_at) VALUES((SELECT id FROM generated_batches WHERE seed_base = $seedBase), $runId, $phaseIndex, 'ps.IAEAphsp', $nodeName, $nodeDigit, $seed, $inputPath, $outputBase, $runFolder, 'Generated', '2026-01-01T00:00:00Z');"
        runInsert.Parameters.AddWithValue("$seedBase", seedBase) |> ignore
        runInsert.Parameters.AddWithValue("$runId", runId) |> ignore
        runInsert.Parameters.AddWithValue("$phaseIndex", phaseIndex) |> ignore
        runInsert.Parameters.AddWithValue("$nodeName", $"node{nodeDigit.PadLeft(2, '0')}") |> ignore
        runInsert.Parameters.AddWithValue("$nodeDigit", nodeDigit) |> ignore
        runInsert.Parameters.AddWithValue("$seed", $"1001{nodeDigit}") |> ignore
        runInsert.Parameters.AddWithValue("$inputPath", inputPath) |> ignore
        runInsert.Parameters.AddWithValue("$outputBase", outputBase) |> ignore
        runInsert.Parameters.AddWithValue("$runFolder", Path.GetDirectoryName(outputBase)) |> ignore
        runInsert.ExecuteNonQuery() |> ignore)

[<Fact>]
let ``Config loads for valid minimal settings`` () =
    let loaded = TsebtConfig.load (buildValidConfig ())
    Assert.True(Result.isOk loaded)

[<Fact>]
let ``Config fails for duplicate node digits`` () =
    let cfg = buildValidConfig ()
    cfg.["Tsebt:Nodes:1:Digit"] <- "1"
    Assert.True(Result.isError (TsebtConfig.load cfg))

[<Fact>]
let ``Config fails when AppRoot is missing`` () =
    let cfg = buildValidConfig ()
    cfg.["Tsebt:AppRoot"] <- null
    Assert.True(Result.isError (TsebtConfig.load cfg))

[<Fact>]
let ``Config fails when nodes are empty`` () =
    let cfg = buildValidConfig ()
    cfg.["Tsebt:Nodes:0:Name"] <- null
    cfg.["Tsebt:Nodes:0:Digit"] <- null
    cfg.["Tsebt:Nodes:1:Name"] <- null
    cfg.["Tsebt:Nodes:1:Digit"] <- null
    Assert.True(Result.isError (TsebtConfig.load cfg))

[<Fact>]
let ``Config fails when phase-space files are empty`` () =
    let cfg = buildValidConfig ()
    cfg.["Tsebt:PhaseSpaceFiles:0:Index"] <- null
    cfg.["Tsebt:PhaseSpaceFiles:0:Value"] <- null
    cfg.["Tsebt:PhaseSpaceFiles:1:Index"] <- null
    cfg.["Tsebt:PhaseSpaceFiles:1:Value"] <- null
    Assert.True(Result.isError (TsebtConfig.load cfg))

[<Fact>]
let ``Config fails for duplicate phase-space indexes`` () =
    let cfg = buildValidConfig ()
    cfg.["Tsebt:PhaseSpaceFiles:1:Index"] <- "01"
    Assert.True(Result.isError (TsebtConfig.load cfg))

[<Fact>]
let ``Config fails for non-numeric seed base`` () =
    let cfg = buildValidConfig ()
    cfg.["Tsebt:Seed:CurrentBase"] <- "seed-x"
    Assert.True(Result.isError (TsebtConfig.load cfg))

[<Fact>]
let ``Config fails for empty placeholders`` () =
    let cfg = buildValidConfig ()
    cfg.["Tsebt:Placeholders:Seed"] <- ""
    Assert.True(Result.isError (TsebtConfig.load cfg))

[<Fact>]
let ``Bootstrap creates required folders and not phsp-files`` () =
    let appRoot = Path.Combine(Path.GetTempPath(), $"xunit-bootstrap-{Guid.NewGuid():N}")
    Directory.CreateDirectory(appRoot) |> ignore

    try
        let settings = buildSettings appRoot
        assertOk (Bootstrap.ensureRootFolders settings) |> ignore
        Assert.True(Directory.Exists(Path.Combine(appRoot, "templates")))
        Assert.True(Directory.Exists(Path.Combine(appRoot, "inputs")))
        Assert.True(Directory.Exists(Path.Combine(appRoot, "runs")))
        Assert.True(Directory.Exists(Path.Combine(appRoot, "outputs")))
        Assert.True(Directory.Exists(Path.Combine(appRoot, "database")))
        Assert.True(Directory.Exists(Path.Combine(appRoot, "logs")))
        Assert.False(Directory.Exists(Path.Combine(appRoot, "phsp-files")))
    finally
        cleanupTestDirectory appRoot

[<Fact>]
let ``Seed and RunId planning follows conventions`` () =
    Assert.Equal("10011", buildSeed "1001" "1")
    Assert.Equal("seed10011_phsp01", buildRunId "01" "10011")
    Assert.Equal("seed10011_phsp01.txt", buildInputFileName "10011" "01")

[<Fact>]
let ``Run folder and output path planning use runs seed folder`` () =
    let settings = buildSettings @"C:\app-root"
    let planned = planGeneratedRuns settings "1001" "template" settings.Nodes settings.PhaseSpaceFiles
    let firstRun = planned |> List.head
    let normalizedRunFolder = firstRun.RunFolder.Replace('\\', '/')
    let normalizedOutputPath = firstRun.OutputFilePath.Replace('\\', '/')
    Assert.EndsWith("runs/1001", normalizedRunFolder)
    Assert.EndsWith("runs/1001/seed10011_phsp01", normalizedOutputPath)

[<Fact>]
let ``Placeholder replacement applies configured tokens`` () =
    let placeholders = {
        PhaseSpaceFile = "__PHSP_FILE__"
        OutputFile = "__OUTPUT_FILE__"
        Seed = "__SEED__"
    }

    let sourceText = "seed=__SEED__; phsp=__PHSP_FILE__; output=__OUTPUT_FILE__"
    let replaced = applyConfiguredPlaceholders placeholders "ps01.IAEAphsp" @"C:\runs\1001\seed10011_phsp01" "10011" sourceText
    Assert.Equal("seed=10011; phsp=ps01.IAEAphsp; output=C:\\runs\\1001\\seed10011_phsp01", replaced)

[<Fact>]
let ``Stitched template text keeps ordering`` () =
    let stitched = stitchTemplateTexts [ "A"; "B"; "C" ]
    let indexA = stitched.IndexOf("A")
    let indexB = stitched.IndexOf("B")
    let indexC = stitched.IndexOf("C")
    Assert.True(indexA < indexB && indexB < indexC)

[<Fact>]
let ``Generate planning expands node and phase-space grid`` () =
    let settings = buildSettings @"C:\app-root"
    let planned = planGeneratedRuns settings "1001" "template" settings.Nodes settings.PhaseSpaceFiles
    Assert.Equal(4, planned.Length)

[<Fact>]
let ``Generate preflight blocks collisions in seed input folder`` () =
    let appRoot = Path.Combine(Path.GetTempPath(), $"xunit-gen-collision-{Guid.NewGuid():N}")
    Directory.CreateDirectory(appRoot) |> ignore

    try
        let settings = buildSettings appRoot
        assertOk (Bootstrap.ensureRootFolders settings) |> ignore
        assertOk (initialize settings) |> ignore
        let folder = Path.Combine(appRoot, "inputs", "1001")
        Directory.CreateDirectory(folder) |> ignore
        File.WriteAllText(Path.Combine(folder, "existing.txt"), "x")
        let planned = planGeneratedRuns settings "1001" "template" settings.Nodes settings.PhaseSpaceFiles
        Assert.True(Result.isError (preflightGenerateCollisions settings "1001" planned))
    finally
        cleanupTestDirectory appRoot

[<Fact>]
let ``Generate writes files and stores DB metadata`` () =
    let appRoot = Path.Combine(Path.GetTempPath(), $"xunit-generate-{Guid.NewGuid():N}")
    Directory.CreateDirectory(appRoot) |> ignore

    try
        let settings = buildSettings appRoot
        let templatesFolder = Path.Combine(appRoot, settings.Paths.Templates)
        Directory.CreateDirectory(templatesFolder) |> ignore
        File.WriteAllText(Path.Combine(templatesFolder, "a.txt"), "seed=__SEED__")
        File.WriteAllText(Path.Combine(templatesFolder, "b.txt"), "phsp=__PHSP_FILE__; out=__OUTPUT_FILE__")

        assertOk (Bootstrap.ensureRootFolders settings) |> ignore
        assertOk (initialize settings) |> ignore

        let request = {
            SelectedTemplatePaths = [ "a.txt"; "b.txt" ]
            SelectedNodeDigits = [ "1"; "2" ]
            SelectedPhaseSpaceIndexes = [ "01"; "02" ]
        }

        let generated = assertOk (generate settings "1001" request)
        Assert.Equal(4, generated.GeneratedInputCount)
        Assert.True(Directory.Exists(Path.Combine(appRoot, "inputs", "1001")))
    finally
        cleanupTestDirectory appRoot

[<Fact>]
let ``Run parsing and manifest formatting are correct`` () =
    Assert.Equal(Ok "12345", parseSlurmJobId "Submitted batch job 12345")
    Assert.True(Result.isError (parseSlurmJobId "bad output"))
    let row = {
        TaskId = 1
        NodeName = "monte-carlo-01"
        RunId = "seed10011_phsp01"
        InputFilePath = "/app/inputs/1001/seed10011_phsp01.txt"
        LogFilePath = "/app/runs/1001/seed10011_phsp01.log"
    }
    Assert.Equal("1\tmonte-carlo-01\tseed10011_phsp01\t/app/inputs/1001/seed10011_phsp01.txt\t/app/runs/1001/seed10011_phsp01.log", formatManifestRow row)

[<Fact>]
let ``Run script includes srun node assignment and topas redirect`` () =
    let settings = buildSettings @"C:\app-root"
    let manifestPath = @"C:\app-root\runs\1001\run_manifest.tsv"
    let script = buildSlurmScriptText settings "1001" manifestPath 4
    Assert.Contains("--array=1-4", script)
    Assert.Contains("srun --nodes=1 --ntasks=1 --nodelist=\"$NODE_NAME\"", script)
    Assert.Contains("\"$TOPAS\" \"$INPUT_FILE\" > \"$LOG_FILE\" 2>&1", script)

[<Fact>]
let ``Manifest preview rows preserve configured node names`` () =
    let settings = buildSettings @"C:\app-root"
    let row = {
        RunId = "seed10011_phsp01"
        InputFilePath = @"C:\app-root\inputs\1001\seed10011_phsp01.txt"
        OutputFilePath = @"C:\app-root\runs\1001\seed10011_phsp01"
        RunFolder = @"C:\app-root\runs\1001"
        Seed = "10011"
        NodeDigit = "1"
        PhaseSpaceIndex = "01"
    }

    let preview = toManifestPreviewRow settings "1001" 1 row
    Assert.Equal("monte-carlo-01", preview.NodeName)

[<Fact>]
let ``Run preflight blocks existing csv and log outputs`` () =
    let appRoot = Path.Combine(Path.GetTempPath(), $"xunit-run-preflight-{Guid.NewGuid():N}")
    Directory.CreateDirectory(appRoot) |> ignore

    try
        let settings = buildSettings appRoot
        assertOk (Bootstrap.ensureRootFolders settings) |> ignore
        assertOk (initialize settings) |> ignore

        let inputFolder = Path.Combine(appRoot, "inputs", "1001")
        let runFolder = Path.Combine(appRoot, "runs", "1001")
        Directory.CreateDirectory(inputFolder) |> ignore
        Directory.CreateDirectory(runFolder) |> ignore

        let inputPath = Path.Combine(inputFolder, "seed10011_phsp01.txt")
        let outputBase = Path.Combine(runFolder, "seed10011_phsp01")
        File.WriteAllText(inputPath, "input")
        File.WriteAllText(outputBase + ".csv", "csv")
        File.WriteAllText(outputBase + ".log", "log")

        let dbPath = Path.Combine(appRoot, "database", "app.db")
        let csb = SqliteConnectionStringBuilder()
        csb.DataSource <- dbPath
        use conn = new SqliteConnection(csb.ConnectionString)
        conn.Open()
        seedGeneratedBatch conn "1001" [ ("seed10011_phsp01", "01", "1", inputPath, outputBase) ]

        let preflight = assertOk (preflightRun settings "1001")
        Assert.False(preflight.CanSubmit)
        Assert.True(preflight.Checks |> List.exists (fun c -> c.Name = "No output CSV collisions" && not c.Ok))
        Assert.True(preflight.Checks |> List.exists (fun c -> c.Name = "No log collisions" && not c.Ok))
    finally
        cleanupTestDirectory appRoot

[<Fact>]
let ``Run submit blocks previously submitted batch`` () =
    let appRoot = Path.Combine(Path.GetTempPath(), $"xunit-run-submit-{Guid.NewGuid():N}")
    Directory.CreateDirectory(appRoot) |> ignore

    try
        let settings = buildSettings appRoot
        assertOk (Bootstrap.ensureRootFolders settings) |> ignore
        assertOk (initialize settings) |> ignore

        let dbPath = Path.Combine(appRoot, "database", "app.db")
        let csb = SqliteConnectionStringBuilder()
        csb.DataSource <- dbPath
        use conn = new SqliteConnection(csb.ConnectionString)
        conn.Open()
        use insert = conn.CreateCommand()
        insert.CommandText <- "INSERT INTO generated_batches(seed_base, created_at, run_status, slurm_job_id) VALUES('1001', '2026-01-01T00:00:00Z', 'Submitted', '12345');"
        insert.ExecuteNonQuery() |> ignore

        assertErrorContains "already been submitted to Slurm as job 12345" (submitRun settings { SeedBase = "1001" })
    finally
        cleanupTestDirectory appRoot

[<Fact>]
let ``Collect preflight blocks missing csv and allows missing log`` () =
    let appRoot = Path.Combine(Path.GetTempPath(), $"xunit-collect-preflight-{Guid.NewGuid():N}")
    Directory.CreateDirectory(appRoot) |> ignore

    try
        let settings = buildSettings appRoot
        assertOk (Bootstrap.ensureRootFolders settings) |> ignore
        assertOk (initialize settings) |> ignore

        let inputFolder = Path.Combine(appRoot, "inputs", "1001")
        let runFolder = Path.Combine(appRoot, "runs", "1001")
        Directory.CreateDirectory(inputFolder) |> ignore
        Directory.CreateDirectory(runFolder) |> ignore

        let inputPath = Path.Combine(inputFolder, "seed10011_phsp01.txt")
        let outputBase = Path.Combine(runFolder, "seed10011_phsp01")
        File.WriteAllText(inputPath, "input")

        let dbPath = Path.Combine(appRoot, "database", "app.db")
        let csb = SqliteConnectionStringBuilder()
        csb.DataSource <- dbPath
        use conn = new SqliteConnection(csb.ConnectionString)
        conn.Open()
        seedGeneratedBatch conn "1001" [ ("seed10011_phsp01", "01", "1", inputPath, outputBase) ]

        let missingCsv = assertOk (preflightCollect settings "1001")
        Assert.False(missingCsv.CanCollect)

        File.WriteAllText(outputBase + ".csv", "x,y,dose\n0,0,1")
        let missingLog = assertOk (preflightCollect settings "1001")
        Assert.True(missingLog.CanCollect)
        Assert.Equal(1, missingLog.MissingLogCount)
    finally
        cleanupTestDirectory appRoot

[<Fact>]
let ``Collect preflight passes when csv and log exist`` () =
    let appRoot = Path.Combine(Path.GetTempPath(), $"xunit-collect-preflight-ok-{Guid.NewGuid():N}")
    Directory.CreateDirectory(appRoot) |> ignore

    try
        let settings = buildSettings appRoot
        assertOk (Bootstrap.ensureRootFolders settings) |> ignore
        assertOk (initialize settings) |> ignore

        let inputFolder = Path.Combine(appRoot, "inputs", "1001")
        let runFolder = Path.Combine(appRoot, "runs", "1001")
        Directory.CreateDirectory(inputFolder) |> ignore
        Directory.CreateDirectory(runFolder) |> ignore

        let inputPath = Path.Combine(inputFolder, "seed10011_phsp01.txt")
        let outputBase = Path.Combine(runFolder, "seed10011_phsp01")
        File.WriteAllText(inputPath, "input")
        File.WriteAllText(outputBase + ".csv", "x,y,dose\n0,0,1")
        File.WriteAllText(outputBase + ".log", "ok")

        let dbPath = Path.Combine(appRoot, "database", "app.db")
        let csb = SqliteConnectionStringBuilder()
        csb.DataSource <- dbPath
        use conn = new SqliteConnection(csb.ConnectionString)
        conn.Open()
        seedGeneratedBatch conn "1001" [ ("seed10011_phsp01", "01", "1", inputPath, outputBase) ]

        let preflight = assertOk (preflightCollect settings "1001")
        Assert.True(preflight.CanCollect)
    finally
        cleanupTestDirectory appRoot

[<Fact>]
let ``Collect merge sums final numeric dose column`` () =
    let folder = Path.Combine(Path.GetTempPath(), $"xunit-merge-{Guid.NewGuid():N}")
    Directory.CreateDirectory(folder) |> ignore

    try
        let a = Path.Combine(folder, "node1.csv")
        let b = Path.Combine(folder, "node2.csv")
        let output = Path.Combine(folder, "phsp01_merged.csv")
        File.WriteAllText(a, "x,y,dose\n0,0,1\n0,1,2")
        File.WriteAllText(b, "x,y,dose\n0,0,3\n0,1,4")
        assertOk (mergeNodeCsvFilesForPhaseSpace [ a; b ] output) |> ignore
        let merged = File.ReadAllText(output)
        Assert.Contains("0,0,4", merged)
        Assert.Contains("0,1,6", merged)
    finally
        cleanupTestDirectory folder

[<Fact>]
let ``Collect merge fails on mismatched shape`` () =
    let folder = Path.Combine(Path.GetTempPath(), $"xunit-merge-mismatch-{Guid.NewGuid():N}")
    Directory.CreateDirectory(folder) |> ignore

    try
        let a = Path.Combine(folder, "a.csv")
        let b = Path.Combine(folder, "b.csv")
        let output = Path.Combine(folder, "merged.csv")
        File.WriteAllText(a, "x,y,dose\n0,0,1\n0,1,2")
        File.WriteAllText(b, "x,y,dose\n0,0,3")
        Assert.True(Result.isError (mergeNodeCsvFilesForPhaseSpace [ a; b ] output))

        File.WriteAllText(b, "x,y,z,dose\n0,0,0,3\n0,1,0,4")
        Assert.True(Result.isError (mergeNodeCsvFilesForPhaseSpace [ a; b ] output))
    finally
        cleanupTestDirectory folder

[<Fact>]
let ``Collect statistics computes mean median sd count`` () =
    let folder = Path.Combine(Path.GetTempPath(), $"xunit-stats-{Guid.NewGuid():N}")
    Directory.CreateDirectory(folder) |> ignore

    try
        let a = Path.Combine(folder, "phsp01.csv")
        let b = Path.Combine(folder, "phsp02.csv")
        let c = Path.Combine(folder, "phsp03.csv")
        let summary = Path.Combine(folder, "dose_summary.csv")
        File.WriteAllText(a, "x,y,dose\n0,0,1\n0,1,2")
        File.WriteAllText(b, "x,y,dose\n0,0,3\n0,1,4")
        File.WriteAllText(c, "x,y,dose\n0,0,5\n0,1,6")
        assertOk (computeDoseSummary [ a; b; c ] summary) |> ignore
        let lines = File.ReadAllLines(summary)
        Assert.Equal("x,y,mean,median,standard_deviation,count", lines[0])
        Assert.Contains(",3,3,2,3", lines[1])
        Assert.Contains(",4,4,2,3", lines[2])
    finally
        cleanupTestDirectory folder

[<Fact>]
let ``Collect statistics fails on mismatched row counts`` () =
    let folder = Path.Combine(Path.GetTempPath(), $"xunit-stats-mismatch-{Guid.NewGuid():N}")
    Directory.CreateDirectory(folder) |> ignore

    try
        let a = Path.Combine(folder, "phsp01.csv")
        let b = Path.Combine(folder, "phsp02.csv")
        let summary = Path.Combine(folder, "dose_summary.csv")
        File.WriteAllText(a, "x,y,dose\n0,0,1\n0,1,2")
        File.WriteAllText(b, "x,y,dose\n0,0,3")
        Assert.True(Result.isError (computeDoseSummary [ a; b ] summary))
    finally
        cleanupTestDirectory folder

[<Fact>]
let ``Collect operation writes outputs and updates status`` () =
    let appRoot = Path.Combine(Path.GetTempPath(), $"xunit-collect-op-{Guid.NewGuid():N}")
    Directory.CreateDirectory(appRoot) |> ignore

    try
        let settings = buildSettings appRoot
        assertOk (Bootstrap.ensureRootFolders settings) |> ignore
        assertOk (initialize settings) |> ignore

        let inputFolder = Path.Combine(appRoot, "inputs", "1001")
        let runFolder = Path.Combine(appRoot, "runs", "1001")
        Directory.CreateDirectory(inputFolder) |> ignore
        Directory.CreateDirectory(runFolder) |> ignore

        let rows = [
            ("seed10011_phsp01", "01", "1", Path.Combine(inputFolder, "seed10011_phsp01.txt"), Path.Combine(runFolder, "seed10011_phsp01"))
            ("seed10012_phsp01", "01", "2", Path.Combine(inputFolder, "seed10012_phsp01.txt"), Path.Combine(runFolder, "seed10012_phsp01"))
        ]

        let dbPath = Path.Combine(appRoot, "database", "app.db")
        let csb = SqliteConnectionStringBuilder()
        csb.DataSource <- dbPath
        use conn = new SqliteConnection(csb.ConnectionString)
        conn.Open()
        seedGeneratedBatch conn "1001" rows

        File.WriteAllText(Path.Combine(inputFolder, "seed10011_phsp01.txt"), "input")
        File.WriteAllText(Path.Combine(inputFolder, "seed10012_phsp01.txt"), "input")
        File.WriteAllText(Path.Combine(runFolder, "seed10011_phsp01.csv"), "x,y,dose\n0,0,1")
        File.WriteAllText(Path.Combine(runFolder, "seed10012_phsp01.csv"), "x,y,dose\n0,0,2")
        File.WriteAllText(Path.Combine(runFolder, "seed10011_phsp01.log"), "ok")
        File.WriteAllText(Path.Combine(runFolder, "seed10012_phsp01.log"), "ok")

        let result = assertOk (collectBatch settings { SeedBase = "1001" })
        Assert.Equal("Collected", result.Status)
        let outputFolder = Path.Combine(appRoot, "outputs", "1001")
        Assert.True(File.Exists(Path.Combine(outputFolder, "collect_manifest.tsv")))
        Assert.True(File.Exists(Path.Combine(outputFolder, "phsp01_merged.csv")))
        Assert.True(File.Exists(Path.Combine(outputFolder, "dose_summary.csv")))

        use statusCommand = conn.CreateCommand()
        statusCommand.CommandText <- "SELECT collect_status FROM generated_batches WHERE seed_base = '1001';"
        let status = string (statusCommand.ExecuteScalar())
        Assert.Equal("Collected", status)
    finally
        cleanupTestDirectory appRoot
