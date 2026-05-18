module Index

open Elmish
open GenerateLogic
open GenerateTypes
open SAFE
open Shared

/// Represents the page type used by the Index module.
type Page = GenerateTypes.Page
/// Represents the generate step type used by the Index module.
type GenerateStep = GenerateTypes.GenerateStep
/// Represents the generate model type used by the Index module.
type GenerateModel = GenerateTypes.GenerateModel
/// Represents the root model type used by the Index module.
type Model = GenerateTypes.Model
/// Represents the message type used by the Index module.
type Msg = GenerateTypes.Msg

let Generate = GenerateTypes.Page.Generate
let Run = GenerateTypes.Page.Run
let Collect = GenerateTypes.Page.Collect

let Welcome = GenerateTypes.GenerateStep.Welcome
let SelectComponents = GenerateTypes.GenerateStep.SelectComponents
let SelectNodes = GenerateTypes.GenerateStep.SelectNodes
let SelectPhaseSpaceFiles = GenerateTypes.GenerateStep.SelectPhaseSpaceFiles
let Review = GenerateTypes.GenerateStep.Review
let Result = GenerateTypes.GenerateStep.Result

let SelectPage = GenerateTypes.Msg.SelectPage
let LoadAppConfig = GenerateTypes.Msg.LoadAppConfig
let LoadTemplateFiles = GenerateTypes.Msg.LoadTemplateFiles
let StartGenerateWizard = GenerateTypes.Msg.StartGenerateWizard
let CancelGenerateWizard = GenerateTypes.Msg.CancelGenerateWizard
let PreviousGenerateStep = GenerateTypes.Msg.PreviousGenerateStep
let NextGenerateStep = GenerateTypes.Msg.NextGenerateStep
let ToggleComponent = GenerateTypes.Msg.ToggleComponent
let ToggleNode = GenerateTypes.Msg.ToggleNode
let TogglePhaseSpaceFile = GenerateTypes.Msg.TogglePhaseSpaceFile
let SelectAllNodes = GenerateTypes.Msg.SelectAllNodes
let SelectNoNodes = GenerateTypes.Msg.SelectNoNodes
let SelectAllPhaseSpaceFiles = GenerateTypes.Msg.SelectAllPhaseSpaceFiles
let SelectNoPhaseSpaceFiles = GenerateTypes.Msg.SelectNoPhaseSpaceFiles
let LoadPreview = GenerateTypes.Msg.LoadPreview
let RunGenerate = GenerateTypes.Msg.RunGenerate

/// Creates a proxy for calling server API endpoints.
let topasApi = Api.makeProxy<ITopasApi> ()

/// Returns the initial client model and startup command.
let init () : Model * Cmd<Msg> =
    let model = { SelectedPage = Generate; Generate = initialGenerateModel () }
    let command = Cmd.batch [ Cmd.ofMsg (LoadAppConfig(Start())); Cmd.ofMsg (LoadTemplateFiles(Start())) ]
    model, command

/// Loads app configuration through the server API.
let loadAppConfigCmd () : Cmd<Msg> =
    Cmd.OfAsync.perform topasApi.getAppConfig () (Finished >> LoadAppConfig)

/// Loads template files through the server API.
let loadTemplateFilesCmd () : Cmd<Msg> =
    Cmd.OfAsync.perform topasApi.getTemplateFiles () (Finished >> LoadTemplateFiles)

/// Requests a generate preview from the server API.
let loadPreviewCmd (request: GeneratePreviewRequest) : Cmd<Msg> =
    Cmd.OfAsync.perform topasApi.previewGenerate request (Finished >> LoadPreview)

/// Requests generation from the server API.
let runGenerateCmd (request: GenerateRequest) : Cmd<Msg> =
    Cmd.OfAsync.perform topasApi.generate request (Finished >> RunGenerate)

/// Updates model state based on incoming messages.
let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | SelectPage page -> { model with SelectedPage = page }, Cmd.none
    | LoadAppConfig call ->
        match call with
        | Start() -> { model with Generate = { model.Generate with Config = model.Generate.Config.StartLoading(); Error = None } }, loadAppConfigCmd ()
        | Finished resultValue ->
            let remoteData, error = setRemoteResult resultValue model.Generate.Config
            { model with Generate = { model.Generate with Config = remoteData; Error = error } }, Cmd.none
    | LoadTemplateFiles call ->
        match call with
        | Start() ->
            { model with Generate = { model.Generate with TemplateFiles = model.Generate.TemplateFiles.StartLoading(); Error = None } }, loadTemplateFilesCmd ()
        | Finished resultValue ->
            let remoteData, error = setRemoteResult resultValue model.Generate.TemplateFiles
            { model with Generate = { model.Generate with TemplateFiles = remoteData; Error = error } }, Cmd.none
    | StartGenerateWizard -> { model with Generate = { model.Generate with Step = SelectComponents; Error = None } }, Cmd.none
    | CancelGenerateWizard -> { model with Generate = { model.Generate with Step = Welcome; Error = None } }, Cmd.none
    | PreviousGenerateStep -> handlePreviousStep model
    | NextGenerateStep -> handleNextStep loadPreviewCmd runGenerateCmd model
    | ToggleComponent relativePath ->
        let updated = toggleSelection relativePath model.Generate.SelectedComponents
        { model with Generate = { model.Generate with SelectedComponents = updated; Error = None } }, Cmd.none
    | ToggleNode nodeDigit ->
        let updated = toggleSelection nodeDigit model.Generate.SelectedNodes
        { model with Generate = { model.Generate with SelectedNodes = updated; Error = None } }, Cmd.none
    | TogglePhaseSpaceFile phaseSpaceIndex ->
        let updated = toggleSelection phaseSpaceIndex model.Generate.SelectedPhaseSpaceFiles
        { model with Generate = { model.Generate with SelectedPhaseSpaceFiles = updated; Error = None } }, Cmd.none
    | SelectAllNodes ->
        let allNodes =
            match model.Generate.Config with
            | Loaded config -> config.Nodes |> List.map _.Digit |> Set.ofList
            | _ -> Set.empty

        { model with Generate = { model.Generate with SelectedNodes = allNodes; Error = None } }, Cmd.none
    | SelectNoNodes -> { model with Generate = { model.Generate with SelectedNodes = Set.empty; Error = None } }, Cmd.none
    | SelectAllPhaseSpaceFiles ->
        let allPhaseSpaceFiles =
            match model.Generate.Config with
            | Loaded config -> config.PhaseSpaceFiles |> List.map _.Index |> Set.ofList
            | _ -> Set.empty

        { model with Generate = { model.Generate with SelectedPhaseSpaceFiles = allPhaseSpaceFiles; Error = None } }, Cmd.none
    | SelectNoPhaseSpaceFiles ->
        { model with Generate = { model.Generate with SelectedPhaseSpaceFiles = Set.empty; Error = None } }, Cmd.none
    | LoadPreview call ->
        match call with
        | Start request ->
            { model with Generate = { model.Generate with Preview = model.Generate.Preview.StartLoading(); Error = None } }, loadPreviewCmd request
        | Finished resultValue ->
            let remoteData, error = setRemoteResult resultValue model.Generate.Preview
            { model with Generate = { model.Generate with Preview = remoteData; Error = error } }, Cmd.none
    | RunGenerate call ->
        match call with
        | Start request ->
            {
                model with
                    Generate = {
                        model.Generate with
                            GenerateResult = model.Generate.GenerateResult.StartLoading()
                            Error = None
                    }
            },
            runGenerateCmd request
        | Finished resultValue ->
            match resultValue with
            | Ok generated ->
                {
                    model with
                        Generate = {
                            model.Generate with
                                Step = Result
                                GenerateResult = Loaded generated
                                Error = None
                        }
                },
                Cmd.ofMsg (LoadAppConfig(Start()))
            | Error errorMessage ->
                { model with Generate = { model.Generate with Error = Some errorMessage } }, Cmd.none
