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
let buildRunId (phaseSpaceIndex: string) (seed: string) : string = $"phsp{phaseSpaceIndex}_seed{seed}"

/// Builds the generated input file name for a run.
let buildInputFileName (seed: string) (phaseSpaceIndex: string) (nodeDigit: string) : string =
    $"input_sd{seed}_ps{phaseSpaceIndex}_n{nodeDigit}.txt"

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

/// Builds the run folder path for a run id.
let buildRunFolderPath (settings: TsebtSettings) (runId: string) : string =
    combineAppRoot settings.AppRoot (Path.Combine(settings.Paths.Runs, runId))

/// Builds the output file base path for a run id.
let buildOutputFilePath (settings: TsebtSettings) (runId: string) : string =
    Path.Combine(buildRunFolderPath settings runId, "dose")

/// Builds the input folder path for a seed base.
let buildInputFolderPath (settings: TsebtSettings) (seedBase: string) : string =
    combineAppRoot settings.AppRoot (Path.Combine(settings.Paths.Inputs, seedBase))

/// Builds the full input file path for generated TOPAS input output.
let buildInputFilePath (settings: TsebtSettings) (seedBase: string) (seed: string) (phaseSpaceIndex: string) (nodeDigit: string) : string =
    let inputFolder = buildInputFolderPath settings seedBase
    let inputFileName = buildInputFileName seed phaseSpaceIndex nodeDigit
    Path.Combine(inputFolder, inputFileName)
