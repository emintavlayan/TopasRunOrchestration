module SqliteInit

open System
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

/// Returns true when the given table has the given column name.
let private hasColumn (connection: SqliteConnection) (tableName: string) (columnName: string) : bool =
    use command = connection.CreateCommand()
    command.CommandText <- $"PRAGMA table_info({tableName});"
    use reader = command.ExecuteReader()
    let mutable found = false

    while reader.Read() && not found do
        let currentName = reader.GetString(1)

        if String.Equals(currentName, columnName, StringComparison.OrdinalIgnoreCase) then
            found <- true

    found

/// Adds a column to generated_batches only when it does not already exist.
let private ensureGeneratedBatchColumn
    (connection: SqliteConnection)
    (columnName: string)
    (columnSqlTypeAndDefault: string)
    : unit =
    if not (hasColumn connection "generated_batches" columnName) then
        executeNonQuery connection $"ALTER TABLE generated_batches ADD COLUMN {columnName} {columnSqlTypeAndDefault};"

/// Ensures run foundation columns exist in generated_batches for backward-compatible databases.
let private ensureRunFoundationColumns (connection: SqliteConnection) : unit =
    ensureGeneratedBatchColumn connection "run_status" "TEXT NOT NULL DEFAULT 'Generated'"
    ensureGeneratedBatchColumn connection "slurm_job_id" "TEXT NULL"
    ensureGeneratedBatchColumn connection "manifest_path" "TEXT NULL"
    ensureGeneratedBatchColumn connection "script_path" "TEXT NULL"
    ensureGeneratedBatchColumn connection "submitted_at" "TEXT NULL"

/// Ensures collect foundation columns exist in generated_batches for backward-compatible databases.
let private ensureCollectFoundationColumns (connection: SqliteConnection) : unit =
    ensureGeneratedBatchColumn connection "collect_status" "TEXT NOT NULL DEFAULT 'NotCollected'"
    ensureGeneratedBatchColumn connection "collected_at" "TEXT NULL"
    ensureGeneratedBatchColumn connection "collect_output_folder" "TEXT NULL"
    ensureGeneratedBatchColumn connection "collect_summary_path" "TEXT NULL"
    ensureGeneratedBatchColumn connection "collect_csv_found_count" "INTEGER NULL"
    ensureGeneratedBatchColumn connection "collect_csv_missing_count" "INTEGER NULL"
    ensureGeneratedBatchColumn connection "collect_log_found_count" "INTEGER NULL"
    ensureGeneratedBatchColumn connection "collect_log_missing_count" "INTEGER NULL"

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
  created_at TEXT NOT NULL,
  run_status TEXT NOT NULL DEFAULT 'Generated',
  slurm_job_id TEXT NULL,
  manifest_path TEXT NULL,
  script_path TEXT NULL,
  submitted_at TEXT NULL,
  collect_status TEXT NOT NULL DEFAULT 'NotCollected',
  collected_at TEXT NULL,
  collect_output_folder TEXT NULL,
  collect_summary_path TEXT NULL,
  collect_csv_found_count INTEGER NULL,
  collect_csv_missing_count INTEGER NULL,
  collect_log_found_count INTEGER NULL,
  collect_log_missing_count INTEGER NULL
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

        ensureRunFoundationColumns connection
        ensureCollectFoundationColumns connection

        Ok databasePath
    with ex ->
        Error $"Failed to initialize SQLite: {ex.Message}"
