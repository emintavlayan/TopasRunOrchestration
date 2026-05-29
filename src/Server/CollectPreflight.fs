module CollectPreflight

open System
open System.IO
open Shared
open CollectPlanning
open TsebtConfig

/// Builds one missing-file compatibility descriptor.
let private missingFile (fileKind: string) (path: string) : MissingCollectFile = { FileKind = fileKind; Path = path }

/// Returns true when one log line contains a fatal TOPAS signature.
let private isFatalLogLine (line: string) : bool =
    [
        "TOPAS is quitting due to a serious error"
        "does not support particle ID"
        "Segmentation fault"
        "Aborted"
        "Exception"
    ]
    |> List.exists (fun token -> line.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)

/// Reads one log file and returns the first fatal line when present.
let private tryFindFatalLogLine (path: string) : Result<string option, string> =
    try
        use reader = new StreamReader(path)
        let mutable fatal: string option = None
        let mutable searching = true

        while searching && not reader.EndOfStream do
            let line = reader.ReadLine()

            if not (isNull line) && isFatalLogLine line then
                fatal <- Some line
                searching <- false

        Ok fatal
    with ex ->
        Error ex.Message

/// Builds one collect file issue row.
let private collectFileIssue
    (row: CollectRunRow)
    (fileKind: string)
    (path: string)
    (problem: string)
    (message: string option)
    : CollectFileIssue =
    {
        RunId = row.RunId
        PhaseSpaceIndex = row.PhaseSpaceIndex
        NodeDigit = row.NodeDigit
        FileKind = fileKind
        Path = path
        Problem = problem
        Message = message
    }

/// Collects file issues for one generated run row.
let private collectRowFileIssues (row: CollectRunRow) : CollectFileIssue list =
    let csvPath, logPath = expectedCsvAndLogPaths row
    let csvIssues =
        if not (File.Exists csvPath) then
            [ collectFileIssue row "csv" csvPath "Missing" None ]
        else
            let info = FileInfo(csvPath)

            if info.Length <= 0L then
                [ collectFileIssue row "csv" csvPath "Empty" None ]
            else
                []

    let logIssues =
        if not (File.Exists logPath) then
            [ collectFileIssue row "log" logPath "Missing" None ]
        else
            match tryFindFatalLogLine logPath with
            | Error message -> [ collectFileIssue row "log" logPath "ReadError" (Some message) ]
            | Ok(Some fatalLine) -> [ collectFileIssue row "log" logPath "FatalContent" (Some fatalLine) ]
            | Ok None -> []

    csvIssues @ logIssues

/// Returns true when one issue belongs to a csv file.
let private isCsvIssue (issue: CollectFileIssue) : bool =
    String.Equals(issue.FileKind, "csv", StringComparison.OrdinalIgnoreCase)

/// Returns true when one issue belongs to a log file.
let private isLogIssue (issue: CollectFileIssue) : bool =
    String.Equals(issue.FileKind, "log", StringComparison.OrdinalIgnoreCase)

/// Applies requested phase-space and node exclusions to collect rows.
let applyCollectExclusions
    (rows: CollectRunRow list)
    (excludedPhaseSpaceIndexes: string list)
    (excludedNodeDigits: string list)
    : CollectRunRow list =
    let excludedPhspSet = excludedPhaseSpaceIndexes |> Set.ofList
    let excludedNodeSet = excludedNodeDigits |> Set.ofList

    rows
    |> List.filter (fun row ->
        not (excludedPhspSet.Contains row.PhaseSpaceIndex)
        && not (excludedNodeSet.Contains row.NodeDigit))

/// Builds one failed preflight check.
let private failedCheck (name: string) (message: string) : CollectPreflightCheck =
    {
        Name = name
        Ok = false
        Message = Some message
    }

/// Builds one passed preflight check.
let private passedCheck (name: string) : CollectPreflightCheck =
    {
        Name = name
        Ok = true
        Message = None
    }

/// Verifies that remaining collect rows have a balanced node set per phase-space index.
let private validateBalancedRows (rows: CollectRunRow list) : Result<unit, string> =
    if rows.IsEmpty then
        Error "No rows remain after exclusions."
    else
        let grouped =
            rows
            |> List.groupBy _.PhaseSpaceIndex
            |> List.map (fun (phaseSpaceIndex, phaseRows) ->
                phaseSpaceIndex, phaseRows |> List.map _.NodeDigit |> Set.ofList)

        let _, firstNodeSet = grouped.Head

        if grouped |> List.exists (fun (_, nodeSet) -> nodeSet <> firstNodeSet) then
            Error "Remaining collect set is unbalanced. Exclude a full phase-space or full node."
        else
            Ok()

/// Builds structured collect preflight result from generated rows, exclusions, and file checks.
let buildCollectPreflightResult
    (settings: TsebtSettings)
    (seedBase: string)
    (rows: CollectRunRow list)
    (excludedPhaseSpaceIndexes: string list)
    (excludedNodeDigits: string list)
    : CollectPreflightResult =
    let runFolder = runFolderPath settings seedBase
    let runFolderExists = Directory.Exists runFolder
    let hasGeneratedRuns = not rows.IsEmpty
    let fileIssues = rows |> List.collect collectRowFileIssues
    let effectiveRows = applyCollectExclusions rows excludedPhaseSpaceIndexes excludedNodeDigits
    let effectiveRunCount = effectiveRows.Length
    let effectivePhaseSpaceCount = effectiveRows |> List.map _.PhaseSpaceIndex |> List.distinct |> List.length
    let effectiveNodeCount = effectiveRows |> List.map _.NodeDigit |> List.distinct |> List.length

    let effectiveRowIds = effectiveRows |> List.map _.RunId |> Set.ofList

    let effectiveFileIssues =
        fileIssues |> List.filter (fun issue -> effectiveRowIds.Contains issue.RunId)

    let foundCsvCount = effectiveRunCount - (effectiveFileIssues |> List.filter isCsvIssue |> List.length)
    let missingCsvCount = effectiveFileIssues |> List.filter isCsvIssue |> List.length
    let foundLogCount = effectiveRunCount - (effectiveFileIssues |> List.filter isLogIssue |> List.length)
    let missingLogCount = effectiveFileIssues |> List.filter isLogIssue |> List.length

    let balanceResult = validateBalancedRows effectiveRows
    let balanceOk = Result.isOk balanceResult

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

            if effectiveRunCount > 0 then
                passedCheck "Rows remain after exclusions"
            else
                failedCheck "Rows remain after exclusions" "No rows remain after exclusions."

            if missingCsvCount = 0 then
                passedCheck "Input CSV files exist and are non-empty"
            else
                failedCheck "Input CSV files exist and are non-empty" $"Detected {missingCsvCount} csv file issues."

            if missingLogCount = 0 then
                passedCheck "Log files exist and have no fatal errors"
            else
                failedCheck "Log files exist and have no fatal errors" $"Detected {missingLogCount} log file issues."

            match balanceResult with
            | Ok() -> passedCheck "Remaining rows are balanced"
            | Error message -> failedCheck "Remaining rows are balanced" message
        ]

    let missingFiles =
        effectiveFileIssues
        |> List.map (fun issue -> missingFile issue.FileKind issue.Path)

    {
        SeedBase = seedBase
        CanCollect =
            hasGeneratedRuns
            && runFolderExists
            && effectiveRunCount > 0
            && missingCsvCount = 0
            && missingLogCount = 0
            && balanceOk
        ExpectedRunCount = rows.Length
        EffectiveRunCount = effectiveRunCount
        EffectivePhaseSpaceCount = effectivePhaseSpaceCount
        EffectiveNodeCount = effectiveNodeCount
        FoundCsvCount = foundCsvCount
        MissingCsvCount = missingCsvCount
        FoundLogCount = foundLogCount
        MissingLogCount = missingLogCount
        ExcludedPhaseSpaceIndexes = excludedPhaseSpaceIndexes
        ExcludedNodeDigits = excludedNodeDigits
        Checks = checks
        MissingFiles = missingFiles
        FileIssues = fileIssues
    }
