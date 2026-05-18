# Generate Wizard UX Specification

## Purpose

The Generate page uses a step-by-step wizard.

The goal is to keep the user focused and prevent one large, confusing form.

Generate remains a planning and file-generation workflow:

```text
selected TOPAS components
+ selected nodes
+ selected phase-space files
+ configured seed base
= generated TOPAS input files
```

The UI should guide the user through this sequence with `Next`, `Previous`, `Cancel`, and final `Generate` actions.

---

## Overall UX Pattern

Generate is opened from the main app navigation.

When the user enters Generate, the app shows the first wizard screen.

The wizard steps are:

```text
1. Welcome / explanation
2. Select TOPAS components
3. Select nodes
4. Select phase-space files
5. Review generated plan
6. Generation result
```

Navigation style:

```text
Cancel    Previous    Next
```

or on the final review step:

```text
Cancel    Previous    Generate
```

Button layout:

```text
Cancel: text-style button, left of the main action area
Previous: normal secondary button when available
Next / Generate / Start: primary button at bottom-right
```

The first screen has only:

```text
Cancel    Start
```

`Start` is the primary button at bottom-right.

---

## Step 1 — Welcome

### Purpose

Explain what Generate will do before the user starts selecting files.

### Content

The screen should show:

```text
Generate creates TOPAS input files for one simulation batch.

It will use:
- the configured seed base
- selected TOPAS components
- selected compute nodes
- selected phase-space files

For each selected node and phase-space file, one TOPAS input file will be generated.
```

It should also show current configuration values:

```text
Current seed base: 1001
Configured nodes: 7
Configured phase-space files: 22
Expected full batch size: 154 input files
Input folder: {AppRoot}/inputs/{batch-folder}
```

### Actions

```text
Cancel
Start
```

`Start` moves to Step 2.

---

## Step 2 — Select TOPAS Components

### Purpose

Let the user choose which TOPAS template/component files should be stitched into each generated input file.

### Content

Screen title:

```text
Select TOPAS components for this run
```

The app lists files from:

```text
{AppRoot}/templates
```

Files are grouped by folder name.

Example UI structure:

```text
physics
  [ ] em_standard_opt4.txt

phantom
  [ ] solid_water_profile_y.txt

linac
  [ ] jaws_reference.txt

source
  [ ] iaea_phase_space_source.txt
```

The order of selected files should be deterministic.

Recommended order:

```text
folder order from configuration if defined
otherwise alphabetical folder order
then alphabetical file order
```

### Actions

```text
Cancel
Previous
Next
```

`Next` is enabled only when at least one component file is selected.

---

## Step 3 — Select Nodes

### Purpose

Choose which configured nodes should participate in this generated batch.

### Content

Screen title:

```text
Choose which nodes to run this simulation
```

The UI lists nodes as numbered checkboxes.

Example:

```text
[ ] 1
[ ] 2
[ ] 3
[ ] 4
[ ] 5
[ ] 6
[ ] 7
```

Each checkbox corresponds to a configured node digit.

The app may show the node name next to the number if configured:

```text
[ ] 1  node01
[ ] 2  node02
[ ] 3  node03
```

### Bulk actions

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
Choose phase-space files to use
```

The UI lists phase-space files as numbered checkboxes.

Example:

```text
[ ] ps01
[ ] ps02
[ ] ps03
...
[ ] ps22
```

The UI should keep this screen compact.

The numbered identity is more important than showing long file paths.

Optional secondary text may show the file stem if space allows.

### Bulk actions

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

Show exactly what will be generated before writing files.

### Content

Screen title:

```text
Review generated TOPAS input plan
```

The review should show:

```text
Seed base: 1001
Selected nodes: 1, 2, 3, 4, 5, 6, 7
Selected phase-space files: ps01 ... ps22
Selected component count: N
Generated input count: selected nodes × selected phase-space files
Input folder: {AppRoot}/inputs/{seed-base-or-batch-folder}
```

The review should show one example stitched input file preview.

The preview should be generated using the first selected node and first selected phase-space file.

Example preview identity:

```text
Preview file: input_sd10011_ps01_n1.txt
RunId: phsp01_seed10011
Seed: 10011
Phase-space: ps01
Node: 1
```

The preview should show the final stitched text after placeholder replacement.

This gives the user a chance to verify:

```text
__PHSP_FILE__ replacement
__OUTPUT_FILE__ replacement
__SEED__ replacement
component stitching order
```

### Actions

```text
Cancel
Previous
Generate
```

`Generate` writes all generated input files and SQLite metadata.

---

## Step 6 — Result

### Purpose

Show the result of the generation operation.

### Success content

On success, show structured summary:

```text
Generation completed.

Seed base: 1001
Generated files: 154
Selected nodes: 7
Selected phase-space files: 22
Input folder: {AppRoot}/inputs/{seed-base-or-batch-folder}
```

Then show the generated files list.

The file list should include at least:

```text
RunId
Input file path
Seed
Node digit
Phase-space index
```

Example:

```text
phsp01_seed10011    input_sd10011_ps01_n1.txt    seed 10011    node 1    ps01
phsp01_seed10012    input_sd10012_ps01_n2.txt    seed 10012    node 2    ps01
```

### Error content

On error, show:

```text
Generation failed.

Error:
{error message}
```

If partial files were created before failure, the result should show whatever the server returns as structured partial result.

For the first implementation, generation may stop on first error and return one error string.

### Actions

```text
Back to Generate
```

Optional later action:

```text
Go to Run
```

---

## State Model Needed by Client

The Generate client model should hold:

```text
current wizard step
available component files
selected component files
available nodes
selected nodes
available phase-space files
selected phase-space files
preview result
generation result
current error
loading state
```

The client does not stitch files itself.

The client asks the server for:

```text
available generate configuration
stitched preview
execute generate
```

---

## Server Operations Needed

Generate requires three server API operations:

```text
getGenerateConfiguration
previewGenerate
generateInputs
```

### getGenerateConfiguration

Returns:

```text
seed base
component files grouped by folder
configured nodes
configured phase-space files
placeholder names
expected full batch count
```

### previewGenerate

Input:

```text
selected component files
selected first node or chosen preview node
selected first phase-space file or chosen preview phase-space file
```

Returns:

```text
run id
seed
input file name
output file path
stitched input text preview
```

### generateInputs

Input:

```text
selected component files
selected nodes
selected phase-space files
```

Returns:

```text
generation summary
generated file records
```

---

## RunId and Filename Rules Used by Wizard

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

## Final UX Behavior

The Generate page is a wizard.

The user starts with an explanation screen.

The user selects TOPAS components.

The user selects nodes by numbered checkboxes.

The user selects phase-space files by numbered checkboxes.

The user reviews the generated plan and one stitched input preview.

The user clicks Generate.

The app shows success or failure with generated file details.
