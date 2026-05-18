module Index

open Elmish
open SAFE
open Shared
open Feliz

type Model = {
    Todos: RemoteData<Todo list>
    Input: string
}

type Msg =
    | SetInput of string
    | LoadTodos of ApiCall<unit, Todo list>
    | SaveTodo of ApiCall<string, Todo list>

/// Returns the initial model for the basic client page.
let init () : Model * Cmd<Msg> =
    {
        Todos = NotStarted
        Input = ""
    },
    Cmd.none

/// Updates the model for incoming messages.
let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | SetInput value -> { model with Input = value }, Cmd.none
    | LoadTodos call ->
        match call with
        | Start() -> { model with Todos = model.Todos.StartLoading() }, Cmd.none
        | Finished todos -> { model with Todos = Loaded todos }, Cmd.none
    | SaveTodo call ->
        match call with
        | Start _ -> model, Cmd.none
        | Finished todos ->
            {
                model with
                    Todos = Loaded todos
                    Input = ""
            },
            Cmd.none

/// Renders a basic page while server features are being implemented.
let view (model: Model) (_dispatch: Msg -> unit) =
    Html.main [
        prop.className "min-h-screen flex items-center justify-center bg-slate-100 text-slate-900"
        prop.children [
            Html.section [
                prop.className "max-w-xl rounded-lg border border-slate-300 bg-white p-8 shadow-sm"
                prop.children [
                    Html.h1 [
                        prop.className "text-3xl font-semibold"
                        prop.text "TopasRunOrchestration"
                    ]
                    Html.p [
                        prop.className "mt-4 text-base"
                        prop.text "Server startup now loads configuration, creates root folders, and initializes SQLite."
                    ]
                    Html.p [
                        prop.className "mt-2 text-sm text-slate-600"
                        prop.text $"Current todos in model: {model.Todos |> RemoteData.map _.Length |> RemoteData.defaultValue 0}"
                    ]
                ]
            ]
        ]
    ]
