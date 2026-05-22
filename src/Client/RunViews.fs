module RunViews

open Feliz
open GenerateTypes
open RunLogic
open SAFE
open GenerateViews

/// Returns classes for preflight status badge.
let preflightStatusClass (ok: bool) =
    if ok then
        "rounded-full bg-emerald-100 px-2 py-1 text-xs font-semibold text-emerald-700"
    else
        "rounded-full bg-red-100 px-2 py-1 text-xs font-semibold text-red-700"

/// Renders run wizard step labels.
let runStepLabel (step: RunStep) =
    match step with
    | RunWelcome -> "Welcome"
    | SelectBatch -> "Select batch"
    | PreflightReview -> "Preflight"
    | SlurmScriptReview -> "Slurm script"
    | RunResult -> "Result"

/// Renders run wizard step content title.
let runStepTitle (step: RunStep) =
    match step with
    | RunWelcome -> "Run Wizard: Welcome"
    | SelectBatch -> "Run Wizard: Select Batch"
    | PreflightReview -> "Run Wizard: Preflight Review"
    | SlurmScriptReview -> "Run Wizard: Slurm Script Review"
    | RunResult -> "Run Wizard: Result"

/// Renders run welcome content.
let viewRunWelcome () =
    Html.div [
        prop.className "space-y-2 text-sm text-slate-700"
        prop.children [
            Html.p "Run submits an already generated batch to Slurm."
            Html.p "It creates a manifest and Slurm script, submits with sbatch, and writes one TOPAS log per run."
        ]
    ]

/// Renders run batch selection table.
let viewSelectBatch (run: RunModel) (dispatch: Msg -> unit) =
    match run.Batches with
    | NotStarted -> Html.p "Run batches not loaded."
    | Loading _ -> Html.p "Loading run batches..."
    | Loaded batches when batches.IsEmpty -> Html.p "No generated batches found."
    | Loaded batches ->
        Html.div [
            prop.className "overflow-x-auto"
            prop.children [
                Html.table [
                    prop.className "min-w-full border-collapse text-sm"
                    prop.children [
                        Html.thead [
                            Html.tr [
                                Html.th [ prop.className "border-b border-slate-200 px-3 py-2 text-left font-semibold"; prop.text "Select" ]
                                Html.th [ prop.className "border-b border-slate-200 px-3 py-2 text-left font-semibold"; prop.text "Seed base" ]
                                Html.th [ prop.className "border-b border-slate-200 px-3 py-2 text-left font-semibold"; prop.text "Created" ]
                                Html.th [ prop.className "border-b border-slate-200 px-3 py-2 text-left font-semibold"; prop.text "Inputs" ]
                                Html.th [ prop.className "border-b border-slate-200 px-3 py-2 text-left font-semibold"; prop.text "Nodes" ]
                                Html.th [ prop.className "border-b border-slate-200 px-3 py-2 text-left font-semibold"; prop.text "Phase-space files" ]
                                Html.th [ prop.className "border-b border-slate-200 px-3 py-2 text-left font-semibold"; prop.text "Status" ]
                                Html.th [ prop.className "border-b border-slate-200 px-3 py-2 text-left font-semibold"; prop.text "Slurm job id" ]
                            ]
                        ]
                        Html.tbody [
                            for batch in batches do
                                let isSelectable = isBatchSelectable batch
                                let isSelected = run.SelectedSeedBase = Some batch.SeedBase

                                Html.tr [
                                    prop.className (if isSelected then "bg-blue-50" else "")
                                    prop.children [
                                        Html.td [
                                            prop.className "border-b border-slate-100 px-3 py-2"
                                            prop.children [
                                                Html.input [
                                                    prop.type'.radio
                                                    prop.name "run-batch"
                                                    prop.isChecked isSelected
                                                    prop.disabled (not isSelectable)
                                                    prop.onChange (fun (_: bool) -> dispatch (SelectRunBatch batch.SeedBase))
                                                ]
                                            ]
                                        ]
                                        Html.td [ prop.className "border-b border-slate-100 px-3 py-2"; prop.text batch.SeedBase ]
                                        Html.td [ prop.className "border-b border-slate-100 px-3 py-2"; prop.text batch.CreatedAt ]
                                        Html.td [ prop.className "border-b border-slate-100 px-3 py-2"; prop.text $"{batch.GeneratedInputCount}" ]
                                        Html.td [ prop.className "border-b border-slate-100 px-3 py-2"; prop.text $"{batch.NodeCount}" ]
                                        Html.td [ prop.className "border-b border-slate-100 px-3 py-2"; prop.text $"{batch.PhaseSpaceCount}" ]
                                        Html.td [ prop.className "border-b border-slate-100 px-3 py-2"; prop.text batch.RunStatus ]
                                        Html.td [ prop.className "border-b border-slate-100 px-3 py-2"; prop.text (defaultArg batch.SlurmJobId "-") ]
                                    ]
                                ]
                        ]
                    ]
                ]
            ]
        ]
    | _ -> Html.p "Loading run batches..."

/// Renders preflight checks table.
let viewPreflight (run: RunModel) =
    match run.Preview with
    | NotStarted -> Html.p "No preflight data loaded."
    | Loading _ -> Html.p "Loading preflight checks..."
    | Loaded preview ->
        Html.div [
            prop.className "space-y-3"
            prop.children [
                Html.table [
                    prop.className "min-w-full border-collapse text-sm"
                    prop.children [
                        Html.thead [
                            Html.tr [
                                Html.th [ prop.className "border-b border-slate-200 px-3 py-2 text-left font-semibold"; prop.text "Check" ]
                                Html.th [ prop.className "border-b border-slate-200 px-3 py-2 text-left font-semibold"; prop.text "Status" ]
                                Html.th [ prop.className "border-b border-slate-200 px-3 py-2 text-left font-semibold"; prop.text "Message" ]
                            ]
                        ]
                        Html.tbody [
                            for check in preview.Preflight.Checks do
                                Html.tr [
                                    Html.td [ prop.className "border-b border-slate-100 px-3 py-2"; prop.text check.Name ]
                                    Html.td [
                                        prop.className "border-b border-slate-100 px-3 py-2"
                                        prop.children [ Html.span [ prop.className (preflightStatusClass check.Ok); prop.text (if check.Ok then "OK" else "Failed") ] ]
                                    ]
                                    Html.td [
                                        prop.className "border-b border-slate-100 px-3 py-2 text-slate-700"
                                        prop.text (defaultArg check.Message "-")
                                    ]
                                ]
                        ]
                    ]
                ]
            ]
        ]
    | _ -> Html.p "No preflight data loaded."

/// Renders script and manifest preview.
let viewSlurmScript (run: RunModel) =
    match run.Preview with
    | Loaded preview ->
        Html.div [
            prop.className "space-y-4"
            prop.children [
                Html.div [
                    prop.className "grid gap-2 text-sm text-slate-700 md:grid-cols-2"
                    prop.children [
                        Html.p [ prop.text $"Seed base: {preview.SeedBase}" ]
                        Html.p [ prop.text $"Generated runs: {preview.RunCount}" ]
                        Html.p [ prop.text $"Manifest path: {preview.ManifestPath}" ]
                        Html.p [ prop.text $"Script path: {preview.ScriptPath}" ]
                    ]
                ]
                Html.h4 [ prop.className "font-semibold"; prop.text "Manifest rows (first entries)" ]
                Html.div [
                    prop.className "overflow-x-auto"
                    prop.children [
                        Html.table [
                            prop.className "min-w-full border-collapse text-xs"
                            prop.children [
                                Html.thead [
                                    Html.tr [
                                        Html.th [ prop.className "border-b border-slate-200 px-2 py-1 text-left"; prop.text "Task" ]
                                        Html.th [ prop.className "border-b border-slate-200 px-2 py-1 text-left"; prop.text "Node" ]
                                        Html.th [ prop.className "border-b border-slate-200 px-2 py-1 text-left"; prop.text "Run id" ]
                                        Html.th [ prop.className "border-b border-slate-200 px-2 py-1 text-left"; prop.text "Input" ]
                                        Html.th [ prop.className "border-b border-slate-200 px-2 py-1 text-left"; prop.text "Log" ]
                                    ]
                                ]
                                Html.tbody [
                                    for row in preview.ManifestRowsPreview do
                                        Html.tr [
                                            Html.td [ prop.className "border-b border-slate-100 px-2 py-1"; prop.text $"{row.TaskId}" ]
                                            Html.td [ prop.className "border-b border-slate-100 px-2 py-1"; prop.text row.NodeName ]
                                            Html.td [ prop.className "border-b border-slate-100 px-2 py-1"; prop.text row.RunId ]
                                            Html.td [ prop.className "border-b border-slate-100 px-2 py-1 break-all"; prop.text row.InputFilePath ]
                                            Html.td [ prop.className "border-b border-slate-100 px-2 py-1 break-all"; prop.text row.LogFilePath ]
                                        ]
                                ]
                            ]
                        ]
                    ]
                ]
                Html.h4 [ prop.className "font-semibold"; prop.text "Slurm script" ]
                Html.pre [
                    prop.className "max-h-80 overflow-auto rounded border border-slate-200 bg-slate-100 p-3 text-xs"
                    prop.text preview.ScriptText
                ]
            ]
        ]
    | Loading _ -> Html.p "Loading script preview..."
    | _ -> Html.p "No script preview available."

/// Renders run submission result.
let viewRunResult (run: RunModel) =
    match run.SubmitResult with
    | Loading _ -> Html.p "Submitting to Slurm..."
    | Loaded resultValue ->
        Html.div [
            prop.className "space-y-2 text-sm text-slate-700"
            prop.children [
                Html.p [ prop.className "font-semibold text-emerald-700"; prop.text "Batch submitted." ]
                Html.p [ prop.text $"Slurm job id: {resultValue.SlurmJobId}" ]
                Html.p [ prop.text $"Submitted run count: {resultValue.SubmittedRunCount}" ]
                Html.p [ prop.text $"Manifest path: {resultValue.ManifestPath}" ]
                Html.p [ prop.text $"Script path: {resultValue.ScriptPath}" ]
                Html.pre [
                    prop.className "max-h-72 overflow-auto rounded border border-slate-200 bg-slate-100 p-3 text-xs"
                    prop.text resultValue.SbatchOutput
                ]
            ]
        ]
    | _ ->
        match run.Error with
        | Some errorMessage ->
            Html.div [
                prop.className "rounded border border-red-200 bg-red-50 p-3 text-sm text-red-700"
                prop.text errorMessage
            ]
        | None -> Html.p "No run submission result yet."

/// Renders current run wizard step content.
let viewRunStep (run: RunModel) (dispatch: Msg -> unit) =
    match run.Step with
    | RunWelcome -> viewRunWelcome ()
    | SelectBatch -> viewSelectBatch run dispatch
    | PreflightReview -> viewPreflight run
    | SlurmScriptReview -> viewSlurmScript run
    | RunResult -> viewRunResult run

/// Renders run wizard navigation controls.
let viewRunWizardNavigation (run: RunModel) (dispatch: Msg -> unit) =
    Html.div [
        prop.className "mt-6 flex justify-end"
        prop.children [
            Html.div [
                prop.className "flex gap-2"
                prop.children [
                    Html.button [
                        prop.className textButtonClass
                        prop.text "Cancel"
                        prop.onClick (fun _ -> dispatch CancelRunWizard)
                    ]

                    if showPreviousRunButton run.Step then
                        Html.button [
                            prop.className outlinedButtonClass
                            prop.text "Previous"
                            prop.onClick (fun _ -> dispatch PreviousRunStep)
                        ]

                    Html.button [
                        prop.className primaryButtonClass
                        prop.disabled (disableRunPrimaryButton run)
                        prop.text (runPrimaryButtonText run.Step)
                        prop.onClick (fun _ -> dispatch NextRunStep)
                    ]
                ]
            ]
        ]
    ]

/// Renders full run wizard.
let viewRunPage (run: RunModel) (dispatch: Msg -> unit) =
    Html.div [
        prop.children [
            Html.h2 [ prop.className "text-xl font-semibold"; prop.text (runStepTitle run.Step) ]
            Html.p [ prop.className "mt-1 text-sm text-slate-600"; prop.text $"Current step: {runStepLabel run.Step}" ]
            Html.div [ prop.className "mt-4"; prop.children [ viewRunStep run dispatch ] ]
            match run.Error with
            | Some errorMessage ->
                Html.div [
                    prop.className "mt-3 rounded border border-red-200 bg-red-50 p-3 text-sm text-red-700"
                    prop.text errorMessage
                ]
            | None -> Html.none
            viewRunWizardNavigation run dispatch
        ]
    ]
