module Server.Tests.GenerateTests

open System
open System.IO
open Xunit
open Shared
open Server.Tests.TestHelpers
open GeneratePlanning
open GenerateOperation
open SqliteInit
open Server
open TsebtConfig

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
    let placeholders: TsebtPlaceholders = {
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

        let request: GenerateRequest = {
            SelectedTemplatePaths = [ "a.txt"; "b.txt" ]
            SelectedNodeDigits = [ "1"; "2" ]
            SelectedPhaseSpaceIndexes = [ "01"; "02" ]
        }

        let generated = assertOk (generate settings "1001" request)
        Assert.Equal(4, generated.GeneratedInputCount)
        Assert.True(Directory.Exists(Path.Combine(appRoot, "inputs", "1001")))
    finally
        cleanupTestDirectory appRoot
