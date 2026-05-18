# Generate Behaviour Specification

## 1. Purpose

`Generate` creates the TOPAS input files for one simulation batch.

The operation is a deterministic text-generation step:

```text
selected TOPAS template/component files
+ configured phase-space files
+ configured nodes
+ configured placeholders
+ seed-base rule
= generated TOPAS input files
```

The generated files are later used by the run step.

---

## 2. Root folder rule

The app uses one configured root folder:

```text
AppRoot
```

Production example:

```text
/srv/cluster/tsebt
```

The app uses the same folder logic in development and deployment. Development only points `AppRoot` to a local test folder.

Observed production-style root:

```text
/srv/cluster/tsebt/
  analysis/
  archive/
  database/
  docs/
  inputs/
  logs/
  outputs/
  phsp-files/
  runs/
  scripts/
  templates/
```

For `Generate`, the directly relevant folders are:

```text
templates/     source TOPAS template/component files
inputs/        generated TOPAS input files
phsp-files/    configured phase-space files
runs/          TOPAS run output folders referenced inside generated inputs
```

---

## 3. Configuration source

`Generate` reads operational setup from `appsettings.json`.

The configuration defines:

```text
AppRoot
Templates folder
Inputs folder
Runs folder
Node list
Phase-space file list
Placeholder tokens
Current seed base / next seed base
```

The intended deployment model is that another clinic can edit `appsettings.json` and use the app without changing F# code.

---

## 4. Node model

The mother machine is also a compute node:

```text
mother = node01
```

Each configured node has a stable node digit:

```text
node01 -> 1
node02 -> 2
node03 -> 3
...
node07 -> 7
```

The node digit is used only for deterministic seed construction and generated file identity.

---

## 5. Template/component model

TOPAS source files live under:

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

The user selects the files that should be stitched into the generated TOPAS input.

The selected files are read as plain text.

---

## 6. Placeholder model

Template/component files contain plain text placeholders.

Current placeholders:

```text
__PHSP_FILE__
__OUTPUT_FILE__
__SEED__
```

The actual placeholder strings are configurable in `appsettings.json`.

Example:

```json
"Placeholders": {
  "PhaseSpaceFile": "__PHSP_FILE__",
  "OutputFile": "__OUTPUT_FILE__",
  "Seed": "__SEED__"
}
```

The app replaces exactly the configured tokens.

---

## 7. Stitching rule

Generate creates one full stitched TOPAS input file per phase-space/node combination.

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

---

## 8. Phase-space expansion rule

Generate expands selected files over every configured phase-space file and every configured node.

Example:

```text
22 phase-space files
7 nodes
```

Generated input count:

```text
22 × 7 = 154 input files
```

Each phase-space file is generated once per configured node.

---

## 9. Seed rule

Seed is built from:

```text
seed base + node digit
```

This is suffix construction.

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

Different phase-space files contain different particle streams, so the same seed base can be reused across phase-space files.

---

## 10. RunId rule

`RunId` is the identity of one generated TOPAS input/run.

The run id is based only on phase-space index and final seed because the final seed already encodes the seed base and node digit.

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

This keeps the generated identity short and deterministic.

---

## 11. Generated filename rule

Generated input files are written to:

```text
{AppRoot}/inputs/{seedBase}/
```

Filename pattern:

```text
input_sd{seed}_ps{phaseSpaceIndex}.txt
```

Examples:

```text
input_sd10011_ps01.txt
input_sd10012_ps01.txt
input_sd10013_ps01.txt

input_sd10011_ps22.txt
input_sd10012_ps22.txt
input_sd10013_ps22.txt
```

The filename is unique because `seed` encodes node and `phaseSpaceIndex` encodes the phase-space file.

---

## 12. Output path rule

TOPAS output path is injected by replacing:

```text
__OUTPUT_FILE__
```

Each generated input points to a unique run folder:

```text
{AppRoot}/runs/{runId}/
```

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

TOPAS writes CSV dose output and `.log` according to the paths defined inside the generated input file.

---

## 13. Replacement values per generated file

For every phase-space/node combination, Generate computes:

```text
RunId
Seed
PhaseSpaceFile path
OutputFile path/base
GeneratedInputFile path
```

Then it replaces configured placeholders:

```text
__PHSP_FILE__   -> configured phase-space file path for that phase-space index
__OUTPUT_FILE__ -> unique output file path/base for that run
__SEED__        -> seed built from seed base and node digit
```

---

## 14. SQLite record

For each generated input file, SQLite stores metadata.

Minimum fields:

```text
SeedBase
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

Initial status:

```text
Generated
```

SQLite stores metadata only. The generated TOPAS input file is stored on disk.

---

## 15. Generate result returned by server

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

The client formats display text from this structured result.

Example displayed summary:

```text
Seed base 1001 generated.
Inputs: 154
Nodes: 7
Phase-space files: 22
Input folder: /srv/cluster/tsebt/inputs/1001
```

---

## 16. Final Generate behavior

Generate reads `appsettings.json`.

Generate lists available TOPAS source files from `templates/`.

The user selects component files.

Generate expands selected files over all configured phase-space files and all configured nodes.

For 22 phase-space files and 7 nodes, Generate creates 154 generated TOPAS input files.

Each generated file is full stitched text.

Each generated file has configured placeholders replaced:

```text
__PHSP_FILE__
__OUTPUT_FILE__
__SEED__
```

Seed is seed base with node digit appended:

```text
1001 + node01 -> 10011
1001 + node02 -> 10012
1001 + node03 -> 10013
```

Seed is reused across different phase-space files.

Each generated run has a unique RunId:

```text
phsp{phaseSpaceIndex}_seed{seed}
```

Each generated file has a unique filename:

```text
input_sd{seed}_ps{phaseSpaceIndex}.txt
```

Each generated run points TOPAS output to:

```text
runs/{runId}/dose
```

SQLite records every generated run with status `Generated`.
