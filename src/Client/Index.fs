module Index

open Elmish
open Feliz

type Page =
    | Generate
    | Run
    | Collect

type Msg =
    | SelectPage of Page

type Model = { SelectedPage: Page }

/// Returns the initial model for the basic client page.
let init () : Model * Cmd<Msg> = { SelectedPage = Generate }, Cmd.none

/// Updates the model for incoming messages.
let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | SelectPage page -> { model with SelectedPage = page }, Cmd.none

/// Returns display text for a page tab.
let pageLabel (page: Page) : string =
    match page with
    | Generate -> "Generate"
    | Run -> "Run"
    | Collect -> "Collect"

/// Returns page body text for the selected tab.
let pageDescription (page: Page) : string =
    match page with
    | Generate -> "Generate selected. Wizard implementation is not started yet."
    | Run -> "Run: Not implemented."
    | Collect -> "Collect: Not implemented."

/// Renders one tab button.
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

/// Renders the basic landing page with Generate, Run, and Collect tabs.
let view (model: Model) (dispatch: Msg -> unit) =
    Html.main [
        prop.className "min-h-screen flex items-center justify-center bg-slate-100 text-slate-900"
        prop.children [
            Html.section [
                prop.className "w-full max-w-2xl rounded-lg border border-slate-300 bg-white p-8 shadow-sm"
                prop.children [
                    Html.h1 [
                        prop.className "text-3xl font-semibold"
                        prop.text "TopasRunOrchestration"
                    ]
                    Html.div [
                        prop.className "mt-6 flex gap-3"
                        prop.children [
                            tabButton model.SelectedPage Generate dispatch
                            tabButton model.SelectedPage Run dispatch
                            tabButton model.SelectedPage Collect dispatch
                        ]
                    ]
                    Html.p [
                        prop.className "mt-6 text-base"
                        prop.text (pageDescription model.SelectedPage)
                    ]
                ]
            ]
        ]
    ]
