module Server

open System
open Microsoft.Extensions.Configuration
open Microsoft.AspNetCore.Http
open FsToolkit.ErrorHandling
open SAFE
open Saturn
open Giraffe
open Shared

/// Builds AppConfigView from Tsebt settings.
let private toAppConfigView (settings: TsebtConfig.TsebtSettings) (seedBase: string) : AppConfigView =
    {
        AppRoot = settings.AppRoot
        SeedBase = seedBase
        Nodes = settings.Nodes |> List.map (fun node -> { Name = node.Name; Digit = node.Digit })
        PhaseSpaceFiles = settings.PhaseSpaceFiles |> List.map (fun file -> { Index = file.Index; Value = file.Value })
        Placeholders =
            [ settings.Placeholders.PhaseSpaceFile
              settings.Placeholders.OutputFile
              settings.Placeholders.Seed ]
    }

/// Builds IConfiguration from base and environment-specific appsettings plus environment variables.
let buildConfiguration () : IConfiguration =
    let basePath = AppContext.BaseDirectory

    let environmentName =
        match Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") with
        | value when not (String.IsNullOrWhiteSpace value) -> value
        | _ ->
            match Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") with
            | value when not (String.IsNullOrWhiteSpace value) -> value
            | _ -> "Production"

    let environmentAppsettingsFile = $"appsettings.{environmentName}.json"

    ConfigurationBuilder()
        .SetBasePath(basePath)
        .AddJsonFile("appsettings.json", false, false)
        .AddJsonFile(environmentAppsettingsFile, true, false)
        .AddEnvironmentVariables()
        .Build()

/// Loads Tsebt settings from configuration.
let private loadSettings () : Result<TsebtConfig.TsebtSettings, string> =
    buildConfiguration () |> TsebtConfig.load

/// Loads Tsebt settings and resolves the runtime next seed base.
let private loadSettingsWithNextSeedBase () : Result<TsebtConfig.TsebtSettings * string, string> =
    result {
        let! settings = loadSettings ()
        let! seedBase = SeedBaseStore.getNextSeedBase settings
        return settings, seedBase
    }

/// Returns configured app info for the generate flow.
let private getAppConfigHandler () : Async<Result<AppConfigView, string>> =
    async {
        return
            result {
                let! settings, seedBase = loadSettingsWithNextSeedBase ()
                return toAppConfigView settings seedBase
            }
    }

/// Returns template file metadata from the configured templates root.
let private getTemplateFilesHandler () : Async<Result<TemplateFileInfo list, string>> =
    async {
        return
            result {
                let! settings = loadSettings ()
                return! TemplateCatalog.listTemplateFiles settings
            }
    }

/// Returns a real generate preview using selected templates and configuration placeholders.
let private previewGenerateHandler (request: GeneratePreviewRequest) : Async<Result<GeneratePreviewResult, string>> =
    async {
        return
            result {
                let! settings, seedBase = loadSettingsWithNextSeedBase ()
                return! GeneratePreview.createPreview settings seedBase request
            }
    }

/// Executes real generation and returns persisted generated run metadata.
let private generateHandler (request: GenerateRequest) : Async<Result<GenerateResult, string>> =
    async {
        return
            result {
                let! settings, seedBase = loadSettingsWithNextSeedBase ()
                return! GenerateOperation.generate settings seedBase request
            }
    }

/// Exposes the temporary TSEBT API skeleton.
let topasApi (_: HttpContext) : ITopasApi = {
    getAppConfig = getAppConfigHandler
    getTemplateFiles = getTemplateFilesHandler
    previewGenerate = previewGenerateHandler
    generate = generateHandler
}

/// Creates the remoting web app for TSEBT API endpoints.
let webApp = Api.make topasApi

/// Returns a minimal server status endpoint.
let healthHandler : HttpHandler = text "Server is running."

/// Creates the Saturn application with only basic middleware and routes.
let app = application {
    use_router (choose [ route "/" >=> healthHandler; webApp ])
    memory_cache
    use_static "public"
    use_gzip
}

/// Runs configuration load, folder bootstrap, and SQLite initialization.
let initializeStartup () : Result<unit, string> =
    result {
        let! settings = loadSettings ()
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
