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

/// Resolves one collect attempt output folder path under the seed-base output root.
let collectionOutputFolderPath (baseOutputFolder: string) (collectionFolderName: string) : string =
    Path.Combine(baseOutputFolder, collectionFolderName)

/// Resolves the absolute node-merged output folder path inside one collect attempt folder.
let mergedOutputFolderPathInCollection (collectionOutputFolder: string) : string =
    Path.Combine(collectionOutputFolder, "merged-over-nodes")

/// Resolves the absolute phase-space-merged output folder path inside one collect attempt folder.
let mergedOverPhaseSpaceOutputFolderPathInCollection (collectionOutputFolder: string) : string =
    Path.Combine(collectionOutputFolder, "merged-over-phsp")

/// Resolves the latest-collection marker file path for one seed base.
let latestCollectionMarkerPath (settings: TsebtSettings) (seedBase: string) : string =
    Path.Combine(outputFolderPath settings seedBase, "latest_collection.txt")

/// Builds expected csv and log file paths from one generated run row.
let expectedCsvAndLogPaths (row: CollectRunRow) : string * string =
    row.OutputFilePath + ".csv", row.OutputFilePath + ".log"

/// Returns distinct phase-space indexes in stable ascending order.
let collectPhaseSpaceIndexes (rows: CollectRunRow list) : string list =
    rows
    |> List.map _.PhaseSpaceIndex
    |> List.distinct
    |> List.sort

/// Builds planned merged csv output paths within one collect attempt output folder.
let plannedMergedFilesInOutputFolder (collectionOutputFolder: string) (rows: CollectRunRow list) : string list =
    let outputFolder = mergedOutputFolderPathInCollection collectionOutputFolder

    rows
    |> collectPhaseSpaceIndexes
    |> List.map (fun phaseSpaceIndex -> Path.Combine(outputFolder, $"phsp{phaseSpaceIndex}_merged.csv"))

/// Builds planned merged csv output paths for one preview placeholder folder.
let plannedMergedFiles (settings: TsebtSettings) (seedBase: string) (rows: CollectRunRow list) : string list =
    plannedMergedFilesInOutputFolder (collectionOutputFolderPath (outputFolderPath settings seedBase) "<timestamp>") rows

/// Builds planned final merged dose output path within one collect attempt output folder.
let plannedSummaryPathInOutputFolder (collectionOutputFolder: string) : string =
    Path.Combine(mergedOverPhaseSpaceOutputFolderPathInCollection collectionOutputFolder, "dose_merged.csv")

/// Builds planned final merged dose output path for one preview placeholder folder.
let plannedSummaryPath (settings: TsebtSettings) (seedBase: string) : string =
    plannedSummaryPathInOutputFolder (collectionOutputFolderPath (outputFolderPath settings seedBase) "<timestamp>")

/// Builds planned dose uncertainty output path within one collect attempt output folder.
let plannedUncertaintyPathInOutputFolder (collectionOutputFolder: string) : string =
    Path.Combine(collectionOutputFolder, "dose_with_uncertainty.csv")

/// Builds planned dose uncertainty output path for one preview placeholder folder.
let plannedUncertaintyPath (settings: TsebtSettings) (seedBase: string) : string =
    plannedUncertaintyPathInOutputFolder (collectionOutputFolderPath (outputFolderPath settings seedBase) "<timestamp>")

/// Builds planned collect manifest output path within one collect attempt output folder.
let plannedManifestPathInOutputFolder (collectionOutputFolder: string) : string =
    Path.Combine(collectionOutputFolder, "collect_manifest.tsv")

/// Builds planned collect manifest output path for one preview placeholder folder.
let plannedManifestPath (settings: TsebtSettings) (seedBase: string) : string =
    plannedManifestPathInOutputFolder (collectionOutputFolderPath (outputFolderPath settings seedBase) "<timestamp>")

/// Builds a failed check with a message.
let failedCheck (name: string) (message: string) : CollectPreflightCheck =
    {
        Name = name
        Ok = false
        Message = Some message
    }

/// Builds a successful check with no message.
let passedCheck (name: string) : CollectPreflightCheck = { Name = name; Ok = true; Message = None }
