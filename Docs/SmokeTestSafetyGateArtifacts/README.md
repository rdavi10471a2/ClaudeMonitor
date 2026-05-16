# Smoke Test Safety Gate Artifacts

Generated: 2026-05-16

These artifacts are an addendum to the smoke coverage review. They document the local safety-gate patch that closed the two review findings:

- malformed C# should not produce a staged record
- `dirty-unexpected` must block further staging until explicit recovery

## Included Files

- `fixture-decision-gate-safety-update-20260516.md`
  - proves syntax-error rejection
  - proves `dirty-unexpected` blocks later staging
  - proves `refresh_file` recovers the block
  - proves re-voting a `blocked-dirty-unexpected` staged record is refused
  - proves `compare_file` can refresh a missing Working copy without clearing a dirty block
  - re-runs the vote-plus-hash decision scenarios

- `fixture-roslyn-surgery-safety-update-20260516.md`
  - re-runs `get_source_map` selector mode
  - re-runs `get_symbol`
  - re-runs `add_using`, `submit_symbol`, `add_symbol`, `remove_symbol`, and `remove_using`
  - verifies the stricter staging gate did not break C# symbol surgery

## Local Verification

```powershell
dotnet build .\MonitorBaseClaude.slnx
dotnet run --project .\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --fixture-decision-gate-smoke
dotnet run --project .\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --fixture-roslyn-surgery-smoke
```

All three commands passed.

## Contract Clarification

C# parse/syntax errors are hard failures before staging.

Overlay compile diagnostics remain validation metadata on staged records. They are not a hard rejection gate because project/reference/generator state can produce false positives.
