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
    Outputs: string
    Database: string
    Logs: string
}

type TsebtPlaceholders = {
    PhaseSpaceFile: string
    OutputFile: string
    Seed: string
}

type TsebtSeed = { CurrentBase: string }

type TsebtTopas = { Executable: string }

type TsebtSlurm = {
    Partition: string
    CpusPerTask: int
}

type TsebtSettings = {
    AppRoot: string
    Paths: TsebtPaths
    Placeholders: TsebtPlaceholders
    Seed: TsebtSeed
    Topas: TsebtTopas
    Slurm: TsebtSlurm
    Nodes: TsebtNode list
    PhaseSpaceFiles: TsebtPhaseSpaceFile list
}

/// Reads and validates a required string configuration value.
let private requireValue (cfg: IConfiguration) (key: string) : Result<string, string> =
    match cfg.[key] with
    | null
    | "" -> Error $"Missing configuration value: {key}"
    | value -> Ok value

/// Reads an optional string configuration value with fallback default.
let private valueOrDefault (cfg: IConfiguration) (key: string) (fallback: string) : string =
    match cfg.[key] with
    | null
    | "" -> fallback
    | value -> value

/// Reads an optional integer configuration value with fallback default.
let private intOrDefault (cfg: IConfiguration) (key: string) (fallback: int) : int =
    match cfg.[key] with
    | null
    | "" -> fallback
    | value ->
        match Int32.TryParse value with
        | true, parsed -> parsed
        | false, _ -> fallback

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

/// Validates that a configured list contains at least one item.
let private validateNonEmptyList (name: string) (items: 'T list) : Result<'T list, string> =
    if List.isEmpty items then
        Error $"Configuration list '{name}' must not be empty"
    else
        Ok items

/// Validates that projected values are unique for a configured list.
let private validateUniqueBy (name: string) (selector: 'T -> string) (items: 'T list) : Result<'T list, string> =
    let duplicates =
        items
        |> List.countBy selector
        |> List.filter (fun (_, count) -> count > 1)
        |> List.map fst

    match duplicates with
    | [] -> Ok items
    | xs ->
        let joined = String.Join(", ", xs)
        Error $"Configuration '{name}' contains duplicate values: {joined}"

/// Validates that the configured seed base is numeric.
let private validateNumericSeedBase (seedBase: string) : Result<string, string> =
    match Int64.TryParse seedBase with
    | true, _ -> Ok seedBase
    | false, _ -> Error $"Configuration 'Tsebt:Seed:CurrentBase' must be numeric but was '{seedBase}'"

/// Loads Tsebt settings from application configuration.
let load (cfg: IConfiguration) : Result<TsebtSettings, string> = result {
    let! appRoot = requireValue cfg "Tsebt:AppRoot"
    let! templates = requireValue cfg "Tsebt:Paths:Templates"
    let! inputs = requireValue cfg "Tsebt:Paths:Inputs"
    let! runs = requireValue cfg "Tsebt:Paths:Runs"
    let! outputs = requireValue cfg "Tsebt:Paths:Outputs"
    let! database = requireValue cfg "Tsebt:Paths:Database"
    let! logs = requireValue cfg "Tsebt:Paths:Logs"
    let! phaseSpaceFilePlaceholder = requireValue cfg "Tsebt:Placeholders:PhaseSpaceFile"
    let! outputFilePlaceholder = requireValue cfg "Tsebt:Placeholders:OutputFile"
    let! seedPlaceholder = requireValue cfg "Tsebt:Placeholders:Seed"
    let! currentSeedBase = requireValue cfg "Tsebt:Seed:CurrentBase"
    let topasExecutable = valueOrDefault cfg "Tsebt:Topas:Executable" "topas"
    let slurmPartition = valueOrDefault cfg "Tsebt:Slurm:Partition" "compute"
    let slurmCpusPerTask = intOrDefault cfg "Tsebt:Slurm:CpusPerTask" 1
    let! nodes = readNodes cfg
    let! phaseSpaceFiles = readPhaseSpaceFiles cfg
    let! _ = validateNonEmptyList "Tsebt:Nodes" nodes
    let! _ = validateNonEmptyList "Tsebt:PhaseSpaceFiles" phaseSpaceFiles
    let! _ = validateUniqueBy "Tsebt:Nodes:Digit" (fun node -> node.Digit) nodes
    let! _ = validateUniqueBy "Tsebt:PhaseSpaceFiles:Index" (fun file -> file.Index) phaseSpaceFiles
    let! _ = validateNumericSeedBase currentSeedBase

    return {
        AppRoot = appRoot
        Paths = {
            Templates = templates
            Inputs = inputs
            Runs = runs
            Outputs = outputs
            Database = database
            Logs = logs
        }
        Placeholders = {
            PhaseSpaceFile = phaseSpaceFilePlaceholder
            OutputFile = outputFilePlaceholder
            Seed = seedPlaceholder
        }
        Seed = { CurrentBase = currentSeedBase }
        Topas = { Executable = topasExecutable }
        Slurm = {
            Partition = slurmPartition
            CpusPerTask = slurmCpusPerTask
        }
        Nodes = nodes
        PhaseSpaceFiles = phaseSpaceFiles
    }
}
