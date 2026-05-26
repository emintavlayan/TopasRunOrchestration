# TopasRunOrchestration

TopasRunOrchestration is a SAFE Stack F# web application that prepares, submits, and collects TOPAS Monte Carlo simulation batches for TSEBT workflows.

## What it does

The application currently supports three end-to-end workflows:

1. Generate
2. Run
3. Collect

At a high level:

- `Generate` builds TOPAS input files from selected templates, nodes, and phase-space files.
- `Run` prepares Slurm manifest/script files and submits the batch with `sbatch`.
- `Collect` reads TOPAS CSV/log outputs, merges node outputs per phase-space, and computes final dose statistics.

## Runtime model

The app runs as a server with browser-based UI clients.

- Server owns file system, SQLite, and orchestration logic.
- Client owns wizard UI state and API calls.
- Shared project owns DTOs and API contracts.

## AppRoot folder structure

The app is driven by one configured root path (`Tsebt:AppRoot`).

```text
templates/   TOPAS source template/component files
inputs/      generated TOPAS input files at inputs/{seedBase}/
runs/        run artifacts and TOPAS run outputs at runs/{seedBase}/
outputs/     collect outputs at outputs/{seedBase}/
database/    SQLite database
logs/        application/runtime logs
```

Notes:

- Phase-space files are not copied or managed by this app.
- Collect reads from shared `runs/{seedBase}` in the current model.

## Generate summary

- Seed rule: `seed = seedBase + nodeDigit`
- RunId rule: `seed{seed}_phsp{phaseSpaceIndex}`
- Generated input file: `seed{seed}_phsp{phaseSpaceIndex}.txt`
- Output placeholder target: `{AppRoot}/runs/{seedBase}/{runId}`

Generate is collision-safe and all-or-nothing.

## Run summary

- Runs against one generated batch (`seedBase`).
- Writes:

```text
runs/{seedBase}/run_manifest.tsv
runs/{seedBase}/run_batch.slurm
```

- Manifest columns:
  - task id
  - node name
  - run id
  - input file path
  - log file path

- Slurm execution line:

```bash
srun --nodes=1 --ntasks=1 --nodelist="$NODE_NAME" "$TOPAS" "$INPUT_FILE" > "$LOG_FILE" 2>&1
```

- Double-submit is blocked when already submitted.

## Collect summary

- Preflight checks expected CSV/log files for a selected `seedBase`.
- Missing CSV blocks collect.
- Missing log is warning-only in current behavior.
- Produces:

```text
outputs/{seedBase}/collect_manifest.tsv
outputs/{seedBase}/phsp{phaseSpaceIndex}_merged.csv
outputs/{seedBase}/dose_summary.csv
```

- Final summary includes `mean`, `median`, `standard_deviation`, `count`.

## Build, run, test

Build:

```powershell
dotnet build Application.sln
```

Run server:

```powershell
dotnet run --project .\src\Server
```

Run tests:

```powershell
dotnet test Application.sln
```

## Testing

Server tests are xUnit-based and run via `dotnet test Application.sln`.

Automated tests:

- do not require TOPAS
- do not require Slurm
- use temporary folders and fake CSV/process output fixtures

Manual smoke testing is still required for:

- real Slurm scheduling behavior
- real TOPAS execution runtime behavior
- clinical interpretation of real dose outputs

## Documentation map

Start here for onboarding:

- `docs/onboarding.md`

Behavior specifications:

- `docs/app-behaviour-spec.md`
- `docs/generate-behaviour-spec.md`
- `docs/run-behaviour-spec.md`
- `docs/collect-behaviour-spec.md`

UX and engineering style:

- `docs/generate-wizard-ux-spec.md`
- `docs/safe-light-fsharp-doctrine.md`

Testing:

- `docs/test-coverage.md`
