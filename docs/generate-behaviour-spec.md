# Generate Behaviour Specification

## Purpose

`Generate` creates TOPAS input files for one simulation batch.

Deterministic workflow:

```text
selected TOPAS component files
+ selected nodes
+ selected phase-space files
+ runtime seed base
+ configured placeholder tokens
= generated TOPAS input files + SQLite metadata
```

## Runtime root and folders

Generate uses configured `AppRoot` and relative paths:

```text
templates/   source TOPAS component files
inputs/      generated TOPAS input files
runs/        run folders referenced by generated TOPAS input files
database/    SQLite app memory
logs/        app/server logs
```

## Runtime seed-base rule

Seed base shown/used is resolved from SQLite:

```text
if generated_batches is empty:
    use configured seed base
else:
    use max(cast(seed_base as integer)) + 1
```

`appsettings.json` is never edited at runtime.

## Expansion rule

Generate expands selected values over:

```text
selected nodes x selected phase-space files
```

Example:

```text
7 selected nodes x 22 selected phase-space files = 154 generated input files
```

## Seed rule

Seed is built by suffixing node digit to seed base:

```text
seed = seedBase + nodeDigit
```

Examples:

```text
seedBase=1001, node 1 -> 10011
seedBase=1001, node 7 -> 10017
```

The same node seed pattern repeats across phase-space files.

## RunId rule

```text
runId = phsp{phaseSpaceIndex}_seed{seed}
```

Example:

```text
phsp01_seed10011
```

## Generated input file naming

```text
input_sd{seed}_ps{phaseSpaceIndex}_n{nodeDigit}.txt
```

Example:

```text
input_sd10011_ps01_n1.txt
```

Written under:

```text
{AppRoot}/inputs/{seedBase}/
```

## Output placeholder rule

`OutputFile` placeholder is replaced with:

```text
{AppRoot}/runs/{runId}/dose
```

## Placeholder replacement

Configured placeholders are replaced for each generated file:

- phase-space placeholder -> configured phase-space value
- output placeholder -> `{AppRoot}/runs/{runId}/dose`
- seed placeholder -> constructed seed

## Collision safety

Generate performs preflight collision checks before any file writes or SQLite inserts.

Checks:

1. Input batch folder collision:
   - target: `{AppRoot}/{Paths.Inputs}/{seedBase}`
   - if folder exists and contains files -> error

2. Planned generated input file collisions:
   - if any planned generated input path already exists -> error

3. Planned run folder collisions:
   - target: `{AppRoot}/{Paths.Runs}/{runId}`
   - if folder already exists -> error

4. Planned SQLite duplicate collisions:
   - if any planned `run_id` already exists in `generated_runs` -> error

All-or-nothing guarantee:

- if any collision is found, no files are written and no SQLite rows are inserted.

## SQLite persistence

On success:

- one `generated_batches` row is inserted with used `seed_base`
- one `generated_runs` row per generated file is inserted with run metadata

Initial run status:

```text
Generated
```

## Server result

Generate returns structured result including:

- `SeedBase`
- `GeneratedInputCount`
- `NodeCount`
- `PhaseSpaceCount`
- `InputFolder`
- `GeneratedRuns`

## Current test coverage

Automated tests currently cover:

- seed construction
- runId construction
- generated filename construction
- run/output path planning
- placeholder replacement
- stitch ordering
- planning expansion semantics
- collision preflight rejection for existing input folder content

