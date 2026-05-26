module Server.Tests.RunTests

open System
open System.IO
open Microsoft.Data.Sqlite
open Xunit
open Shared
open Server.Tests.TestHelpers
open RunOperation
open SqliteInit
open Server

/// Asserts that an error Result contains expected text.
let private assertErrorContains (expected: string) result =
    let message: string = assertError result
    Assert.Contains(expected, message)

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
