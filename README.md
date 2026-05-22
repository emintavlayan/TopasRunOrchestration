# TopasRunOrchestration

TopasRunOrchestration is a SAFE Stack F# web application for preparing and managing TOPAS Monte Carlo simulation batches for TSEBT workflows.

The app is intended to run on a central Linux machine on the treatment-planning or compute network. Users access it through a browser. The server reads local configuration, lists available TOPAS component files, generates stitched TOPAS input files, and records generation metadata in SQLite.

## Current status

Implemented:

- SAFE Stack application skeleton.
- App configuration from `appsettings.json` / `appsettings.Development.json`.
- Local root bootstrap for required folders.
- SQLite initialization.
- SQLite-backed seed-base progression.
- Recursive TOPAS component listing from `templates/`.
- Generate wizard UI.
- Generate preview.
- Real generation of stitched TOPAS input files.
- SQLite metadata insertion for generated batches and runs.
- Collision-safe Generate preflight (input folder, generated input files, run folders, duplicate run IDs).
- Basic automated tests for Generate rules and collision preflight.

Not implemented yet:

- Run workflow.
- Slurm submission / execution orchestration.
- Collect workflow.
- Dose CSV merging.
- Statistics across phase-space files.
- History / batch browser UI.
- Production systemd service setup.

## Runtime root folder

The app is driven by one configured root folder:

```text
AppRoot
```

Development example:

```text
C:\Dev\tsebt-local-root
```

Production example:

```text
/srv/cluster/tsebt
```

Expected folders under `AppRoot`:

```text
templates/   TOPAS source template/component files
inputs/      generated TOPAS input files
runs/        TOPAS output/run folders referenced by generated inputs
database/    SQLite database
logs/        application/runtime logs
```

## Generate summary

Generate creates TOPAS input files for one simulation batch from:

```text
selected TOPAS component files
selected nodes
selected phase-space files
configured placeholders
runtime seed base
```

Seed rule:

```text
seed = seedBase + nodeDigit
```

RunId rule:

```text
seed{seed}_phsp{phaseSpaceIndex}
```

Generated filename rule:

```text
seed{seed}_phsp{phaseSpaceIndex}.txt
```

Output placeholder value:

```text
{AppRoot}/runs/{seedBase}/{runId}
```

## Generate safety

Generate validates all planned targets before writing files or inserting SQLite metadata.

Preflight checks:

- input seed folder already contains files -> error
- any planned generated input file already exists -> error
- run seed folder already contains files -> error
- any planned output base path already exists (`path`, `path.csv`, `path.log`) -> error
- any planned run ID already exists in SQLite -> error

All-or-nothing behavior:

- on any collision error, no files are written and no SQLite rows are inserted.

## Build, run, test

Build:

```powershell
dotnet build Application.sln
```

Run:

```powershell
dotnet run --project .\src\Server
```

Test:

```powershell
dotnet test Application.sln
```

## Documentation

See:

- `docs/app-behaviour-spec.md`
- `docs/generate-behaviour-spec.md`
- `docs/generate-wizard-ux-spec.md`
