namespace Shared

type NodeInfo = { Name: string; Digit: string }

type PhaseSpaceFileInfo = { Index: string; Value: string }

type TemplateFileInfo = {
    Group: string
    FileName: string
    RelativePath: string
}

type GeneratePreviewRequest = {
    SelectedTemplatePaths: string list
    NodeDigit: string
    PhaseSpaceIndex: string
}

type GeneratePreviewResult = {
    RunId: string
    Seed: string
    InputFileName: string
    OutputFilePath: string
    StitchedPreviewText: string
}

type GenerateRequest = {
    SelectedTemplatePaths: string list
    SelectedNodeDigits: string list
    SelectedPhaseSpaceIndexes: string list
}

type GeneratedRunInfo = {
    RunId: string
    InputFilePath: string
    Seed: string
    NodeDigit: string
    PhaseSpaceIndex: string
}

type GenerateResult = {
    SeedBase: string
    GeneratedInputCount: int
    NodeCount: int
    PhaseSpaceCount: int
    InputFolder: string
    GeneratedRuns: GeneratedRunInfo list
}

type AppConfigView = {
    AppRoot: string
    SeedBase: string
    Nodes: NodeInfo list
    PhaseSpaceFiles: PhaseSpaceFileInfo list
    Placeholders: string list
}

type ITopasApi = {
    getAppConfig: unit -> Async<Result<AppConfigView, string>>
    getTemplateFiles: unit -> Async<Result<TemplateFileInfo list, string>>
    previewGenerate: GeneratePreviewRequest -> Async<Result<GeneratePreviewResult, string>>
    generate: GenerateRequest -> Async<Result<GenerateResult, string>>
}
