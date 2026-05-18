module TemplateCatalog

open System
open System.IO
open Shared
open TsebtConfig
open Bootstrap

/// Converts a full template file path into a TemplateFileInfo value.
let private toTemplateFileInfo (templatesRoot: string) (fullPath: string) : TemplateFileInfo =
    let relativePath = Path.GetRelativePath(templatesRoot, fullPath)
    let normalizedRelativePath = relativePath.Replace('\\', '/')
    let folderPath = Path.GetDirectoryName(normalizedRelativePath)

    let groupName =
        if String.IsNullOrWhiteSpace(folderPath) then
            "."
        else
            folderPath.Replace('\\', '/')

    {
        Group = groupName
        FileName = Path.GetFileName(fullPath)
        RelativePath = normalizedRelativePath
    }

/// Lists template files recursively from the configured templates root.
let listTemplateFiles (settings: TsebtSettings) : Result<TemplateFileInfo list, string> =
    try
        let templatesRoot = combineAppRoot settings.AppRoot settings.Paths.Templates

        if not (Directory.Exists templatesRoot) then
            Ok []
        else
            Directory.GetFiles(templatesRoot, "*", SearchOption.AllDirectories)
            |> Array.map (toTemplateFileInfo templatesRoot)
            |> Array.sortBy (fun file -> file.Group, file.FileName)
            |> Array.toList
            |> Ok
    with ex ->
        Error $"Failed to list template files: {ex.Message}"