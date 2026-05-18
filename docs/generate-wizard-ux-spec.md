# Generate Wizard UX Specification

## Purpose

The Generate page is a step-by-step wizard that guides users through safe TOPAS input generation.

## Wizard steps

```text
1. Welcome
2. Select TOPAS components
3. Select nodes
4. Select phase-space files
5. Review
6. Result
```

## Navigation behavior

- Left action: `Cancel`
- Secondary action: `Previous` (when applicable)
- Primary action: `Start`, `Next`, `Generate`, or `Back to Generate`

## Step details

### 1. Welcome

Shows:

- purpose text
- current runtime seed base
- configured node count
- configured phase-space file count

### 2. Select TOPAS components

Shows grouped template files from `{AppRoot}/templates`.

Rule:

- `Next` enabled only when at least one component is selected.

### 3. Select nodes

Shows configured nodes with bulk actions:

- `Select all`
- `Select none`

Rule:

- `Next` enabled only when at least one node is selected.

### 4. Select phase-space files

Shows configured phase-space indexes (for example `ps01`, `ps02`) with bulk actions:

- `Select all`
- `Select none`

Rule:

- `Next` enabled only when at least one phase-space file is selected.

### 5. Review

Shows:

- selected components
- selected nodes
- selected phase-space files
- expected generated count
- stitched preview text with placeholders already replaced

Primary action:

- `Generate`

Rule:

- `Generate` enabled only when preview is loaded.

### 6. Result

On success, shows:

- used seed base
- generated file count
- input folder path
- generated run summary lines

On error, shows the returned server error string.

Primary action:

- `Back to Generate`

## Server API used by wizard

- `getAppConfig`
- `getTemplateFiles`
- `previewGenerate`
- `generate`

## UX safety behavior

Generate errors are surfaced in the existing wizard error area.

Important server-side safety now reflected in UX:

- if collisions are detected during Generate preflight, operation fails with clear error and no partial output.
- after successful Generate, UI refreshes config so next seed base is immediately visible.

