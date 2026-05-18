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

## Not implemented

- Run workflow.
- Collect workflow.
- Slurm orchestration.
- Dose merge/statistics workflow.
- Full history UI.

## Generate safety contract

Before Generate writes any files or inserts any metadata, the server validates:

- input seed folder collision
- generated input file collisions
- run folder collisions
- duplicate run id collisions in SQLite

If any check fails, Generate returns `Error` and performs no file write or metadata insert.

