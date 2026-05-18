module SqliteInit

open Microsoft.Data.Sqlite
open TsebtConfig
open Bootstrap

/// Resolves the SQLite database file path from settings.
let getDatabasePath (settings: TsebtSettings) : string =
    combineAppRoot settings.AppRoot settings.Paths.Database

/// Executes a single SQL statement against an open connection.
let private executeNonQuery (connection: SqliteConnection) (sql: string) : unit =
    use command = connection.CreateCommand()
    command.CommandText <- sql
    command.ExecuteNonQuery() |> ignore

/// Initializes SQLite file and required tables if missing.
let initialize (settings: TsebtSettings) : Result<string, string> =
    let databasePath = getDatabasePath settings

    try
        let connectionStringBuilder = SqliteConnectionStringBuilder()
        connectionStringBuilder.DataSource <- databasePath

        use connection = new SqliteConnection(connectionStringBuilder.ConnectionString)
        connection.Open()

        executeNonQuery
            connection
            """CREATE TABLE IF NOT EXISTS generated_batches (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  seed_base TEXT NOT NULL,
  created_at TEXT NOT NULL
);"""

        executeNonQuery
            connection
            """CREATE TABLE IF NOT EXISTS generated_runs (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  batch_id INTEGER NOT NULL,
  run_id TEXT NOT NULL,
  phase_space_index TEXT NOT NULL,
  phase_space_file TEXT NOT NULL,
  node_name TEXT NOT NULL,
  node_digit TEXT NOT NULL,
  seed TEXT NOT NULL,
  input_file_path TEXT NOT NULL,
  output_file_path TEXT NOT NULL,
  run_folder TEXT NOT NULL,
  status TEXT NOT NULL,
  created_at TEXT NOT NULL,
  FOREIGN KEY (batch_id) REFERENCES generated_batches(id)
);"""

        Ok databasePath
    with ex ->
        Error $"Failed to initialize SQLite: {ex.Message}"