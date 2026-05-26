# App Behaviour Specification

## Runtime model

- The app is a SAFE Light F# web app.
- `Server` owns configuration, file system access, SQLite, and orchestration.
- `Client` owns Elmish state and UI flow.
- `Shared` defines API contracts and DTOs.

## General settings

- App uses SQLite as local persistent runtime memory.
- SQLite stores generated batch/run metadata and statuses.
- Large simulation files remain on disk; database stores references.
- On startup, server creates required folders and initializes SQLite schema if missing.
- App configuration is file/environment based, not edited through the web UI.
- Windows is the development environment.
- Ubuntu 24.04 is the target runtime host.
- Deployment target is a published app folder copied to Linux, then optionally managed by systemd.

## Current implemented feature set

- Generate wizard end-to-end.
- Generate preview with real placeholder replacement.
- Real Generate file output.
- SQLite metadata persistence for generated batches and runs.
- SQLite-driven next seed base progression.
- Collision-safe Generate preflight validation before any write/insert.
- Run wizard, preflight, Slurm script preview, and submission.
- Collect wizard, preflight, CSV merge, and dose summary statistics.

## Not implemented

- Full history UI.

## Folder model

- AppRoot is configured in `appsettings`.
- Generated TOPAS inputs are written to `inputs/{seedBase}/`.
- TOPAS run outputs are expected directly in shared `runs/{seedBase}/`.
- Collect outputs are written to `outputs/{seedBase}/`.
- Phase-space files are not copied/managed by this app; TOPAS configuration defines their source paths.

## Run model

- Run reads generated batch metadata from SQLite.
- Run writes `runs/{seedBase}/run_manifest.tsv` and `runs/{seedBase}/run_batch.slurm`.
- Manifest rows include task id, node name, run id, input path, and log path.
- Slurm execution uses node assignment from manifest:
  - `srun --nodes=1 --ntasks=1 --nodelist="$NODE_NAME" "$TOPAS" "$INPUT_FILE" > "$LOG_FILE" 2>&1`
- Node names come from `appsettings` nodes.
- Run prevents double-submit and stores Slurm job metadata back to SQLite.

## UI path display

- Server data remains full absolute paths.
- Client UI may show paths relative to AppRoot for readability.

## Generate safety contract

Before Generate writes any files or inserts any metadata, the server validates:

- input seed folder collision
- generated input file collisions
- run folder collisions
- duplicate run id collisions in SQLite

If any check fails, Generate returns `Error` and performs no file write or metadata insert.

