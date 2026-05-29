module CollectCsvMerge

open System
open System.Globalization
open System.IO
open System.Text
open FsToolkit.ErrorHandling

/// Represents parsed csv content used for safe phase-space merge.
type ParsedMergeCsv = {
    HeaderLines: string list
    DataRows: string array list
    DoseColumnIndex: int
    DoseColumnName: string option
}

/// Represents the header preamble and first data row extracted from one csv stream.
type private MergeCsvPreamble = {
    HeaderLines: string list
    FirstDataRow: string array
}

/// Represents one streaming merge source file with reader state.
type private StreamingMergeSource = {
    Path: string
    Reader: StreamReader
    HeaderLines: string list
    DoseColumnIndex: int
    DoseColumnName: string option
    DataColumnCount: int
    mutable PendingDataRow: string array option
}

/// Splits one csv line into comma-separated columns.
let private splitCsvLine (line: string) : string array = line.Split(',')

/// Returns true when one csv line should be ignored by parsers.
let private isIgnoredCsvLine (line: string) : bool =
    String.IsNullOrWhiteSpace line || line.TrimStart().StartsWith("#", StringComparison.Ordinal)

/// Returns one UTC timestamp string used by collect csv merge logs.
let private mergeLogTimestampUtc () : string = DateTime.UtcNow.ToString("O")

/// Writes one collect csv merge stage log line to stdout.
let private logCollectMergeStage (stage: string) (message: string) : unit =
    Console.WriteLine($"[{mergeLogTimestampUtc()}] [CollectCsvMerge] [{stage}] {message}")

/// Returns true when the value can be parsed as a floating-point number.
let private tryParseFloat (value: string) : bool =
    let mutable parsed = 0.0
    Double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, &parsed)

/// Parses one floating-point value using invariant culture.
let private parseDoseCell (value: string) : Result<float, string> =
    let mutable parsed = 0.0

    if Double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, &parsed) then
        Ok parsed
    else
        Error $"Failed parsing numeric value '{value}'."

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

/// Parses csv text into header lines, data rows, and dose column index.
let parseCsvForMerge (csvText: string) : Result<ParsedMergeCsv, string> =
    let lines =
        csvText.Replace("\r\n", "\n").Split('\n', StringSplitOptions.None)
        |> Array.filter (fun line -> not (isIgnoredCsvLine line))

    let headerBuffer = ResizeArray<string>()
    let dataBuffer = ResizeArray<string array>()

    for line in lines do
        let columns = splitCsvLine line

        match lastNumericColumnIndex columns with
        | Some _ -> dataBuffer.Add columns
        | None -> headerBuffer.Add line

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

/// Returns zero when a value is NaN or infinity.
let private finiteOrZero (value: float) : float =
    if Double.IsNaN value || Double.IsInfinity value then 0.0 else value

/// Formats one floating-point value using invariant culture.
let private formatFloat (value: float) : string =
    finiteOrZero value
    |> fun safeValue -> safeValue.ToString("G17", CultureInfo.InvariantCulture)

/// Tries to read one next non-blank csv row from a stream reader.
let private tryReadNextNonBlankRow (reader: StreamReader) : string array option =
    let mutable nextRow: string array option = None
    let mutable continueReading = true

    while continueReading && not reader.EndOfStream do
        let line = reader.ReadLine()

        if not (isNull line) && not (isIgnoredCsvLine line) then
            nextRow <- Some(splitCsvLine line)
            continueReading <- false

    nextRow

/// Reads header lines and the first numeric data row from one csv stream.
let private readHeaderAndFirstDataRow
    (reader: StreamReader)
    (path: string)
    : Result<MergeCsvPreamble, string> =
    let headers = ResizeArray<string>()
    let mutable firstDataRow: string array option = None
    let mutable continueReading = true

    while continueReading && not reader.EndOfStream do
        let line = reader.ReadLine()

        if not (isNull line) && not (isIgnoredCsvLine line) then
            let columns = splitCsvLine line

            match lastNumericColumnIndex columns with
            | Some _ ->
                firstDataRow <- Some columns
                continueReading <- false
            | None -> headers.Add line

    match firstDataRow with
    | Some row ->
        Ok
            {
                HeaderLines = headers |> Seq.toList
                FirstDataRow = row
            }
    | None -> Error $"CSV did not contain any numeric data rows: {path}"

/// Resolves a dose column using header priority or first-row numeric fallback.
let private resolveDoseColumnFromHeaderOrFallback
    (path: string)
    (headerLines: string list)
    (firstDataRow: string array)
    : Result<int * string option, string> =
    let expectedColumnCount = firstDataRow.Length

    let usableHeaderColumns =
        match headerLines |> List.tryLast with
        | Some lastHeaderLine ->
            let columns = splitCsvLine lastHeaderLine
            if columns.Length = expectedColumnCount then Some columns else None
        | None -> None

    match usableHeaderColumns with
    | Some headerColumns ->
        match tryResolveDoseColumnFromHeader headerColumns with
        | Some(doseColumnIndex, doseColumnName) -> Ok(doseColumnIndex, Some doseColumnName)
        | None ->
            let headerText = headerColumns |> Array.map _.Trim() |> String.concat ", "
            Error $"Unable to locate dose column from header in '{path}'. Header columns: {headerText}"
    | None ->
        match lastNumericColumnIndex firstDataRow with
        | Some doseColumnIndex -> Ok(doseColumnIndex, None)
        | None -> Error $"CSV dose column could not be determined for '{path}'."

/// Creates one streaming source descriptor from an opened csv stream.
let private createStreamingMergeSource (path: string) (reader: StreamReader) : Result<StreamingMergeSource, string> =
    result {
        let! preamble = readHeaderAndFirstDataRow reader path
        let! doseColumnIndex, doseColumnName =
            resolveDoseColumnFromHeaderOrFallback path preamble.HeaderLines preamble.FirstDataRow

        if doseColumnIndex >= preamble.FirstDataRow.Length then
            return! Error $"Dose column index {doseColumnIndex} is out of bounds for '{path}'."

        let! _ = parseDoseCell preamble.FirstDataRow[doseColumnIndex]

        return
            {
                Path = path
                Reader = reader
                HeaderLines = preamble.HeaderLines
                DoseColumnIndex = doseColumnIndex
                DoseColumnName = doseColumnName
                DataColumnCount = preamble.FirstDataRow.Length
                PendingDataRow = Some preamble.FirstDataRow
            }
    }

/// Opens one csv reader for streaming merge.
let private openCsvReader (path: string) : Result<StreamReader, string> =
    try
        Ok(new StreamReader(path))
    with ex ->
        Error $"Failed opening csv file '{path}': {ex.Message}"

/// Returns the next data row from a streaming source including its pending first row.
let private readNextDataRow (source: StreamingMergeSource) : string array option =
    match source.PendingDataRow with
    | Some pending ->
        source.PendingDataRow <- None
        Some pending
    | None -> tryReadNextNonBlankRow source.Reader

/// Builds merged output header lines with canonical coordinate and node diagnostic columns.
let private buildMergedOutputHeaderLines () : string list =
    [ "x,y,z,dose_sum_Gy,dose_mean_node_Gy,dose_sd_node_Gy,dose_sem_node_Gy,dose_rel_sem_node_percent,node_count" ]

/// Computes sample standard deviation from count, sum, and sum-of-squares.
let private sampleStandardDeviationFromMoments (count: int) (sum: float) (sumSquares: float) : float =
    if count < 2 then
        0.0
    else
        let numerator = sumSquares - ((sum * sum) / float count)
        let variance = Math.Max(0.0, numerator / float (count - 1))
        sqrt variance

/// Extracts x,y,z coordinate values from the first three non-dose columns.
let private extractCoordinateColumns (baseRow: string array) (doseColumnIndex: int) : Result<string * string * string, string> =
    let coordinates =
        baseRow
        |> Array.mapi (fun index value -> index, value)
        |> Array.choose (fun (index, value) -> if index = doseColumnIndex then None else Some value)

    if coordinates.Length < 3 then
        Error $"Input data row does not contain at least three non-dose coordinate columns. Found {coordinates.Length}."
    else
        Ok(coordinates[0], coordinates[1], coordinates[2])

/// Builds one merged output row using x,y,z and appending node diagnostics.
let private buildMergedOutputRow
    (xValue: string)
    (yValue: string)
    (zValue: string)
    (sumDose: float)
    (meanDose: float)
    (sdDose: float)
    (semDose: float)
    (relSemPercent: float)
    (count: int)
    : string =
    let builder = StringBuilder()

    builder.Append(xValue) |> ignore
    builder.Append(',').Append(yValue) |> ignore
    builder.Append(',').Append(zValue) |> ignore

    builder.Append(',').Append(formatFloat sumDose) |> ignore
    builder.Append(',').Append(formatFloat meanDose) |> ignore
    builder.Append(',').Append(formatFloat sdDose) |> ignore
    builder.Append(',').Append(formatFloat semDose) |> ignore
    builder.Append(',').Append(formatFloat relSemPercent) |> ignore
    builder.Append(',').Append(count) |> ignore
    builder.ToString()

/// Disposes all stream readers in a best-effort way.
let private disposeReaders (readers: ResizeArray<StreamReader>) : unit =
    for reader in readers do
        try
            reader.Dispose()
        with _ ->
            ()

/// Merges csv files for one phase-space and writes the merged csv output file.
let mergeNodeCsvFilesForPhaseSpace (inputCsvPaths: string list) (outputCsvPath: string) : Result<unit, string> =
    if inputCsvPaths.IsEmpty then
        Error "No input csv files were provided for merge."
    else
        logCollectMergeStage
            "Start"
            $"inputCsvCount={inputCsvPaths.Length}; outputCsvPath={outputCsvPath}"

        let readers = ResizeArray<StreamReader>()

        try
            result {
                let! sources =
                    inputCsvPaths
                    |> List.map (fun path -> result {
                        logCollectMergeStage "ReadParseInputStart" $"path={path}"
                        let! reader = openCsvReader path

                        readers.Add reader

                        let! source = createStreamingMergeSource path reader
                        let doseNameText = defaultArg source.DoseColumnName "(fallback-last-numeric)"

                        logCollectMergeStage
                            "DoseColumnResolved"
                            $"path={path}; doseColumnIndex={source.DoseColumnIndex}; doseColumnName={doseNameText}; dataColumnCount={source.DataColumnCount}"

                        return source
                    })
                    |> List.sequenceResultM
                    |> Result.map List.toArray

                let firstSource = sources[0]
                let expectedColumnCount = firstSource.DataColumnCount

                match
                    sources
                    |> Array.tryFind (fun source -> source.DataColumnCount <> expectedColumnCount)
                with
                | Some source ->
                    return!
                        Error
                            $"CSV column count mismatch between files. Expected {expectedColumnCount}, got {source.DataColumnCount} for '{source.Path}'."
                | None -> ()

                let parentFolder = Path.GetDirectoryName outputCsvPath

                if not (String.IsNullOrWhiteSpace parentFolder) then
                    Directory.CreateDirectory(parentFolder) |> ignore

                use writer = new StreamWriter(outputCsvPath, false)

                let outputHeaderLines =
                    buildMergedOutputHeaderLines ()

                for headerLine in outputHeaderLines do
                    writer.WriteLine headerLine

                logCollectMergeStage "MergeRowsStart" $"fileCount={sources.Length}"

                let mutable rowIndex = 0
                let mutable completed = false

                while not completed do
                    let rows = Array.zeroCreate<string array> sources.Length
                    let mutable endedCount = 0
                    let mutable endedPath = ""

                    for sourceIndex in 0 .. sources.Length - 1 do
                        match readNextDataRow sources[sourceIndex] with
                        | Some row -> rows[sourceIndex] <- row
                        | None ->
                            endedCount <- endedCount + 1

                            if String.IsNullOrEmpty endedPath then
                                endedPath <- sources[sourceIndex].Path

                    if endedCount = sources.Length then
                        completed <- true
                    elif endedCount > 0 then
                        return!
                            Error
                                $"CSV row count mismatch at row index {rowIndex} in '{endedPath}'. At least one input file ended early."
                    else
                        let baseRow = rows[0]
                        let mutable rowError: string option = None

                        for sourceIndex in 0 .. sources.Length - 1 do
                            let source = sources[sourceIndex]
                            let row = rows[sourceIndex]

                            if row.Length <> source.DataColumnCount then
                                rowError <-
                                    Some
                                        $"CSV column count mismatch at row index {rowIndex} for '{source.Path}'. Expected {source.DataColumnCount}, got {row.Length}."

                        match rowError with
                        | Some errorMessage -> return! Error errorMessage
                        | None ->
                            let mutable sumDose = 0.0
                            let mutable sumSquares = 0.0
                            let mutable parseError: string option = None

                            for sourceIndex in 0 .. sources.Length - 1 do
                                let source = sources[sourceIndex]
                                let row = rows[sourceIndex]

                                if source.DoseColumnIndex >= row.Length then
                                    parseError <-
                                        Some
                                            $"Dose column index {source.DoseColumnIndex} is out of bounds at row index {rowIndex} for '{source.Path}'."
                                else
                                    match parseDoseCell row[source.DoseColumnIndex] with
                                    | Ok doseValue ->
                                        sumDose <- sumDose + doseValue
                                        sumSquares <- sumSquares + (doseValue * doseValue)
                                    | Error parseMessage ->
                                        parseError <-
                                            Some
                                                $"Failed parsing dose value at row index {rowIndex} for '{source.Path}': {parseMessage}"

                            match parseError with
                            | Some errorMessage -> return! Error errorMessage
                            | None ->
                                let! xValue, yValue, zValue =
                                    match extractCoordinateColumns baseRow firstSource.DoseColumnIndex with
                                    | Ok coordinates -> Ok coordinates
                                    | Error message ->
                                        Error $"Coordinate parse failed at row index {rowIndex}: {message}"

                                let nodeCount = sources.Length
                                let meanDose = sumDose / float nodeCount
                                let sdDose = sampleStandardDeviationFromMoments nodeCount sumDose sumSquares
                                let semDose = if nodeCount < 2 then 0.0 else sdDose / sqrt (float nodeCount)

                                let relSemPercent =
                                    if meanDose = 0.0 then
                                        0.0
                                    else
                                        100.0 * semDose / abs meanDose

                                let mergedRow =
                                    buildMergedOutputRow
                                        xValue
                                        yValue
                                        zValue
                                        sumDose
                                        meanDose
                                        sdDose
                                        semDose
                                        relSemPercent
                                        nodeCount

                                writer.WriteLine mergedRow
                                rowIndex <- rowIndex + 1

                                if rowIndex % 100000 = 0 then
                                    logCollectMergeStage "MergeRowsProgress" $"rowIndex={rowIndex}"

                writer.Flush()
                logCollectMergeStage "MergeRowsEnd" $"rowCount={rowIndex}"
                logCollectMergeStage "WriteEnd" $"outputCsvPath={outputCsvPath}"
                return ()
            }
        finally
            disposeReaders readers
