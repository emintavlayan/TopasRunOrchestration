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

type RunManifestPreviewRow = {
    TaskId: int
    NodeName: string
    RunId: string
    InputFilePath: string
    LogFilePath: string
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

type RunNodeScriptPreview = {
    NodeName: string
    ManifestPath: string
    ScriptPath: string
    TaskCount: int
    ManifestRowsPreview: RunManifestPreviewRow list
    ScriptText: string
}

type RunScriptPreview = {
    SeedBase: string
    ManifestPath: string
    ScriptPath: string
    ScriptText: string
    RunCount: int
    ManifestRowsPreview: RunManifestPreviewRow list
    NodeScriptPreviews: RunNodeScriptPreview list
    Preflight: RunPreflightResult
}

type SubmitRunRequest = { SeedBase: string }

type SubmitRunResult = {
    SeedBase: string
    RunStatus: string
    SlurmJobId: string
    SlurmJobIds: string list
    SubmittedRunCount: int
    ManifestPath: string
    ScriptPath: string
    SbatchOutput: string
    SubmittedAt: string
}

type CollectBatchSummary = {
    SeedBase: string
    CreatedAt: string
    GeneratedRunCount: int
    NodeCount: int
    PhaseSpaceCount: int
    RunStatus: string option
    CollectStatus: string
    CollectSummaryPath: string option
}

type CollectBatchDetails = {
    SeedBase: string
    CreatedAt: string
    GeneratedRunCount: int
    NodeCount: int
    PhaseSpaceCount: int
    RunStatus: string option
    CollectStatus: string
    CollectedAt: string option
    CollectOutputFolder: string option
    CollectSummaryPath: string option
    CollectCsvFoundCount: int option
    CollectCsvMissingCount: int option
    CollectLogFoundCount: int option
    CollectLogMissingCount: int option
}

type CollectPreflightCheck = {
    Name: string
    Ok: bool
    Message: string option
}

type MissingCollectFile = {
    FileKind: string
    Path: string
}

type CollectPreflightResult = {
    SeedBase: string
    CanCollect: bool
    ExpectedRunCount: int
    FoundCsvCount: int
    MissingCsvCount: int
    FoundLogCount: int
    MissingLogCount: int
    Checks: CollectPreflightCheck list
    MissingFiles: MissingCollectFile list
}

type CollectPreviewRequest = { SeedBase: string }

type CollectPreviewResult = {
    SeedBase: string
    ExpectedRunCount: int
    PhaseSpaceCount: int
    NodeCount: int
    OutputFolder: string
    PlannedMergedFiles: string list
    FinalSummaryPath: string
    ManifestPath: string
    Preflight: CollectPreflightResult
}

type CollectRequest = { SeedBase: string }

type CollectedPhaseSpaceResult = {
    PhaseSpaceIndex: string
    MergedFilePath: string
    SourceCsvCount: int
}

type CollectResult = {
    SeedBase: string
    ExpectedRunCount: int
    CsvReadCount: int
    LogFoundCount: int
    MergedPhaseSpaceCount: int
    OutputFolder: string
    SummaryPath: string
    MergedFiles: CollectedPhaseSpaceResult list
    ManifestPath: string
    Status: string
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
    getCollectBatches: unit -> Async<Result<CollectBatchSummary list, string>>
    getCollectBatchDetails: string -> Async<Result<CollectBatchDetails, string>>
    previewCollect: CollectPreviewRequest -> Async<Result<CollectPreviewResult, string>>
    collectBatch: CollectRequest -> Async<Result<CollectResult, string>>
}
