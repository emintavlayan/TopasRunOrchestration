# Onboarding Guide

## 1. Prerequisites

- .NET 8 SDK
- Node.js (for client development tooling)
- Access to a writable `AppRoot` location

Optional for full Run workflow:

- Slurm (`sbatch`) available on host
- TOPAS executable available on host

## 2. First local setup

1. Open `src/Server/appsettings.json`.
2. Set `Tsebt:AppRoot` to a local writable folder.
3. Verify paths under `Tsebt:Paths`.
4. Verify node names/digits and phase-space entries.
5. For Run on Slurm, verify:
   - `Tsebt:Slurm:Partition`
   - `Tsebt:Slurm:CpusPerTask`
   - `Tsebt:Slurm:Account` (cluster-specific, optional in local dev)
6. Verify `Tsebt:Topas:Executable` points to a valid TOPAS command/wrapper.

## 3. Run the app

```powershell
dotnet run --project .\src\Server
```

Then open the client URL shown by the server.

## 4. Expected workflow

1. Generate: create one batch (`seedBase`) and input files.
2. Run: preflight, preview Slurm script, submit with `sbatch`.
3. Collect: preflight outputs, merge over nodes, merge over phase-space files, compute raw-batch uncertainty.

## 5. Key runtime folders

```text
inputs/{seedBase}   generated TOPAS input files
runs/{seedBase}     manifest/script and TOPAS CSV/log outputs
outputs/{seedBase}  merged-over-nodes + merged-over-phsp + dose_with_uncertainty.csv
```

## 6. Test before changes

```powershell
dotnet test Application.sln --logger "console;verbosity=normal"
```

## 7. Where to read next

- App overview: `docs/app-behaviour-spec.md`
- Unified wizard UX: `docs/wizard-ux-flow-spec.md`
- Generate details: `docs/generate-behaviour-spec.md`
- Run details: `docs/run-behaviour-spec.md`
- Collect details: `docs/collect-behaviour-spec.md`
- Test scope: `docs/test-coverage.md`
