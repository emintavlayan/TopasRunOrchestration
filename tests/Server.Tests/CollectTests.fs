module Server.Tests.CollectTests

open System
open System.Globalization
open System.IO
open Microsoft.Data.Sqlite
open Xunit
open Server.Tests.TestHelpers
open CollectOperation
open CollectPreflight
open CollectCsvMerge
open CollectStatistics
open SqliteInit
open Server

[<Fact>]
let ``Collect preflight blocks missing csv and missing log`` () =
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
        Assert.False(missingLog.CanCollect)
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
let ``Collect preflight detects empty csv and fatal log`` () =
    let appRoot = Path.Combine(Path.GetTempPath(), $"xunit-collect-preflight-fatal-{Guid.NewGuid():N}")
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
        File.WriteAllText(outputBase + ".csv", "")
        File.WriteAllText(outputBase + ".log", "TOPAS is quitting due to a serious error")

        let dbPath = Path.Combine(appRoot, "database", "app.db")
        let csb = SqliteConnectionStringBuilder()
        csb.DataSource <- dbPath
        use conn = new SqliteConnection(csb.ConnectionString)
        conn.Open()
        seedGeneratedBatch conn "1001" [ ("seed10011_phsp01", "01", "1", inputPath, outputBase) ]

        let preflight = assertOk (preflightCollect settings "1001")
        Assert.False(preflight.CanCollect)
        Assert.True(preflight.FileIssues |> List.exists (fun issue -> issue.Problem = "Empty"))
        Assert.True(preflight.FileIssues |> List.exists (fun issue -> issue.Problem = "FatalContent"))
    finally
        cleanupTestDirectory appRoot

[<Fact>]
let ``Collect preview supports excluding failed phase-space or node`` () =
    let appRoot = Path.Combine(Path.GetTempPath(), $"xunit-collect-preview-exclude-{Guid.NewGuid():N}")
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
            ("seed10013_phsp20", "20", "1", Path.Combine(inputFolder, "seed10013_phsp20.txt"), Path.Combine(runFolder, "seed10013_phsp20"))
            ("seed10014_phsp20", "20", "2", Path.Combine(inputFolder, "seed10014_phsp20.txt"), Path.Combine(runFolder, "seed10014_phsp20"))
        ]

        let dbPath = Path.Combine(appRoot, "database", "app.db")
        let csb = SqliteConnectionStringBuilder()
        csb.DataSource <- dbPath
        use conn = new SqliteConnection(csb.ConnectionString)
        conn.Open()
        seedGeneratedBatch conn "1001" rows

        for (_, _, _, inputPath, _) in rows do
            File.WriteAllText(inputPath, "input")

        File.WriteAllText(Path.Combine(runFolder, "seed10011_phsp01.csv"), "x,y,dose\n0,0,1")
        File.WriteAllText(Path.Combine(runFolder, "seed10011_phsp01.log"), "ok")
        File.WriteAllText(Path.Combine(runFolder, "seed10012_phsp01.csv"), "x,y,dose\n0,0,2")
        File.WriteAllText(Path.Combine(runFolder, "seed10012_phsp01.log"), "ok")
        File.WriteAllText(Path.Combine(runFolder, "seed10013_phsp20.csv"), "")
        File.WriteAllText(Path.Combine(runFolder, "seed10013_phsp20.log"), "does not support particle ID")
        File.WriteAllText(Path.Combine(runFolder, "seed10014_phsp20.csv"), "x,y,dose\n0,0,4")
        File.WriteAllText(Path.Combine(runFolder, "seed10014_phsp20.log"), "ok")

        let strictPreview =
            assertOk (
                previewCollect
                    settings
                    {
                        SeedBase = "1001"
                        ExcludedPhaseSpaceIndexes = []
                        ExcludedNodeDigits = []
                    }
            )
        Assert.False(strictPreview.Preflight.CanCollect)
        Assert.True(strictPreview.Preflight.FileIssues |> List.exists (fun issue -> issue.PhaseSpaceIndex = "20"))

        let excludedPhspPreview =
            assertOk (
                previewCollect
                    settings
                    {
                        SeedBase = "1001"
                        ExcludedPhaseSpaceIndexes = [ "20" ]
                        ExcludedNodeDigits = []
                    }
            )
        Assert.True(excludedPhspPreview.Preflight.CanCollect)
        Assert.Equal(2, excludedPhspPreview.Preflight.EffectiveRunCount)
        Assert.Equal(1, excludedPhspPreview.Preflight.EffectivePhaseSpaceCount)
        Assert.Equal(2, excludedPhspPreview.Preflight.EffectiveNodeCount)

        let excludedNodePreview =
            assertOk (
                previewCollect
                    settings
                    {
                        SeedBase = "1001"
                        ExcludedPhaseSpaceIndexes = []
                        ExcludedNodeDigits = [ "1" ]
                    }
            )
        Assert.True(excludedNodePreview.Preflight.CanCollect)
        Assert.Equal(2, excludedNodePreview.Preflight.EffectiveRunCount)
        Assert.Equal(2, excludedNodePreview.Preflight.EffectivePhaseSpaceCount)
        Assert.Equal(1, excludedNodePreview.Preflight.EffectiveNodeCount)
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
        File.WriteAllText(a, "# TOPAS Version...\n# DoseToMedium ( Gy ) : Sum\n0,0,0,1\n0,1,0,2")
        File.WriteAllText(b, "# TOPAS Version...\n# DoseToMedium ( Gy ) : Sum\n0,0,0,3\n0,1,0,4")
        assertOk (mergeNodeCsvFilesForPhaseSpace [ a; b ] output) |> ignore
        let lines = File.ReadAllLines(output)
        Assert.Equal("x,y,z,dose_sum_Gy,dose_mean_node_Gy,dose_sd_node_Gy,dose_sem_node_Gy,dose_rel_sem_node_percent,node_count", lines[0])

        let row = lines[1].Split(',')
        Assert.Equal("0", row[0])
        Assert.Equal("0", row[1])
        Assert.Equal("0", row[2])
        Assert.Equal(4.0, Double.Parse(row[3], CultureInfo.InvariantCulture))
        Assert.Equal(2.0, Double.Parse(row[4], CultureInfo.InvariantCulture))
        Assert.Equal(1.4142135623730951, Double.Parse(row[5], CultureInfo.InvariantCulture), 12)
        Assert.Equal(1.0, Double.Parse(row[6], CultureInfo.InvariantCulture), 12)
        Assert.Equal(50.0, Double.Parse(row[7], CultureInfo.InvariantCulture), 12)
        Assert.Equal("2", row[8])
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
        File.WriteAllText(a, "x,y,z,dose_sum_Gy\n0,0,0,1\n0,1,0,2")
        File.WriteAllText(b, "x,y,z,dose_sum_Gy\n0,0,0,3\n0,1,0,4")
        File.WriteAllText(c, "x,y,z,dose_sum_Gy\n0,0,0,5\n0,1,0,6")
        assertOk (computeDoseSummary [ a; b; c ] summary) |> ignore
        let lines = File.ReadAllLines(summary)
        Assert.Equal("x,y,z,total_dose_sum_Gy,phsp_mean_Gy,phsp_median_Gy,phsp_sd_Gy,phsp_sem_Gy,phsp_rel_sem_percent,phsp_count", lines[0])

        let row = lines[1].Split(',')
        Assert.Equal("0", row[0])
        Assert.Equal("0", row[1])
        Assert.Equal("0", row[2])
        Assert.Equal(9.0, Double.Parse(row[3], CultureInfo.InvariantCulture), 12)
        Assert.Equal(3.0, Double.Parse(row[4], CultureInfo.InvariantCulture), 12)
        Assert.Equal(3.0, Double.Parse(row[5], CultureInfo.InvariantCulture), 12)
        Assert.Equal(2.0, Double.Parse(row[6], CultureInfo.InvariantCulture), 12)
        Assert.Equal(1.1547005383792515, Double.Parse(row[7], CultureInfo.InvariantCulture), 12)
        Assert.Equal(38.490017945975058, Double.Parse(row[8], CultureInfo.InvariantCulture), 12)
        Assert.Equal("3", row[9])
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
        File.WriteAllText(a, "x,y,z,dose_sum_Gy\n0,0,0,1\n0,1,0,2")
        File.WriteAllText(b, "x,y,z,dose_sum_Gy\n0,0,0,3")
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
        File.WriteAllText(Path.Combine(runFolder, "seed10011_phsp01.csv"), "# TOPAS Version...\n# DoseToMedium ( Gy ) : Sum\n0,0,0,1")
        File.WriteAllText(Path.Combine(runFolder, "seed10012_phsp01.csv"), "# TOPAS Version...\n# DoseToMedium ( Gy ) : Sum\n0,0,0,2")
        File.WriteAllText(Path.Combine(runFolder, "seed10011_phsp01.log"), "ok")
        File.WriteAllText(Path.Combine(runFolder, "seed10012_phsp01.log"), "ok")

        let result =
            assertOk (
                collectBatch
                    settings
                    {
                        SeedBase = "1001"
                        ExcludedPhaseSpaceIndexes = []
                        ExcludedNodeDigits = []
                    }
            )
        Assert.Equal("Collected", result.Status)
        let outputFolder = Path.Combine(appRoot, "outputs", "1001")
        Assert.True(File.Exists(Path.Combine(outputFolder, "collect_manifest.tsv")))
        Assert.True(File.Exists(Path.Combine(outputFolder, "merged", "phsp01_merged.csv")))
        Assert.True(File.Exists(Path.Combine(outputFolder, "dose_summary.csv")))
        let mergedHeader = File.ReadLines(Path.Combine(outputFolder, "merged", "phsp01_merged.csv")) |> Seq.head
        let summaryHeader = File.ReadLines(Path.Combine(outputFolder, "dose_summary.csv")) |> Seq.head
        Assert.Equal("x,y,z,dose_sum_Gy,dose_mean_node_Gy,dose_sd_node_Gy,dose_sem_node_Gy,dose_rel_sem_node_percent,node_count", mergedHeader)
        Assert.Equal("x,y,z,total_dose_sum_Gy,phsp_mean_Gy,phsp_median_Gy,phsp_sd_Gy,phsp_sem_Gy,phsp_rel_sem_percent,phsp_count", summaryHeader)

        use statusCommand = conn.CreateCommand()
        statusCommand.CommandText <- "SELECT collect_status FROM generated_batches WHERE seed_base = '1001';"
        let status = string (statusCommand.ExecuteScalar())
        Assert.Equal("Collected", status)
    finally
        cleanupTestDirectory appRoot
