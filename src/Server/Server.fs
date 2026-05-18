module Server

open Microsoft.Extensions.Configuration
open FsToolkit.ErrorHandling
open Saturn
open Giraffe
open Shared

module Storage =
    /// Stores in-memory todos for legacy tests.
    let todos = ResizeArray<Todo>()

    /// Adds a todo when the description is valid.
    let addTodo (todo: Todo) : Result<unit, string> =
        if Todo.isValid todo.Description then
            todos.Add todo
            Ok ()
        else
            Error "Invalid todo"

/// Returns a minimal server status endpoint.
let healthHandler : HttpHandler = text "Server is running."

/// Creates the Saturn application with only basic middleware and routes.
let app = application {
    use_router (choose [ route "/" >=> healthHandler ])
    memory_cache
    use_static "public"
    use_gzip
}

/// Builds IConfiguration including development appsettings.
let buildConfiguration () : IConfiguration =
    ConfigurationBuilder().SetBasePath(System.Environment.CurrentDirectory).AddJsonFile("appsettings.Development.json", false, false).Build()

/// Runs configuration load, folder bootstrap, and SQLite initialization.
let initializeStartup () : Result<unit, string> =
    result {
        let configuration = buildConfiguration ()
        let! settings = TsebtConfig.load configuration
        do! Bootstrap.ensureRootFolders settings
        let! _ = SqliteInit.initialize settings
        return ()
    }

/// Starts the web server after startup initialization succeeds.
[<EntryPoint>]
let main _ =
    match initializeStartup () with
    | Ok () ->
        run app
        0
    | Error errorMessage ->
        eprintfn "%s" errorMessage
        1
