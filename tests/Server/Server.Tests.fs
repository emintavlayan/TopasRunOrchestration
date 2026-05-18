module Server.Tests

open Expecto

open Shared
open Server
open GenerateOperation
open GeneratePlanning
open TsebtConfig
open SqliteInit
open System
open System.IO

let server =
    testList "Server" [
        testCaseAsync "Topas API config stub returns Ok"
        <| async {
            let api = topasApi null
            let! result = api.getAppConfig ()

            Expect.isOk result "Config stub should succeed"
        }

        testCase "Seed construction appends node digit"
        <| fun _ ->
            Expect.equal (buildSeed "1001" "1") "10011" "Seed should append node digit 1"
            Expect.equal (buildSeed "1001" "7") "10017" "Seed should append node digit 7"

        testCase "RunId construction uses phase-space index and seed"
        <| fun _ -> Expect.equal (buildRunId "01" "10011") "phsp01_seed10011" "RunId should match expected format"

        testCase "Generated input file name follows expected pattern"
        <| fun _ ->
            Expect.equal
                (buildInputFileName "10011" "01" "1")
                "input_sd10011_ps01_n1.txt"
                "Generated filename should match expected format"

        testCase "Run folder and output path planning uses runs/runId/dose"
        <| fun _ ->
            let settings = {
                AppRoot = @"C:\app-root"
                Paths = {
                    Templates = "templates"
                    Inputs = "inputs"
                    Runs = "runs"
                    Database = "database\\app.db"
                    Logs = "logs"
                }
                Placeholders = {
                    PhaseSpaceFile = "__PHSP_FILE__"
                    OutputFile = "__OUTPUT_FILE__"
                    Seed = "__SEED__"
                }
                Seed = { CurrentBase = "1001" }
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
                (normalizedRunFolder.EndsWith("runs/phsp01_seed10011"))
                "Run folder should end with runs/phsp01_seed10011"

            Expect.isTrue
                (normalizedOutputPath.EndsWith("runs/phsp01_seed10011/dose"))
                "Output file path should end with runs/phsp01_seed10011/dose"

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
                    @"C:\runs\phsp01_seed10011\dose"
                    "10011"
                    sourceText

            Expect.equal
                replaced
                "seed=10011; phsp=ps01.IAEAphsp; output=C:\\runs\\phsp01_seed10011\\dose"
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
                    Database = "database\\app.db"
                    Logs = "logs"
                }
                Placeholders = {
                    PhaseSpaceFile = "__PHSP_FILE__"
                    OutputFile = "__OUTPUT_FILE__"
                    Seed = "__SEED__"
                }
                Seed = { CurrentBase = "1001" }
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
                    Database = "database\\app.db"
                    Logs = "logs"
                }
                Placeholders = {
                    PhaseSpaceFile = "__PHSP_FILE__"
                    OutputFile = "__OUTPUT_FILE__"
                    Seed = "__SEED__"
                }
                Seed = { CurrentBase = "1001" }
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

let all = testList "All" [ Shared.Tests.shared; server ]

[<EntryPoint>]
let main _ = runTestsWithCLIArgs [] [||] all
