# Smoke Test Coverage Review Artifacts

Generated: 2026-05-16

This folder is a compact evidence set for reviewing MonitorBaseClaude smoke-test coverage. It focuses on deterministic tool/workflow coverage, not local-model quality. Ollama route drills are useful behavior probes, but they are intentionally excluded from this coverage package because they do not prove Monitor correctness.

## Fresh Verification

These commands were run on 2026-05-16 and passed:

```powershell
dotnet build .\MonitorBaseClaude.slnx

dotnet run --project .\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --fixture-decision-gate-smoke

dotnet run --project .\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --fixture-roslyn-surgery-smoke

dotnet run --project .\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --fixture-razor-smoke

dotnet run --project .\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --source-map-smoke EditorSurface --scope folder --mode navigation
```

## Coverage Matrix

| Area | Evidence | What It Proves |
| --- | --- | --- |
| Build health | `dotnet build .\MonitorBaseClaude.slnx` | UI, MCP server, and smoke runner compile together. |
| Monitor root/config binding | fixture status steps | Smoke runner can start the real Monitor MCP server against disposable watched-project configs. |
| No-op candidate handling | `fixture-decision-gate-summary-20260516.md` | Identical staged candidate returns `no-op-staged` instead of enqueueing a normal diff. |
| Vote-plus-hash accept | decision gate + Roslyn surgery + Razor summaries | `accepted` requires Operator reported accepted and watched hash equals staged hash. |
| Vote-plus-hash reject | decision gate summary | `rejected` requires Operator reported rejected and watched hash equals original hash. |
| Dirty mismatch blocking | decision gate summary | Accept-not-applied, reject-after-save, and unrelated dirty edits classify `dirty-unexpected` and block further edits. |
| C# source-map discovery | Roslyn surgery + EditorSurface summaries | `get_source_map` returns parse status, diagnostics count, symbol metadata, mode/purpose, token proxy, and next-call affordances. |
| Stable-key symbol read | Roslyn surgery summary | `get_symbol` can read a method body selected from source-map stable selector data. |
| Staged C# symbol surgery | Roslyn surgery summary | `add_using`, `submit_symbol`, `add_symbol`, `remove_symbol`, and `remove_using` stage candidates and validate overlays without directly mutating watched source. |
| Overlay validation | decision gate + Roslyn surgery summaries | Staged C# candidates are syntax checked and overlay-compiled against the fixture project. |
| Razor safe lane | Razor summary | `.razor` uses find/read/full-file staging, reports `razor-validation-pending`, and still relies on vote-plus-hash acceptance. |
| Real DBV2 read-only source-map lane | EditorSurface source-map summary | Real watched DBV2 folder can be mapped in navigation mode without mutation. |
| Source-map hierarchy | EditorSurface source-map summary | Folder navigation mode returns ranked `get_source_map(... mode: selector)` next calls and stays under budget. |

## Included Evidence Files

- `fixture-decision-gate-summary-20260516.md`
- `fixture-roslyn-surgery-summary-20260516.md`
- `fixture-razor-summary-20260516.md`
- `dbv2-editor-surface-navigation-source-map-summary-20260516.md`

## Related Contract Files

The review package also includes:

- `AGENTS.md`
- `CLAUDE.md`
- `MCP_CLIENT_TESTING.md`
- `Docs/OperatorWorkflowRules.md`
- `Docs/ExistingCodeEditCoveragePlan.md`
- `Docs/ArchitectureAndDataFlow.md`
- `MonitorBaseClaude.McpServer/MONITOR_MCP_TOOL_MANIFEST.md`
- `MonitorBaseClaude.ToolSmokeTests/Program.cs`

## Known Coverage Gaps

These are not proven by the current deterministic smoke package:

- Real Claude Desktop or Claude Code runtime behavior. The package proves the tools and harness, not Claude's live judgment.
- Real DBV2 mutation. DBV2 source-map smoke is read-only; mutation-path smokes use disposable fixtures.
- WinMerge GUI lifetime from a real Host/operator session. The smokes simulate full-candidate save/leave-unchanged states and verify hashes.
- Multi-file all-or-none batch behavior. Current deterministic smokes are single-file.
- Class/file lifecycle tools such as add/remove class or new-file structural candidates, if those are later exposed.
- Razor-aware semantic outline/build validation. Current Razor behavior is intentionally safe-mode full-file staging with `razor-validation-pending`.
- Hard token-budget truncation, if configured as a future enforcement feature rather than advisory metadata.
- Local Ollama model quality. Ollama route tests probe model behavior only and should not be counted as Monitor correctness coverage.

## Review Questions

1. Does the decision-gate smoke cover the complete vote-plus-hash truth table needed for v1?
2. Does the Roslyn surgery smoke cover enough staged C# edit primitives for the next Claude runtime trial?
3. Is Razor safe-mode coverage sufficient until Razor-aware semantic validation exists?
4. Is DBV2 read-only source-map coverage enough to trust navigation/selector discovery before real mutation testing?
5. Which coverage gap should block first real Claude staging, if any?
