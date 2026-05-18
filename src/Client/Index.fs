module Index

open Elmish
open Feliz
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

/// Creates a proxy for calling server API endpoints.
let topasApi = Api.makeProxy<ITopasApi> ()

/// Creates the initial wizard model.
let initialGenerateModel () : GenerateModel =
    {
        Step = Welcome
        Config = NotStarted
        TemplateFiles = NotStarted
        SelectedComponents = Set.empty
        SelectedNodes = Set.empty
        SelectedPhaseSpaceFiles = Set.empty
        Preview = NotStarted
        GenerateResult = NotStarted
        Error = None
    }

/// Returns the initial client model and startup command.
let init () : Model * Cmd<Msg> =
    let model = { SelectedPage = Generate; Generate = initialGenerateModel () }
    let command = Cmd.batch [ Cmd.ofMsg (LoadAppConfig(Start())); Cmd.ofMsg (LoadTemplateFiles(Start())) ]
    model, command

/// Maps a Result value into remote data and error state.
let setRemoteResult (resultValue: Result<'a, string>) (loadingState: RemoteData<'a>) : RemoteData<'a> * string option =
    match resultValue with
    | Ok value -> Loaded value, None
    | Error errorMessage -> loadingState, Some errorMessage

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

/// Returns the first selected node digit for preview.
let firstSelectedNode (generate: GenerateModel) : string option =
    generate.SelectedNodes |> Seq.sort |> Seq.tryHead

/// Returns the first selected phase-space index for preview.
let firstSelectedPhaseSpaceIndex (generate: GenerateModel) : string option =
    generate.SelectedPhaseSpaceFiles |> Seq.sort |> Seq.tryHead

/// Builds a preview request from current wizard selection.
let buildPreviewRequest (generate: GenerateModel) : Result<GeneratePreviewRequest, string> =
    match firstSelectedNode generate, firstSelectedPhaseSpaceIndex generate with
    | Some _, Some _ ->
        Ok {
            SelectedTemplatePaths = generate.SelectedComponents |> Seq.sort |> List.ofSeq
            SelectedNodeDigits = generate.SelectedNodes |> Seq.sort |> List.ofSeq
            SelectedPhaseSpaceIndexes = generate.SelectedPhaseSpaceFiles |> Seq.sort |> List.ofSeq
        }
    | _ -> Error "Select at least one node and one phase-space file before preview."

/// Builds a generate request from current wizard selection.
let buildGenerateRequest (generate: GenerateModel) : GenerateRequest = {
    SelectedTemplatePaths = generate.SelectedComponents |> Seq.sort |> List.ofSeq
    SelectedNodeDigits = generate.SelectedNodes |> Seq.sort |> List.ofSeq
    SelectedPhaseSpaceIndexes = generate.SelectedPhaseSpaceFiles |> Seq.sort |> List.ofSeq
}

/// Returns true when component selection is valid for next step.
let canProceedComponents (generate: GenerateModel) : bool = not generate.SelectedComponents.IsEmpty

/// Returns true when node selection is valid for next step.
let canProceedNodes (generate: GenerateModel) : bool = not generate.SelectedNodes.IsEmpty

/// Returns true when phase-space selection is valid for next step.
let canProceedPhaseSpaceFiles (generate: GenerateModel) : bool = not generate.SelectedPhaseSpaceFiles.IsEmpty

/// Handles transitions when the next wizard button is pressed.
let handleNextStep (model: Model) : Model * Cmd<Msg> =
    match model.Generate.Step with
    | Welcome -> { model with Generate = { model.Generate with Step = SelectComponents; Error = None } }, Cmd.none
    | SelectComponents when canProceedComponents model.Generate ->
        { model with Generate = { model.Generate with Step = SelectNodes; Error = None } }, Cmd.none
    | SelectComponents -> { model with Generate = { model.Generate with Error = Some "Select at least one component." } }, Cmd.none
    | SelectNodes when canProceedNodes model.Generate ->
        { model with Generate = { model.Generate with Step = SelectPhaseSpaceFiles; Error = None } }, Cmd.none
    | SelectNodes -> { model with Generate = { model.Generate with Error = Some "Select at least one node." } }, Cmd.none
    | SelectPhaseSpaceFiles when canProceedPhaseSpaceFiles model.Generate ->
        match buildPreviewRequest model.Generate with
        | Ok previewRequest ->
            let updatedModel =
                {
                    model with
                        Generate = {
                            model.Generate with
                                Step = Review
                                Preview = model.Generate.Preview.StartLoading()
                                Error = None
                        }
                }

            updatedModel, loadPreviewCmd previewRequest
        | Error errorMessage -> { model with Generate = { model.Generate with Error = Some errorMessage } }, Cmd.none
    | SelectPhaseSpaceFiles ->
        { model with Generate = { model.Generate with Error = Some "Select at least one phase-space file." } }, Cmd.none
    | Review ->
        let request = buildGenerateRequest model.Generate

        let updatedModel =
            {
                model with
                    Generate = {
                        model.Generate with
                            GenerateResult = model.Generate.GenerateResult.StartLoading()
                            Error = None
                    }
            }

        updatedModel, runGenerateCmd request
    | Result -> model, Cmd.none

/// Handles transitions when the previous wizard button is pressed.
let handlePreviousStep (model: Model) : Model * Cmd<Msg> =
    let previousStep =
        match model.Generate.Step with
        | Welcome -> Welcome
        | SelectComponents -> Welcome
        | SelectNodes -> SelectComponents
        | SelectPhaseSpaceFiles -> SelectNodes
        | Review -> SelectPhaseSpaceFiles
        | Result -> Review

    { model with Generate = { model.Generate with Step = previousStep; Error = None } }, Cmd.none

/// Toggles a selected value in a set.
let toggleSelection (value: string) (selection: Set<string>) : Set<string> =
    if selection.Contains value then selection.Remove value else selection.Add value

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
    | NextGenerateStep -> handleNextStep model
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
                Cmd.none
            | Error errorMessage ->
                { model with Generate = { model.Generate with Error = Some errorMessage } }, Cmd.none

/// Returns display text for a top-level page tab.
let pageLabel (page: Page) : string =
    match page with
    | Generate -> "Generate"
    | Run -> "Run"
    | Collect -> "Collect"

/// Returns display text for a wizard step heading.
let stepLabel (step: GenerateStep) : string =
    match step with
    | Welcome -> "Welcome"
    | SelectComponents -> "Select TOPAS Components"
    | SelectNodes -> "Select Nodes"
    | SelectPhaseSpaceFiles -> "Select Phase-Space Files"
    | Review -> "Review"
    | Result -> "Result"

/// Renders one top-level tab button.
let tabButton (selectedPage: Page) (page: Page) (dispatch: Msg -> unit) =
    let isSelected = selectedPage = page

    Html.button [
        prop.className (
            if isSelected then
                "rounded-md bg-slate-800 px-4 py-2 text-white"
            else
                "rounded-md bg-slate-200 px-4 py-2 text-slate-900 hover:bg-slate-300"
        )
        prop.text (pageLabel page)
        prop.onClick (fun _ -> dispatch (SelectPage page))
    ]

/// Groups template files by configured folder group.
let groupTemplateFiles (files: TemplateFileInfo list) : (string * TemplateFileInfo list) list =
    files
    |> List.groupBy _.Group
    |> List.sortBy fst
    |> List.map (fun (groupName, groupedFiles) -> groupName, groupedFiles |> List.sortBy _.FileName)

/// Renders a checkbox with label text.
let checkBoxRow (isChecked: bool) (labelText: string) (onToggle: unit -> unit) =
    Html.label [
        prop.className "flex items-center gap-3 py-1"
        prop.children [
            Html.input [
                prop.type'.checkbox
                prop.isChecked isChecked
                prop.onChange (fun (_: bool) -> onToggle ())
            ]
            Html.span labelText
        ]
    ]

/// Renders the welcome step.
let viewWelcome (generate: GenerateModel) =
    let seedBase =
        match generate.Config with
        | Loaded config -> config.SeedBase
        | _ -> "..."

    let nodeCount =
        match generate.Config with
        | Loaded config -> config.Nodes.Length
        | _ -> 0

    let phaseSpaceCount =
        match generate.Config with
        | Loaded config -> config.PhaseSpaceFiles.Length
        | _ -> 0

    Html.div [
        Html.p "Generate creates TOPAS input files for one simulation batch."
        Html.p [ prop.className "mt-2"; prop.text $"Current seed base: {seedBase}" ]
        Html.p [ prop.className "mt-1"; prop.text $"Configured nodes: {nodeCount}" ]
        Html.p [ prop.className "mt-1"; prop.text $"Configured phase-space files: {phaseSpaceCount}" ]
    ]

/// Renders the component selection step.
let viewComponents (generate: GenerateModel) (dispatch: Msg -> unit) =
    match generate.TemplateFiles with
    | NotStarted -> Html.p "Template files not loaded."
    | Loading _ -> Html.p "Loading template files..."
    | Loaded files when files.IsEmpty -> Html.p "No template files available."
    | Loaded files ->
        Html.div [
            for groupName, groupedFiles in groupTemplateFiles files do
                Html.div [
                    prop.className "mb-4"
                    prop.children [
                        Html.h3 [ prop.className "font-semibold"; prop.text groupName ]
                        Html.div [
                            prop.className "mt-2"
                            prop.children [
                                for templateFile in groupedFiles do
                                    checkBoxRow
                                        (generate.SelectedComponents.Contains templateFile.RelativePath)
                                        templateFile.FileName
                                        (fun () -> dispatch (ToggleComponent templateFile.RelativePath))
                            ]
                        ]
                    ]
                ]
        ]

/// Renders the node selection step.
let viewNodes (generate: GenerateModel) (dispatch: Msg -> unit) =
    match generate.Config with
    | Loaded config ->
        Html.div [
            Html.div [
                prop.className "mb-3 flex gap-2"
                prop.children [
                    Html.button [
                        prop.className "rounded bg-slate-200 px-3 py-1"
                        prop.text "Select all"
                        prop.onClick (fun _ -> dispatch SelectAllNodes)
                    ]
                    Html.button [
                        prop.className "rounded bg-slate-200 px-3 py-1"
                        prop.text "Select none"
                        prop.onClick (fun _ -> dispatch SelectNoNodes)
                    ]
                ]
            ]
            Html.div [
                for node in config.Nodes |> List.sortBy _.Digit do
                    checkBoxRow
                        (generate.SelectedNodes.Contains node.Digit)
                        $"{node.Digit} {node.Name}"
                        (fun () -> dispatch (ToggleNode node.Digit))
            ]
        ]
    | _ -> Html.p "Loading nodes..."

/// Renders the phase-space selection step.
let viewPhaseSpaceFiles (generate: GenerateModel) (dispatch: Msg -> unit) =
    match generate.Config with
    | Loaded config ->
        Html.div [
            Html.div [
                prop.className "mb-3 flex gap-2"
                prop.children [
                    Html.button [
                        prop.className "rounded bg-slate-200 px-3 py-1"
                        prop.text "Select all"
                        prop.onClick (fun _ -> dispatch SelectAllPhaseSpaceFiles)
                    ]
                    Html.button [
                        prop.className "rounded bg-slate-200 px-3 py-1"
                        prop.text "Select none"
                        prop.onClick (fun _ -> dispatch SelectNoPhaseSpaceFiles)
                    ]
                ]
            ]
            Html.div [
                for phaseSpaceFile in config.PhaseSpaceFiles |> List.sortBy _.Index do
                    checkBoxRow
                        (generate.SelectedPhaseSpaceFiles.Contains phaseSpaceFile.Index)
                        $"ps{phaseSpaceFile.Index}"
                        (fun () -> dispatch (TogglePhaseSpaceFile phaseSpaceFile.Index))
            ]
        ]
    | _ -> Html.p "Loading phase-space files..."

/// Renders the review step.
let viewReview (generate: GenerateModel) =
    let selectedComponents = generate.SelectedComponents |> Seq.sort |> String.concat ", "
    let selectedNodes = generate.SelectedNodes |> Seq.sort |> Seq.map (fun digit -> $"n{digit}") |> String.concat ", "
    let selectedPhaseSpaces = generate.SelectedPhaseSpaceFiles |> Seq.sort |> Seq.map (fun index -> $"ps{index}") |> String.concat ", "

    Html.div [
        Html.p [ prop.text $"Selected components: {selectedComponents}" ]
        Html.p [ prop.className "mt-1"; prop.text $"Selected nodes: {selectedNodes}" ]
        Html.p [ prop.className "mt-1"; prop.text $"Selected phase-space files: {selectedPhaseSpaces}" ]
        Html.h4 [ prop.className "mt-4 font-semibold"; prop.text "Preview" ]
        match generate.Preview with
        | NotStarted -> Html.p "No preview available."
        | Loading _ -> Html.p "Loading preview..."
        | Loaded preview ->
            Html.div [
                prop.children [
                    Html.p [
                        prop.className "mt-2 text-sm"
                        prop.text $"Expected generated count: {preview.ExpectedGeneratedCount}"
                    ]
                    Html.pre [
                        prop.className "mt-2 max-h-72 overflow-auto rounded bg-slate-100 p-3 text-xs"
                        prop.text preview.StitchedPreviewText
                    ]
                ]
            ]
        | Loading (Some preview) ->
            Html.pre [
                prop.className "mt-2 max-h-72 overflow-auto rounded bg-slate-100 p-3 text-xs"
                prop.text preview.StitchedPreviewText
            ]
    ]

/// Renders the result step.
let viewResult (generate: GenerateModel) =
    match generate.GenerateResult with
    | NotStarted -> Html.p "No generation result yet."
    | Loading _ -> Html.p "Generating..."
    | Loaded generated ->
        Html.div [
            Html.p [ prop.text $"Seed base: {generated.SeedBase}" ]
            Html.p [ prop.className "mt-1"; prop.text $"Generated files: {generated.GeneratedInputCount}" ]
            Html.p [ prop.className "mt-1"; prop.text $"Input folder: {generated.InputFolder}" ]
            Html.h4 [ prop.className "mt-4 font-semibold"; prop.text "Generated runs" ]
            if generated.GeneratedRuns.IsEmpty then
                Html.p [ prop.className "mt-2"; prop.text "No generated runs returned yet." ]
            else
                Html.ul [
                    prop.className "mt-2 list-disc pl-6"
                    prop.children [
                        for run in generated.GeneratedRuns do
                            Html.li $"{run.RunId} | {run.InputFilePath} | seed {run.Seed}"
                    ]
                ]
        ]

/// Renders wizard step content.
let viewGenerateStep (generate: GenerateModel) (dispatch: Msg -> unit) =
    match generate.Step with
    | Welcome -> viewWelcome generate
    | SelectComponents -> viewComponents generate dispatch
    | SelectNodes -> viewNodes generate dispatch
    | SelectPhaseSpaceFiles -> viewPhaseSpaceFiles generate dispatch
    | Review -> viewReview generate
    | Result -> viewResult generate

/// Returns true when previous navigation should be shown.
let showPreviousButton (step: GenerateStep) : bool =
    match step with
    | Welcome -> false
    | _ -> true

/// Returns text for the primary navigation button.
let primaryButtonText (step: GenerateStep) : string =
    match step with
    | Welcome -> "Start"
    | Review -> "Generate"
    | Result -> "Back to Generate"
    | _ -> "Next"

/// Returns true when primary button should be disabled.
let disablePrimaryButton (generate: GenerateModel) : bool =
    match generate.Step with
    | SelectComponents -> not (canProceedComponents generate)
    | SelectNodes -> not (canProceedNodes generate)
    | SelectPhaseSpaceFiles -> not (canProceedPhaseSpaceFiles generate)
    | Review ->
        match generate.Preview with
        | Loaded _ -> false
        | _ -> true
    | _ -> false

/// Handles primary button click behavior.
let onPrimaryClick (generate: GenerateModel) (dispatch: Msg -> unit) =
    match generate.Step with
    | Result -> dispatch CancelGenerateWizard
    | _ -> dispatch NextGenerateStep

/// Renders wizard navigation controls.
let viewWizardNavigation (generate: GenerateModel) (dispatch: Msg -> unit) =
    Html.div [
        prop.className "mt-6 flex items-center justify-between"
        prop.children [
            Html.button [
                prop.className "rounded px-3 py-2 text-slate-700 hover:bg-slate-100"
                prop.text "Cancel"
                prop.onClick (fun _ -> dispatch CancelGenerateWizard)
            ]
            Html.div [
                prop.className "flex gap-2"
                prop.children [
                    if showPreviousButton generate.Step then
                        Html.button [
                            prop.className "rounded bg-slate-200 px-3 py-2"
                            prop.text "Previous"
                            prop.onClick (fun _ -> dispatch PreviousGenerateStep)
                        ]

                    Html.button [
                        prop.className "rounded bg-slate-800 px-4 py-2 text-white disabled:opacity-40"
                        prop.disabled (disablePrimaryButton generate)
                        prop.text (primaryButtonText generate.Step)
                        prop.onClick (fun _ -> onPrimaryClick generate dispatch)
                    ]
                ]
            ]
        ]
    ]

/// Renders the Generate page with wizard state.
let viewGeneratePage (generate: GenerateModel) (dispatch: Msg -> unit) =
    Html.div [
        Html.h2 [ prop.className "text-xl font-semibold"; prop.text $"Generate Wizard: {stepLabel generate.Step}" ]
        Html.div [ prop.className "mt-4"; prop.children [ viewGenerateStep generate dispatch ] ]
        match generate.Error with
        | Some errorMessage -> Html.p [ prop.className "mt-3 text-sm text-red-700"; prop.text errorMessage ]
        | None -> Html.none
        viewWizardNavigation generate dispatch
    ]

/// Renders the Run page placeholder.
let viewRunPage () = Html.p "Run: Not implemented."

/// Renders the Collect page placeholder.
let viewCollectPage () = Html.p "Collect: Not implemented."

/// Renders page content based on selected top-level tab.
let viewPageContent (model: Model) (dispatch: Msg -> unit) =
    match model.SelectedPage with
    | Generate -> viewGeneratePage model.Generate dispatch
    | Run -> viewRunPage ()
    | Collect -> viewCollectPage ()

/// Renders the client landing page and selected content.
let view (model: Model) (dispatch: Msg -> unit) =
    Html.main [
        prop.className "min-h-screen bg-slate-100 text-slate-900"
        prop.children [
            Html.section [
                prop.className "mx-auto w-full max-w-4xl p-8"
                prop.children [
                    Html.h1 [ prop.className "text-3xl font-semibold"; prop.text "TopasRunOrchestration" ]
                    Html.div [
                        prop.className "mt-6 flex gap-3"
                        prop.children [
                            tabButton model.SelectedPage Generate dispatch
                            tabButton model.SelectedPage Run dispatch
                            tabButton model.SelectedPage Collect dispatch
                        ]
                    ]
                    Html.div [
                        prop.className "mt-6 rounded-lg border border-slate-300 bg-white p-6 shadow-sm"
                        prop.children [ viewPageContent model dispatch ]
                    ]
                ]
            ]
        ]
    ]
