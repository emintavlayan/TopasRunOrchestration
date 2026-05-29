module CollectLogic

open GenerateTypes
open SAFE

/// Creates the initial collect wizard model.
let initialCollectModel () : CollectModel = {
    Step = CollectWelcome
    Batches = NotStarted
    SelectedSeedBase = None
    ExcludedPhaseSpaceIndexes = []
    ExcludedNodeDigits = []
    Preview = NotStarted
    CollectResult = NotStarted
    Error = None
}

/// Returns true when a collect batch row is selectable.
let isCollectBatchSelectable (batch: Shared.CollectBatchSummary) : bool =
    not (System.String.Equals(batch.CollectStatus, "Collected", System.StringComparison.OrdinalIgnoreCase))
    && batch.GeneratedRunCount > 0

/// Returns true when collect batch selection can proceed.
let canProceedCollectBatchSelection (collect: CollectModel) : bool =
    match collect.SelectedSeedBase, collect.Batches with
    | Some selectedSeedBase, Loaded batches ->
        batches
        |> List.exists (fun batch -> batch.SeedBase = selectedSeedBase && isCollectBatchSelectable batch)
    | _ -> false

/// Returns true when collect preflight allows proceeding.
let canProceedCollectPreflight (collect: CollectModel) : bool =
    match collect.Preview with
    | Loaded preview -> preview.Preflight.CanCollect
    | _ -> false

/// Returns true when previous button should be shown.
let showPreviousCollectButton (step: CollectStep) : bool =
    match step with
    | CollectWelcome -> false
    | _ -> true

/// Returns collect step primary action button text.
let collectPrimaryButtonText (collect: CollectModel) : string =
    match collect.Step, collect.CollectResult with
    | CollectMergeReview, Loading _ -> "Collecting..."
    | CollectWelcome, _ -> "Start"
    | CollectSelectBatch, _ -> "Next"
    | CollectPreflightReview, _ -> "Next"
    | CollectMergeReview, _ -> "Collect"
    | CollectResult, _ -> "Back to Collect"

/// Returns true when collect primary action should be disabled.
let disableCollectPrimaryButton (collect: CollectModel) : bool =
    match collect.Step with
    | CollectSelectBatch -> not (canProceedCollectBatchSelection collect)
    | CollectPreflightReview -> not (canProceedCollectPreflight collect)
    | CollectMergeReview ->
        match collect.CollectResult with
        | Loading _ -> true
        | _ ->
            match collect.Preview with
            | Loaded preview -> not preview.Preflight.CanCollect
            | _ -> true
    | _ -> false
