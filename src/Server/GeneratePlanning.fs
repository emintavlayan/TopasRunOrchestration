module GeneratePlanning

open System
open System.IO
open TsebtConfig
open Bootstrap

/// Replaces one placeholder token in text.
let private replaceToken (token: string) (value: string) (text: string) : string = text.Replace(token, value)

/// Builds a seed from seed base and node digit.
let buildSeed (seedBase: string) (nodeDigit: string) : string = $"{seedBase}{nodeDigit}"

/// Builds a run id from phase-space index and seed.
let buildRunId (phaseSpaceIndex: string) (seed: string) : string = $"seed{seed}_phsp{phaseSpaceIndex}"

/// Builds the generated input file name for a run.
let buildInputFileName (seed: string) (phaseSpaceIndex: string) : string = $"seed{seed}_phsp{phaseSpaceIndex}.txt"

/// Stitches template texts in deterministic list order.
let stitchTemplateTexts (templateTexts: string list) : string =
    String.concat $"{Environment.NewLine}{Environment.NewLine}" templateTexts

/// Applies configured placeholders to stitched TOPAS template text.
let applyConfiguredPlaceholders
    (placeholders: TsebtPlaceholders)
    (phaseSpaceFile: string)
    (outputFilePath: string)
    (seed: string)
    (stitchedTemplateText: string)
    : string =
    stitchedTemplateText
    |> replaceToken placeholders.PhaseSpaceFile phaseSpaceFile
    |> replaceToken placeholders.OutputFile outputFilePath
    |> replaceToken placeholders.Seed seed

/// Builds the run folder path for a seed base.
let buildRunFolderPath (settings: TsebtSettings) (seedBase: string) : string =
    combineAppRoot settings.AppRoot (Path.Combine(settings.Paths.Runs, seedBase))

/// Builds the output file base path for a run.
let buildOutputFilePath (settings: TsebtSettings) (seedBase: string) (runId: string) : string =
    Path.Combine(buildRunFolderPath settings seedBase, runId)

/// Builds the input folder path for a seed base.
let buildInputFolderPath (settings: TsebtSettings) (seedBase: string) : string =
    combineAppRoot settings.AppRoot (Path.Combine(settings.Paths.Inputs, seedBase))

/// Builds the full input file path for generated TOPAS input output.
let buildInputFilePath (settings: TsebtSettings) (seedBase: string) (seed: string) (phaseSpaceIndex: string) : string =
    let inputFolder = buildInputFolderPath settings seedBase
    let inputFileName = buildInputFileName seed phaseSpaceIndex
    Path.Combine(inputFolder, inputFileName)
