module Server

open Microsoft.Extensions.Configuration
open Microsoft.AspNetCore.Http
open FsToolkit.ErrorHandling
open SAFE
open Saturn
open Giraffe
open Shared

/// Builds AppConfigView from Tsebt settings.
let private toAppConfigView (settings: TsebtConfig.TsebtSettings) : AppConfigView =
    {
        AppRoot = settings.AppRoot
        SeedBase = settings.Seed.CurrentBase
        Nodes = settings.Nodes |> List.map (fun node -> { Name = node.Name; Digit = node.Digit })
        PhaseSpaceFiles = settings.PhaseSpaceFiles |> List.map (fun file -> { Index = file.Index; Value = file.Value })
        Placeholders =
            [ settings.Placeholders.PhaseSpaceFile
              settings.Placeholders.OutputFile
              settings.Placeholders.Seed ]
    }

/// Builds IConfiguration including development appsettings.
let buildConfiguration () : IConfiguration =
    ConfigurationBuilder().SetBasePath(System.Environment.CurrentDirectory).AddJsonFile("appsettings.Development.json", false, false).Build()

/// Loads Tsebt settings from configuration.
let private loadSettings () : Result<TsebtConfig.TsebtSettings, string> =
    buildConfiguration () |> TsebtConfig.load

/// Returns configured app info for the generate flow.
let private getAppConfigHandler () : Async<Result<AppConfigView, string>> =
    async { return loadSettings () |> Result.map toAppConfigView }

/// Returns template file metadata from the configured templates root.
let private getTemplateFilesHandler () : Async<Result<TemplateFileInfo list, string>> =
    async {
        return
            result {
                let! settings = loadSettings ()
                return! TemplateCatalog.listTemplateFiles settings
            }
    }

/// Returns a deterministic preview placeholder until real stitching is implemented.
let private previewGenerateHandler (request: GeneratePreviewRequest) : Async<Result<GeneratePreviewResult, string>> =
    async {
        let runId = $"phsp{request.PhaseSpaceIndex}_seedpreview{request.NodeDigit}"

        return
            Ok {
                RunId = runId
                Seed = $"preview{request.NodeDigit}"
                InputFileName = $"input_preview_ps{request.PhaseSpaceIndex}_n{request.NodeDigit}.txt"
                OutputFilePath = $"runs/{runId}/dose"
                StitchedPreviewText = "# Preview stub. Real stitching not implemented yet."
            }
    }

/// Returns a generation summary placeholder until real generation is implemented.
let private generateHandler (request: GenerateRequest) : Async<Result<GenerateResult, string>> =
    async {
        let generatedCount = request.SelectedNodeDigits.Length * request.SelectedPhaseSpaceIndexes.Length

        return
            Ok {
                SeedBase = "preview"
                GeneratedInputCount = generatedCount
                NodeCount = request.SelectedNodeDigits.Length
                PhaseSpaceCount = request.SelectedPhaseSpaceIndexes.Length
                InputFolder = "inputs/preview"
                GeneratedRuns = []
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
