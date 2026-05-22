module RunLogic

open GenerateTypes
open SAFE
open Shared

/// Creates the initial run wizard model.
let initialRunModel () : RunModel = {
    Step = RunWelcome
    Batches = NotStarted
    SelectedSeedBase = None
    Preview = NotStarted
    SubmitResult = NotStarted
    Error = None
}

/// Returns true when the batch can be selected for submission.
let isBatchSelectable (batch: RunBatchSummary) : bool =
    batch.RunStatus = "Generated"

/// Returns true when selected batch can proceed to preflight.
let canProceedBatchSelection (run: RunModel) : bool =
    match run.SelectedSeedBase, run.Batches with
    | Some selectedSeedBase, Loaded batches ->
        batches |> List.exists (fun batch -> batch.SeedBase = selectedSeedBase && isBatchSelectable batch)
    | _ -> false

/// Returns true when preflight checks passed.
let canProceedPreflight (run: RunModel) : bool =
    match run.Preview with
    | Loaded preview -> preview.Preflight.CanSubmit
    | _ -> false

/// Returns true when previous button should be shown in run wizard.
let showPreviousRunButton (step: RunStep) : bool =
    match step with
    | RunWelcome -> false
    | _ -> true

/// Returns primary button text for run wizard step.
let runPrimaryButtonText (step: RunStep) : string =
    match step with
    | RunWelcome -> "Start"
    | SelectBatch -> "Next"
    | PreflightReview -> "Next"
    | SlurmScriptReview -> "Submit"
    | RunResult -> "Back to Run"

/// Returns true when run primary button should be disabled.
let disableRunPrimaryButton (run: RunModel) : bool =
    match run.Step with
    | SelectBatch -> not (canProceedBatchSelection run)
    | PreflightReview -> not (canProceedPreflight run)
    | SlurmScriptReview ->
        match run.Preview with
        | Loaded preview -> not preview.Preflight.CanSubmit
        | _ -> true
    | _ -> false
