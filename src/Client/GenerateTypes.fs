module GenerateTypes

open SAFE
open Shared

type Page =
    | Generate
    | Run
    | Collect

type GenerateStep =
    | Welcome
    | SelectComponents
    | SelectNodes
    | SelectPhaseSpaceFiles
    | Review
    | Result

type GenerateModel = {
    Step: GenerateStep
    Config: RemoteData<AppConfigView>
    TemplateFiles: RemoteData<TemplateFileInfo list>
    SelectedComponents: Set<string>
    SelectedNodes: Set<string>
    SelectedPhaseSpaceFiles: Set<string>
    Preview: RemoteData<GeneratePreviewResult>
    GenerateResult: RemoteData<GenerateResult>
    Error: string option
}

type Model = {
    SelectedPage: Page
    Generate: GenerateModel
}

type Msg =
    | SelectPage of Page
    | LoadAppConfig of ApiCall<unit, Result<AppConfigView, string>>
    | LoadTemplateFiles of ApiCall<unit, Result<TemplateFileInfo list, string>>
    | StartGenerateWizard
    | CancelGenerateWizard
    | PreviousGenerateStep
    | NextGenerateStep
    | ToggleComponent of string
    | ToggleNode of string
    | TogglePhaseSpaceFile of string
    | SelectAllNodes
    | SelectNoNodes
    | SelectAllPhaseSpaceFiles
    | SelectNoPhaseSpaceFiles
    | LoadPreview of ApiCall<GeneratePreviewRequest, Result<GeneratePreviewResult, string>>
    | RunGenerate of ApiCall<GenerateRequest, Result<GenerateResult, string>>
