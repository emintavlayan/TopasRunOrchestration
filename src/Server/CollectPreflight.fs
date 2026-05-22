module CollectPreflight

open System.IO
open Shared
open CollectPlanning
open TsebtConfig

/// Represents computed collect preflight counts and missing file details.
type CollectPreflightComputed = {
    ExpectedRunCount: int
    FoundCsvCount: int
    MissingCsvCount: int
    FoundLogCount: int
    MissingLogCount: int
    MissingFiles: MissingCollectFile list
}

/// Builds a missing collect file descriptor.
let private missingFile (fileKind: string) (path: string) : MissingCollectFile = { FileKind = fileKind; Path = path }

/// Computes collect file presence counts and missing file details.
let computeCollectFilePresence (rows: CollectRunRow list) : CollectPreflightComputed =
    let expectedRunCount = rows.Length

    let folder =
        rows
        |> List.map (fun row ->
            let csvPath, logPath = expectedCsvAndLogPaths row
            let csvExists = File.Exists csvPath
            let logExists = File.Exists logPath

            let csvMissing = if csvExists then None else Some(missingFile "csv" csvPath)
            let logMissing = if logExists then None else Some(missingFile "log" logPath)
            csvExists, logExists, csvMissing, logMissing)

    let foundCsvCount = folder |> List.sumBy (fun (csvExists, _, _, _) -> if csvExists then 1 else 0)
    let foundLogCount = folder |> List.sumBy (fun (_, logExists, _, _) -> if logExists then 1 else 0)

    let missingFiles =
        folder
        |> List.collect (fun (_, _, csvMissing, logMissing) -> [ csvMissing; logMissing ])
        |> List.choose id

    let missingCsvCount = missingFiles |> List.sumBy (fun file -> if file.FileKind = "csv" then 1 else 0)
    let missingLogCount = missingFiles |> List.sumBy (fun file -> if file.FileKind = "log" then 1 else 0)

    {
        ExpectedRunCount = expectedRunCount
        FoundCsvCount = foundCsvCount
        MissingCsvCount = missingCsvCount
        FoundLogCount = foundLogCount
        MissingLogCount = missingLogCount
        MissingFiles = missingFiles
    }

/// Builds structured collect preflight result from generated rows and folder checks.
let buildCollectPreflightResult
    (settings: TsebtSettings)
    (seedBase: string)
    (rows: CollectRunRow list)
    : CollectPreflightResult =
    let runFolder = runFolderPath settings seedBase
    let runFolderExists = Directory.Exists runFolder
    let filePresence = computeCollectFilePresence rows
    let hasGeneratedRuns = rows |> List.isEmpty |> not
    let csvFilesOk = filePresence.MissingCsvCount = 0

    let checks =
        [
            if hasGeneratedRuns then
                passedCheck "Generated runs found"
            else
                failedCheck "Generated runs found" $"No generated runs were found for seed base: {seedBase}"
            if runFolderExists then
                passedCheck "Run folder exists"
            else
                failedCheck "Run folder exists" $"Run folder does not exist: {runFolder}"
            if csvFilesOk then
                passedCheck "Input CSV files exist"
            else
                failedCheck "Input CSV files exist" $"Missing {filePresence.MissingCsvCount} expected csv files."
            if filePresence.MissingLogCount = 0 then
                passedCheck "Log files exist"
            else
                failedCheck "Log files exist" $"Missing {filePresence.MissingLogCount} expected log files."
        ]

    {
        SeedBase = seedBase
        CanCollect = hasGeneratedRuns && runFolderExists && csvFilesOk
        ExpectedRunCount = filePresence.ExpectedRunCount
        FoundCsvCount = filePresence.FoundCsvCount
        MissingCsvCount = filePresence.MissingCsvCount
        FoundLogCount = filePresence.FoundLogCount
        MissingLogCount = filePresence.MissingLogCount
        Checks = checks
        MissingFiles = filePresence.MissingFiles
    }
