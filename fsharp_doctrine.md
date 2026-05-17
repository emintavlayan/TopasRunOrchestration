1. Shape of this SAFE project
Shared
  Domain types
  API contracts
  DTOs shared by Client and Server

Server
  File IO
  Template loading
  Text replacement
  Copy/generate operations
  API implementation

Client
  Elmish Model / Msg / update / view
  UI state
  Calls server API

No heavy architecture.
No enterprise DDD.
No repository/factory/aggregate ceremony.

2. Core rules
Raw outside, typed inside.
Effects at edges.
Small named functions.
Result for fallible work.
Pure functions by default.
Return data, not prose.
Format messages at UI/log boundary.

3. Error style

For this project, use:

Result<'T, string>

Reason: this is an orchestration tool, mostly reading files, editing text, copying files, generating run folders, and reporting what happened.

Use typed errors only when branching really matters.

4. Function style

Every function should be small and intent-named.

/// Reads template text from disk
let readTemplate path = ...

/// Replaces token in template text
let replaceToken token value text = ...

/// Builds generated TOPAS input path
let buildInputPath runId root = ...

Avoid:

let processStuff x = ...
let handle y = ...
let run2 z = ...
5. Module style

Use modules by responsibility, not by ceremony.
Only add files when the current one becomes unclear.

6. ROP rule

Use FsToolkit.ErrorHandling for sequential fallible work.

open FsToolkit.ErrorHandling

/// Generates TOPAS input file from selected template
let generateInput deps request =
    result {
        let! templateText = deps.ReadFile request.TemplatePath
        let renderedText = Template.render request.Parameters templateText
        let outputPath = RunPath.inputFile request.RunId deps.Root
        do! deps.WriteFile outputPath renderedText
        return { RunId = request.RunId; InputPath = outputPath }
    }

Use result {} when failure should stop the operation.
Use per-item Result when partial success is allowed.

7. Pure vs effectful split

Keep these pure:

Path construction
Template token replacement
Validation
RunId formatting
Config mapping
Summary calculation

Keep these effectful and isolated:

File read/write
Directory creation
Copying files
Process execution
SQLite access
Server API handler

8. Shared domain rule

Shared.fs should contain types that both sides need.

Example:

type RunId = RunId of string

type GenerateRunRequest =
    { RunId: RunId
      TemplateName: string
      OutputFolder: string }

type GeneratedRun =
    { RunId: RunId
      InputFile: string
      OutputFolder: string }

type ITopasApi =
    { generateRun: GenerateRunRequest -> Async<Result<GeneratedRun, string>>
      getRuns: unit -> Async<GeneratedRun list> }

Keep shared types boring and serializable.

9. Server rule

Server owns real work.

Client asks.
Server validates.
Server reads/writes/copies/generates.
Server returns Result data.

Do not put file-generation logic in Elmish client code.

10. Client rule

Elmish client owns state and UI only.

Model = current UI state
Msg = possible UI events
update = state transition + commands
view = HTML

Client should not know low-level file mechanics.

11. Boundary message rule

Domain/server returns structured data:

type GeneratedRun =
    { RunId: RunId
      InputFile: string
      OutputFolder: string }

UI turns it into prose:

$"Generated run {runId} at {path}"

Not the other way around.