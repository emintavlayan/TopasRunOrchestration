module TsebtConfig

open System
open FsToolkit.ErrorHandling
open Microsoft.Extensions.Configuration

type TsebtNode = { Name: string; Digit: string }

type TsebtPhaseSpaceFile = { Index: string; Value: string }

type TsebtPaths = {
    Templates: string
    Inputs: string
    Runs: string
    Database: string
    Logs: string
}

type TsebtPlaceholders = {
    PhaseSpaceFile: string
    OutputFile: string
    Seed: string
}

type TsebtSeed = { CurrentBase: string }

type TsebtSettings = {
    AppRoot: string
    Paths: TsebtPaths
    Placeholders: TsebtPlaceholders
    Seed: TsebtSeed
    Nodes: TsebtNode list
    PhaseSpaceFiles: TsebtPhaseSpaceFile list
}

/// Reads and validates a required string configuration value.
let private requireValue (cfg: IConfiguration) (key: string) : Result<string, string> =
    match cfg.[key] with
    | null
    | "" -> Error $"Missing configuration value: {key}"
    | value -> Ok value

/// Reads all configured nodes with name and digit.
let private readNodes (cfg: IConfiguration) : Result<TsebtNode list, string> =
    cfg.GetSection("Tsebt:Nodes").GetChildren()
    |> Seq.map (fun section -> result {
        let! name = requireValue section "Name"
        let! digit = requireValue section "Digit"
        return { Name = name; Digit = digit }
    })
    |> Seq.toList
    |> List.sequenceResultM

/// Reads all configured phase-space files with index and value.
let private readPhaseSpaceFiles (cfg: IConfiguration) : Result<TsebtPhaseSpaceFile list, string> =
    cfg.GetSection("Tsebt:PhaseSpaceFiles").GetChildren()
    |> Seq.map (fun section -> result {
        let! index = requireValue section "Index"
        let! value = requireValue section "Value"
        return { Index = index; Value = value }
    })
    |> Seq.toList
    |> List.sequenceResultM

/// Loads Tsebt settings from application configuration.
let load (cfg: IConfiguration) : Result<TsebtSettings, string> = result {
    let! appRoot = requireValue cfg "Tsebt:AppRoot"
    let! templates = requireValue cfg "Tsebt:Paths:Templates"
    let! inputs = requireValue cfg "Tsebt:Paths:Inputs"
    let! runs = requireValue cfg "Tsebt:Paths:Runs"
    let! database = requireValue cfg "Tsebt:Paths:Database"
    let! logs = requireValue cfg "Tsebt:Paths:Logs"
    let! phaseSpaceFilePlaceholder = requireValue cfg "Tsebt:Placeholders:PhaseSpaceFile"
    let! outputFilePlaceholder = requireValue cfg "Tsebt:Placeholders:OutputFile"
    let! seedPlaceholder = requireValue cfg "Tsebt:Placeholders:Seed"
    let! currentSeedBase = requireValue cfg "Tsebt:Seed:CurrentBase"
    let! nodes = readNodes cfg
    let! phaseSpaceFiles = readPhaseSpaceFiles cfg

    return {
        AppRoot = appRoot
        Paths = {
            Templates = templates
            Inputs = inputs
            Runs = runs
            Database = database
            Logs = logs
        }
        Placeholders = {
            PhaseSpaceFile = phaseSpaceFilePlaceholder
            OutputFile = outputFilePlaceholder
            Seed = seedPlaceholder
        }
        Seed = { CurrentBase = currentSeedBase }
        Nodes = nodes
        PhaseSpaceFiles = phaseSpaceFiles
    }
}