# Review Request

Generated: 2026-05-16

This branch exists to give GitHub auto-review a normal PR conversation surface.

## Review Focus

Please review the current MonitorBaseClaude safety-gate checkpoint with special attention to:

- `dirty-unexpected` blocking before later staging
- `refresh_file` recovery behavior
- C# parse/syntax rejection before a staged record is written
- preservation of advisory overlay compile diagnostics
- fixture smoke coverage for decision-gate and Roslyn surgery flows

## Relevant Files

- `MonitorBaseClaude.McpServer/MonitorWorkflowService.cs`
- `MonitorBaseClaude.ToolSmokeTests/Program.cs`
- `MonitorBaseClaude.McpServer/MONITOR_MCP_TOOL_MANIFEST.md`
- `Docs/SafetyGatePatchREADME.md`
- `Docs/SmokeTestSafetyGateArtifacts/README.md`

## Local Verification

```powershell
dotnet build .\MonitorBaseClaude.slnx
dotnet run --project .\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --fixture-decision-gate-smoke
dotnet run --project .\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --fixture-roslyn-surgery-smoke
```

All three commands passed before this branch was created.
