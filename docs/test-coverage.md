# Server Test Coverage Checklist

This checklist tracks practical behavioral coverage for implemented server workflows using the xUnit suite in `tests/Server.XUnit`.

| Workflow area | Status | Test location |
|---|---|---|
| Config validation | Covered | `tests/Server.XUnit/Server.XUnit.Tests.fs` |
| Bootstrap folders | Covered | `tests/Server.XUnit/Server.XUnit.Tests.fs` |
| Generate planning | Covered | `tests/Server.XUnit/Server.XUnit.Tests.fs` |
| Generate collision checks | Covered | `tests/Server.XUnit/Server.XUnit.Tests.fs` |
| Generate filesystem operation | Covered | `tests/Server.XUnit/Server.XUnit.Tests.fs` |
| Run manifest and script planning | Covered | `tests/Server.XUnit/Server.XUnit.Tests.fs` |
| Run submission guards | Covered | `tests/Server.XUnit/Server.XUnit.Tests.fs` |
| Collect preflight | Covered | `tests/Server.XUnit/Server.XUnit.Tests.fs` |
| Collect CSV merge | Covered | `tests/Server.XUnit/Server.XUnit.Tests.fs` |
| Collect statistics | Covered | `tests/Server.XUnit/Server.XUnit.Tests.fs` |
| Collect operation | Covered | `tests/Server.XUnit/Server.XUnit.Tests.fs` |

Notes:
- This is workflow-level behavioral coverage, not line/branch coverage.
- Tests use temporary folders, fake CSV data, and fake sbatch output parsing paths.
- Real Slurm execution and real TOPAS execution remain manual smoke-test responsibilities.
