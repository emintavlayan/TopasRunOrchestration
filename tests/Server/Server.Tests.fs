module Server.Tests

open Expecto

open Shared
open Server
open GenerateOperation
open GeneratePlanning
open RunOperation
open CollectCsvMerge
open CollectStatistics
open TsebtConfig
open SqliteInit
open System
open System.IO
open Microsoft.Extensions.Configuration
open Microsoft.Data.Sqlite

/// Builds a minimal valid in-memory configuration for TSEBT settings tests.
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

/// Builds a valid settings record used by planning and bootstrap tests.
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
            { Name = "node01"; Digit = "1" }
            { Name = "node02"; Digit = "2" }
        ]
        PhaseSpaceFiles = [
            { Index = "01"; Value = "ps01.IAEAphsp" }
            { Index = "02"; Value = "ps02.IAEAphsp" }
        ]
    }

let configTests =
    testList "Config" [
        testCase "TSEBT load succeeds for valid minimal config"
        <| fun _ ->
            let cfg = buildValidConfig ()
            let loaded = TsebtConfig.load cfg
            Expect.isOk loaded "Load should succeed for a valid minimal configuration"

        testCase "TSEBT load fails when AppRoot is missing"
        <| fun _ ->
            let cfg = buildValidConfig ()
            cfg.["Tsebt:AppRoot"] <- null
            let loaded = TsebtConfig.load cfg
            Expect.isError loaded "Load should fail when AppRoot is missing"

        testCase "TSEBT load fails when nodes list is empty"
        <| fun _ ->
            let cfg = buildValidConfig ()
            cfg.["Tsebt:Nodes:0:Name"] <- null
            cfg.["Tsebt:Nodes:0:Digit"] <- null
            cfg.["Tsebt:Nodes:1:Name"] <- null
            cfg.["Tsebt:Nodes:1:Digit"] <- null
            let loaded = TsebtConfig.load cfg
            Expect.isError loaded "Load should fail when no nodes are configured"

        testCase "TSEBT load fails when phase-space files list is empty"
        <| fun _ ->
            let cfg = buildValidConfig ()
            cfg.["Tsebt:PhaseSpaceFiles:0:Index"] <- null
            cfg.["Tsebt:PhaseSpaceFiles:0:Value"] <- null
            cfg.["Tsebt:PhaseSpaceFiles:1:Index"] <- null
            cfg.["Tsebt:PhaseSpaceFiles:1:Value"] <- null
            let loaded = TsebtConfig.load cfg
            Expect.isError loaded "Load should fail when no phase-space files are configured"

        testCase "TSEBT load fails when node digits are duplicated"
        <| fun _ ->
            let cfg = buildValidConfig ()
            cfg.["Tsebt:Nodes:1:Digit"] <- "1"
            let loaded = TsebtConfig.load cfg
            Expect.isError loaded "Load should fail when node digits are not unique"

        testCase "TSEBT load fails when phase-space indexes are duplicated"
        <| fun _ ->
            let cfg = buildValidConfig ()
            cfg.["Tsebt:PhaseSpaceFiles:1:Index"] <- "01"
            let loaded = TsebtConfig.load cfg
            Expect.isError loaded "Load should fail when phase-space indexes are not unique"

        testCase "TSEBT load fails when seed base is not numeric"
        <| fun _ ->
            let cfg = buildValidConfig ()
            cfg.["Tsebt:Seed:CurrentBase"] <- "seed-x"
            let loaded = TsebtConfig.load cfg
            Expect.isError loaded "Load should fail when seed base is non-numeric"

        testCase "TSEBT load fails when placeholder values are empty"
        <| fun _ ->
            let cfg = buildValidConfig ()
            cfg.["Tsebt:Placeholders:Seed"] <- ""
            let loaded = TsebtConfig.load cfg
            Expect.isError loaded "Load should fail when placeholders are empty"
    ]

let bootstrapTests =
    testList "Bootstrap" [
        testCase "Bootstrap creates required folders and does not create phsp-files"
        <| fun _ ->
            let appRoot = Path.Combine(Path.GetTempPath(), $"tsebt-bootstrap-{Guid.NewGuid():N}")
            Directory.CreateDirectory(appRoot) |> ignore

            let settings = buildSettings appRoot
            let ensured = Bootstrap.ensureRootFolders settings
            Expect.isOk ensured "Bootstrap should create required folders"

            let expectedFolders = [
                Path.Combine(appRoot, "templates")
                Path.Combine(appRoot, "inputs")
                Path.Combine(appRoot, "runs")
                Path.Combine(appRoot, "outputs")
                Path.Combine(appRoot, "database")
                Path.Combine(appRoot, "logs")
            ]

            expectedFolders
            |> List.iter (fun folder -> Expect.isTrue (Directory.Exists folder) $"Expected folder '{folder}'")

            Expect.isFalse (Directory.Exists(Path.Combine(appRoot, "phsp-files"))) "Bootstrap should not create phsp-files"
            Directory.Delete(appRoot, true)
    ]

let generatePlanningTests =
    testList "Generate planning" [
        testCase "Seed construction appends node digit"
        <| fun _ ->
            Expect.equal (buildSeed "1001" "1") "10011" "Seed should append node digit 1"
            Expect.equal (buildSeed "1001" "7") "10017" "Seed should append node digit 7"

        testCase "RunId construction uses phase-space index and seed"
        <| fun _ -> Expect.equal (buildRunId "01" "10011") "seed10011_phsp01" "RunId should match expected format"

        testCase "Generated input file name follows expected pattern"
        <| fun _ ->
            Expect.equal (buildInputFileName "10011" "01") "seed10011_phsp01.txt" "Generated filename should match expected format"

        testCase "Run folder and output path planning uses runs/seedBase/seed_phsp"
        <| fun _ ->
            let settings = (buildSettings @"C:\app-root")
            let planned = planGeneratedRuns settings "1001" "template" settings.Nodes settings.PhaseSpaceFiles
            let firstRun = planned |> List.head
            let normalizedRunFolder = firstRun.RunFolder.Replace('\\', '/')
            let normalizedOutputPath = firstRun.OutputFilePath.Replace('\\', '/')

            Expect.isTrue (normalizedRunFolder.EndsWith("runs/1001")) "Run folder should end with runs/1001"
            Expect.isTrue (normalizedOutputPath.EndsWith("runs/1001/seed10011_phsp01")) "Output file path should end with runs/1001/seed10011_phsp01"

        testCase "Placeholder replacement applies all configured tokens"
        <| fun _ ->
            let placeholders = {
                PhaseSpaceFile = "__PHSP_FILE__"
                OutputFile = "__OUTPUT_FILE__"
                Seed = "__SEED__"
            }

            let sourceText = "seed=__SEED__; phsp=__PHSP_FILE__; output=__OUTPUT_FILE__"

            let replaced =
                applyConfiguredPlaceholders placeholders "ps01.IAEAphsp" @"C:\runs\1001\seed10011_phsp01" "10011" sourceText

            Expect.equal replaced "seed=10011; phsp=ps01.IAEAphsp; output=C:\\runs\\1001\\seed10011_phsp01" "All placeholders should be replaced"

        testCase "Stitched text keeps input ordering"
        <| fun _ ->
            let stitched = stitchTemplateTexts [ "A"; "B"; "C" ]
            let indexA = stitched.IndexOf("A")
            let indexB = stitched.IndexOf("B")
            let indexC = stitched.IndexOf("C")
            Expect.isTrue (indexA < indexB && indexB < indexC) "A should appear before B, and B before C"

        testCase "Planning expands nodes and phase-space files deterministically"
        <| fun _ ->
            let settings =
                { buildSettings @"C:\app-root" with
                    Nodes = [
                        { Name = "node01"; Digit = "1" }
                        { Name = "node02"; Digit = "2" }
                        { Name = "node03"; Digit = "3" }
                    ] }

            let selectedNodes = settings.Nodes |> List.take 2
            let planned = planGeneratedRuns settings "1001" "template" selectedNodes settings.PhaseSpaceFiles

            Expect.equal planned.Length 4 "2 phase-space files x 2 nodes should produce 4 planned runs"

            let findSeed phase node =
                planned
                |> List.find (fun run -> run.PhaseSpaceIndex = phase && run.NodeDigit = node)
                |> _.Seed

            Expect.equal (findSeed "01" "1") "10011" "ps01 node1 seed should be 10011"
            Expect.equal (findSeed "01" "2") "10012" "ps01 node2 seed should be 10012"
            Expect.equal (findSeed "02" "1") "10011" "ps02 node1 seed should be 10011"
            Expect.equal (findSeed "02" "2") "10012" "ps02 node2 seed should be 10012"
    ]

let generateCollisionTests =
    testList "Generate collisions" [
        testCase "Preflight fails when input seed folder already contains files"
        <| fun _ ->
            let appRoot = Path.Combine(Path.GetTempPath(), $"tsebt-collision-{Guid.NewGuid():N}")
            Directory.CreateDirectory(appRoot) |> ignore

            let settings =
                { buildSettings appRoot with
                    Nodes = [ { Name = "node01"; Digit = "1" } ]
                    PhaseSpaceFiles = [ { Index = "01"; Value = "ps01.IAEAphsp" } ] }

            match Bootstrap.ensureRootFolders settings with
            | Error errorMessage -> failtestf "Failed creating root folders: %s" errorMessage
            | Ok() ->
                match initialize settings with
                | Error errorMessage -> failtestf "Failed initializing sqlite: %s" errorMessage
                | Ok _ ->
                    let usedSeedBase = "1001"
                    let existingInputFolder = Path.Combine(appRoot, "inputs", usedSeedBase)
                    File.WriteAllText(Path.Combine(existingInputFolder, "already-there.txt"), "existing")

                    let beforeFileCount = Directory.GetFiles(existingInputFolder, "*", SearchOption.AllDirectories).Length
                    let plannedRuns = planGeneratedRuns settings usedSeedBase "template" settings.Nodes settings.PhaseSpaceFiles

                    let result = preflightGenerateCollisions settings usedSeedBase plannedRuns
                    Expect.isError result "Preflight should fail when seed input folder already contains files"

                    let afterFileCount = Directory.GetFiles(existingInputFolder, "*", SearchOption.AllDirectories).Length
                    Expect.equal afterFileCount beforeFileCount "Preflight should not write any new files"

            Directory.Delete(appRoot, true)
    ]

let generateFilesystemTests =
    testList "Generate filesystem" [
        testCase "Generate writes expected files and metadata for one batch"
        <| fun _ ->
            let appRoot = Path.Combine(Path.GetTempPath(), $"tsebt-generate-{Guid.NewGuid():N}")
            Directory.CreateDirectory(appRoot) |> ignore

            let settings = buildSettings appRoot
            let templatesFolder = Path.Combine(appRoot, settings.Paths.Templates)
            Directory.CreateDirectory(templatesFolder) |> ignore

            let templateA = Path.Combine(templatesFolder, "a.txt")
            let templateB = Path.Combine(templatesFolder, "b.txt")
            File.WriteAllText(templateA, "seed=__SEED__")
            File.WriteAllText(templateB, "phsp=__PHSP_FILE__; out=__OUTPUT_FILE__")

            match Bootstrap.ensureRootFolders settings with
            | Error errorMessage -> failtestf "Failed creating root folders: %s" errorMessage
            | Ok() ->
                match initialize settings with
                | Error errorMessage -> failtestf "Failed initializing sqlite: %s" errorMessage
                | Ok _ ->
                    let request = {
                        SelectedTemplatePaths = [ "a.txt"; "b.txt" ]
                        SelectedNodeDigits = [ "1"; "2" ]
                        SelectedPhaseSpaceIndexes = [ "01"; "02" ]
                    }

                    let result = generate settings "1001" request
                    Expect.isOk result "Generate should succeed for valid request"

                    match result with
                    | Error _ -> ()
                    | Ok generated ->
                        Expect.equal generated.GeneratedInputCount 4 "Generate should produce 4 input files"

                        let expectedFiles = [
                            "seed10011_phsp01.txt"
                            "seed10012_phsp01.txt"
                            "seed10011_phsp02.txt"
                            "seed10012_phsp02.txt"
                        ]

                        let inputFolder = Path.Combine(appRoot, "inputs", "1001")
                        Expect.isTrue (Directory.Exists inputFolder) "Input folder should exist"

                        expectedFiles
                        |> List.iter (fun fileName ->
                            let fullPath = Path.Combine(inputFolder, fileName)
                            Expect.isTrue (File.Exists fullPath) $"Generated file '{fileName}' should exist"
                            let content = File.ReadAllText(fullPath)
                            Expect.stringContains content "seed=1001" "Content should include replaced seed"
                            Expect.stringContains content "phsp=ps0" "Content should include replaced phase-space file"
                            Expect.stringContains content "out=" "Content should include replaced output path")

                        let runsFolder = Path.Combine(appRoot, "runs", "1001")
                        Expect.isTrue (Directory.Exists runsFolder) "Runs folder should exist"

                        let databasePath = Path.Combine(appRoot, "database", "app.db")
                        let csb = SqliteConnectionStringBuilder()
                        csb.DataSource <- databasePath
                        use connection = new SqliteConnection(csb.ConnectionString)
                        connection.Open()

                        use batchCommand = connection.CreateCommand()
                        batchCommand.CommandText <- "SELECT COUNT(*) FROM generated_batches WHERE seed_base = $seedBase;"
                        batchCommand.Parameters.AddWithValue("$seedBase", "1001") |> ignore
                        let batchCount = Convert.ToInt32(batchCommand.ExecuteScalar())
                        Expect.equal batchCount 1 "generated_batches should contain one row for seed base 1001"

                        use runCommand = connection.CreateCommand()
                        runCommand.CommandText <- "SELECT COUNT(*) FROM generated_runs WHERE batch_id IN (SELECT id FROM generated_batches WHERE seed_base = $seedBase);"
                        runCommand.Parameters.AddWithValue("$seedBase", "1001") |> ignore
                        let runCount = Convert.ToInt32(runCommand.ExecuteScalar())
                        Expect.equal runCount 4 "generated_runs should contain four rows for seed base 1001"

            Directory.Delete(appRoot, true)

        testCase "Generate rejects second run for same seed and preserves original files"
        <| fun _ ->
            let appRoot = Path.Combine(Path.GetTempPath(), $"tsebt-generate-collision-{Guid.NewGuid():N}")
            Directory.CreateDirectory(appRoot) |> ignore
            let settings = buildSettings appRoot

            let templatesFolder = Path.Combine(appRoot, settings.Paths.Templates)
            Directory.CreateDirectory(templatesFolder) |> ignore
            let templatePath = Path.Combine(templatesFolder, "base.txt")
            File.WriteAllText(templatePath, "seed=__SEED__; phsp=__PHSP_FILE__; out=__OUTPUT_FILE__")

            match Bootstrap.ensureRootFolders settings with
            | Error errorMessage -> failtestf "Failed creating root folders: %s" errorMessage
            | Ok() ->
                match initialize settings with
                | Error errorMessage -> failtestf "Failed initializing sqlite: %s" errorMessage
                | Ok _ ->
                    let request = {
                        SelectedTemplatePaths = [ "base.txt" ]
                        SelectedNodeDigits = [ "1" ]
                        SelectedPhaseSpaceIndexes = [ "01" ]
                    }

                    let first = generate settings "1001" request
                    Expect.isOk first "First generate call should succeed"

                    let generatedFile = Path.Combine(appRoot, "inputs", "1001", "seed10011_phsp01.txt")
                    let before = File.ReadAllText generatedFile

                    let second = generate settings "1001" request
                    Expect.isError second "Second generate call should fail because of collision protection"

                    let after = File.ReadAllText generatedFile
                    Expect.equal after before "Collision failure should not overwrite existing generated file"

            Directory.Delete(appRoot, true)
    ]

let runPlanningTests =
    testList "Run planning" [
        testCase "Parse Slurm job id from sbatch success output"
        <| fun _ ->
            let parsed = parseSlurmJobId "Submitted batch job 12345"
            Expect.equal parsed (Ok "12345") "Should parse numeric job id from sbatch output"

        testCase "Parse Slurm job id fails for invalid output"
        <| fun _ ->
            let parsed = parseSlurmJobId "sbatch: something unexpected"
            Expect.isError parsed "Should fail when sbatch output does not contain job id pattern"

        testCase "Manifest row formatting produces expected TSV columns"
        <| fun _ ->
            let row = {
                TaskId = 1
                NodeName = "node01"
                RunId = "seed10011_phsp01"
                InputFilePath = "/app/inputs/1001/seed10011_phsp01.txt"
                LogFilePath = "/app/runs/1001/seed10011_phsp01.log"
            }

            let line = formatManifestRow row
            Expect.equal line "1\tnode01\tseed10011_phsp01\t/app/inputs/1001/seed10011_phsp01.txt\t/app/runs/1001/seed10011_phsp01.log" "Manifest row should use tab-separated layout"

        testCase "Slurm script includes topas command and log redirection"
        <| fun _ ->
            let settings = {
                buildSettings "/app" with
                    Paths = {
                        Templates = "templates"
                        Inputs = "inputs"
                        Runs = "runs"
                        Outputs = "outputs"
                        Database = "database/app.db"
                        Logs = "logs"
                    }
                    Nodes = [ { Name = "node01"; Digit = "1" } ]
                    PhaseSpaceFiles = [ { Index = "01"; Value = "ps01.IAEAphsp" } ]
            }

            let script = buildSlurmScriptText settings "1001" "/app/runs/1001/run_manifest.tsv" 4
            Expect.stringContains script "\"topas\" \"$INPUT_FILE\" > \"$LOG_FILE\" 2>&1" "Script should execute TOPAS and redirect log output"
            Expect.stringContains script "#SBATCH --job-name=tsebt-1001" "Script should include seed batch job name"
            Expect.stringContains script "#SBATCH --partition=compute" "Script should include configured partition"
            Expect.stringContains script "#SBATCH --array=1-4" "Script should include array task range"
            Expect.stringContains script "SLURM_ARRAY_TASK_ID" "Script should resolve row by task id"
    ]

let runSubmissionTests =
    testList "Run submission" [
        testCase "Run preflight fails when output csv or log collisions exist"
        <| fun _ ->
            let appRoot = Path.Combine(Path.GetTempPath(), $"tsebt-run-preflight-{Guid.NewGuid():N}")
            Directory.CreateDirectory(appRoot) |> ignore
            let settings = buildSettings appRoot

            match Bootstrap.ensureRootFolders settings with
            | Error errorMessage -> failtestf "Failed creating root folders: %s" errorMessage
            | Ok() ->
                match initialize settings with
                | Error errorMessage -> failtestf "Failed initializing sqlite: %s" errorMessage
                | Ok _ ->
                    let dbPath = Path.Combine(appRoot, "database", "app.db")
                    let csb = SqliteConnectionStringBuilder()
                    csb.DataSource <- dbPath
                    use conn = new SqliteConnection(csb.ConnectionString)
                    conn.Open()

                    use batchInsert = conn.CreateCommand()
                    batchInsert.CommandText <- "INSERT INTO generated_batches(seed_base, created_at, run_status) VALUES('1001', '2026-01-01T00:00:00Z', 'Generated');"
                    batchInsert.ExecuteNonQuery() |> ignore

                    let inputFolder = Path.Combine(appRoot, "inputs", "1001")
                    let runFolder = Path.Combine(appRoot, "runs", "1001")
                    Directory.CreateDirectory(inputFolder) |> ignore
                    Directory.CreateDirectory(runFolder) |> ignore

                    let inputPath = Path.Combine(inputFolder, "seed10011_phsp01.txt")
                    let outputBase = Path.Combine(runFolder, "seed10011_phsp01")
                    File.WriteAllText(inputPath, "input")
                    File.WriteAllText(outputBase + ".csv", "csv")
                    File.WriteAllText(outputBase + ".log", "log")

                    use runInsert = conn.CreateCommand()
                    runInsert.CommandText <- "INSERT INTO generated_runs(batch_id, run_id, phase_space_index, phase_space_file, node_name, node_digit, seed, input_file_path, output_file_path, run_folder, status, created_at) VALUES((SELECT id FROM generated_batches WHERE seed_base='1001'), 'seed10011_phsp01', '01', 'ps01.IAEAphsp', 'node01', '1', '10011', $inputPath, $outputBase, $runFolder, 'Generated', '2026-01-01T00:00:00Z');"
                    runInsert.Parameters.AddWithValue("$inputPath", inputPath) |> ignore
                    runInsert.Parameters.AddWithValue("$outputBase", outputBase) |> ignore
                    runInsert.Parameters.AddWithValue("$runFolder", runFolder) |> ignore
                    runInsert.ExecuteNonQuery() |> ignore

                    let preflight = preflightRun settings "1001"
                    Expect.isOk preflight "Preflight should return checks"

                    match preflight with
                    | Error _ -> ()
                    | Ok result ->
                        Expect.isFalse result.CanSubmit "CSV/log collisions should block submission"
                        let csvCheck = result.Checks |> List.find (fun c -> c.Name = "No output CSV collisions")
                        let logCheck = result.Checks |> List.find (fun c -> c.Name = "No log collisions")
                        Expect.isFalse csvCheck.Ok "Existing .csv should fail csv collision check"
                        Expect.isFalse logCheck.Ok "Existing .log should fail log collision check"

            Directory.Delete(appRoot, true)

        testCase "Submit run blocks already submitted batch before sbatch"
        <| fun _ ->
            let appRoot = Path.Combine(Path.GetTempPath(), $"tsebt-run-submitted-{Guid.NewGuid():N}")
            Directory.CreateDirectory(appRoot) |> ignore
            let settings = buildSettings appRoot

            match Bootstrap.ensureRootFolders settings with
            | Error errorMessage -> failtestf "Failed creating root folders: %s" errorMessage
            | Ok() ->
                match initialize settings with
                | Error errorMessage -> failtestf "Failed initializing sqlite: %s" errorMessage
                | Ok _ ->
                    let dbPath = Path.Combine(appRoot, "database", "app.db")
                    let csb = SqliteConnectionStringBuilder()
                    csb.DataSource <- dbPath
                    use conn = new SqliteConnection(csb.ConnectionString)
                    conn.Open()

                    use insert = conn.CreateCommand()
                    insert.CommandText <- "INSERT INTO generated_batches(seed_base, created_at, run_status, slurm_job_id) VALUES('1001', '2026-01-01T00:00:00Z', 'Submitted', '12345');"
                    insert.ExecuteNonQuery() |> ignore

                    let result = submitRun settings { SeedBase = "1001" }
                    Expect.isError result "Already submitted batches should be blocked"

                    match result with
                    | Ok _ -> ()
                    | Error message ->
                        Expect.stringContains message "already been submitted to Slurm as job 12345" "Error should mention existing Slurm job id"

            Directory.Delete(appRoot, true)
    ]

let collectCsvTests =
    testList "Collect csv/statistics" [
        testCase "Collect csv merge sums last numeric dose column across node files"
        <| fun _ ->
            let folder = Path.Combine(Path.GetTempPath(), $"collect-merge-{Guid.NewGuid():N}")
            Directory.CreateDirectory(folder) |> ignore

            let csvA = Path.Combine(folder, "node01.csv")
            let csvB = Path.Combine(folder, "node02.csv")
            let output = Path.Combine(folder, "phsp01_merged.csv")

            File.WriteAllText(csvA, String.concat Environment.NewLine [ "# header"; "x,y,dose"; "0,0,1.0"; "0,1,2.5" ])
            File.WriteAllText(csvB, String.concat Environment.NewLine [ "# header"; "x,y,dose"; "0,0,3.0"; "0,1,4.5" ])

            let merged = mergeNodeCsvFilesForPhaseSpace [ csvA; csvB ] output
            Expect.isOk merged "Merge should succeed for compatible csv files"
            Expect.isTrue (File.Exists output) "Merged csv output file should be written"

            let outputText = File.ReadAllText output
            Expect.stringContains outputText "0,0,4" "First dose value should be summed to 4.0"
            Expect.stringContains outputText "0,1,7" "Second dose value should be summed to 7.0"
            Directory.Delete(folder, true)

        testCase "Collect csv merge fails when row counts do not match"
        <| fun _ ->
            let folder = Path.Combine(Path.GetTempPath(), $"collect-merge-mismatch-{Guid.NewGuid():N}")
            Directory.CreateDirectory(folder) |> ignore

            let csvA = Path.Combine(folder, "node01.csv")
            let csvB = Path.Combine(folder, "node02.csv")
            let output = Path.Combine(folder, "phsp01_merged.csv")

            File.WriteAllText(csvA, String.concat Environment.NewLine [ "x,y,dose"; "0,0,1.0"; "0,1,2.0" ])
            File.WriteAllText(csvB, String.concat Environment.NewLine [ "x,y,dose"; "0,0,1.5" ])

            let merged = mergeNodeCsvFilesForPhaseSpace [ csvA; csvB ] output
            Expect.isError merged "Merge should fail when csv row counts differ"
            Directory.Delete(folder, true)

        testCase "Collect statistics computes mean median standard deviation and count"
        <| fun _ ->
            let folder = Path.Combine(Path.GetTempPath(), $"collect-stats-{Guid.NewGuid():N}")
            Directory.CreateDirectory(folder) |> ignore

            let fileA = Path.Combine(folder, "phsp01_merged.csv")
            let fileB = Path.Combine(folder, "phsp02_merged.csv")
            let fileC = Path.Combine(folder, "phsp03_merged.csv")
            let summary = Path.Combine(folder, "dose_summary.csv")

            File.WriteAllText(fileA, String.concat Environment.NewLine [ "x,y,dose"; "0,0,1.0"; "0,1,2.0" ])
            File.WriteAllText(fileB, String.concat Environment.NewLine [ "x,y,dose"; "0,0,3.0"; "0,1,4.0" ])
            File.WriteAllText(fileC, String.concat Environment.NewLine [ "x,y,dose"; "0,0,5.0"; "0,1,6.0" ])

            let result = computeDoseSummary [ fileA; fileB; fileC ] summary
            Expect.isOk result "Summary computation should succeed for aligned merged files"
            Expect.isTrue (File.Exists summary) "Summary output file should be written"

            let lines = File.ReadAllLines summary
            Expect.equal lines[0] "x,y,mean,median,standard_deviation,count" "Header should include summary columns"
            Expect.stringContains lines[1] ",3,3,2,3" "First voxel should have mean=3 median=3 sd=2 count=3"
            Expect.stringContains lines[2] ",4,4,2,3" "Second voxel should have mean=4 median=4 sd=2 count=3"
            Directory.Delete(folder, true)

        testCase "Collect statistics fails when merged csv row counts do not match"
        <| fun _ ->
            let folder = Path.Combine(Path.GetTempPath(), $"collect-stats-mismatch-{Guid.NewGuid():N}")
            Directory.CreateDirectory(folder) |> ignore

            let fileA = Path.Combine(folder, "phsp01_merged.csv")
            let fileB = Path.Combine(folder, "phsp02_merged.csv")
            let summary = Path.Combine(folder, "dose_summary.csv")

            File.WriteAllText(fileA, String.concat Environment.NewLine [ "x,y,dose"; "0,0,1.0"; "0,1,2.0" ])
            File.WriteAllText(fileB, String.concat Environment.NewLine [ "x,y,dose"; "0,0,3.0" ])

            let result = computeDoseSummary [ fileA; fileB ] summary
            Expect.isError result "Summary computation should fail for mismatched row counts"
            Directory.Delete(folder, true)
    ]

let server =
    testList "Server" [
        testCaseAsync "Topas API config stub returns Ok"
        <| async {
            let api = topasApi null
            let! result = api.getAppConfig ()
            Expect.isOk result "Config stub should succeed"
        }
        configTests
        bootstrapTests
        generatePlanningTests
        generateCollisionTests
        generateFilesystemTests
        runPlanningTests
        runSubmissionTests
        collectCsvTests
    ]

let all = testList "All" [ Shared.Tests.shared; server ]

[<EntryPoint>]
let main _ = runTestsWithCLIArgs [] [||] all
