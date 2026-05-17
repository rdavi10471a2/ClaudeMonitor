# Safety Gate Patch README

Generated: 2026-05-16

This update closes the two safety gaps found in the post-checkpoint review.

## What Changed

- `dirty-unexpected` now blocks later staging for the same source file.
- `refresh_file` is the v1 explicit recovery path. It refreshes the monitor Working copy and marks blocked staged records as `recovered-by-refresh`.
- Re-voting a `blocked-dirty-unexpected` staged record is refused so the dirty block cannot be downgraded to accepted/rejected.
- `compare_file` may refresh a missing Working copy for normal compare flow, but that implicit refresh does not recover a dirty block.
- C# parse/syntax errors now fail before a staged record is written.
- Overlay compile diagnostics remain advisory validation metadata on staged candidates because project references and generated state can produce false positives.

## What Was Verified

Commands run locally:

```powershell
dotnet build .\MonitorBaseClaude.slnx
dotnet run --project .\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --fixture-decision-gate-smoke
dotnet run --project .\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --fixture-roslyn-surgery-smoke
```

Results:

- Build: passed with 0 warnings and 0 errors.
- Decision-gate smoke: passed.
- Roslyn surgery smoke: passed.

New decision-gate coverage:

- malformed C# candidate is rejected before staging
- `accepted` vote without saved candidate classifies `dirty-unexpected`
- `dirty-unexpected` blocks the next staged edit
- `refresh_file` recovers the block
- re-voting the blocked staged record is refused
- implicit compare refresh does not recover the block
- rejected-after-save dirty state can be recovered by `refresh_file`
- unrelated external dirty edit blocks later staging

## Review Notes

This patch does not change the vote-plus-hash accept/reject classifier.

This patch does not make overlay compile errors a hard rejection gate. That remains intentionally softer than syntax parsing because compile errors can be noisy when references, generated code, or fixture state differ from the real project.

This patch does not add `symbolsChanged` metadata for `submit_symbol`; that remains a future telemetry improvement, not a current safety blocker.
