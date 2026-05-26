module WizardShell

open Feliz

/// Represents one wizard step with title and one-line instruction.
type WizardStepItem = {
    Title: string
    Instruction: string
}

/// Returns classes for text-style cancel buttons.
let cancelButtonClass = "rounded px-3 py-2 text-slate-700 transition hover:bg-slate-100"

/// Returns classes for outlined previous buttons.
let previousButtonClass =
    "rounded border border-slate-300 bg-white px-3 py-2 text-slate-700 transition hover:bg-slate-50 disabled:opacity-40"

/// Returns classes for primary action buttons.
let primaryButtonClass =
    "rounded bg-blue-700 px-4 py-2 text-white transition hover:bg-blue-800 disabled:opacity-40"

/// Returns classes for one step marker circle based on step state.
let private stepMarkerClass (isCurrent: bool) (isCompleted: bool) =
    if isCurrent then
        "h-4 w-4 rounded-full border-2 border-blue-700 bg-white"
    elif isCompleted then
        "h-4 w-4 rounded-full bg-blue-700"
    else
        "h-4 w-4 rounded-full border border-slate-300 bg-white"

/// Returns classes for one step connector line based on completion state.
let private stepConnectorClass (isCompleted: bool) =
    if isCompleted then
        "absolute left-[0.45rem] top-4 h-[calc(100%-0.4rem)] w-0.5 bg-blue-700"
    else
        "absolute left-[0.45rem] top-4 h-[calc(100%-0.4rem)] w-0.5 bg-slate-300"

/// Renders the shared vertical wizard stepper column.
let private viewVerticalStepper (currentStepIndex: int) (steps: WizardStepItem list) =
    Html.div [
        prop.className "w-64 shrink-0"
        prop.children [
            Html.div [
                prop.className "space-y-1"
                prop.children [
                    for index, step in steps |> List.indexed do
                        let isCurrent = index = currentStepIndex
                        let isCompleted = index < currentStepIndex
                        let isFuture = index > currentStepIndex

                        Html.div [
                            prop.className (
                                "relative rounded-lg pb-5 pr-2 last:pb-0 "
                                + if isCurrent then "bg-slate-100/80" else ""
                            )
                            prop.children [
                                if index < steps.Length - 1 then
                                    Html.div [ prop.className (stepConnectorClass isCompleted) ]

                                Html.div [
                                    prop.className "flex items-start gap-3"
                                    prop.children [
                                        Html.div [
                                            prop.className ("mt-0.5 " + stepMarkerClass isCurrent isCompleted)
                                        ]
                                        Html.div [
                                            prop.className "min-w-0"
                                            prop.children [
                                                Html.p [
                                                    prop.className (
                                                        if isCurrent then
                                                            "text-sm font-semibold text-blue-700"
                                                        elif isFuture then
                                                            "text-sm font-medium text-base-content/60"
                                                        else
                                                            "text-sm font-medium"
                                                    )
                                                    prop.text step.Title
                                                ]
                                                if isCurrent then
                                                    Html.p [
                                                        prop.className "mt-1 text-xs text-base-content/70"
                                                        prop.text step.Instruction
                                                    ]
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                        ]
                ]
            ]
        ]
    ]

/// Renders the shared wizard shell with vertical stepper, content panel, and navigation footer.
let viewWizardShell
    (steps: WizardStepItem list)
    (currentStepIndex: int)
    (content: ReactElement)
    (errorMessage: string option)
    (showPrevious: bool)
    (primaryText: string)
    (primaryDisabled: bool)
    (showForwardChevron: bool)
    (onCancel: unit -> unit)
    (onPrevious: unit -> unit)
    (onPrimary: unit -> unit)
    =
    Html.div [
        prop.className "card w-full border border-base-content/20 bg-base-100 shadow-lg"
        prop.children [
            Html.div [
                prop.className "card-body p-6"
                prop.children [
                    Html.div [
                        prop.className "flex gap-8"
                        prop.children [
                            Html.div [
                                prop.className "border-r border-base-content/15 pr-6"
                                prop.children [ viewVerticalStepper currentStepIndex steps ]
                            ]

                            Html.div [
                                prop.className "flex h-[68vh] grow flex-col"
                                prop.children [
                                    Html.div [
                                        prop.className "grow overflow-y-auto pr-2 pb-2"
                                        prop.children [
                                            Html.div [
                                                prop.className "space-y-4 p-1 text-sm"
                                                prop.children [ content ]
                                            ]
                                        ]
                                    ]

                                    match errorMessage with
                                    | Some message ->
                                        Html.div [
                                            prop.className "alert alert-error mt-3 text-sm"
                                            prop.text message
                                        ]
                                    | None -> Html.none

                                    Html.div [
                                        prop.className "mt-6 flex items-center justify-between border-t border-base-content/15 pt-4"
                                        prop.children [
                                            Html.button [
                                                prop.className cancelButtonClass
                                                prop.text "Cancel"
                                                prop.onClick (fun _ -> onCancel ())
                                            ]

                                            Html.div [
                                                prop.className "flex items-center gap-2"
                                                prop.children [
                                                    if showPrevious then
                                                        Html.button [
                                                            prop.className previousButtonClass
                                                            prop.text "< Previous"
                                                            prop.onClick (fun _ -> onPrevious ())
                                                        ]

                                                    Html.button [
                                                        prop.className primaryButtonClass
                                                        prop.disabled primaryDisabled
                                                        prop.text (if showForwardChevron then $"{primaryText} >" else primaryText)
                                                        prop.onClick (fun _ -> onPrimary ())
                                                    ]
                                                ]
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]
        ]
    ]
