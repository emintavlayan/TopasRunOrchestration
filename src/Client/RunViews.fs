module RunViews

open Feliz
open GenerateTypes
open RunLogic
open WizardShell
open SAFE

/// Normalizes path separators for readable UI path rendering.
let private normalizePathForDisplay (pathValue: string) =
    pathValue.Replace('\\', '/')

/// Returns AppRoot-relative path when possible, otherwise returns original path.
let makeRelativePath (appRoot: string option) (fullPath: string) =
    match appRoot with
    | None -> normalizePathForDisplay fullPath
    | Some rootPath ->
        let normalizedRoot =
            normalizePathForDisplay rootPath
            |> fun value -> value.TrimEnd('/')

        let normalizedFull = normalizePathForDisplay fullPath

        if normalizedFull.StartsWith(normalizedRoot + "/", System.StringComparison.OrdinalIgnoreCase) then
            normalizedFull.Substring(normalizedRoot.Length + 1)
        elif System.String.Equals(normalizedFull, normalizedRoot, System.StringComparison.OrdinalIgnoreCase) then
            "."
        else
            normalizedFull

/// Returns classes for preflight status badge.
let preflightStatusClass (ok: bool) =
    if ok then
        "badge badge-success badge-sm"
    else
        "badge badge-error badge-sm"

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
                    prop.className "table table-zebra text-sm"
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
                    prop.className "table table-zebra text-sm"
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

/// Renders script and manifest preview.
let viewSlurmScript (appRoot: string option) (run: RunModel) =
    match run.Preview with
    | Loaded preview ->
        let rootDisplay = defaultArg appRoot "-"

        Html.div [
            prop.className "space-y-4"
            prop.children [
                Html.div [
                    prop.className "grid gap-2 rounded-box bg-base-200 p-3 text-sm md:grid-cols-2"
                    prop.children [
                        Html.p [ prop.text $"Seed base: {preview.SeedBase}" ]
                        Html.p [ prop.text $"Generated runs: {preview.RunCount}" ]
                        Html.p [ prop.className "md:col-span-2 break-all font-mono text-xs"; prop.text $"Root: {rootDisplay}" ]
                    ]
                ]
                for nodePreview in preview.NodeScriptPreviews do
                    let manifestDisplayPath = makeRelativePath appRoot nodePreview.ManifestPath
                    let scriptDisplayPath = makeRelativePath appRoot nodePreview.ScriptPath
                    Html.h4 [ prop.className "font-semibold"; prop.text $"Node: {nodePreview.NodeName}" ]
                    Html.div [
                        prop.className "space-y-2"
                        prop.children [
                            Html.p [ prop.className "break-all font-mono text-xs"; prop.text $"Manifest: {manifestDisplayPath}" ]
                            Html.p [ prop.className "break-all font-mono text-xs"; prop.text $"Script: {scriptDisplayPath}" ]
                            Html.p [ prop.text $"Task count: {nodePreview.TaskCount}" ]
                            Html.div [
                                prop.className "overflow-x-auto"
                                prop.children [
                                    Html.table [
                                        prop.className "table table-zebra text-xs"
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
                                                for row in nodePreview.ManifestRowsPreview do
                                                    let inputDisplayPath = makeRelativePath appRoot row.InputFilePath
                                                    let logDisplayPath = makeRelativePath appRoot row.LogFilePath

                                                    Html.tr [
                                                        Html.td [ prop.className "border-b border-slate-100 px-2 py-1"; prop.text $"{row.TaskId}" ]
                                                        Html.td [ prop.className "border-b border-slate-100 px-2 py-1"; prop.text row.NodeName ]
                                                        Html.td [ prop.className "border-b border-slate-100 px-2 py-1"; prop.text row.RunId ]
                                                        Html.td [ prop.className "border-b border-slate-100 px-2 py-1 break-all font-mono"; prop.text inputDisplayPath ]
                                                        Html.td [ prop.className "border-b border-slate-100 px-2 py-1 break-all font-mono"; prop.text logDisplayPath ]
                                                    ]
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                            Html.pre [
                                prop.className "max-h-80 overflow-auto rounded-box bg-base-200 p-3 font-mono text-xs"
                                prop.text nodePreview.ScriptText
                            ]
                        ]
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
        let jobIdsText = String.concat ", " resultValue.SlurmJobIds
        Html.div [
            prop.className "space-y-2 text-sm text-slate-700"
            prop.children [
                Html.p [ prop.className "font-semibold text-emerald-700"; prop.text "Batch submitted." ]
                Html.p [ prop.text $"Slurm job ids: {jobIdsText}" ]
                Html.p [ prop.text $"Submitted run count: {resultValue.SubmittedRunCount}" ]
                Html.p [ prop.text $"Manifest path: {resultValue.ManifestPath}" ]
                Html.p [ prop.text $"Script path: {resultValue.ScriptPath}" ]
                Html.pre [
                    prop.className "max-h-72 overflow-auto rounded-box bg-base-200 p-3 font-mono text-xs"
                    prop.text resultValue.SbatchOutput
                ]
            ]
        ]
    | _ ->
        match run.Error with
        | Some errorMessage ->
            Html.div [
                prop.className "alert alert-error text-sm"
                prop.text errorMessage
            ]
        | None -> Html.p "No run submission result yet."

/// Renders current run wizard step content.
let viewRunStep (appRoot: string option) (run: RunModel) (dispatch: Msg -> unit) =
    match run.Step with
    | RunWelcome -> viewRunWelcome ()
    | SelectBatch -> viewSelectBatch run dispatch
    | PreflightReview -> viewPreflight run
    | SlurmScriptReview -> viewSlurmScript appRoot run
    | RunResult -> viewRunResult run

/// Renders full run wizard.
let viewRunPage (appRoot: string option) (run: RunModel) (dispatch: Msg -> unit) =
    let steps =
        [
            { Title = "Welcome"; Instruction = "Review what Run submits to Slurm." }
            { Title = "Batch"; Instruction = "Select a generated batch." }
            { Title = "Preflight"; Instruction = "Verify files and collision checks." }
            { Title = "Script"; Instruction = "Review manifest rows and Slurm script." }
            { Title = "Result"; Instruction = "Review Slurm submission result." }
        ]
    let currentStepIndex =
        run.Step
        |> function
            | RunWelcome -> 0
            | SelectBatch -> 1
            | PreflightReview -> 2
            | SlurmScriptReview -> 3
            | RunResult -> 4
    let isFinalAction = run.Step = SlurmScriptReview || run.Step = RunResult

    viewWizardShell
        steps
        currentStepIndex
        (viewRunStep appRoot run dispatch)
        run.Error
        (showPreviousRunButton run.Step)
        (runPrimaryButtonText run.Step)
        (disableRunPrimaryButton run)
        (not isFinalAction)
        (fun () -> dispatch CancelRunWizard)
        (fun () -> dispatch PreviousRunStep)
        (fun () -> dispatch NextRunStep)
