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

/// Parses csv text into header lines, data rows, and dose column index.
let parseCsvForMerge (csvText: string) : Result<ParsedMergeCsv, string> =
    let lines =
        csvText.Replace("\r\n", "\n").Split('\n', StringSplitOptions.None)
        |> Array.toList
        |> List.filter (fun line -> not (String.IsNullOrWhiteSpace line))

    let headerLines, dataRows =
        lines
        |> List.fold
            (fun (headers, rows) line ->
                let columns = splitCsvLine line

                match lastNumericColumnIndex columns with
                | Some _ -> headers, rows @ [ columns ]
                | None -> headers @ [ line ], rows)
            ([], [])

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

/// Merges parsed csv data rows by summing the dose column for each row.
let private mergeDoseColumnRows
    (doseColumnIndex: int)
    (baseRows: string array list)
    (otherFilesRows: (string array list) list)
    : Result<string array list, string> =
    try
        let mergedRows =
            [ 0 .. baseRows.Length - 1 ]
            |> List.map (fun rowIndex ->
                let baseRow = baseRows[rowIndex] |> Array.copy

                let sumDose =
                    otherFilesRows
                    |> List.fold
                        (fun acc rows ->
                            let value = rows[rowIndex].[doseColumnIndex].Trim()
                            acc + Double.Parse(value, CultureInfo.InvariantCulture))
                        (Double.Parse(baseRow[doseColumnIndex].Trim(), CultureInfo.InvariantCulture))

                baseRow[doseColumnIndex] <- sumDose.ToString("G17", CultureInfo.InvariantCulture)
                baseRow)

        Ok mergedRows
    with ex ->
        Error $"Failed merging dose column values: {ex.Message}"

/// Merges csv files for one phase-space and writes the merged csv output file.
let mergeNodeCsvFilesForPhaseSpace (inputCsvPaths: string list) (outputCsvPath: string) : Result<unit, string> =
    result {
        match inputCsvPaths with
        | [] -> return! Error "No input csv files were provided for merge."
        | firstPath :: otherPaths ->
            let! firstText =
                try
                    File.ReadAllText firstPath |> Ok
                with ex ->
                    Error $"Failed reading csv file '{firstPath}': {ex.Message}"

            let! firstParsed = parseCsvForMerge firstText
            let expectedRows = firstParsed.DataRows.Length
            let expectedCols = firstParsed.DataRows.Head.Length

            let! parsedOthers =
                otherPaths
                |> List.map (fun path -> result {
                    let! text =
                        try
                            File.ReadAllText path |> Ok
                        with ex ->
                            Error $"Failed reading csv file '{path}': {ex.Message}"

                    let! parsed = parseCsvForMerge text
                    do! validateSameShape expectedRows expectedCols parsed.DataRows
                    return parsed.DataRows
                })
                |> List.sequenceResultM

            let! mergedRows = mergeDoseColumnRows firstParsed.DoseColumnIndex firstParsed.DataRows parsedOthers

            let outputLines =
                (firstParsed.HeaderLines @ (mergedRows |> List.map (fun row -> String.concat "," row)))
                |> String.concat Environment.NewLine

            do!
                try
                    let parentFolder = Path.GetDirectoryName outputCsvPath

                    if not (String.IsNullOrWhiteSpace parentFolder) then
                        Directory.CreateDirectory(parentFolder) |> ignore

                    File.WriteAllText(outputCsvPath, outputLines)
                    Ok()
                with ex ->
                    Error $"Failed writing merged csv '{outputCsvPath}': {ex.Message}"
    }
