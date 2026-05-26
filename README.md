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
- Run wizard UI.
- Run preflight, Slurm script preview, and `sbatch` submission.
- Collect wizard UI.
- Collect preflight and preview.
- Per-phase-space node CSV merge.
- Final dose summary statistics (mean, median, standard deviation, count).
- Collect operation with SQLite metadata updates.
- Automated tests across config, generate, run, and collect workflows.

Not implemented yet:

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
inputs/      generated TOPAS input files at inputs/{seedBase}/
runs/        shared run/output folder at runs/{seedBase}/ (TOPAS CSV/log files are written here)
outputs/     collect outputs per seed base (merged csv + dose summary)
database/    SQLite database
logs/        application/runtime logs
```

Notes:

- Phase-space files are not copied or managed by the app; TOPAS input configuration controls where phsp data is read from.
- Collect reads directly from the shared `runs/{seedBase}` folder; no node copy-back flow is required in the current model.

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

## Run summary

Run submits one generated batch (`seedBase`) using SQLite batch metadata.

Run writes:

```text
runs/{seedBase}/run_manifest.tsv
runs/{seedBase}/run_batch.slurm
```

Manifest columns:

- task id
- node name
- run id
- input file path
- log file path

Slurm script execution line:

```bash
srun --nodes=1 --ntasks=1 --nodelist="$NODE_NAME" "$TOPAS" "$INPUT_FILE" > "$LOG_FILE" 2>&1
```

Run behavior notes:

- node names come from `appsettings` node configuration
- partition/topas executable/cpus-per-task come from configuration
- double-submit is blocked when a batch is already submitted
- Slurm submission metadata is written back to SQLite

UI display rule:

- server stores full absolute paths
- Run UI may display AppRoot-relative paths for readability

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

## Testing coverage

Automated tests are xUnit-based and run with `dotnet test Application.sln`.
They use temporary folders plus fake CSV/process output fixtures, and do not require TOPAS or Slurm.

Coverage currently includes:

- configuration validation and bootstrap folder creation
- generate planning, placeholder replacement, filesystem generation, and collision protection
- run manifest/script/job-id parsing logic and preflight/double-submit guards (without real Slurm)
- collect preflight, CSV merge/statistics, and full collect operation with fake CSV/log data

Manual verification is still required for:

- real `sbatch` submission and cluster behavior
- real TOPAS execution
- clinical review of real TOPAS CSV outputs and dose interpretation

## Documentation

See:

- `docs/app-behaviour-spec.md`
- `docs/generate-behaviour-spec.md`
- `docs/collect-behaviour-spec.md`
- `docs/generate-wizard-ux-spec.md`
