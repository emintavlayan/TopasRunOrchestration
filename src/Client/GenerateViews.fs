module GenerateViews

open Feliz
open GenerateLogic
open GenerateTypes
open SAFE
open Shared

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
        Html.p [
            prop.className "mt-1"
            prop.text $"Configured phase-space files: {phaseSpaceCount}"
        ]
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
                    checkBoxRow (generate.SelectedNodes.Contains node.Digit) $"{node.Digit} {node.Name}" (fun () ->
                        dispatch (ToggleNode node.Digit))
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
    let selectedComponents = generate.SelectedComponents |> Seq.sort |> List.ofSeq

    let selectedNodes =
        generate.SelectedNodes
        |> Seq.sort
        |> Seq.map (fun digit -> $"n{digit}")
        |> String.concat ", "

    let selectedPhaseSpaces =
        generate.SelectedPhaseSpaceFiles
        |> Seq.sort
        |> Seq.map (fun index -> $"ps{index}")
        |> String.concat ", "

    Html.div [
        Html.h3 [ prop.className "text-lg font-semibold"; prop.text "Generate Wizard: Review" ]
        Html.div [
            prop.className "mt-3"
            prop.children [
                Html.p [ prop.className "font-medium"; prop.text "Selected components:" ]
                if selectedComponents.IsEmpty then
                    Html.p [
                        prop.className "mt-1 text-sm text-slate-600"
                        prop.text "No components selected."
                    ]
                else
                    Html.ul [
                        prop.className "mt-1 list-disc pl-6"
                        prop.children [
                            for componentPath in selectedComponents do
                                Html.li componentPath
                        ]
                    ]
            ]
        ]
        Html.div [
            prop.className "mt-4"
            prop.children [
                Html.p [ prop.className "font-medium"; prop.text "Selected nodes:" ]
                Html.ul [
                    prop.className "mt-1 list-disc pl-6"
                    prop.children [ Html.li selectedNodes ]
                ]
            ]
        ]
        Html.div [
            prop.className "mt-4"
            prop.children [
                Html.p [ prop.className "font-medium"; prop.text "Selected phase-space files:" ]
                Html.ul [
                    prop.className "mt-1 list-disc pl-6"
                    prop.children [ Html.li selectedPhaseSpaces ]
                ]
            ]
        ]
        Html.h4 [ prop.className "mt-4 font-semibold"; prop.text "Preview" ]
        match generate.Preview with
        | NotStarted -> Html.p "No preview available."
        | Loading(Some preview) ->
            Html.pre [
                prop.className "mt-2 max-h-72 overflow-auto rounded bg-slate-100 p-3 text-xs"
                prop.text preview.StitchedPreviewText
            ]
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
    ]

/// Renders the result step.
let viewResult (generate: GenerateModel) =
    match generate.GenerateResult with
    | NotStarted -> Html.p "No generation result yet."
    | Loading _ -> Html.p "Generating..."
    | Loaded generated ->
        Html.div [
            Html.p [ prop.text $"Seed base: {generated.SeedBase}" ]
            Html.p [
                prop.className "mt-1"
                prop.text $"Generated files: {generated.GeneratedInputCount}"
            ]
            Html.p [ prop.className "mt-1"; prop.text $"Input folder: {generated.InputFolder}" ]
            Html.h4 [ prop.className "mt-4 font-semibold"; prop.text "Generated runs" ]
            if generated.GeneratedRuns.IsEmpty then
                Html.p [ prop.className "mt-2"; prop.text "No generated runs returned yet." ]
            else
                Html.ul [
                    prop.className "mt-2 list-disc pl-6"
                    prop.children [
                        for run in generated.GeneratedRuns do
                            Html.li $"{run.RunId} | input: {run.InputFilePath} | output: {run.OutputFilePath} | run folder: {run.RunFolder}"
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
        Html.h2 [
            prop.className "text-xl font-semibold"
            prop.text $"Generate Wizard: {stepLabel generate.Step}"
        ]
        Html.div [ prop.className "mt-4"; prop.children [ viewGenerateStep generate dispatch ] ]
        match generate.Error with
        | Some errorMessage -> Html.p [ prop.className "mt-3 text-sm text-red-700"; prop.text errorMessage ]
        | None -> Html.none
        viewWizardNavigation generate dispatch
    ]
