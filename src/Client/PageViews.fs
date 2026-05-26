module PageViews

open Feliz
open GenerateLogic
open GenerateTypes
open GenerateViews
open RunViews
open CollectViews
open Shared
open SAFE

/// Returns classes for top-level workflow navigation buttons.
let topNavButtonClass (isSelected: bool) =
    if isSelected then
        "btn btn-primary btn-sm md:btn-md normal-case gap-2 whitespace-nowrap"
    else
        "btn btn-ghost btn-sm md:btn-md normal-case gap-2 whitespace-nowrap"

/// Returns a small icon for each top-level workflow page.
let pageIcon (page: Page) =
    match page with
    | Generate -> Html.span [ prop.className "text-base"; prop.text "⚙" ]
    | Run -> Html.span [ prop.className "text-base"; prop.text "▶" ]
    | Collect -> Html.span [ prop.className "text-base"; prop.text "📥" ]

/// Renders one top-level navigation button.
let topNavButton (selectedPage: Page) (page: Page) (dispatch: Msg -> unit) =
    let isSelected = selectedPage = page

    Html.button [
        prop.className (topNavButtonClass isSelected)
        prop.children [ pageIcon page; Html.span (pageLabel page) ]
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
        prop.className "min-h-screen bg-base-300 text-base-content"
        prop.custom ("data-theme", "light")
        prop.children [
            Html.section [
                prop.className "mx-auto w-[96vw] max-w-[1600px] px-4 py-6"
                prop.children [
                    Html.div [
                        prop.className "navbar mt-2 rounded-box border border-base-content/20 bg-base-100 px-4 shadow-md"
                        prop.children [
                            Html.div [
                                prop.className "flex-1"
                                prop.children [
                                    Html.h1 [ prop.className "text-lg font-semibold md:text-2xl"; prop.text "Topas Run Orchestration" ]
                                ]
                            ]
                            Html.div [
                                prop.className "flex items-center gap-2"
                                prop.children [
                                    topNavButton model.SelectedPage Generate dispatch
                                    topNavButton model.SelectedPage Run dispatch
                                    topNavButton model.SelectedPage Collect dispatch
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
