module GeneratePreview

open System
open System.IO
open FsToolkit.ErrorHandling
open Shared
open TsebtConfig
open Bootstrap
open GeneratePlanning

/// Reads and stitches selected template files from templates root.
let private readAndStitchTemplates
    (templatesRoot: string)
    (relativeTemplatePaths: string list)
    : Result<string, string> =
    try
        relativeTemplatePaths
        |> List.map (fun relativePath ->
            let normalizedPath = relativePath.Replace('/', Path.DirectorySeparatorChar)
            let fullPath = Path.Combine(templatesRoot, normalizedPath)

            if File.Exists fullPath then
                Ok(File.ReadAllText(fullPath))
            else
                Error $"Template file not found: {relativePath}")
        |> List.sequenceResultM
        |> Result.map stitchTemplateTexts
    with ex ->
        Error $"Failed reading template files: {ex.Message}"

/// Finds a configured node by its node digit.
let private tryFindNode (settings: TsebtSettings) (nodeDigit: string) : Result<TsebtNode, string> =
    settings.Nodes
    |> List.tryFind (fun node -> node.Digit = nodeDigit)
    |> Result.requireSome $"Node digit not found in configuration: {nodeDigit}"

/// Finds a configured phase-space file by its phase-space index.
let private tryFindPhaseSpaceFile
    (settings: TsebtSettings)
    (phaseSpaceIndex: string)
    : Result<TsebtPhaseSpaceFile, string> =
    settings.PhaseSpaceFiles
    |> List.tryFind (fun file -> file.Index = phaseSpaceIndex)
    |> Result.requireSome $"Phase-space index not found in configuration: {phaseSpaceIndex}"

/// Returns the first selected value from a list.
let private firstSelected (name: string) (values: string list) : Result<string, string> =
    values
    |> List.tryHead
    |> Result.requireSome $"At least one {name} must be selected."

/// Builds a real preview result from selected templates, nodes, and phase-space files.
let createPreview
    (settings: TsebtSettings)
    (seedBase: string)
    (request: GeneratePreviewRequest)
    : Result<GeneratePreviewResult, string> =
    result {
        let! firstNodeDigit = firstSelected "node" request.SelectedNodeDigits
        let! firstPhaseSpaceIndex = firstSelected "phase-space file" request.SelectedPhaseSpaceIndexes
        let! node = tryFindNode settings firstNodeDigit
        let! phaseSpaceFile = tryFindPhaseSpaceFile settings firstPhaseSpaceIndex

        let templatesRoot = combineAppRoot settings.AppRoot settings.Paths.Templates
        let! stitchedTemplateText = readAndStitchTemplates templatesRoot request.SelectedTemplatePaths

        let seed = buildSeed seedBase node.Digit
        let runId = buildRunId phaseSpaceFile.Index seed
        let outputFilePath = buildOutputFilePath settings seedBase runId
        let previewText = applyConfiguredPlaceholders settings.Placeholders phaseSpaceFile.Value outputFilePath seed stitchedTemplateText

        let expectedGeneratedCount =
            request.SelectedNodeDigits.Length * request.SelectedPhaseSpaceIndexes.Length

        return {
            RunId = runId
            Seed = seed
            InputFileName = buildInputFileName seed phaseSpaceFile.Index
            OutputFilePath = outputFilePath
            StitchedPreviewText = previewText
            ExpectedGeneratedCount = expectedGeneratedCount
        }
    }
