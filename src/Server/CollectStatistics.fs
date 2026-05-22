module CollectStatistics

open System
open System.Globalization
open System.IO
open FsToolkit.ErrorHandling
open CollectCsvMerge

/// Validates parsed file shape against expected row and column counts.
let private validateShape
    (expectedRows: int)
    (expectedColumns: int)
    (rows: string array list)
    : Result<unit, string> =
    if rows.Length <> expectedRows then
        Error $"CSV row count mismatch. Expected {expectedRows}, got {rows.Length}."
    elif rows |> List.exists (fun row -> row.Length <> expectedColumns) then
        Error "CSV column count mismatch in one or more rows."
    else
        Ok()

/// Calculates sample standard deviation for values list.
let private sampleStandardDeviation (values: float list) (mean: float) : float =
    match values with
    | []
    | [ _ ] -> 0.0
    | _ ->
        let sumSquares = values |> List.sumBy (fun value -> pown (value - mean) 2)
        sqrt (sumSquares / float (values.Length - 1))

/// Calculates median for sorted values.
let private medianOfSorted (sortedValues: float list) : float =
    let count = sortedValues.Length

    if count % 2 = 1 then
        sortedValues[count / 2]
    else
        let upper = sortedValues[count / 2]
        let lower = sortedValues[(count / 2) - 1]
        (lower + upper) / 2.0

/// Builds one summary output row from dose values aligned by row index.
let private buildSummaryRow
    (baseRow: string array)
    (doseColumnIndex: int)
    (doseValues: float list)
    : string array =
    let count = doseValues.Length
    let mean = doseValues |> List.average
    let sorted = doseValues |> List.sort
    let median = medianOfSorted sorted
    let sd = sampleStandardDeviation doseValues mean

    let preservedColumns =
        baseRow
        |> Array.mapi (fun index value -> index, value)
        |> Array.choose (fun (index, value) -> if index = doseColumnIndex then None else Some value)
        |> Array.toList

    [
        yield! preservedColumns
        mean.ToString("G17", CultureInfo.InvariantCulture)
        median.ToString("G17", CultureInfo.InvariantCulture)
        sd.ToString("G17", CultureInfo.InvariantCulture)
        string count
    ]
    |> List.toArray

/// Builds updated header lines with statistics column names.
let private buildSummaryHeaders (parsed: ParsedMergeCsv) : string list =
    match parsed.HeaderLines with
    | [] -> []
    | headerLines ->
        let lastLine = headerLines |> List.last
        let lastColumns = lastLine.Split(',')

        if lastColumns.Length = parsed.DataRows.Head.Length then
            let prefix = headerLines |> List.take (headerLines.Length - 1)

            let nonDoseColumns =
                lastColumns
                |> Array.mapi (fun index value -> index, value)
                |> Array.choose (fun (index, value) -> if index = parsed.DoseColumnIndex then None else Some value)
                |> String.concat ","

            prefix @ [ $"{nonDoseColumns},mean,median,standard_deviation,count" ]
        else
            headerLines

/// Computes summary statistics across phase-space merged csv files and writes dose_summary.csv.
let computeDoseSummary (mergedCsvPaths: string list) (outputSummaryPath: string) : Result<unit, string> =
    result {
        match mergedCsvPaths with
        | [] -> return! Error "No merged phase-space csv files were provided."
        | firstPath :: otherPaths ->
            let! firstText =
                try
                    File.ReadAllText firstPath |> Ok
                with ex ->
                    Error $"Failed reading merged csv '{firstPath}': {ex.Message}"

            let! firstParsed = parseCsvForMerge firstText
            let expectedRows = firstParsed.DataRows.Length
            let expectedColumns = firstParsed.DataRows.Head.Length
            let doseColumnIndex = firstParsed.DoseColumnIndex

            let! parsedOthers =
                otherPaths
                |> List.map (fun path -> result {
                    let! text =
                        try
                            File.ReadAllText path |> Ok
                        with ex ->
                            Error $"Failed reading merged csv '{path}': {ex.Message}"

                    let! parsed = parseCsvForMerge text
                    do! validateShape expectedRows expectedColumns parsed.DataRows
                    return parsed
                })
                |> List.sequenceResultM

            let allRowsByFile = firstParsed :: parsedOthers |> List.map _.DataRows

            let outputRows =
                [ 0 .. expectedRows - 1 ]
                |> List.map (fun rowIndex ->
                    let baseRow = firstParsed.DataRows[rowIndex]

                    let doseValues =
                        allRowsByFile
                        |> List.map (fun rows ->
                            rows[rowIndex].[doseColumnIndex].Trim()
                            |> fun value -> Double.Parse(value, CultureInfo.InvariantCulture))

                    buildSummaryRow baseRow doseColumnIndex doseValues
                    |> String.concat ",")

            let outputLines = buildSummaryHeaders firstParsed @ outputRows
            let outputText = String.concat Environment.NewLine outputLines

            do!
                try
                    let parentFolder = Path.GetDirectoryName outputSummaryPath

                    if not (String.IsNullOrWhiteSpace parentFolder) then
                        Directory.CreateDirectory(parentFolder) |> ignore

                    File.WriteAllText(outputSummaryPath, outputText)
                    Ok()
                with ex ->
                    Error $"Failed writing summary csv '{outputSummaryPath}': {ex.Message}"
    }
