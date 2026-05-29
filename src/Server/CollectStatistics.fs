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

/// Returns zero when value is NaN or infinity.
let private finiteOrZero (value: float) : float =
    if Double.IsNaN value || Double.IsInfinity value then 0.0 else value

/// Calculates median for sorted values.
let private medianOfSorted (sortedValues: float list) : float =
    let count = sortedValues.Length

    if count % 2 = 1 then
        sortedValues[count / 2]
    else
        let upper = sortedValues[count / 2]
        let lower = sortedValues[(count / 2) - 1]
        (lower + upper) / 2.0

/// Resolves dose_sum_Gy column index from a usable merged csv header.
let private resolveDoseSumColumnIndex (parsed: ParsedMergeCsv) : Result<int, string> =
    match parsed.HeaderLines |> List.tryLast with
    | None -> Error "Merged csv header is missing, expected column 'dose_sum_Gy'."
    | Some headerLine ->
        let headerColumns = headerLine.Split(',')

        if headerColumns.Length <> parsed.DataRows.Head.Length then
            Error "Merged csv header shape does not match data rows, expected column 'dose_sum_Gy'."
        else
            match
                headerColumns
                |> Array.mapi (fun index name -> index, name.Trim())
                |> Array.tryFind (fun (_, name) -> String.Equals(name, "dose_sum_Gy", StringComparison.OrdinalIgnoreCase))
            with
            | Some(index, _) -> Ok index
            | None ->
                let headerText = headerColumns |> Array.map _.Trim() |> String.concat ", "
                Error $"Merged csv is missing required column 'dose_sum_Gy'. Header columns: {headerText}"

/// Formats one floating-point value with invariant culture.
let private formatFloat (value: float) : string =
    finiteOrZero value
    |> fun safeValue -> safeValue.ToString("G17", CultureInfo.InvariantCulture)

/// Builds one summary output row from dose_sum_Gy values aligned by row index.
let private buildSummaryRow
    (baseRow: string array)
    (doseColumnIndex: int)
    (doseValues: float list)
    : string array =
    let count = doseValues.Length
    let totalDose = doseValues |> List.sum
    let mean = if count = 0 then 0.0 else totalDose / float count
    let sorted = doseValues |> List.sort
    let median = medianOfSorted sorted
    let sd = sampleStandardDeviation doseValues mean
    let sem = if count < 2 then 0.0 else sd / sqrt (float count)

    let relSemPercent =
        if mean = 0.0 then
            0.0
        else
            100.0 * sem / abs mean

    let preservedColumns =
        baseRow
        |> Array.mapi (fun index value -> index, value)
        |> Array.choose (fun (index, value) -> if index = doseColumnIndex then None else Some value)
        |> Array.toList

    [
        yield! preservedColumns
        formatFloat totalDose
        formatFloat mean
        formatFloat median
        formatFloat sd
        formatFloat sem
        formatFloat relSemPercent
        string count
    ]
    |> List.toArray

/// Builds updated header lines with statistics column names.
let private buildSummaryHeaders (parsed: ParsedMergeCsv) (doseColumnIndex: int) : string list =
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
                |> Array.choose (fun (index, value) -> if index = doseColumnIndex then None else Some value)
                |> String.concat ","

            prefix
            @ [
                $"{nonDoseColumns},total_dose_sum_Gy,phsp_mean_Gy,phsp_median_Gy,phsp_sd_Gy,phsp_sem_Gy,phsp_rel_sem_percent,phsp_count"
              ]
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
            let! firstDoseSumColumnIndex = resolveDoseSumColumnIndex firstParsed

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
                    let! doseSumColumnIndex = resolveDoseSumColumnIndex parsed
                    return parsed, doseSumColumnIndex
                })
                |> List.sequenceResultM

            let allRowsByFile = (firstParsed, firstDoseSumColumnIndex) :: parsedOthers

            let outputRows =
                [ 0 .. expectedRows - 1 ]
                |> List.map (fun rowIndex ->
                    let baseRow = firstParsed.DataRows[rowIndex]

                    let doseValues =
                        allRowsByFile
                        |> List.map (fun (parsed, doseSumColumnIndex) ->
                            parsed.DataRows[rowIndex].[doseSumColumnIndex].Trim()
                            |> fun value -> Double.Parse(value, CultureInfo.InvariantCulture))

                    buildSummaryRow baseRow firstDoseSumColumnIndex doseValues
                    |> String.concat ",")

            let outputLines = buildSummaryHeaders firstParsed firstDoseSumColumnIndex @ outputRows
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
