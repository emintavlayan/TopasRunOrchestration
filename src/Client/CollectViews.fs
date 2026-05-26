module CollectViews

open Feliz
open GenerateTypes
open CollectLogic
open SharedWizardViews
open SAFE

/// Returns classes for collect preflight status badges.
let collectStatusClass (ok: bool) =
    if ok then
        "rounded-full bg-emerald-100 px-2 py-1 text-xs font-semibold text-emerald-700"
    else
        "rounded-full bg-red-100 px-2 py-1 text-xs font-semibold text-red-700"

/// Renders collect welcome content.
let viewCollectWelcome () =
    Html.div [
        prop.className "space-y-2 text-sm text-slate-700"
        prop.children [
            Html.p "Collect reads TOPAS CSV/log outputs for one batch."
            Html.p "It checks expected files, merges node outputs per phase-space, and computes dose statistics."
            Html.p "Statistics include mean, median, standard deviation, and count."
        ]
    ]

/// Renders collect batch table.
let viewCollectBatchSelection (collect: CollectModel) (dispatch: Msg -> unit) =
    match collect.Batches with
    | NotStarted -> Html.p "Collect batches not loaded."
    | Loading _ -> Html.p "Loading collect batches..."
    | Loaded batches when batches.IsEmpty -> Html.p "No collect batches available."
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
                                Html.th [ prop.className "border-b border-slate-200 px-3 py-2 text-left font-semibold"; prop.text "Runs" ]
                                Html.th [ prop.className "border-b border-slate-200 px-3 py-2 text-left font-semibold"; prop.text "Nodes" ]
                                Html.th [ prop.className "border-b border-slate-200 px-3 py-2 text-left font-semibold"; prop.text "Phase-spaces" ]
                                Html.th [ prop.className "border-b border-slate-200 px-3 py-2 text-left font-semibold"; prop.text "Run status" ]
                                Html.th [ prop.className "border-b border-slate-200 px-3 py-2 text-left font-semibold"; prop.text "Collect status" ]
                                Html.th [ prop.className "border-b border-slate-200 px-3 py-2 text-left font-semibold"; prop.text "Summary path" ]
                            ]
                        ]
                        Html.tbody [
                            for batch in batches do
                                let selectable = isCollectBatchSelectable batch
                                let selected = collect.SelectedSeedBase = Some batch.SeedBase

                                Html.tr [
                                    prop.className (if selected then "bg-blue-50" else "")
                                    prop.children [
                                        Html.td [
                                            prop.className "border-b border-slate-100 px-3 py-2"
                                            prop.children [
                                                Html.input [
                                                    prop.type'.radio
                                                    prop.name "collect-batch"
                                                    prop.isChecked selected
                                                    prop.disabled (not selectable)
                                                    prop.onChange (fun (_: bool) -> dispatch (SelectCollectBatch batch.SeedBase))
                                                ]
                                            ]
                                        ]
                                        Html.td [ prop.className "border-b border-slate-100 px-3 py-2"; prop.text batch.SeedBase ]
                                        Html.td [ prop.className "border-b border-slate-100 px-3 py-2"; prop.text batch.CreatedAt ]
                                        Html.td [ prop.className "border-b border-slate-100 px-3 py-2"; prop.text $"{batch.GeneratedRunCount}" ]
                                        Html.td [ prop.className "border-b border-slate-100 px-3 py-2"; prop.text $"{batch.NodeCount}" ]
                                        Html.td [ prop.className "border-b border-slate-100 px-3 py-2"; prop.text $"{batch.PhaseSpaceCount}" ]
                                        Html.td [ prop.className "border-b border-slate-100 px-3 py-2"; prop.text (defaultArg batch.RunStatus "-") ]
                                        Html.td [ prop.className "border-b border-slate-100 px-3 py-2"; prop.text batch.CollectStatus ]
                                        Html.td [ prop.className "border-b border-slate-100 px-3 py-2 break-all"; prop.text (defaultArg batch.CollectSummaryPath "-") ]
                                    ]
                                ]
                        ]
                    ]
                ]
            ]
        ]

/// Renders collect preflight review.
let viewCollectPreflight (collect: CollectModel) =
    match collect.Preview with
    | NotStarted -> Html.p "No preflight data loaded."
    | Loading _ -> Html.p "Loading collect preflight..."
    | Loaded preview ->
        Html.div [
            prop.className "space-y-4"
            prop.children [
                Html.div [
                    prop.className "grid gap-2 text-sm md:grid-cols-2"
                    prop.children [
                        Html.p [ prop.text $"Expected runs: {preview.Preflight.ExpectedRunCount}" ]
                        Html.p [ prop.text $"CSV found/missing: {preview.Preflight.FoundCsvCount}/{preview.Preflight.MissingCsvCount}" ]
                        Html.p [ prop.text $"Logs found/missing: {preview.Preflight.FoundLogCount}/{preview.Preflight.MissingLogCount}" ]
                    ]
                ]
                Html.table [
                    prop.className "min-w-full border-collapse text-sm"
                    prop.children [
                        Html.thead [
                            Html.tr [
                                Html.th [ prop.className "border-b border-slate-200 px-3 py-2 text-left font-semibold"; prop.text "Check" ]
                                Html.th [ prop.className "border-b border-slate-200 px-3 py-2 text-left font-semibold"; prop.text "Status" ]
                                Html.th [ prop.className "border-b border-slate-200 px-3 py-2 text-left font-semibold"; prop.text "Details" ]
                            ]
                        ]
                        Html.tbody [
                            for check in preview.Preflight.Checks do
                                Html.tr [
                                    Html.td [ prop.className "border-b border-slate-100 px-3 py-2"; prop.text check.Name ]
                                    Html.td [
                                        prop.className "border-b border-slate-100 px-3 py-2"
                                        prop.children [ Html.span [ prop.className (collectStatusClass check.Ok); prop.text (if check.Ok then "OK" else "Failed") ] ]
                                    ]
                                    Html.td [ prop.className "border-b border-slate-100 px-3 py-2"; prop.text (defaultArg check.Message "-") ]
                                ]
                        ]
                    ]
                ]
                if preview.Preflight.MissingFiles.IsEmpty then
                    Html.none
                else
                    Html.div [
                        Html.h4 [ prop.className "font-semibold"; prop.text "Missing files" ]
                        Html.ul [
                            prop.className "mt-2 list-disc pl-5 text-sm"
                            prop.children [
                                for missing in preview.Preflight.MissingFiles do
                                    Html.li $"{missing.FileKind}: {missing.Path}"
                            ]
                        ]
                    ]
            ]
        ]

/// Renders collect merge review.
let viewCollectMergeReview (collect: CollectModel) =
    match collect.Preview with
    | Loaded preview ->
        Html.div [
            prop.className "space-y-3 text-sm text-slate-700"
            prop.children [
                Html.p $"Output folder: {preview.OutputFolder}"
                Html.p $"Manifest: {preview.ManifestPath}"
                Html.p $"Final summary: {preview.FinalSummaryPath}"
                Html.h4 [ prop.className "font-semibold"; prop.text "Planned merged files" ]
                Html.ul [
                    prop.className "list-disc pl-5"
                    prop.children [
                        for path in preview.PlannedMergedFiles do
                            Html.li path
                    ]
                ]
                Html.p "Summary statistics: mean, median, standard deviation, count."
            ]
        ]
    | Loading _ -> Html.p "Loading collect review..."
    | _ -> Html.p "No collect preview loaded."

/// Renders collect result content.
let viewCollectResult (collect: CollectModel) =
    match collect.CollectResult with
    | Loading _ -> Html.p "Running collect..."
    | Loaded value ->
        Html.div [
            prop.className "space-y-2 text-sm text-slate-700"
            prop.children [
                Html.p [ prop.className "font-semibold text-emerald-700"; prop.text "Collect completed." ]
                Html.p $"CSV files read: {value.CsvReadCount}"
                Html.p $"Logs found: {value.LogFoundCount}"
                Html.p $"Merged phase-space count: {value.MergedPhaseSpaceCount}"
                Html.p $"Summary path: {value.SummaryPath}"
                Html.h4 [ prop.className "font-semibold"; prop.text "Merged files" ]
                Html.ul [
                    prop.className "list-disc pl-5"
                    prop.children [
                        for merged in value.MergedFiles do
                            Html.li $"{merged.PhaseSpaceIndex}: {merged.MergedFilePath}"
                    ]
                ]
            ]
        ]
    | _ ->
        match collect.Error with
        | Some message ->
            Html.div [
                prop.className "rounded border border-red-200 bg-red-50 p-3 text-sm text-red-700"
                prop.text message
            ]
        | None -> Html.p "No collect result yet."

/// Renders collect step content.
let viewCollectStep (collect: CollectModel) (dispatch: Msg -> unit) =
    match collect.Step with
    | CollectWelcome -> viewCollectWelcome ()
    | CollectSelectBatch -> viewCollectBatchSelection collect dispatch
    | CollectPreflightReview -> viewCollectPreflight collect
    | CollectMergeReview -> viewCollectMergeReview collect
    | CollectResult -> viewCollectResult collect

/// Renders full collect wizard page.
let viewCollectPage (collect: CollectModel) (dispatch: Msg -> unit) =
    let steps =
        [
            { Title = "Welcome"; Instruction = "Review what Collect reads and writes." }
            { Title = "Batch"; Instruction = "Select a batch with TOPAS outputs." }
            { Title = "Preflight"; Instruction = "Check expected CSV and log files." }
            { Title = "Review"; Instruction = "Review planned merge and summary outputs." }
            { Title = "Result"; Instruction = "Review collected outputs." }
        ]
    let currentStepIndex =
        collect.Step
        |> function
            | CollectWelcome -> 0
            | CollectSelectBatch -> 1
            | CollectPreflightReview -> 2
            | CollectMergeReview -> 3
            | CollectResult -> 4
    let isFinalAction = collect.Step = CollectMergeReview || collect.Step = CollectResult

    viewWizardShell
        steps
        currentStepIndex
        (viewCollectStep collect dispatch)
        collect.Error
        (showPreviousCollectButton collect.Step)
        (collectPrimaryButtonText collect.Step)
        (disableCollectPrimaryButton collect)
        (not isFinalAction)
        (fun () -> dispatch CancelCollectWizard)
        (fun () -> dispatch PreviousCollectStep)
        (fun () -> dispatch NextCollectStep)
