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
        "btn btn-primary btn-sm normal-case whitespace-nowrap"
    else
        "btn btn-ghost btn-sm normal-case whitespace-nowrap"

/// Renders one top-level navigation button.
let topNavButton (selectedPage: Page) (page: Page) (dispatch: Msg -> unit) =
    let isSelected = selectedPage = page

    Html.button [
        prop.className (topNavButtonClass isSelected)
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
        prop.custom ("data-theme", "light")
        prop.children [
            Html.section [
                prop.className "mx-auto w-[96vw] max-w-[1600px] p-4"
                prop.children [
                    Html.div [
                        prop.className "navbar min-h-16 rounded-box border border-base-300 bg-base-100 px-4 shadow-sm"
                        prop.children [
                            Html.div [
                                prop.className "flex-1 items-center"
                                prop.children [
                                    Html.h1 [ prop.className "text-base font-semibold tracking-tight md:text-lg"; prop.text "Topas Run Orchestration" ]
                                ]
                            ]
                            Html.div [
                                prop.className "flex flex-nowrap items-center justify-end gap-2"
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
