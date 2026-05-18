module PageViews

open Feliz
open GenerateLogic
open GenerateTypes
open GenerateViews

/// Renders one top-level tab button.
let tabButton (selectedPage: Page) (page: Page) (dispatch: Msg -> unit) =
    let isSelected = selectedPage = page

    Html.button [
        prop.className (
            if isSelected then
                "rounded-md bg-slate-800 px-4 py-2 text-white"
            else
                "rounded-md bg-slate-200 px-4 py-2 text-slate-900 hover:bg-slate-300"
        )
        prop.text (pageLabel page)
        prop.onClick (fun _ -> dispatch (SelectPage page))
    ]

/// Renders the Run page placeholder.
let viewRunPage () = Html.p "Run: Not implemented."

/// Renders the Collect page placeholder.
let viewCollectPage () = Html.p "Collect: Not implemented."

/// Renders page content based on selected top-level tab.
let viewPageContent (model: Model) (dispatch: Msg -> unit) =
    match model.SelectedPage with
    | Generate -> viewGeneratePage model.Generate dispatch
    | Run -> viewRunPage ()
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
                        prop.className "mt-6 flex gap-3"
                        prop.children [
                            tabButton model.SelectedPage Generate dispatch
                            tabButton model.SelectedPage Run dispatch
                            tabButton model.SelectedPage Collect dispatch
                        ]
                    ]
                    Html.div [
                        prop.className "mt-6 rounded-lg border border-slate-300 bg-white p-6 shadow-sm"
                        prop.children [ viewPageContent model dispatch ]
                    ]
                ]
            ]
        ]
    ]
