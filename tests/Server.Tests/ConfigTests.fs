module Server.Tests.ConfigTests

open System
open System.IO
open Xunit
open TsebtConfig
open Server
open Server.Tests.TestHelpers

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
