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

let configTests =
    testList "Config" [
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

        testCase "Seed construction appends node digit"
        <| fun _ ->
            Expect.equal (buildSeed "1001" "1") "10011" "Seed should append node digit 1"
            Expect.equal (buildSeed "1001" "7") "10017" "Seed should append node digit 7"

        testCase "RunId construction uses phase-space index and seed"
        <| fun _ -> Expect.equal (buildRunId "01" "10011") "seed10011_phsp01" "RunId should match expected format"

        testCase "Generated input file name follows expected pattern"
        <| fun _ ->
            Expect.equal
                (buildInputFileName "10011" "01")
                "seed10011_phsp01.txt"
                "Generated filename should match expected format"

        testCase "Run folder and output path planning uses runs/seedBase/seed_phsp"
        <| fun _ ->
            let settings = {
                AppRoot = @"C:\app-root"
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
                Nodes = [ { Name = "node01"; Digit = "1" } ]
                PhaseSpaceFiles = [
                    {
                        Index = "01"
                        Value = "ps01.IAEAphsp"
                    }
                ]
            }

            let planned =
                planGeneratedRuns settings "1001" "template" settings.Nodes settings.PhaseSpaceFiles

            let firstRun = planned |> List.head
            let normalizedRunFolder = firstRun.RunFolder.Replace('\\', '/')
            let normalizedOutputPath = firstRun.OutputFilePath.Replace('\\', '/')

            Expect.isTrue
                (normalizedRunFolder.EndsWith("runs/1001"))
                "Run folder should end with runs/1001"

            Expect.isTrue
                (normalizedOutputPath.EndsWith("runs/1001/seed10011_phsp01"))
                "Output file path should end with runs/1001/seed10011_phsp01"

        testCase "Placeholder replacement applies all configured tokens"
        <| fun _ ->
            let placeholders = {
                PhaseSpaceFile = "__PHSP_FILE__"
                OutputFile = "__OUTPUT_FILE__"
                Seed = "__SEED__"
            }

            let sourceText = "seed=__SEED__; phsp=__PHSP_FILE__; output=__OUTPUT_FILE__"

            let replaced =
                applyConfiguredPlaceholders
                    placeholders
                    "ps01.IAEAphsp"
                    @"C:\runs\1001\seed10011_phsp01"
                    "10011"
                    sourceText

            Expect.equal
                replaced
                "seed=10011; phsp=ps01.IAEAphsp; output=C:\\runs\\1001\\seed10011_phsp01"
                "All placeholders should be replaced"

        testCase "Stitched text keeps input ordering"
        <| fun _ ->
            let stitched = stitchTemplateTexts [ "A"; "B"; "C" ]
            let indexA = stitched.IndexOf("A")
            let indexB = stitched.IndexOf("B")
            let indexC = stitched.IndexOf("C")
            Expect.isTrue (indexA < indexB && indexB < indexC) "A should appear before B, and B before C"

        testCase "Planning expands nodes and phase-space files deterministically"
        <| fun _ ->
            let settings = {
                AppRoot = @"C:\app-root"
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
                    { Name = "node03"; Digit = "3" }
                ]
                PhaseSpaceFiles = [
                    {
                        Index = "01"
                        Value = "ps01.IAEAphsp"
                    }
                    {
                        Index = "02"
                        Value = "ps02.IAEAphsp"
                    }
                ]
            }

            let selectedNodes = settings.Nodes |> List.take 2
            let selectedPhaseSpaces = settings.PhaseSpaceFiles

            let planned =
                planGeneratedRuns settings "1001" "template" selectedNodes selectedPhaseSpaces

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
            Expect.equal
                (buildInputFileName "10011" "01")
                "seed10011_phsp01.txt"
                "Generated filename should match expected format"

        testCase "Run folder and output path planning uses runs/seedBase/seed_phsp"
        <| fun _ ->
            let settings = {
                AppRoot = @"C:\app-root"
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
                Nodes = [ { Name = "node01"; Digit = "1" } ]
                PhaseSpaceFiles = [
                    {
                        Index = "01"
                        Value = "ps01.IAEAphsp"
                    }
                ]
            }

            let planned =
                planGeneratedRuns settings "1001" "template" settings.Nodes settings.PhaseSpaceFiles

            let firstRun = planned |> List.head
            let normalizedRunFolder = firstRun.RunFolder.Replace('\\', '/')
            let normalizedOutputPath = firstRun.OutputFilePath.Replace('\\', '/')

            Expect.isTrue
                (normalizedRunFolder.EndsWith("runs/1001"))
                "Run folder should end with runs/1001"

            Expect.isTrue
                (normalizedOutputPath.EndsWith("runs/1001/seed10011_phsp01"))
                "Output file path should end with runs/1001/seed10011_phsp01"

        testCase "Placeholder replacement applies all configured tokens"
        <| fun _ ->
            let placeholders = {
                PhaseSpaceFile = "__PHSP_FILE__"
                OutputFile = "__OUTPUT_FILE__"
                Seed = "__SEED__"
            }

            let sourceText = "seed=__SEED__; phsp=__PHSP_FILE__; output=__OUTPUT_FILE__"

            let replaced =
                applyConfiguredPlaceholders
                    placeholders
                    "ps01.IAEAphsp"
                    @"C:\runs\1001\seed10011_phsp01"
                    "10011"
                    sourceText

            Expect.equal
                replaced
                "seed=10011; phsp=ps01.IAEAphsp; output=C:\\runs\\1001\\seed10011_phsp01"
                "All placeholders should be replaced"

        testCase "Stitched text keeps input ordering"
        <| fun _ ->
            let stitched = stitchTemplateTexts [ "A"; "B"; "C" ]
            let indexA = stitched.IndexOf("A")
            let indexB = stitched.IndexOf("B")
            let indexC = stitched.IndexOf("C")
            Expect.isTrue (indexA < indexB && indexB < indexC) "A should appear before B, and B before C"

        testCase "Planning expands nodes and phase-space files deterministically"
        <| fun _ ->
            let settings = {
                AppRoot = @"C:\app-root"
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
                    { Name = "node03"; Digit = "3" }
                ]
                PhaseSpaceFiles = [
                    {
                        Index = "01"
                        Value = "ps01.IAEAphsp"
                    }
                    {
                        Index = "02"
                        Value = "ps02.IAEAphsp"
                    }
                ]
            }

            let selectedNodes = settings.Nodes |> List.take 2
            let selectedPhaseSpaces = settings.PhaseSpaceFiles

            let planned =
                planGeneratedRuns settings "1001" "template" selectedNodes selectedPhaseSpaces

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
            let appRoot =
                Path.Combine(Path.GetTempPath(), $"tsebt-collision-{Guid.NewGuid():N}")

            Directory.CreateDirectory(appRoot) |> ignore

            let settings = {
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
                Nodes = [ { Name = "node01"; Digit = "1" } ]
                PhaseSpaceFiles = [
                    {
                        Index = "01"
                        Value = "ps01.IAEAphsp"
                    }
                ]
            }

            match Bootstrap.ensureRootFolders settings with
            | Error errorMessage -> failtestf "Failed creating root folders: %s" errorMessage
            | Ok() ->
                match initialize settings with
                | Error errorMessage -> failtestf "Failed initializing sqlite: %s" errorMessage
                | Ok _ ->
                    let usedSeedBase = "1001"
                    let existingInputFolder = Path.Combine(appRoot, "inputs", usedSeedBase)
                    File.WriteAllText(Path.Combine(existingInputFolder, "already-there.txt"), "existing")

                    let beforeFileCount =
                        Directory.GetFiles(existingInputFolder, "*", SearchOption.AllDirectories).Length

                    let plannedRuns =
                        planGeneratedRuns settings usedSeedBase "template" settings.Nodes settings.PhaseSpaceFiles

                    let result = preflightGenerateCollisions settings usedSeedBase plannedRuns
                    Expect.isError result "Preflight should fail when seed input folder already contains files"

                    let afterFileCount =
                        Directory.GetFiles(existingInputFolder, "*", SearchOption.AllDirectories).Length

                    Expect.equal afterFileCount beforeFileCount "Preflight should not write any new files"

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
                AppRoot = "/app"
                Paths = {
                    Templates = "templates"
                    Inputs = "inputs"
                    Runs = "runs"
                    Outputs = "outputs"
                    Database = "database/app.db"
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
                Nodes = [ { Name = "node01"; Digit = "1" } ]
                PhaseSpaceFiles = [ { Index = "01"; Value = "ps01.IAEAphsp" } ]
            }

            let script =
                buildSlurmScriptText settings "1001" "/app/runs/1001/run_manifest.tsv" 4

            Expect.stringContains script "\"topas\" \"$INPUT_FILE\" > \"$LOG_FILE\" 2>&1" "Script should execute TOPAS and redirect log output"

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

            File.WriteAllText(
                csvA,
                String.concat
                    Environment.NewLine
                    [ "# header"
                      "x,y,dose"
                      "0,0,1.0"
                      "0,1,2.5" ]
            )

            File.WriteAllText(
                csvB,
                String.concat
                    Environment.NewLine
                    [ "# header"
                      "x,y,dose"
                      "0,0,3.0"
                      "0,1,4.5" ]
            )

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
        generatePlanningTests
        generateCollisionTests
        runPlanningTests
        collectCsvTests
    ]

let all = testList "All" [ Shared.Tests.shared; server ]

[<EntryPoint>]
let main _ = runTestsWithCLIArgs [] [||] all
