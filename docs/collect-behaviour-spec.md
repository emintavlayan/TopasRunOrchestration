# Collect Behaviour Specification

## Purpose

`Collect` consolidates TOPAS run outputs for one generated batch (`seedBase`), producing merged phase-space CSV files and one final dose summary.

Current cluster model:

- run outputs are read from shared `runs/{seedBase}` under AppRoot
- no node-local copy-back step is required in the current implementation
- phase-space source files are not managed by Collect

## Input path rules

Batch id:

```text
seedBase
```

Run id rule:

```text
seed{seed}_phsp{phaseSpaceIndex}
```

Expected TOPAS CSV per run:

```text
{AppRoot}/{Paths.Runs}/{seedBase}/seed{seed}_phsp{phaseSpaceIndex}.csv
```

Expected TOPAS log per run:

```text
{AppRoot}/{Paths.Runs}/{seedBase}/seed{seed}_phsp{phaseSpaceIndex}.log
```

## Output folder and files

Collect output folder:

```text
{AppRoot}/{Paths.Outputs}/{seedBase}
```

Files produced:

```text
collect_manifest.tsv
merged/phsp{phaseSpaceIndex}_merged.csv (one per phase-space index)
dose_summary.csv
```

## Preflight behavior

Collect preflight reads `generated_runs` for the selected `seedBase` and checks:

- generated runs exist
- run folder exists
- expected CSV files exist
- expected CSV files are non-empty
- expected CSV files contain at least one numeric TOPAS scorer row
- expected log files exist
- expected logs contain TOPAS completion timing footer markers

Required TOPAS completion markers:

- `Elapsed times:`
- `Parameter Reading`
- `Initialization:`
- `Execution:`
- `Finalization:`
- `Total:`

Important rule:

- Geant4/TOPAS warning-like lines (for example `G4Exception`, `ERROR`, `Exception`) do not fail collect if completion footer markers exist.
- Missing completion footer blocks collect.

## Exclusion behavior

Collect supports exclusion-based recovery from failed runs:

- Exclude phase-space indexes (`ExcludedPhaseSpaceIndexes`)
- Exclude node digits (`ExcludedNodeDigits`)

Both are applied before health and balance checks.
Current UI allows one mode at a time (phase-space or node exclusion).

After exclusions, collect requires balanced remaining rows:

- each remaining phase-space index must have the same remaining node set
- otherwise collect is blocked with:
  `Remaining collect set is unbalanced. Exclude a full phase-space or full node.`

## Merge behavior

Per phase-space index:

- group generated runs by `phase_space_index`
- read one CSV per node in the group
- require compatible row and column counts
- preserve non-dose columns from the first file
- sum the last numeric dose-like column across node files
- write `phspXX_merged.csv`

## Summary statistics behavior

Across all merged phase-space files:

- require compatible row and column counts
- use the same target dose-like numeric column
- preserve non-dose columns from first merged file
- compute per row:
  - `mean`
  - `median`
  - `standard_deviation` (sample SD, `n-1`; `0` when `count=1`)
  - `count`
- write `dose_summary.csv`

## Collect operation behavior

`collectBatch`:

1. Runs preflight.
2. Rejects when CSV/log/row-balance preflight fails.
3. Rejects output collisions before writing files.
4. Merges phase-space CSV files.
5. Computes `dose_summary.csv`.
6. Writes `collect_manifest.tsv`.
7. Updates `generated_batches` collect metadata.

All-or-nothing for status:

- `collect_status` is set to `Collected` only after all outputs succeed.
- partial files are not auto-deleted on failure in current version.

## SQLite metadata

`generated_batches` collect columns:

- `collect_status` (`NotCollected` default, `Collected` after success)
- `collected_at`
- `collect_output_folder`
- `collect_summary_path`
- `collect_csv_found_count`
- `collect_csv_missing_count`
- `collect_log_found_count`
- `collect_log_missing_count`

## Current limitations

- CSV parser assumes compatible row/column structure across files being merged.
- Merge logic assumes the last numeric column is the dose-like value to sum.
- Missing/empty/non-numeric CSV files block collect.
- Missing TOPAS completion timing footer blocks collect.
- No recollect/overwrite workflow in current version.
