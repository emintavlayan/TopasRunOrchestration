module PageViews

open Feliz
open GenerateLogic
open GenerateTypes
open GenerateViews
open RunViews
open CollectViews
open Shared
open SAFE

/// Returns classes for top-level navigation buttons in selected/unselected state.
let tabButtonClass (isSelected: bool) =
    if isSelected then
        "tab tab-active font-semibold"
    else
        "tab font-medium text-base-content/70"

/// Renders one top-level tab button.
let tabButton (selectedPage: Page) (page: Page) (dispatch: Msg -> unit) =
    let isSelected = selectedPage = page

    Html.button [
        prop.className (tabButtonClass isSelected)
        prop.text (pageLabel page)
        prop.onClick (fun _ -> dispatch (SelectPage page))
    ]

/// Renders page content based on selected top-level tab.
let viewPageContent (model: Model) (dispatch: Msg -> unit) =
    match model.SelectedPage with
    | Generate -> viewGeneratePage model.Generate dispatch
    | Run ->
        let appRoot =
            match model.Generate.Config with
            | Loaded config -> Some config.AppRoot
            | _ -> None

        viewRunPage appRoot model.Run dispatch
    | Collect -> viewCollectPage model.Collect dispatch

/// Renders the client landing page and selected content.
let view (model: Model) (dispatch: Msg -> unit) =
    Html.main [
        prop.className "min-h-screen bg-base-200 text-base-content"
        prop.children [
            Html.section [
                prop.className "mx-auto w-[96vw] max-w-[1600px] px-4 py-6"
                prop.children [
                    Html.h1 [ prop.className "text-3xl font-semibold"; prop.text "TopasRunOrchestration" ]
                    Html.div [
                        prop.className "card mt-6 bg-base-100 shadow"
                        prop.children [
                            Html.div [
                                prop.className "card-body p-3"
                                prop.children [
                                    Html.div [
                                        prop.className "tabs tabs-boxed w-full bg-base-200 p-1"
                                        prop.children [
                                            tabButton model.SelectedPage Generate dispatch
                                            tabButton model.SelectedPage Run dispatch
                                            tabButton model.SelectedPage Collect dispatch
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                    Html.div [
                        prop.className "mt-4"
                        prop.children [ viewPageContent model dispatch ]
                    ]
                ]
            ]
        ]
    ]
