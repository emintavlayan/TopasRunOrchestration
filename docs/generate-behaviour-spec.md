# Generate Behaviour Specification

## Purpose

`Generate` creates TOPAS input files for one simulation batch.

It is a deterministic text-generation workflow:

```text
selected TOPAS component files
+ selected nodes
+ selected phase-space files
+ runtime seed base
+ configured placeholder tokens
= generated TOPAS input files + SQLite metadata
```

The generated files are used later by the Run step.

---

## Root Folder

The app uses one configured root folder:

```text
AppRoot
```

Production example:

```text
/srv/cluster/tsebt
```

Development uses the same folder logic with a local `AppRoot`, for example:

```text
C:\Dev\tsebt-local-root
```

For Generate, the relevant folders are:

```text
templates/   source TOPAS component files
inputs/      generated TOPAS input files
runs/        run folders referenced inside generated TOPAS input files
database/    SQLite app memory
logs/        app/server logs
```

The app must not depend on a separate development-only code path. Only `AppRoot` changes.

---

## Configuration Source

Generate reads static operational defaults from `appsettings.json` / `appsettings.Development.json`.

The configuration defines:

```text
AppRoot
relative paths for templates, inputs, runs, database, logs
placeholder tokens
default seed base
node list
phase-space file list
```

The intended deployment model is that another clinic can edit the JSON configuration and use the app without changing F# code.

---

## SQLite Runtime State

SQLite is the server-side runtime memory.

The seed base shown in the app is resolved as:

```text
if generated_batches is empty:
    use configured default seed base
else:
    use max(generated_batches.seed_base) + 1
```

After a successful Generate operation, the used seed base is inserted into `generated_batches`.

Example:

```text
configured default seed base = 1001

first generated batch uses 1001
next Generate screen shows 1002
next successful batch uses 1002
next Generate screen shows 1003
```

If `database/app.db` is deleted, the app starts again from the configured default seed base.

The app does not edit `appsettings.json` at runtime.

---

## Node Model

The mother machine is also a compute node:

```text
mother = node01
```

Each configured node has:

```text
Name
Digit
```

Example:

```text
node01 -> digit 1
node02 -> digit 2
node03 -> digit 3
...
node07 -> digit 7
```

The node digit is used for seed construction and generated input filename identity.

No node-specific root paths are configured for Generate.

---

## Phase-Space Model

Phase-space files are configured as indexed values.

Example:

```text
ps01 -> ps01.IAEAphsp
ps02 -> ps02.IAEAphsp
...
ps22 -> ps22.IAEAphsp
```

Generate does not manage phase-space folders. It only injects the configured phase-space value into the generated TOPAS input file.

---

## Component File Model

TOPAS component files live under:

```text
{AppRoot}/templates
```

The UI lists files grouped by folder name.

Example:

```text
templates/
  physics/
    em_standard_opt4.txt

  phantom/
    solid_water_profile_y.txt

  linac/
    jaws_reference.txt

  source/
    iaea_phase_space_source.txt
```

The user selects the files to stitch into generated TOPAS input files.

The selected files are read as plain text.

The selected file order must be deterministic:

```text
group/folder order
then file name order
```

---

## Placeholder Model

Component files contain plain text placeholders.

Current placeholder roles:

```text
PhaseSpaceFile
OutputFile
Seed
```

Default token values:

```text
__PHSP_FILE__
__OUTPUT_FILE__
__SEED__
```

The actual token strings are configurable.

Example:

```json
"Placeholders": {
  "PhaseSpaceFile": "__PHSP_FILE__",
  "OutputFile": "__OUTPUT_FILE__",
  "Seed": "__SEED__"
}
```

Generate replaces exactly the configured tokens.

---

## Stitching Rule

Generate creates one full stitched TOPAS input file per selected phase-space/node combination.

Generated file structure:

```text
generated header
blank line
content of selected file 1 after placeholder replacement
blank line
content of selected file 2 after placeholder replacement
blank line
content of selected file 3 after placeholder replacement
...
```

Generated header format:

```text
################################################################################
# GENERATED TOPAS INPUT
# PhaseSpace: ps01
# Node: node01
# Seed: 10011
# RunId: phsp01_seed10011
# Do not edit manually.
################################################################################
```

The final generated input file is self-contained stitched text, not only `includeFile` lines.

---

## Expansion Rule

Generate expands selected files over:

```text
selected nodes × selected phase-space files
```

Example:

```text
7 selected nodes
22 selected phase-space files
```

Generated input count:

```text
7 × 22 = 154 input files
```

---

## Seed Rule

Seed is constructed by suffixing the node digit to the runtime seed base.

Example:

```text
SeedBase = 1001
```

Generated node seeds:

```text
node01 -> 10011
node02 -> 10012
node03 -> 10013
node04 -> 10014
node05 -> 10015
node06 -> 10016
node07 -> 10017
```

The phase-space index does not change the seed.

Example:

```text
ps01 node01 -> 10011
ps01 node02 -> 10012
ps01 node03 -> 10013

ps22 node01 -> 10011
ps22 node02 -> 10012
ps22 node03 -> 10013
```

Different phase-space files contain different particle streams, so the same node seed pattern can be reused across phase-space files.

---

## RunId Rule

`RunId` identifies one generated TOPAS input/run.

RunId format:

```text
phsp{phaseSpaceIndex}_seed{seed}
```

Examples:

```text
phsp01_seed10011
phsp01_seed10012
phsp01_seed10013

phsp22_seed10011
phsp22_seed10012
phsp22_seed10013
```

The seed already encodes the seed base and node digit, so the node name is not duplicated in the RunId.

---

## Generated Filename Rule

Generated input files are written to:

```text
{AppRoot}/inputs/{seedBase}/
```

Filename pattern:

```text
input_sd{seed}_ps{phaseSpaceIndex}_n{nodeDigit}.txt
```

Examples:

```text
input_sd10011_ps01_n1.txt
input_sd10012_ps01_n2.txt
input_sd10013_ps01_n3.txt

input_sd10011_ps22_n1.txt
input_sd10012_ps22_n2.txt
input_sd10013_ps22_n3.txt
```

The filename is unique because:

```text
seed encodes node digit
phaseSpaceIndex encodes phase-space file
nodeDigit is also present for visual/debug clarity
```

---

## Output Path Rule

TOPAS output path is injected by replacing the configured `OutputFile` placeholder.

Output file base pattern:

```text
{AppRoot}/runs/{runId}/dose
```

Example:

```text
/srv/cluster/tsebt/runs/phsp01_seed10011/dose
/srv/cluster/tsebt/runs/phsp01_seed10012/dose
/srv/cluster/tsebt/runs/phsp22_seed10017/dose
```

TOPAS writes dose CSV and `.log` files according to paths defined inside the generated input file.

---

## Replacement Values Per Generated File

For every selected phase-space/node combination, Generate computes:

```text
RunId
Seed
PhaseSpaceFile value
OutputFile path/base
GeneratedInputFile path
RunFolder
```

Then it replaces configured placeholders:

```text
PhaseSpaceFile token -> configured phase-space value
OutputFile token     -> {AppRoot}/runs/{runId}/dose
Seed token           -> seed built from seed base and node digit
```

---

## Collision Safety

Before writing files, Generate should check for existing output collisions.

Required safety rule:

```text
if {AppRoot}/inputs/{seedBase} exists and contains files:
    return an error

if any planned run folder already exists and contains files:
    return an error
```

Generate should not silently overwrite generated input files or existing run folders.

This protects manual reset scenarios where the database is deleted but old generated files remain on disk.

---

## SQLite Records

For each successful generation batch, SQLite stores one batch record:

```text
SeedBase
CreatedAt
```

For each generated input file, SQLite stores one run record:

```text
BatchId
RunId
PhaseSpaceIndex
PhaseSpaceFile
NodeName
NodeDigit
Seed
InputFilePath
OutputFilePath
RunFolder
Status
CreatedAt
```

Initial run status:

```text
Generated
```

SQLite stores metadata only. Generated TOPAS input files remain normal files on disk.

---

## Server Result

The server returns structured data.

Minimum result fields:

```text
SeedBase
GeneratedInputCount
NodeCount
PhaseSpaceCount
InputFolder
GeneratedRuns
```

Each generated run record returned to the client includes:

```text
RunId
PhaseSpaceIndex
NodeDigit
Seed
InputFilePath
OutputFilePath
RunFolder
Status
```

The client formats human-readable text from this structured result.

---

## Final Generate Behaviour

Generate reads static configuration from JSON and runtime seed state from SQLite.

Generate lists available TOPAS component files from `templates/`.

The user selects component files, nodes, and phase-space files.

Generate expands selections over:

```text
selected nodes × selected phase-space files
```

Each generated input file is full stitched text with configured placeholders replaced.

Each generated run has:

```text
RunId:      phsp{phaseSpaceIndex}_seed{seed}
Filename:   input_sd{seed}_ps{phaseSpaceIndex}_n{nodeDigit}.txt
Run folder: {AppRoot}/runs/{runId}
Output:     {AppRoot}/runs/{runId}/dose
Status:     Generated
```

After successful generation, SQLite records the used seed base so the next Generate operation uses the next seed base.
