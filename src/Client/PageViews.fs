module PageViews

open Feliz
open GenerateLogic
open GenerateTypes
open GenerateViews
open RunViews

/// Returns classes for underline-style tabs in selected/unselected state.
let tabButtonClass (isSelected: bool) =
    if isSelected then
        "w-full border-b-2 border-blue-700 px-4 py-3 text-base font-semibold text-blue-700"
    else
        "w-full border-b-2 border-transparent px-4 py-3 text-base font-medium text-slate-600 hover:border-slate-300 hover:text-slate-900"

/// Renders one top-level tab button.
let tabButton (selectedPage: Page) (page: Page) (dispatch: Msg -> unit) =
    let isSelected = selectedPage = page

    Html.button [
        prop.className (tabButtonClass isSelected)
        prop.text (pageLabel page)
        prop.onClick (fun _ -> dispatch (SelectPage page))
    ]

/// Renders the Collect page placeholder.
let viewCollectPage () = Html.p "Collect: Not implemented."

/// Renders page content based on selected top-level tab.
let viewPageContent (model: Model) (dispatch: Msg -> unit) =
    match model.SelectedPage with
    | Generate -> viewGeneratePage model.Generate dispatch
    | Run -> viewRunPage model.Run dispatch
    | Collect -> viewCollectPage ()

/// Renders the client landing page and selected content.
let view (model: Model) (dispatch: Msg -> unit) =
    Html.main [
        prop.className "min-h-screen bg-slate-100 text-slate-900"
        prop.children [
            Html.section [
                prop.className "mx-auto w-full max-w-4xl p-8"
                prop.children [
                    Html.h1 [ prop.className "text-3xl font-semibold"; prop.text "TopasRunOrchestration" ]
                    Html.div [
                        prop.className "mt-6 grid grid-cols-3 rounded-t-lg border border-slate-200 border-b-0 bg-white px-2 pt-1"
                        prop.children [
                            tabButton model.SelectedPage Generate dispatch
                            tabButton model.SelectedPage Run dispatch
                            tabButton model.SelectedPage Collect dispatch
                        ]
                    ]
                    Html.div [
                        prop.className "rounded-b-lg rounded-tr-lg border border-slate-300 bg-white p-6 shadow-sm"
                        prop.children [ viewPageContent model dispatch ]
                    ]
                ]
            ]
        ]
    ]
