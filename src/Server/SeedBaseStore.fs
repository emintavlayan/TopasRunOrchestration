module SeedBaseStore

open System
open System.IO
open Microsoft.Data.Sqlite
open FsToolkit.ErrorHandling
open TsebtConfig
open Bootstrap

/// Resolves the absolute SQLite database file path from settings.
let private getDatabasePath (settings: TsebtSettings) : string =
    combineAppRoot settings.AppRoot settings.Paths.Database

/// Parses a seed base string into an integer.
let private parseSeedBase (seedBase: string) : Result<int, string> =
    match Int32.TryParse seedBase with
    | true, value -> Ok value
    | false, _ -> Error $"Seed base is not a valid integer: {seedBase}"

/// Checks whether a table exists in the current SQLite database.
let private tableExists (connection: SqliteConnection) (tableName: string) : bool =
    use command = connection.CreateCommand()
    command.CommandText <- "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$tableName LIMIT 1;"
    command.Parameters.AddWithValue("$tableName", tableName) |> ignore
    not (isNull (command.ExecuteScalar()))

/// Gets the list of column names for a SQLite table.
let private getTableColumns (connection: SqliteConnection) (tableName: string) : string list =
    use command = connection.CreateCommand()
    command.CommandText <- $"PRAGMA table_info({tableName});"
    use reader = command.ExecuteReader()
    let mutable names = []

    while reader.Read() do
        names <- reader.GetString(1) :: names

    names |> List.rev

/// Selects the seed-base column name from existing generated_batches schema.
let private selectSeedBaseColumn (columns: string list) : Result<string, string> =
    let byName =
        columns
        |> List.tryFind (fun column -> String.Equals(column, "seed_base", StringComparison.OrdinalIgnoreCase))

    match byName with
    | Some column -> Ok column
    | None ->
        let columnsText = String.concat ", " columns
        Error $"No seed-base column found in generated_batches. Existing columns: {columnsText}"

/// Reads the maximum used seed base from generated_batches if available.
let private tryReadMaxUsedSeedBase (connection: SqliteConnection) : Result<int option, string> =
    if not (tableExists connection "generated_batches") then
        Ok None
    else
        result {
            let columns = getTableColumns connection "generated_batches"
            let! seedBaseColumn = selectSeedBaseColumn columns
            use command = connection.CreateCommand()
            command.CommandText <- $"SELECT MAX(CAST([{seedBaseColumn}] AS INTEGER)) FROM generated_batches;"
            let scalar = command.ExecuteScalar()

            if isNull scalar || scalar = box DBNull.Value then
                return None
            else
                let maxValueText = Convert.ToString(scalar)
                let! maxValue = parseSeedBase maxValueText
                return Some maxValue
        }

/// Gets the runtime next seed base using SQLite if available, otherwise config default.
let getNextSeedBase (settings: TsebtSettings) : Result<string, string> = result {
    let! configSeedBase = parseSeedBase settings.Seed.CurrentBase
    let databasePath = getDatabasePath settings

    if not (File.Exists databasePath) then
        return string configSeedBase
    else
        let connectionStringBuilder = SqliteConnectionStringBuilder()
        connectionStringBuilder.DataSource <- databasePath
        use connection = new SqliteConnection(connectionStringBuilder.ConnectionString)
        connection.Open()
        let! maxUsedSeedBase = tryReadMaxUsedSeedBase connection

        match maxUsedSeedBase with
        | None -> return string configSeedBase
        | Some maxUsed -> return string (maxUsed + 1)
}