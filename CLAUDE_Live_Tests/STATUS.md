# Status Log

## Pass 1 — 2026-05-17 — Reorientation And Skill-Pack Review

### Branch State

- Started on `VVG_LIVE_ANALYSIS`, behind the new universe on `origin/main`.
- Merged `origin/main` into `VVG_LIVE_ANALYSIS`. Clean merge, no conflicts.
- Local `.mcp.json` uncommitted change switched to PowerShell launcher scripts. Same change was already on `origin/main`; the stash was dropped as redundant.
- These notes ship on a separate branch (`claude/live-test-notes-20260517`) cut from `origin/main` so the PR is just the notes, not the VVG-branch merge churn.

### What Was Done

- Read top-level project rules: `CLAUDE.md`, `MCP_CLIENT_TESTING.md`.
- Read the minimal review pack: `Docs/ClaudeMinimalReviewPack/README.md`, manifest, and all ten skill cards.
- Read the active mirror at `Docs/Skills/` to confirm content parity with the pack.
- Inspected `MonitorBaseClaude.McpServer/Program.cs` to compare implemented tools against the currently advertised tool list.
- Compared advertised tool list against manifest claims.

### Confirmations Requested

- Roslyn/compiler-first over grep for C# semantic work: present and clear in `RoslynFirstNavigation.md`, `MonitorBaseClaudeSkillPack.md` Core Rule, and `CLAUDE.md` line 110.
- Coupled files must be staged together under one session before first review: present and clear in `SessionOverlayValidation.md`, `SystemMonitorStaging.md` line 13, and manifest line 500.
- `get_smoke_test_catalog` is debug/maintainer-only: present and clear in the pack README, `SystemMonitorStaging.md` line 50, and manifest lines 539 to 541.

### Skill Card Loading Reality

- These `.md` cards are workflow context documents, not Claude Code first-class skills. No JSON registration is required.
- The repository design routes Claude through `get_staging_guide` (MCP tool) for on-demand card delivery. The C# implementation exists at `MonitorBaseClaude.McpServer/Program.cs` line 433.
- The currently running MCP server binary predates that tool. Today `tools/list` does not expose `get_staging_guide`, `get_smoke_test_catalog`, `add_field`, `add_property`, `add_method`, `add_constructor`, `add_nested_type`, or `set_type_partial`.
- Until the server is rebuilt and Claude Code reconnects to it, there is no live server-side card-serving path. Claude must read cards directly from `Docs/Skills/` (canonical) or `Docs/ClaudeMinimalReviewPack/Skills/` (export snapshot).
- A short interim note was added to `CLAUDE.md` reflecting this until the server is rebuilt and the tool surface catches up.

### Build

- Skipped. C: drive had roughly 2.6 GB free. A .NET 10 plus Roslyn build needs more headroom than that comfortably.
- Recommended: free space or redirect build output, then `dotnet build MonitorBaseClaude.slnx`.

### MCP Tool Discovery — Cheap Pass

- `tools/list`: deferred-tool list contains the expected core surface. Missing: `get_staging_guide`, `get_smoke_test_catalog`, typed insertion tools, `set_type_partial` (see findings).
- `get_monitor_status`, `get_tool_manifest`, `get_workflow_status`: visible in the deferred list, callable after schema load.
- `get_staging_guide`: not visible (binary staleness).

### Open Items

- Operator: free disk space and rebuild `MonitorBaseClaude.McpServer` so the new tools surface.
- Operator or Codex: decide canonical location for skill cards. The active mirror at `Docs/Skills/` versus the pack at `Docs/ClaudeMinimalReviewPack/Skills/` will drift over time unless one is generated from the other.
- Codex: triage the five findings in `FINDINGS.md` and merge accepted items into active docs.

### Next Claude Pass — When Server Is Rebuilt

- Re-run `tools/list`, then call `get_monitor_status`, `get_tool_manifest`, `get_staging_guide`, `get_workflow_status` and confirm each returns the documented payload shape.
- Walk the source-map narrowing flow once on a small file in DBV2 (read-only, no staging) and confirm `suggestedNextCalls` ranks correctly.
- File any new findings as Pass 2.
