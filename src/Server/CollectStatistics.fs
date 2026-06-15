module CollectStatistics

open System
open System.Globalization
open System.IO
open System.Text
open FsToolkit.ErrorHandling

/// Represents per-voxel uncertainty metrics derived from independent raw batches.
type VoxelUncertaintyMetrics = {
    DoseSum: float
    BatchCount: int
    MeanBatchDose: float
    BatchStandardDeviation: float
    StandardUncertaintyOfSummedDose: float
    RelativeUncertaintyPercent: float option
}

/// Represents the header preamble and first numeric data row extracted from one csv stream.
type private MergeCsvPreamble = {
    HeaderLines: string list
    FirstDataRow: string array
}

/// Represents one streaming dose source file with reader state.
type private StreamingDoseSource = {
    Path: string
    Reader: StreamReader
    DoseColumnIndex: int
    DataColumnCount: int
    mutable PendingDataRow: string array option
}

/// Splits one csv line into comma-separated columns.
let private splitCsvLine (line: string) : string array = line.Split(',')

/// Returns true when one csv line should be ignored by parsers.
let private isIgnoredCsvLine (line: string) : bool =
    String.IsNullOrWhiteSpace line || line.TrimStart().StartsWith("#", StringComparison.Ordinal)

/// Returns one UTC timestamp string used by collect statistics logs.
let private statisticsLogTimestampUtc () : string = DateTime.UtcNow.ToString("O")

/// Writes one collect statistics stage log line to stdout.
let private logCollectStatisticsStage (stage: string) (message: string) : unit =
    Console.WriteLine($"[{statisticsLogTimestampUtc()}] [CollectStatistics] [{stage}] {message}")

/// Returns true when the value can be parsed as a floating-point number.
let private tryParseFloat (value: string) : bool =
    let mutable parsed = 0.0
    Double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, &parsed)

/// Parses one floating-point value using invariant culture.
let private parseFloat (value: string) : Result<float, string> =
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

/// Returns the first matching header column index from a list of preferred names.
let private tryResolveHeaderColumnByNames (headerColumns: string array) (preferredNames: string list) : (int * string) option =
    let normalizedColumns =
        headerColumns
        |> Array.mapi (fun index name -> index, name, normalizeHeaderToken name)

    preferredNames
    |> List.tryPick (fun preferred ->
        normalizedColumns
        |> Array.tryFind (fun (_, _, normalized) -> normalized = preferred)
        |> Option.map (fun (index, originalName, _) -> index, originalName))

/// Returns preferred raw-dose header names in strict priority order.
let private preferredRawDoseHeaderNames: string list =
    [
        "dose_sum_gy"
        "dose_gy"
        "dose"
        "dosetomedium"
        "dose_to_medium"
        "dose-to-medium"
        "medium dose"
    ]

/// Returns zero when value is NaN or infinity.
let private finiteOrZero (value: float) : float =
    if Double.IsNaN value || Double.IsInfinity value then 0.0 else value

/// Formats one floating-point value with invariant culture.
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

/// Resolves the raw dose column using header priority or first-row numeric fallback.
let private resolveDoseColumnFromHeaderOrFallback
    (path: string)
    (headerLines: string list)
    (firstDataRow: string array)
    : Result<int, string> =
    let expectedColumnCount = firstDataRow.Length

    let usableHeaderColumns =
        match headerLines |> List.tryLast with
        | Some lastHeaderLine ->
            let columns = splitCsvLine lastHeaderLine
            if columns.Length = expectedColumnCount then Some columns else None
        | None -> None

    match usableHeaderColumns with
    | Some headerColumns ->
        match tryResolveHeaderColumnByNames headerColumns preferredRawDoseHeaderNames with
        | Some(doseColumnIndex, _) -> Ok doseColumnIndex
        | None ->
            let headerText = headerColumns |> Array.map _.Trim() |> String.concat ", "
            Error $"Unable to locate dose column from header in '{path}'. Header columns: {headerText}"
    | None ->
        match lastNumericColumnIndex firstDataRow with
        | Some doseColumnIndex -> Ok doseColumnIndex
        | None -> Error $"CSV dose column could not be determined for '{path}'."

/// Resolves one required named column from a usable header line.
let private resolveRequiredColumnFromHeader
    (path: string)
    (headerLines: string list)
    (firstDataRow: string array)
    (preferredNames: string list)
    (columnLabel: string)
    : Result<int, string> =
    let expectedColumnCount = firstDataRow.Length

    match headerLines |> List.tryLast with
    | None -> Error $"CSV header is missing in '{path}', expected column '{columnLabel}'."
    | Some lastHeaderLine ->
        let headerColumns = splitCsvLine lastHeaderLine

        if headerColumns.Length <> expectedColumnCount then
            Error $"CSV header shape mismatch in '{path}', expected column '{columnLabel}'."
        else
            match tryResolveHeaderColumnByNames headerColumns preferredNames with
            | Some(columnIndex, _) -> Ok columnIndex
            | None ->
                let headerText = headerColumns |> Array.map _.Trim() |> String.concat ", "
                Error $"CSV is missing required column '{columnLabel}' in '{path}'. Header columns: {headerText}"

/// Opens one csv reader for streaming statistics work.
let private openCsvReader (path: string) : Result<StreamReader, string> =
    try
        Ok(new StreamReader(path))
    with ex ->
        Error $"Failed opening csv '{path}': {ex.Message}"

/// Creates one streaming dose source descriptor for a raw batch csv.
let private createStreamingDoseSourceForRawBatch (path: string) (reader: StreamReader) : Result<StreamingDoseSource, string> =
    result {
        let! preamble = readHeaderAndFirstDataRow reader path
        let! doseColumnIndex = resolveDoseColumnFromHeaderOrFallback path preamble.HeaderLines preamble.FirstDataRow

        if doseColumnIndex >= preamble.FirstDataRow.Length then
            return! Error $"Dose column index {doseColumnIndex} is out of bounds for '{path}'."

        let! _ = parseFloat preamble.FirstDataRow[doseColumnIndex]

        return
            {
                Path = path
                Reader = reader
                DoseColumnIndex = doseColumnIndex
                DataColumnCount = preamble.FirstDataRow.Length
                PendingDataRow = Some preamble.FirstDataRow
            }
    }

/// Creates one streaming dose source descriptor for a csv with a required named dose column.
let private createStreamingDoseSourceForNamedColumn
    (path: string)
    (reader: StreamReader)
    (preferredNames: string list)
    (columnLabel: string)
    : Result<StreamingDoseSource, string> =
    result {
        let! preamble = readHeaderAndFirstDataRow reader path

        let! doseColumnIndex =
            resolveRequiredColumnFromHeader path preamble.HeaderLines preamble.FirstDataRow preferredNames columnLabel

        if doseColumnIndex >= preamble.FirstDataRow.Length then
            return! Error $"Dose column index {doseColumnIndex} is out of bounds for '{path}'."

        let! _ = parseFloat preamble.FirstDataRow[doseColumnIndex]

        return
            {
                Path = path
                Reader = reader
                DoseColumnIndex = doseColumnIndex
                DataColumnCount = preamble.FirstDataRow.Length
                PendingDataRow = Some preamble.FirstDataRow
            }
    }

/// Returns the next data row from a streaming dose source.
let private readNextDataRow (source: StreamingDoseSource) : string array option =
    match source.PendingDataRow with
    | Some pending ->
        source.PendingDataRow <- None
        Some pending
    | None -> tryReadNextNonBlankRow source.Reader

/// Returns the variance roundoff tolerance derived from the moment magnitudes.
let private varianceNumeratorRoundoffTolerance (sum: float) (sumSquares: float) (count: int) : float =
    let expectedSquareSum = (sum * sum) / float count
    let scale = max 1.0 (max (abs sumSquares) (abs expectedSquareSum))
    1e-12 * scale

/// Computes sample variance across independent batch doses from count, sum, and sum-of-squares.
let private computeSampleVarianceAcrossIndependentBatchDoses
    (count: int)
    (sum: float)
    (sumSquares: float)
    : Result<float, string> =
    if count < 2 then
        Error "At least two batch values are required to compute sample uncertainty."
    else
        let varianceNumerator = sumSquares - ((sum * sum) / float count)

        if varianceNumerator < 0.0 then
            let tolerance = varianceNumeratorRoundoffTolerance sum sumSquares count

            if abs varianceNumerator <= tolerance then
                Ok 0.0
            else
                Error
                    $"Sample variance numerator is negative beyond floating-point roundoff tolerance. numerator={formatFloat varianceNumerator}; tolerance={formatFloat tolerance}."
        else
            Ok(varianceNumerator / float (count - 1))

/// Computes sample standard deviation across independent batch doses from count, sum, and sum-of-squares.
let private computeSampleStandardDeviationAcrossIndependentBatchDoses
    (count: int)
    (sum: float)
    (sumSquares: float)
    : Result<float, string> =
    computeSampleVarianceAcrossIndependentBatchDoses count sum sumSquares
    |> Result.map sqrt

/// Computes voxel uncertainty from independent raw dose batches.
let computeVoxelUncertaintyMetrics
    (doseSum: float)
    (sumSquares: float)
    (batchCount: int)
    : Result<VoxelUncertaintyMetrics, string> =
    result {
        if batchCount < 2 then
            return! Error "At least two batch values are required to compute uncertainty of the summed dose."

        let meanBatchDose = doseSum / float batchCount
        let! batchStandardDeviation =
            computeSampleStandardDeviationAcrossIndependentBatchDoses batchCount doseSum sumSquares
        let standardUncertaintyOfSummedDose = sqrt (float batchCount) * batchStandardDeviation

        let relativeUncertaintyPercent =
            if doseSum <= 0.0 then
                None
            else
                Some(100.0 * standardUncertaintyOfSummedDose / doseSum)

        return
            {
                DoseSum = doseSum
                BatchCount = batchCount
                MeanBatchDose = meanBatchDose
                BatchStandardDeviation = batchStandardDeviation
                StandardUncertaintyOfSummedDose = standardUncertaintyOfSummedDose
                RelativeUncertaintyPercent = relativeUncertaintyPercent
            }
    }

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

/// Validates that one row carries the expected x,y,z coordinates.
let private ensureMatchingCoordinates
    (expectedCoordinates: string * string * string)
    (source: StreamingDoseSource)
    (row: string array)
    (rowIndex: int)
    : Result<unit, string> =
    result {
        let! actualCoordinates =
            extractCoordinateColumns row source.DoseColumnIndex
            |> Result.mapError (fun message -> $"Coordinate parse failed at row index {rowIndex} for '{source.Path}': {message}")

        if actualCoordinates <> expectedCoordinates then
            let expectedX, expectedY, expectedZ = expectedCoordinates
            let actualX, actualY, actualZ = actualCoordinates

            return!
                Error
                    $"Coordinate mismatch at row index {rowIndex} for '{source.Path}'. Expected ({expectedX}, {expectedY}, {expectedZ}), got ({actualX}, {actualY}, {actualZ})."
    }

/// Builds canonical final merged dose header line.
let private buildDoseMergedHeaders () : string list = [ "x,y,z,dose_to_medium_Gy" ]

/// Builds one final merged dose output row.
let private buildDoseMergedRow (xValue: string) (yValue: string) (zValue: string) (doseSum: float) : string =
    let builder = StringBuilder()
    builder.Append(xValue) |> ignore
    builder.Append(',').Append(yValue) |> ignore
    builder.Append(',').Append(zValue) |> ignore
    builder.Append(',').Append(formatFloat doseSum) |> ignore
    builder.ToString()

/// Builds canonical dose-with-uncertainty header line for summed-dose uncertainty output.
let private buildDoseWithUncertaintyHeaders () : string list =
    [
        "x,y,z,dose_to_medium_Gy,batch_count,mean_batch_dose_Gy,batch_standard_deviation_Gy,standard_uncertainty_Gy,relative_uncertainty_percent"
    ]

/// Builds one dose-with-uncertainty output row for the summed-dose uncertainty metrics.
let private buildDoseWithUncertaintyRow
    (xValue: string)
    (yValue: string)
    (zValue: string)
    (metrics: VoxelUncertaintyMetrics)
    : string =
    let builder = StringBuilder()

    builder.Append(xValue) |> ignore
    builder.Append(',').Append(yValue) |> ignore
    builder.Append(',').Append(zValue) |> ignore
    builder.Append(',').Append(formatFloat metrics.DoseSum) |> ignore
    builder.Append(',').Append(metrics.BatchCount) |> ignore
    builder.Append(',').Append(formatFloat metrics.MeanBatchDose) |> ignore
    builder.Append(',').Append(formatFloat metrics.BatchStandardDeviation) |> ignore
    builder.Append(',').Append(formatFloat metrics.StandardUncertaintyOfSummedDose) |> ignore
    builder.Append(',') |> ignore

    match metrics.RelativeUncertaintyPercent with
    | Some value -> builder.Append(formatFloat value) |> ignore
    | None -> ()

    builder.ToString()

/// Disposes all stream readers in a best-effort way.
let private disposeReaders (readers: ResizeArray<StreamReader>) : unit =
    for reader in readers do
        try
            reader.Dispose()
        with _ ->
            ()

/// Validates that every source advertises the same data column count.
let private validateUniformColumnCount (sources: StreamingDoseSource array) : Result<unit, string> =
    let firstSource = sources[0]
    let expectedColumnCount = firstSource.DataColumnCount

    match sources |> Array.tryFind (fun source -> source.DataColumnCount <> expectedColumnCount) with
    | Some source ->
        Error
            $"CSV column count mismatch between files. Expected {expectedColumnCount}, got {source.DataColumnCount} for '{source.Path}'."
    | None -> Ok()

/// Ensures the parent folder exists for one output file path.
let private ensureParentFolderExists (path: string) : unit =
    let parentFolder = Path.GetDirectoryName path

    if not (String.IsNullOrWhiteSpace parentFolder) then
        Directory.CreateDirectory(parentFolder) |> ignore

/// Merges node-merged phase-space csv files into one final dose_merged.csv.
let mergePhaseSpaceDoseCsvFiles (mergedCsvPaths: string list) (outputSummaryPath: string) : Result<unit, string> =
    if mergedCsvPaths.IsEmpty then
        Error "No merged phase-space csv files were provided."
    else
        logCollectStatisticsStage
            "FinalMergeStart"
            $"mergedFileCount={mergedCsvPaths.Length}; outputSummaryPath={outputSummaryPath}"

        let readers = ResizeArray<StreamReader>()

        try
            result {
                let! sources =
                    mergedCsvPaths
                    |> List.map (fun path -> result {
                        let! reader = openCsvReader path
                        readers.Add reader

                        let! source =
                            createStreamingDoseSourceForNamedColumn path reader [ "dose_sum_gy" ] "dose_sum_Gy"

                        logCollectStatisticsStage
                            "FinalMergeDoseColumnResolved"
                            $"path={path}; doseColumnIndex={source.DoseColumnIndex}; dataColumnCount={source.DataColumnCount}"

                        return source
                    })
                    |> List.sequenceResultM
                    |> Result.map List.toArray

                do! validateUniformColumnCount sources
                ensureParentFolderExists outputSummaryPath

                use writer = new StreamWriter(outputSummaryPath, false)

                for headerLine in buildDoseMergedHeaders () do
                    writer.WriteLine headerLine

                let firstSource = sources[0]
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
                        let mutable rowError: string option = None
                        let mutable sumDose = 0.0

                        let baseCoordinatesResult =
                            extractCoordinateColumns baseRow firstSource.DoseColumnIndex
                            |> Result.mapError (fun message -> $"Coordinate parse failed at row index {rowIndex}: {message}")

                        match baseCoordinatesResult with
                        | Error message -> return! Error message
                        | Ok(xValue, yValue, zValue) ->
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
                                elif rowError.IsNone then
                                    match ensureMatchingCoordinates (xValue, yValue, zValue) source row rowIndex with
                                    | Ok() ->
                                        match parseFloat row[source.DoseColumnIndex] with
                                        | Ok doseValue -> sumDose <- sumDose + doseValue
                                        | Error parseMessage ->
                                            rowError <-
                                                Some
                                                    $"Failed parsing dose_sum_Gy at row index {rowIndex} for '{source.Path}': {parseMessage}"
                                    | Error message -> rowError <- Some message

                            match rowError with
                            | Some errorMessage -> return! Error errorMessage
                            | None ->
                                writer.WriteLine(buildDoseMergedRow xValue yValue zValue sumDose)
                                rowIndex <- rowIndex + 1

                                if rowIndex % 100000 = 0 then
                                    logCollectStatisticsStage "FinalMergeProgress" $"rowIndex={rowIndex}"

                writer.Flush()
                logCollectStatisticsStage "FinalMergeEnd" $"rowCount={rowIndex}; outputSummaryPath={outputSummaryPath}"
                return ()
            }
        finally
            disposeReaders readers

/// Computes dose_with_uncertainty.csv directly from independent raw batch csv files.
let computeDoseWithUncertaintyFromRawBatchCsvFiles
    (inputCsvPaths: string list)
    (outputUncertaintyPath: string)
    : Result<unit, string> =
    if inputCsvPaths.IsEmpty then
        Error "No raw batch csv files were provided for uncertainty calculation."
    elif inputCsvPaths.Length < 2 then
        Error "At least two raw batch csv files are required to compute uncertainty."
    else
        logCollectStatisticsStage
            "UncertaintyStart"
            $"rawBatchFileCount={inputCsvPaths.Length}; outputUncertaintyPath={outputUncertaintyPath}"

        let readers = ResizeArray<StreamReader>()

        try
            result {
                let! sources =
                    inputCsvPaths
                    |> List.map (fun path -> result {
                        let! reader = openCsvReader path
                        readers.Add reader

                        let! source = createStreamingDoseSourceForRawBatch path reader
                        logCollectStatisticsStage
                            "UncertaintyDoseColumnResolved"
                            $"path={path}; doseColumnIndex={source.DoseColumnIndex}; dataColumnCount={source.DataColumnCount}"

                        return source
                    })
                    |> List.sequenceResultM
                    |> Result.map List.toArray

                do! validateUniformColumnCount sources
                ensureParentFolderExists outputUncertaintyPath

                use writer = new StreamWriter(outputUncertaintyPath, false)

                for headerLine in buildDoseWithUncertaintyHeaders () do
                    writer.WriteLine headerLine

                let firstSource = sources[0]
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
                                $"CSV row count mismatch at row index {rowIndex} in '{endedPath}'. At least one raw batch file ended early."
                    else
                        let baseRow = rows[0]
                        let mutable rowError: string option = None
                        let mutable doseSum = 0.0
                        let mutable doseSumSquares = 0.0
                        let mutable batchCount = 0

                        let baseCoordinatesResult =
                            extractCoordinateColumns baseRow firstSource.DoseColumnIndex
                            |> Result.mapError (fun message -> $"Coordinate parse failed at row index {rowIndex}: {message}")

                        match baseCoordinatesResult with
                        | Error message -> return! Error message
                        | Ok(xValue, yValue, zValue) ->
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
                                elif rowError.IsNone then
                                    match ensureMatchingCoordinates (xValue, yValue, zValue) source row rowIndex with
                                    | Ok() ->
                                        match parseFloat row[source.DoseColumnIndex] with
                                        | Ok doseValue ->
                                            doseSum <- doseSum + doseValue
                                            doseSumSquares <- doseSumSquares + (doseValue * doseValue)
                                            batchCount <- batchCount + 1
                                        | Error parseMessage ->
                                            rowError <-
                                                Some
                                                    $"Failed parsing raw batch dose at row index {rowIndex} for '{source.Path}': {parseMessage}"
                                    | Error message -> rowError <- Some message

                            match rowError with
                            | Some errorMessage -> return! Error errorMessage
                            | None ->
                                let! metrics = computeVoxelUncertaintyMetrics doseSum doseSumSquares batchCount
                                writer.WriteLine(buildDoseWithUncertaintyRow xValue yValue zValue metrics)
                                rowIndex <- rowIndex + 1

                                if rowIndex % 100000 = 0 then
                                    logCollectStatisticsStage "UncertaintyProgress" $"rowIndex={rowIndex}"

                writer.Flush()
                logCollectStatisticsStage "UncertaintyEnd" $"rowCount={rowIndex}; outputUncertaintyPath={outputUncertaintyPath}"
                return ()
            }
        finally
            disposeReaders readers

/// Returns the floating-point tolerance used for dose cross-checks.
let private doseValidationTolerance (left: float) (right: float) : float =
    let scale = max 1.0 (max (abs left) (abs right))
    max 1e-12 (1e-9 * scale)

/// Validates that dose_merged.csv matches dose_with_uncertainty.csv voxel-by-voxel.
let validateDoseMergedMatchesUncertainty
    (doseMergedPath: string)
    (doseWithUncertaintyPath: string)
    : Result<unit, string> =
    logCollectStatisticsStage
        "ValidationStart"
        $"doseMergedPath={doseMergedPath}; doseWithUncertaintyPath={doseWithUncertaintyPath}"

    let readers = ResizeArray<StreamReader>()

    try
        result {
            let! leftReader = openCsvReader doseMergedPath
            readers.Add leftReader

            let! rightReader = openCsvReader doseWithUncertaintyPath
            readers.Add rightReader

            let! doseMergedSource =
                createStreamingDoseSourceForNamedColumn
                    doseMergedPath
                    leftReader
                    [ "dose_to_medium_gy" ]
                    "dose_to_medium_Gy"

            let! doseWithUncertaintySource =
                createStreamingDoseSourceForNamedColumn
                    doseWithUncertaintyPath
                    rightReader
                    [ "dose_to_medium_gy" ]
                    "dose_to_medium_Gy"

            let mutable rowIndex = 0
            let mutable completed = false

            while not completed do
                let leftRow = readNextDataRow doseMergedSource
                let rightRow = readNextDataRow doseWithUncertaintySource

                match leftRow, rightRow with
                | None, None -> completed <- true
                | None, Some _ ->
                    return!
                        Error
                            $"CSV row count mismatch at row index {rowIndex} in '{doseWithUncertaintyPath}'. dose_merged.csv ended early."
                | Some _, None ->
                    return!
                        Error
                            $"CSV row count mismatch at row index {rowIndex} in '{doseMergedPath}'. dose_with_uncertainty.csv ended early."
                | Some leftValues, Some rightValues ->
                    if leftValues.Length <> doseMergedSource.DataColumnCount then
                        return!
                            Error
                                $"CSV column count mismatch at row index {rowIndex} for '{doseMergedPath}'. Expected {doseMergedSource.DataColumnCount}, got {leftValues.Length}."

                    if rightValues.Length <> doseWithUncertaintySource.DataColumnCount then
                        return!
                            Error
                                $"CSV column count mismatch at row index {rowIndex} for '{doseWithUncertaintyPath}'. Expected {doseWithUncertaintySource.DataColumnCount}, got {rightValues.Length}."

                    let! leftCoordinates =
                        extractCoordinateColumns leftValues doseMergedSource.DoseColumnIndex
                        |> Result.mapError (fun message -> $"Coordinate parse failed at row index {rowIndex} for '{doseMergedPath}': {message}")

                    do! ensureMatchingCoordinates leftCoordinates doseWithUncertaintySource rightValues rowIndex

                    let! leftDose =
                        parseFloat leftValues[doseMergedSource.DoseColumnIndex]
                        |> Result.mapError (fun message -> $"Failed parsing merged dose at row index {rowIndex} for '{doseMergedPath}': {message}")

                    let! rightDose =
                        parseFloat rightValues[doseWithUncertaintySource.DoseColumnIndex]
                        |> Result.mapError (fun message ->
                            $"Failed parsing dose_to_medium_Gy at row index {rowIndex} for '{doseWithUncertaintyPath}': {message}")

                    let difference = abs (leftDose - rightDose)
                    let tolerance = doseValidationTolerance leftDose rightDose

                    if difference > tolerance then
                        return!
                            Error
                                $"Dose mismatch at row index {rowIndex}. dose_merged.csv={formatFloat leftDose}, dose_with_uncertainty.csv={formatFloat rightDose}, tolerance={formatFloat tolerance}."

                    rowIndex <- rowIndex + 1

                    if rowIndex % 100000 = 0 then
                        logCollectStatisticsStage "ValidationProgress" $"rowIndex={rowIndex}"

            logCollectStatisticsStage "ValidationEnd" $"rowCount={rowIndex}"
            return ()
        }
    finally
        disposeReaders readers
