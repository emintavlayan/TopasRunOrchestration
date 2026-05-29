module CollectCsvMerge

open System
open System.Globalization
open System.IO
open FsToolkit.ErrorHandling

/// Represents parsed csv content used for safe phase-space merge.
type ParsedMergeCsv = {
    HeaderLines: string list
    DataRows: string array list
    DoseColumnIndex: int
    DoseColumnName: string option
}

/// Splits one csv line into comma-separated columns.
let private splitCsvLine (line: string) : string array = line.Split(',')

/// Returns one UTC timestamp string used by collect csv merge logs.
let private mergeLogTimestampUtc () : string = DateTime.UtcNow.ToString("O")

/// Writes one collect csv merge stage log line to stdout.
let private logCollectMergeStage (stage: string) (message: string) : unit =
    Console.WriteLine($"[{mergeLogTimestampUtc()}] [CollectCsvMerge] [{stage}] {message}")

/// Returns true when the value can be parsed as a floating-point number.
let private tryParseFloat (value: string) : bool =
    let mutable parsed = 0.0
    Double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, &parsed)

/// Finds the last numeric column index in one row.
let private lastNumericColumnIndex (columns: string array) : int option =
    columns
    |> Array.mapi (fun index value -> index, value)
    |> Array.choose (fun (index, value) -> if tryParseFloat value then Some index else None)
    |> Array.tryLast

/// Returns normalized header token for case-insensitive matching.
let private normalizeHeaderToken (value: string) : string =
    value.Trim().ToLowerInvariant()

/// Returns preferred dose header names in strict priority order.
let private preferredDoseHeaderNames: string list =
    [
        "dose_sum_gy"
        "dose_gy"
        "dose"
        "dose"
        "dosetomedium"
        "dose_to_medium"
        "dose-to-medium"
        "medium dose"
    ]

/// Tries to resolve dose column from header columns using preferred names.
let private tryResolveDoseColumnFromHeader (headerColumns: string array) : (int * string) option =
    let normalizedColumns =
        headerColumns
        |> Array.mapi (fun index name -> index, name, normalizeHeaderToken name)

    preferredDoseHeaderNames
    |> List.tryPick (fun preferred ->
        normalizedColumns
        |> Array.tryFind (fun (_, _, normalized) -> normalized = preferred)
        |> Option.map (fun (index, originalName, _) -> index, originalName))

/// Returns true when selected dose column is numeric across all data rows.
let private isDoseColumnNumericAcrossRows (doseColumnIndex: int) (rows: string array list) : bool =
    rows
    |> List.forall (fun row -> doseColumnIndex < row.Length && tryParseFloat row[doseColumnIndex])

/// Appends one parsed csv line into header or data buffers.
let private appendParsedLine
    (headerLines: ResizeArray<string>)
    (dataRows: ResizeArray<string array>)
    (line: string)
    : unit =
    let columns = splitCsvLine line

    match lastNumericColumnIndex columns with
    | Some _ -> dataRows.Add columns
    | None -> headerLines.Add line

/// Parses csv text into header lines, data rows, and dose column index.
let parseCsvForMerge (csvText: string) : Result<ParsedMergeCsv, string> =
    let lines =
        csvText.Replace("\r\n", "\n").Split('\n', StringSplitOptions.None)
        |> Array.filter (fun line -> not (String.IsNullOrWhiteSpace line))

    let headerBuffer = ResizeArray<string>()
    let dataBuffer = ResizeArray<string array>()
    lines |> Array.iter (appendParsedLine headerBuffer dataBuffer)

    let headerLines = headerBuffer |> Seq.toList
    let dataRows = dataBuffer |> Seq.toList

    match dataRows with
    | [] -> Error "CSV did not contain any numeric data rows."
    | firstRow :: _ ->
        let expectedColumnCount = firstRow.Length

        if dataRows |> List.exists (fun row -> row.Length <> expectedColumnCount) then
            Error "CSV column count mismatch in one or more data rows."
        else
            let usableHeaderColumns =
                match headerLines |> List.tryLast with
                | Some lastHeaderLine ->
                    let columns = splitCsvLine lastHeaderLine

                    if columns.Length = expectedColumnCount then Some columns else None
                | None -> None

            match usableHeaderColumns with
            | Some headerColumns ->
                match tryResolveDoseColumnFromHeader headerColumns with
                | Some(doseColumnIndex, doseColumnName) ->
                    if isDoseColumnNumericAcrossRows doseColumnIndex dataRows then
                        Ok
                            {
                                HeaderLines = headerLines
                                DataRows = dataRows
                                DoseColumnIndex = doseColumnIndex
                                DoseColumnName = Some doseColumnName
                            }
                    else
                        Error $"Resolved dose column '{doseColumnName}' is not numeric across all rows."
                | None ->
                    let headerText = headerColumns |> Array.map _.Trim() |> String.concat ", "
                    Error $"Unable to locate dose column from header. Header columns: {headerText}"
            | None ->
                // Fallback: when no usable header exists, keep legacy behavior and use the last numeric column.
                match lastNumericColumnIndex firstRow with
                | Some doseColumnIndex ->
                    if isDoseColumnNumericAcrossRows doseColumnIndex dataRows then
                        Ok
                            {
                                HeaderLines = headerLines
                                DataRows = dataRows
                                DoseColumnIndex = doseColumnIndex
                                DoseColumnName = None
                            }
                    else
                        Error "Fallback dose column is not numeric across all rows."
                | None -> Error "CSV dose column could not be determined."

/// Validates that parsed csv rows align with the first parsed file structure.
let private validateSameShape
    (expectedRowCount: int)
    (expectedColumnCount: int)
    (candidateRows: string array list)
    : Result<unit, string> =
    if candidateRows.Length <> expectedRowCount then
        Error $"CSV row count mismatch. Expected {expectedRowCount}, got {candidateRows.Length}."
    elif candidateRows |> List.exists (fun row -> row.Length <> expectedColumnCount) then
        Error "CSV column count mismatch in one or more data rows."
    else
        Ok()

/// Represents node-level dose statistics for one merged voxel row.
type private NodeDoseStats = {
    Sum: float
    Mean: float
    StandardDeviation: float
    StandardError: float
    RelativeStandardErrorPercent: float
    Count: int
}

/// Parses one dose cell value using invariant culture.
let private parseDoseCell (value: string) : Result<float, string> =
    let mutable parsed = 0.0

    if Double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, &parsed) then
        Ok parsed
    else
        Error $"Failed parsing dose value '{value}'."

/// Returns sample standard deviation for node dose values.
let private sampleStandardDeviation (values: float list) (mean: float) : float =
    match values with
    | []
    | [ _ ] -> 0.0
    | _ ->
        let sumSquares = values |> List.sumBy (fun value -> pown (value - mean) 2)
        sqrt (sumSquares / float (values.Length - 1))

/// Returns zero when a value is NaN or infinity.
let private finiteOrZero (value: float) : float =
    if Double.IsNaN value || Double.IsInfinity value then 0.0 else value

/// Computes node-level dose statistics for one merged voxel row.
let private computeNodeDoseStats (doseValues: float list) : NodeDoseStats =
    let count = doseValues.Length
    let sumDose = doseValues |> List.sum
    let meanDose = if count = 0 then 0.0 else sumDose / float count
    let sdDose = sampleStandardDeviation doseValues meanDose
    let semDose = if count < 2 then 0.0 else sdDose / sqrt (float count)

    let relSemPercent =
        if meanDose = 0.0 then
            0.0
        else
            100.0 * semDose / abs meanDose

    {
        Sum = finiteOrZero sumDose
        Mean = finiteOrZero meanDose
        StandardDeviation = finiteOrZero sdDose
        StandardError = finiteOrZero semDose
        RelativeStandardErrorPercent = finiteOrZero relSemPercent
        Count = count
    }

/// Formats one floating-point value using invariant culture.
let private formatFloat (value: float) : string =
    finiteOrZero value
    |> fun safeValue -> safeValue.ToString("G17", CultureInfo.InvariantCulture)

/// Builds merged output header lines with node diagnostic columns.
let private buildMergedOutputHeaderLines (parsed: ParsedMergeCsv) : string list =
    match parsed.HeaderLines with
    | [] -> []
    | headerLines ->
        let lastLine = headerLines |> List.last
        let lastColumns = splitCsvLine lastLine

        if lastColumns.Length = parsed.DataRows.Head.Length then
            let prefixLines = headerLines |> List.take (headerLines.Length - 1)

            let preservedColumns =
                lastColumns
                |> Array.mapi (fun index value -> index, value)
                |> Array.choose (fun (index, value) -> if index = parsed.DoseColumnIndex then None else Some value)
                |> String.concat ","

            prefixLines
            @ [
                $"{preservedColumns},dose_sum_Gy,dose_mean_node_Gy,dose_sd_node_Gy,dose_sem_node_Gy,dose_rel_sem_node_percent,node_count"
              ]
        else
            headerLines

/// Builds one merged output row preserving non-dose columns and appending node diagnostics.
let private buildMergedOutputRow (baseRow: string array) (doseColumnIndex: int) (doseValues: float list) : string =
    let preservedColumns =
        baseRow
        |> Array.mapi (fun index value -> index, value)
        |> Array.choose (fun (index, value) -> if index = doseColumnIndex then None else Some value)
        |> Array.toList

    let stats = computeNodeDoseStats doseValues

    [
        yield! preservedColumns
        formatFloat stats.Sum
        formatFloat stats.Mean
        formatFloat stats.StandardDeviation
        formatFloat stats.StandardError
        formatFloat stats.RelativeStandardErrorPercent
        string stats.Count
    ]
    |> String.concat ","

/// Merges csv files for one phase-space and writes the merged csv output file.
let mergeNodeCsvFilesForPhaseSpace (inputCsvPaths: string list) (outputCsvPath: string) : Result<unit, string> =
    result {
        logCollectMergeStage
            "Start"
            $"inputCsvCount={inputCsvPaths.Length}; outputCsvPath={outputCsvPath}"

        let! parsedInputs =
            inputCsvPaths
            |> List.map (fun path -> result {
                logCollectMergeStage "ReadParseInputStart" $"path={path}"

                let! text =
                    try
                        File.ReadAllText path |> Ok
                    with ex ->
                        Error $"Failed reading csv file '{path}': {ex.Message}"

                let! parsed = parseCsvForMerge text
                logCollectMergeStage "ReadParseInputEnd" $"path={path}; rowCount={parsed.DataRows.Length}"
                return path, parsed
            })
            |> List.sequenceResultM

        match parsedInputs with
        | [] -> return! Error "No input csv files were provided for merge."
        | (_, firstParsed) :: otherParsed ->
            let expectedRows = firstParsed.DataRows.Length
            let expectedCols = firstParsed.DataRows.Head.Length
            logCollectMergeStage "FirstInputShape" $"rowCount={expectedRows}; columnCount={expectedCols}"

            do!
                otherParsed
                |> List.map (fun (_, parsed) -> validateSameShape expectedRows expectedCols parsed.DataRows)
                |> List.sequenceResultM
                |> Result.map (fun _ -> ())

            let parsedFiles = firstParsed :: (otherParsed |> List.map snd)
            logCollectMergeStage "MergeRowsStart" $"rowCount={expectedRows}; fileCount={parsedFiles.Length}"

            let! mergedRows =
                [ 0 .. expectedRows - 1 ]
                |> List.map (fun rowIndex -> result {
                    let baseRow = firstParsed.DataRows[rowIndex]

                    let! doseValues =
                        parsedFiles
                        |> List.map (fun parsed ->
                            let value = parsed.DataRows[rowIndex][parsed.DoseColumnIndex]
                            parseDoseCell value)
                        |> List.sequenceResultM

                    return buildMergedOutputRow baseRow firstParsed.DoseColumnIndex doseValues
                })
                |> List.sequenceResultM
            logCollectMergeStage "MergeRowsEnd" $"mergedRowCount={mergedRows.Length}"

            let outputLines =
                (buildMergedOutputHeaderLines firstParsed @ mergedRows)
                |> String.concat Environment.NewLine

            do!
                try
                    logCollectMergeStage "WriteStart" $"outputCsvPath={outputCsvPath}"
                    let parentFolder = Path.GetDirectoryName outputCsvPath

                    if not (String.IsNullOrWhiteSpace parentFolder) then
                        Directory.CreateDirectory(parentFolder) |> ignore

                    File.WriteAllText(outputCsvPath, outputLines)
                    logCollectMergeStage "WriteEnd" $"outputCsvPath={outputCsvPath}"
                    Ok()
                with ex ->
                    Error $"Failed writing merged csv '{outputCsvPath}': {ex.Message}"
    }
