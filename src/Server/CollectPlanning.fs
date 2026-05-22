module CollectPlanning

open System.IO
open Shared
open TsebtConfig

/// Represents one generated run row needed for collect preflight and planning.
type CollectRunRow = {
    RunId: string
    PhaseSpaceIndex: string
    NodeDigit: string
    OutputFilePath: string
}

/// Resolves the absolute run folder path for one seed base.
let runFolderPath (settings: TsebtSettings) (seedBase: string) : string =
    Path.Combine(settings.AppRoot, settings.Paths.Runs, seedBase)

/// Resolves the absolute collect output folder path for one seed base.
let outputFolderPath (settings: TsebtSettings) (seedBase: string) : string =
    Path.Combine(settings.AppRoot, settings.Paths.Outputs, seedBase)

/// Builds expected csv and log file paths from one generated run row.
let expectedCsvAndLogPaths (row: CollectRunRow) : string * string =
    row.OutputFilePath + ".csv", row.OutputFilePath + ".log"

/// Returns distinct phase-space indexes in stable ascending order.
let collectPhaseSpaceIndexes (rows: CollectRunRow list) : string list =
    rows
    |> List.map _.PhaseSpaceIndex
    |> List.distinct
    |> List.sort

/// Builds planned merged csv output paths for each phase-space index.
let plannedMergedFiles (settings: TsebtSettings) (seedBase: string) (rows: CollectRunRow list) : string list =
    let outputFolder = outputFolderPath settings seedBase

    rows
    |> collectPhaseSpaceIndexes
    |> List.map (fun phaseSpaceIndex -> Path.Combine(outputFolder, $"phsp{phaseSpaceIndex}_merged.csv"))

/// Builds planned dose summary output path.
let plannedSummaryPath (settings: TsebtSettings) (seedBase: string) : string =
    Path.Combine(outputFolderPath settings seedBase, "dose_summary.csv")

/// Builds planned collect manifest output path.
let plannedManifestPath (settings: TsebtSettings) (seedBase: string) : string =
    Path.Combine(outputFolderPath settings seedBase, "collect_manifest.tsv")

/// Builds a failed check with a message.
let failedCheck (name: string) (message: string) : CollectPreflightCheck =
    {
        Name = name
        Ok = false
        Message = Some message
    }

/// Builds a successful check with no message.
let passedCheck (name: string) : CollectPreflightCheck = { Name = name; Ok = true; Message = None }
