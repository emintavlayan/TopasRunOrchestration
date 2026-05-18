# Generate Wizard UX Specification

## Purpose

The Generate page uses a step-by-step wizard.

The goal is to avoid one large form and guide the user through a predictable file-generation workflow:

```text
select TOPAS components
select nodes
select phase-space files
review generated plan
generate files
show result
```

The client does not stitch files. It asks the server for configuration, preview, and generation.

---

## Wizard Steps

The Generate wizard has six steps:

```text
1. Welcome
2. Select TOPAS components
3. Select nodes
4. Select phase-space files
5. Review
6. Result
```

Navigation pattern:

```text
Cancel      Previous      Next
Cancel      Previous      Generate
```

The first screen has:

```text
Cancel      Start
```

Button behavior:

```text
Cancel      text-style button on the left
Previous    secondary button when available
Start/Next/Generate/Back to Generate    primary button on the right
```

---

## Step 1 — Welcome

### Purpose

Explain what Generate does and show the current runtime seed base before the user starts.

### Content

The screen shows:

```text
Generate creates TOPAS input files for one simulation batch.

For each selected node and selected phase-space file, one TOPAS input file will be generated.
```

It also shows runtime/configuration values:

```text
Current seed base: 1002
Configured nodes: 7
Configured phase-space files: 22
Expected full batch size: 154 input files
```

The current seed base comes from SQLite runtime state:

```text
if no previous generated batch exists:
    configured default seed base
else:
    max generated batch seed base + 1
```

### Actions

```text
Cancel
Start
```

`Start` moves to component selection.

---

## Step 2 — Select TOPAS Components

### Purpose

Choose the TOPAS component files that will be stitched into each generated input file.

### Content

Screen title:

```text
Select TOPAS components
```

Files are listed from:

```text
{AppRoot}/templates
```

Files are grouped by folder.

Example:

```text
linac
  [ ] jaws_reference.txt

phantom
  [ ] solid_water_profile_y.txt

physics
  [ ] em_standard_opt4.txt

source
  [ ] iaea_phase_space_source.txt
```

The selected file order must be deterministic.

### Actions

```text
Cancel
Previous
Next
```

`Next` is enabled only when at least one component is selected.

---

## Step 3 — Select Nodes

### Purpose

Choose which configured nodes should be included in this generated batch.

### Content

Screen title:

```text
Select nodes
```

The UI lists configured nodes as numbered checkboxes.

Example:

```text
[ ] 1 node01
[ ] 2 node02
[ ] 3 node03
[ ] 4 node04
[ ] 5 node05
[ ] 6 node06
[ ] 7 node07
```

The node digit is the value used in seed construction.

### Bulk Actions

```text
Select all
Select none
```

### Actions

```text
Cancel
Previous
Next
```

`Next` is enabled only when at least one node is selected.

---

## Step 4 — Select Phase-Space Files

### Purpose

Choose which configured phase-space files should be used.

### Content

Screen title:

```text
Select phase-space files
```

The UI lists phase-space files as compact numbered checkboxes.

Example:

```text
[ ] ps01
[ ] ps02
[ ] ps03
...
[ ] ps22
```

The screen should stay compact. The numbered phase-space identity is the main UI label.

### Bulk Actions

```text
Select all
Select none
```

### Actions

```text
Cancel
Previous
Next
```

`Next` is enabled only when at least one phase-space file is selected.

---

## Step 5 — Review

### Purpose

Show what will be generated before writing files.

### Content

Screen title:

```text
Generate Wizard: Review
```

The selected values are shown in readable grouped form.

Example layout:

```text
Selected components:
- linac/jaws_reference.txt
- phantom/solid_water_profile_y.txt
- physics/em_standard_opt4.txt

Selected nodes:
- n1, n3, n5

Selected phase-space files:
- ps02, ps04, ps06
```

The review also shows preview metadata when available:

```text
Expected generated count: 9
Preview file: input_sd10012_ps02_n2.txt
RunId: phsp02_seed10012
Seed: 10012
Phase-space: ps02
Node: 2
```

The preview shows one stitched generated input file.

Preview rule:

```text
use the first selected node
use the first selected phase-space file
use the current runtime seed base
```

The preview text is the final stitched text after placeholder replacement.

The user can verify:

```text
component stitching order
PhaseSpaceFile placeholder replacement
OutputFile placeholder replacement
Seed placeholder replacement
```

### Actions

```text
Cancel
Previous
Generate
```

`Generate` is enabled only after a preview has loaded.

---

## Step 6 — Result

### Purpose

Show the result of the generation operation.

### Success Content

On success, show structured summary:

```text
Seed base: 1002
Generated files: 154
Input folder: C:\Dev\tsebt-local-root\inputs\1002
```

Then show generated run details.

Each generated run line should include at least:

```text
RunId
Input file path
Seed
```

Useful expanded display:

```text
phsp01_seed10021 | input_sd10021_ps01_n1.txt | seed 10021
phsp01_seed10022 | input_sd10022_ps01_n2.txt | seed 10022
```

After successful generation, the client refreshes app configuration so returning to Generate immediately shows the next seed base without requiring browser reload.

### Error Content

On error, show:

```text
Generation failed.

{error message}
```

The error message comes from the server.

### Actions

```text
Back to Generate
```

`Back to Generate` returns to the Welcome step.

---

## Client State

The Generate client model holds:

```text
current wizard step
runtime app configuration
available component files
selected component files
selected nodes
selected phase-space files
preview result
generation result
current error
loading state
```

The client does not read or write local files.

---

## Server API Used by Wizard

The wizard uses these server API operations:

```text
getAppConfig
getTemplateFiles
previewGenerate
generate
```

### getAppConfig

Returns:

```text
runtime next seed base
configured nodes
configured phase-space files
path/configuration values needed for display
```

The seed base returned by this API is SQLite-backed.

### getTemplateFiles

Returns files under:

```text
{AppRoot}/templates
```

Each file has:

```text
group/folder
file name
relative path
```

### previewGenerate

Input:

```text
selected component relative paths
selected node digits
selected phase-space indexes
```

Returns:

```text
expected generated count
preview run id
preview seed
preview input filename
preview output file path
stitched preview text
```

The preview does not write files and does not insert SQLite records.

### generate

Input:

```text
selected component relative paths
selected node digits
selected phase-space indexes
```

Returns:

```text
seed base used
generated input count
input folder
generated run records
```

The operation writes files and inserts SQLite metadata.

---

## RunId, Filename, and Seed Rules

RunId:

```text
phsp{phaseSpaceIndex}_seed{seed}
```

Example:

```text
phsp01_seed10011
phsp22_seed10017
```

Generated input filename:

```text
input_sd{seed}_ps{phaseSpaceIndex}_n{nodeDigit}.txt
```

Example:

```text
input_sd10011_ps01_n1.txt
input_sd10017_ps22_n7.txt
```

Seed:

```text
seed = seedBase with node digit appended
```

Example:

```text
seedBase = 1001
node 1 -> 10011
node 7 -> 10017
```

The same seed pattern is reused across phase-space files.

---

## Final UX Behaviour

The Generate page opens as a wizard.

The user starts with a welcome screen showing the current runtime seed base.

The user selects TOPAS components.

The user selects nodes.

The user selects phase-space files.

The review screen shows selected values and one stitched preview.

The user clicks Generate.

The result screen shows generated file details.

After generation, the next seed base is refreshed from SQLite and visible immediately when returning to Generate.
