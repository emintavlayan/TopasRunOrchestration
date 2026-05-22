module Bootstrap

open System.IO
open TsebtConfig

/// Combines AppRoot and a relative path into an absolute path.
let combineAppRoot (appRoot: string) (relativePath: string) : string =
    Path.GetFullPath(Path.Combine(appRoot, relativePath))

/// Creates required root subfolders if they do not already exist.
let ensureRootFolders (settings: TsebtSettings) : Result<unit, string> =
    try
        let requiredFolders =
            [
                settings.Paths.Templates
                settings.Paths.Inputs
                settings.Paths.Runs
                settings.Paths.Outputs
                Path.GetDirectoryName(settings.Paths.Database)
                settings.Paths.Logs
            ]
            |> List.choose (fun value ->
                if System.String.IsNullOrWhiteSpace(value) then
                    None
                else
                    Some value)
            |> List.map (combineAppRoot settings.AppRoot)
            |> List.distinct

        requiredFolders
        |> List.iter (fun folderPath -> Directory.CreateDirectory(folderPath) |> ignore)

        Ok()
    with ex ->
        Error $"Failed to ensure root folders: {ex.Message}"
