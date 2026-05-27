module WizardShell

open Feliz

/// Represents one wizard step with title and one-line description.
type WizardStepItem = {
    Title: string
    Description: string
}

let private stepTitleClass (isCurrent: bool) (isCompleted: bool) =
    if isCurrent then "text-primary font-semibold"
    elif isCompleted then "text-base-content font-medium"
    else "text-base-content/60"

let private markerClass (isCurrent: bool) (isCompleted: bool) =
    if isCompleted then
        "h-6 w-6 rounded-full bg-primary text-primary-content text-xs font-semibold flex items-center justify-center"
    elif isCurrent then
        "h-6 w-6 rounded-full bg-base-100 text-primary text-xs font-semibold border border-primary ring ring-primary/30 flex items-center justify-center"
    else
        "h-6 w-6 rounded-full bg-base-200 text-base-content/60 text-xs font-semibold border border-base-300 flex items-center justify-center"

let private connectorClass (isCompleted: bool) =
    if isCompleted then "w-px flex-1 mt-1 mb-1 bg-primary" else "w-px flex-1 mt-1 mb-1 bg-base-300"

let private viewVerticalStepper (steps: WizardStepItem list) (currentIndex: int) =
    Html.aside [
        prop.className "w-[280px] shrink-0 border-r border-base-300 pr-4"
        prop.children [
            Html.div [
                prop.className "pt-2"
                prop.children [
                    for index, step in steps |> List.indexed do
                        let isCurrent = index = currentIndex
                        let isCompleted = index < currentIndex

                        Html.div [
                            prop.className "flex gap-3"
                            prop.children [
                                Html.div [
                                    prop.className "flex w-8 shrink-0 flex-col items-center"
                                    prop.children [
                                        Html.div [
                                            prop.className (markerClass isCurrent isCompleted)
                                            prop.text $"{index + 1}"
                                        ]
                                        if index < steps.Length - 1 then
                                            Html.div [ prop.className (connectorClass isCompleted) ]
                                    ]
                                ]
                                Html.div [
                                    prop.className "grow pb-6"
                                    prop.children [
                                        Html.p [
                                            prop.className (stepTitleClass isCurrent isCompleted)
                                            prop.text step.Title
                                        ]
                                        if isCurrent then
                                            Html.p [
                                                prop.className "mt-1 text-sm text-base-content/70"
                                                prop.text step.Description
                                            ]
                                    ]
                                ]
                            ]
                        ]
                ]
            ]
        ]
    ]

/// Renders the shared wizard shell with sidebar stepper, scrollable content body, and fixed footer actions.
let viewWizardShell
    (steps: WizardStepItem list)
    (currentIndex: int)
    (content: ReactElement)
    (errorText: string option)
    (canGoPrevious: bool)
    (primaryText: string)
    (primaryDisabled: bool)
    (onCancel: unit -> unit)
    (onPrevious: unit -> unit)
    (onPrimary: unit -> unit)
    =
    Html.div [
        prop.className "card h-[calc(100vh-8rem)] min-h-[620px] w-full border border-base-300 bg-base-100 shadow-sm"
        prop.children [
            Html.div [
                prop.className "grid h-full grid-cols-[280px_1fr]"
                prop.children [
                    viewVerticalStepper steps currentIndex
                    Html.div [
                        prop.className "flex h-full min-w-0 flex-col"
                        prop.children [
                            Html.div [
                                prop.className "grow min-h-0 overflow-y-auto overflow-x-hidden p-6"
                                prop.children [ content ]
                            ]
                            Html.div [
                                prop.className "border-t border-base-300 p-4"
                                prop.children [
                                    match errorText with
                                    | Some message ->
                                        Html.div [
                                            prop.className "alert alert-error mb-3"
                                            prop.text message
                                        ]
                                    | None -> Html.none

                                    Html.div [
                                        prop.className "flex items-center justify-between"
                                        prop.children [
                                            Html.button [
                                                prop.className "btn btn-ghost"
                                                prop.text "Cancel"
                                                prop.onClick (fun _ -> onCancel ())
                                            ]
                                            Html.div [
                                                prop.className "flex items-center gap-2"
                                                prop.children [
                                                    if canGoPrevious then
                                                        Html.button [
                                                            prop.className "btn btn-outline"
                                                            prop.text "Previous"
                                                            prop.onClick (fun _ -> onPrevious ())
                                                        ]

                                                    Html.button [
                                                        prop.className "btn btn-primary"
                                                        prop.disabled primaryDisabled
                                                        prop.text primaryText
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
