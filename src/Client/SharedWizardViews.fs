module SharedWizardViews

open Feliz

/// Represents one wizard step with title and one-line instruction.
type WizardStepItem = {
    Title: string
    Instruction: string
}

/// Shared classes for text-style action buttons.
let textButtonClass = "rounded px-3 py-2 text-slate-700 transition hover:bg-slate-100"

/// Shared classes for outlined secondary action buttons.
let secondaryButtonClass =
    "rounded border border-slate-300 bg-white px-3 py-2 text-slate-700 transition hover:bg-slate-50 disabled:opacity-40"

/// Shared classes for primary action buttons.
let primaryButtonClass =
    "rounded bg-blue-700 px-4 py-2 text-white transition hover:bg-blue-800 disabled:opacity-40"

/// Returns classes for one step marker circle based on step state.
let private stepMarkerClass (isCurrent: bool) (isCompleted: bool) =
    if isCurrent || isCompleted then
        "h-4 w-4 rounded-full bg-blue-700"
    else
        "h-4 w-4 rounded-full bg-slate-300"

/// Returns classes for one step connector line based on completion.
let private stepConnectorClass (isCompleted: bool) =
    if isCompleted then
        "absolute left-[0.45rem] top-4 h-[calc(100%-0.5rem)] w-0.5 bg-blue-700"
    else
        "absolute left-[0.45rem] top-4 h-[calc(100%-0.5rem)] w-0.5 bg-slate-200"

/// Renders the shared vertical stepper column.
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
                            prop.className "relative pb-5 last:pb-0"
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
                                                            "text-sm font-medium text-slate-500"
                                                        else
                                                            "text-sm font-medium text-slate-700"
                                                    )
                                                    prop.text step.Title
                                                ]
                                                if isCurrent then
                                                    Html.p [
                                                        prop.className "mt-1 text-xs text-slate-600"
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

/// Renders the shared wizard shell with vertical stepper, content panel, and footer actions.
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
        prop.className "rounded-xl border border-slate-300 bg-white p-6 shadow-sm"
        prop.children [
            Html.div [
                prop.className "flex gap-8"
                prop.children [
                    viewVerticalStepper currentStepIndex steps

                    Html.div [
                        prop.className "flex min-h-[65vh] grow flex-col"
                        prop.children [
                            Html.p [
                                prop.className "text-sm text-slate-600"
                                prop.text steps[currentStepIndex].Instruction
                            ]

                            Html.div [
                                prop.className "mt-4 grow overflow-y-auto pr-2"
                                prop.children [ content ]
                            ]

                            match errorMessage with
                            | Some message ->
                                Html.div [
                                    prop.className "mt-3 rounded border border-red-200 bg-red-50 p-3 text-sm text-red-700"
                                    prop.text message
                                ]
                            | None -> Html.none

                            Html.div [
                                prop.className "mt-6 flex items-center justify-between border-t border-slate-200 pt-4"
                                prop.children [
                                    Html.button [
                                        prop.className textButtonClass
                                        prop.text "Cancel"
                                        prop.onClick (fun _ -> onCancel ())
                                    ]

                                    Html.div [
                                        prop.className "flex items-center gap-2"
                                        prop.children [
                                            if showPrevious then
                                                Html.button [
                                                    prop.className secondaryButtonClass
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
