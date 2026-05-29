module CollectStatistics

open System
open System.Globalization
open System.IO
open System.Text
open FsToolkit.ErrorHandling

/// Represents one streaming summary source file with reader state.
type private StreamingSummarySource = {
    Path: string
    Reader: StreamReader
    HeaderLines: string list
    DoseColumnIndex: int
    DataColumnCount: int
    mutable PendingDataRow: string array option
}

/// Splits one csv line into comma-separated columns.
let private splitCsvLine (line: string) : string array = line.Split(',')

/// Returns true when one csv line should be ignored by parsers.
let private isIgnoredCsvLine (line: string) : bool =
    String.IsNullOrWhiteSpace line || line.TrimStart().StartsWith("#", StringComparison.Ordinal)

/// Returns one UTC timestamp string used by collect summary logs.
let private summaryLogTimestampUtc () : string = DateTime.UtcNow.ToString("O")

/// Writes one collect summary stage log line to stdout.
let private logCollectSummaryStage (stage: string) (message: string) : unit =
    Console.WriteLine($"[{summaryLogTimestampUtc()}] [CollectStatistics] [{stage}] {message}")

/// Parses one floating-point value using invariant culture.
let private parseFloat (value: string) : Result<float, string> =
    let mutable parsed = 0.0

    if Double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, &parsed) then
        Ok parsed
    else
        Error $"Failed parsing numeric value '{value}'."

/// Returns zero when value is NaN or infinity.
let private finiteOrZero (value: float) : float =
    if Double.IsNaN value || Double.IsInfinity value then 0.0 else value

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

/// Reads one real csv header line and first data row from one merged csv stream.
let private readHeaderAndFirstDataRow
    (reader: StreamReader)
    (path: string)
    : Result<string list * string array, string> =
    let mutable headerLine: string option = None
    let mutable firstDataRow: string array option = None

    while headerLine.IsNone && not reader.EndOfStream do
        let line = reader.ReadLine()

        if not (isNull line) && not (isIgnoredCsvLine line) then
            headerLine <- Some line

    while firstDataRow.IsNone && not reader.EndOfStream do
        let line = reader.ReadLine()

        if not (isNull line) && not (isIgnoredCsvLine line) then
            firstDataRow <- Some(splitCsvLine line)

    match headerLine, firstDataRow with
    | Some header, Some row -> Ok([ header ], row)
    | None, _ -> Error $"Merged csv header is missing in '{path}'."
    | _, None -> Error $"Merged csv did not contain any data rows: {path}"

/// Resolves dose_sum_Gy column index from a usable merged csv header.
let private resolveDoseSumColumnIndexFromHeader
    (path: string)
    (headerLines: string list)
    (firstDataRow: string array)
    : Result<int, string> =
    match headerLines |> List.tryLast with
    | None -> Error $"Merged csv header is missing in '{path}', expected column 'dose_sum_Gy'."
    | Some headerLine ->
        let headerColumns = splitCsvLine headerLine

        if headerColumns.Length <> firstDataRow.Length then
            Error $"Merged csv header shape mismatch in '{path}', expected column 'dose_sum_Gy'."
        else
            match
                headerColumns
                |> Array.mapi (fun index name -> index, name.Trim())
                |> Array.tryFind (fun (_, name) -> String.Equals(name, "dose_sum_Gy", StringComparison.OrdinalIgnoreCase))
            with
            | Some(index, _) -> Ok index
            | None ->
                let headerText = headerColumns |> Array.map _.Trim() |> String.concat ", "
                Error $"Merged csv is missing required column 'dose_sum_Gy' in '{path}'. Header columns: {headerText}"

/// Formats one floating-point value with invariant culture.
let private formatFloat (value: float) : string =
    finiteOrZero value
    |> fun safeValue -> safeValue.ToString("G17", CultureInfo.InvariantCulture)

/// Opens one csv reader for streaming summary.
let private openCsvReader (path: string) : Result<StreamReader, string> =
    try
        Ok(new StreamReader(path))
    with ex ->
        Error $"Failed opening merged csv '{path}': {ex.Message}"

/// Creates one streaming summary source descriptor from an opened csv stream.
let private createStreamingSummarySource (path: string) (reader: StreamReader) : Result<StreamingSummarySource, string> =
    result {
        let! headerLines, firstDataRow = readHeaderAndFirstDataRow reader path
        let! doseColumnIndex = resolveDoseSumColumnIndexFromHeader path headerLines firstDataRow

        if doseColumnIndex >= firstDataRow.Length then
            return! Error $"Dose column index {doseColumnIndex} is out of bounds for '{path}'."

        let! _ = parseFloat firstDataRow[doseColumnIndex]

        return
            {
                Path = path
                Reader = reader
                HeaderLines = headerLines
                DoseColumnIndex = doseColumnIndex
                DataColumnCount = firstDataRow.Length
                PendingDataRow = Some firstDataRow
            }
    }

/// Returns the next data row from a streaming summary source.
let private readNextDataRow (source: StreamingSummarySource) : string array option =
    match source.PendingDataRow with
    | Some pending ->
        source.PendingDataRow <- None
        Some pending
    | None -> tryReadNextNonBlankRow source.Reader

/// Computes sample standard deviation from count, sum, and sum-of-squares.
let private sampleStandardDeviationFromMoments (count: int) (sum: float) (sumSquares: float) : float =
    if count < 2 then
        0.0
    else
        let numerator = sumSquares - ((sum * sum) / float count)
        let variance = Math.Max(0.0, numerator / float (count - 1))
        sqrt variance

/// Computes the median value from an unsorted numeric array.
let private medianFromValues (values: float array) : float =
    let sorted = Array.copy values
    Array.Sort sorted
    let count = sorted.Length

    if count % 2 = 1 then
        sorted[count / 2]
    else
        let upper = sorted[count / 2]
        let lower = sorted[(count / 2) - 1]
        (lower + upper) / 2.0

/// Builds canonical summary output header line.
let private buildSummaryHeaders () : string list =
    [ "x,y,z,total_dose_sum_Gy,phsp_mean_Gy,phsp_median_Gy,phsp_sd_Gy,phsp_sem_Gy,phsp_rel_sem_percent,phsp_count" ]

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

/// Builds one summary output row using x,y,z and appending summary statistics.
let private buildSummaryOutputRow
    (xValue: string)
    (yValue: string)
    (zValue: string)
    (totalDose: float)
    (meanDose: float)
    (medianDose: float)
    (sdDose: float)
    (semDose: float)
    (relSemPercent: float)
    (count: int)
    : string =
    let builder = StringBuilder()

    builder.Append(xValue) |> ignore
    builder.Append(',').Append(yValue) |> ignore
    builder.Append(',').Append(zValue) |> ignore

    builder.Append(',').Append(formatFloat totalDose) |> ignore
    builder.Append(',').Append(formatFloat meanDose) |> ignore
    builder.Append(',').Append(formatFloat medianDose) |> ignore
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

/// Computes summary statistics across phase-space merged csv files and writes dose_summary.csv.
let computeDoseSummary (mergedCsvPaths: string list) (outputSummaryPath: string) : Result<unit, string> =
    if mergedCsvPaths.IsEmpty then
        Error "No merged phase-space csv files were provided."
    else
        logCollectSummaryStage
            "SummaryStart"
            $"mergedFileCount={mergedCsvPaths.Length}; outputSummaryPath={outputSummaryPath}"

        let readers = ResizeArray<StreamReader>()

        try
            result {
                let! sources =
                    mergedCsvPaths
                    |> List.map (fun path -> result {
                        let! reader = openCsvReader path
                        readers.Add reader

                        let! source = createStreamingSummarySource path reader
                        logCollectSummaryStage
                            "DoseColumnResolved"
                            $"path={path}; doseColumnIndex={source.DoseColumnIndex}; dataColumnCount={source.DataColumnCount}"

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
                            $"CSV column count mismatch between merged files. Expected {expectedColumnCount}, got {source.DataColumnCount} for '{source.Path}'."
                | None -> ()

                let parentFolder = Path.GetDirectoryName outputSummaryPath

                if not (String.IsNullOrWhiteSpace parentFolder) then
                    Directory.CreateDirectory(parentFolder) |> ignore

                use writer = new StreamWriter(outputSummaryPath, false)

                let summaryHeaders =
                    buildSummaryHeaders ()

                for headerLine in summaryHeaders do
                    writer.WriteLine headerLine

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
                                $"CSV row count mismatch at row index {rowIndex} in '{endedPath}'. At least one merged file ended early."
                    else
                        let baseRow = rows[0]
                        let values = Array.zeroCreate<float> sources.Length
                        let mutable rowError: string option = None
                        let mutable sumDose = 0.0
                        let mutable sumSquares = 0.0

                        for sourceIndex in 0 .. sources.Length - 1 do
                            let source = sources[sourceIndex]
                            let row = rows[sourceIndex]

                            if row.Length <> source.DataColumnCount then
                                rowError <-
                                    Some
                                        $"CSV column count mismatch at row index {rowIndex} for '{source.Path}'. Expected {source.DataColumnCount}, got {row.Length}."
                            elif source.DoseColumnIndex >= row.Length then
                                rowError <-
                                    Some
                                        $"Dose column index {source.DoseColumnIndex} is out of bounds at row index {rowIndex} for '{source.Path}'."
                            else
                                match parseFloat row[source.DoseColumnIndex] with
                                | Ok doseValue ->
                                    values[sourceIndex] <- doseValue
                                    sumDose <- sumDose + doseValue
                                    sumSquares <- sumSquares + (doseValue * doseValue)
                                | Error parseMessage ->
                                    rowError <-
                                        Some
                                            $"Failed parsing dose_sum_Gy at row index {rowIndex} for '{source.Path}': {parseMessage}"

                        match rowError with
                        | Some errorMessage -> return! Error errorMessage
                        | None ->
                            let! xValue, yValue, zValue =
                                match extractCoordinateColumns baseRow firstSource.DoseColumnIndex with
                                | Ok coordinates -> Ok coordinates
                                | Error message ->
                                    Error $"Coordinate parse failed at row index {rowIndex}: {message}"

                            let phspCount = sources.Length
                            let meanDose = sumDose / float phspCount
                            let medianDose = medianFromValues values
                            let sdDose = sampleStandardDeviationFromMoments phspCount sumDose sumSquares
                            let semDose = if phspCount < 2 then 0.0 else sdDose / sqrt (float phspCount)

                            let relSemPercent =
                                if meanDose = 0.0 then
                                    0.0
                                else
                                    100.0 * semDose / abs meanDose

                            let summaryRow =
                                buildSummaryOutputRow
                                    xValue
                                    yValue
                                    zValue
                                    sumDose
                                    meanDose
                                    medianDose
                                    sdDose
                                    semDose
                                    relSemPercent
                                    phspCount

                            writer.WriteLine summaryRow
                            rowIndex <- rowIndex + 1

                            if rowIndex % 100000 = 0 then
                                logCollectSummaryStage "SummaryProgress" $"rowIndex={rowIndex}"

                writer.Flush()
                logCollectSummaryStage "SummaryEnd" $"rowCount={rowIndex}; outputSummaryPath={outputSummaryPath}"
                return ()
            }
        finally
            disposeReaders readers
