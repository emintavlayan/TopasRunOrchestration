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
    SelectedNodeDigits: string list
    SelectedPhaseSpaceIndexes: string list
}

type GeneratePreviewResult = {
    RunId: string
    Seed: string
    InputFileName: string
    OutputFilePath: string
    StitchedPreviewText: string
    ExpectedGeneratedCount: int
}

type GenerateRequest = {
    SelectedTemplatePaths: string list
    SelectedNodeDigits: string list
    SelectedPhaseSpaceIndexes: string list
}

type GeneratedRunInfo = {
    RunId: string
    InputFilePath: string
    OutputFilePath: string
    RunFolder: string
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

type RunBatchSummary = {
    SeedBase: string
    CreatedAt: string
    GeneratedInputCount: int
    NodeCount: int
    PhaseSpaceCount: int
    RunStatus: string
    SlurmJobId: string option
}

type RunManifestRow = {
    RunId: string
    InputFilePath: string
    OutputFilePath: string
    RunFolder: string
    Seed: string
    NodeDigit: string
    PhaseSpaceIndex: string
}

type RunBatchDetails = {
    SeedBase: string
    CreatedAt: string
    GeneratedInputCount: int
    NodeCount: int
    PhaseSpaceCount: int
    RunStatus: string
    SlurmJobId: string option
    ManifestPath: string option
    ScriptPath: string option
    SubmittedAt: string option
    Rows: RunManifestRow list
}

type RunPreflightCheck = {
    Name: string
    Ok: bool
    Message: string option
}

type RunPreflightResult = {
    SeedBase: string
    CanSubmit: bool
    Checks: RunPreflightCheck list
}

type RunScriptPreview = {
    SeedBase: string
    ManifestPath: string
    ScriptPath: string
    ScriptText: string
    RunCount: int
    Preflight: RunPreflightResult
}

type SubmitRunRequest = { SeedBase: string }

type SubmitRunResult = {
    SeedBase: string
    RunStatus: string
    SlurmJobId: string option
    ManifestPath: string option
    ScriptPath: string option
    SubmittedAt: string option
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
    getRunBatches: unit -> Async<Result<RunBatchSummary list, string>>
    getRunBatchDetails: string -> Async<Result<RunBatchDetails, string>>
    previewRun: string -> Async<Result<RunScriptPreview, string>>
    submitRun: SubmitRunRequest -> Async<Result<SubmitRunResult, string>>
}
