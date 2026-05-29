# App Behaviour Specification

## Architecture

The application follows SAFE Light separation:

- `Shared`: DTOs and API contracts
- `Server`: filesystem/SQLite/process orchestration
- `Client`: wizard UI state and flow

## Implemented workflows

- Generate workflow (preview + execution + metadata persistence)
- Run workflow (batch list, preflight, per-node Slurm script planning, submit)
- Collect workflow (preflight + optional exclusions, CSV merge, statistics, metadata update)

## Persistent model

SQLite stores orchestration metadata (not large simulation files):

- generated batch metadata
- generated run metadata
- run submission metadata
- collect metadata and output pointers

Files remain on disk under `AppRoot`.

## Folder model

```text
templates/   TOPAS source fragments/components
inputs/      generated TOPAS inputs per seed base
runs/        run artifacts and TOPAS CSV/log outputs
outputs/     collect output files
database/    SQLite database
logs/        application logs
```

## Batch identity model

- `seedBase` is the batch id.
- `RunId` format: `seed{seed}_phsp{phaseSpaceIndex}`.
- Seed format: `seedBase + nodeDigit`.

## Safety model

Generate and Run perform preflight/collision checks before committing state.

- Generate is all-or-nothing on collision failures.
- Run blocks double-submit and preflight failures.
- Collect blocks when required CSV/log health checks fail or remaining rows are unbalanced.

Collect preflight success requires:

- CSV exists
- CSV is non-empty
- CSV has at least one numeric TOPAS data row
- log exists
- log contains TOPAS completion timing footer markers

## Runtime assumptions

- Dev host is typically Windows.
- Target runtime is Linux (cluster/server).
- Slurm/TOPAS availability is host-dependent and not required for unit tests.

## Client UX model

- One shared app shell with top navbar for `Generate`, `Run`, `Collect`.
- One shared wizard shell for all workflows:
  - left vertical stepper
  - scrollable content body
  - fixed footer actions (`Cancel`, `Previous`, primary)
- Theme selector supports: `Light`, `Dark`, `Corporate`, `Night`, `Cyberpunk`.

## Not implemented

- History/batch-browser UI remains minimal.
- No collect overwrite/recollect flow in first version.
- No run rerun/overwrite in first version.
