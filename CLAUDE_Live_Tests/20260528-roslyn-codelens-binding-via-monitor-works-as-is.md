---
status: new
type: finding
created: 2026-05-28
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---

## Summary

When a Claude Code session does not bind `mcp__roslyn-codelens__*` tools directly, the MonitorBaseClaude WinForms hub still routes a working roslyn-codelens instance to the Monitor MCP server as its sole consumer, and Monitor exposes Roslyn-derived answers through its own surface (`get_source_map`, `find_indexed_symbols`, `find_indexed_references`, `find_indexed_callers`, `find_indexed_relationships`). For the documented workflow this is sufficient: Claude Code reaches Roslyn semantic data via Monitor without needing the roslyn-codelens namespace attached to the session. Filing this as "works as is" so it does not get re-investigated as a defect later.

## Repro

1. Open a fresh Claude Code session against this repo.
2. At session start, observe the deferred-tools system reminder: `mcp__monitor-base-claude__*` lands; `mcp__roslyn-codelens__*` is shown as "still connecting" but never resolves in the session.
3. Open the MonitorBaseClaude MCP Client dashboard `Roslyn Tooling` tab.
4. Restart roslyn-codelens (or trigger any event that fires `Tools/Start-RoslynCodeLensMcp.ps1`).
5. Watch the dashboard: a new roslyn-codelens process spawns; the WinForms hub sees `initialize`, `logging/setLevel`, `notifications/initialized`, `tools/list`, and the 33,131-byte tools/list response. Every one of those request rows is attributed to the Monitor McpServer PID, not to Claude Code's bridge.
6. From the same Claude Code session call `ToolSearch select:mcp__roslyn-codelens__search_symbols,mcp__roslyn-codelens__list_solutions,mcp__roslyn-codelens__get_type_overview` → `No matching deferred tools found`.
7. From the same session call `mcp__monitor-base-claude__find_indexed_symbols` on any known C# symbol → returns Roslyn-derived results with stable keys, file paths, line spans, signatures.

## Expected

Either:

- Claude Code's `roslyn-codelens` entry from `.mcp.json` binds and surfaces 33KB of tools in the session, OR
- The roslyn-codelens namespace stays unbound but Roslyn-derived answers remain reachable through Monitor's own tools.

## Actual

The second case is what happens, consistently, and it is workable.

- `.mcp.json` declares both `monitor-base-claude` and `roslyn-codelens` launchers. VS Code's MCP UI lists both as attachable because it reads the declaration file, but "attachable" is not "bound".
- The `roslyn-codelens` bridge launcher (`Tools/Start-RoslynCodeLensMcp.ps1` → `McpHubBridge --server roslyn`) connects roslyn-codelens stdio to the named pipe `MonitorBaseClaude.McpProxyHub` ([Tools/McpHubBridge/Program.cs:9](Tools/McpHubBridge/Program.cs#L9)).
- The WinForms hub (`McpProxyHubService` at [McpProxyHubService.cs:16](McpProxyHubService.cs#L16)) routes the roslyn surface to Monitor (PID 43868 in this session) as its consumer. The dashboard "Source" column shows `live` rows whose Method/Tool sequences are addressed by Monitor, not by Claude Code.
- The Monitor McpServer references `Microsoft.CodeAnalysis.CSharp 4.14.0` + `Microsoft.CodeAnalysis.CSharp.Workspaces 4.14.0` ([MonitorBaseClaude.McpServer/MonitorBaseClaude.McpServer.csproj:11-14](MonitorBaseClaude.McpServer/MonitorBaseClaude.McpServer.csproj#L11-L14)) and uses `CSharpSyntaxTree.ParseText` + `compilation.GetSemanticModel(...)` directly ([Services/SolutionIndexService.cs](Services/SolutionIndexService.cs)). So Monitor's indexed-symbol surface is itself a Roslyn answer, not a text approximation.
- The Monitor tool descriptions are honest about this: `find_indexed_references` and `find_indexed_callers` explicitly say "uses the monitor-owned SQLite index, **not the live Roslyn MCP reference tool**". The two paths coexist by design.

## Evidence

- Live dashboard sequence (Roslyn Tooling tab) for the most recent restart:
  - `12:54:23.046` `server/start roslyn-codelens` PID 2412 (new roslyn-codelens process)
  - `12:54:23.047` `initialize` from Monitor PID 43868
  - `12:54:27.614` `logging/setLevel`
  - `12:54:27.615` `notifications/initialized`
  - `12:54:27.616` `tools/list` from Monitor PID 43868
  - `12:54:27.642` `tools/list` response, 33,131 bytes
- Earlier in the same session: `12:21:09.346`, `12:29:14.852`, `12:29:18.608`, `12:47:52.622` all show identical handshake shapes against the same Monitor consumer PID.
- Session-start deferred-tools reminder: monitor-base-claude bound, roslyn-codelens "still connecting" and never resolves for this session.
- `ToolSearch select:mcp__roslyn-codelens__*` returns "No matching deferred tools found" before AND after each restart.
- `mcp__monitor-base-claude__find_indexed_symbols { text: "OpenColumnMergeReviewAsync", kind: "method" }` returned exactly one row with full stable-key + Roslyn-derived metadata, confirming Monitor's Roslyn-backed surface is fully usable from Claude Code.

## Notes

Operator framing (2026-05-28): this is an example of architecture growing past one of its own moving parts. With Monitor embedding `Microsoft.CodeAnalysis.CSharp.Workspaces` AND owning a live MCP-client connection to roslyn-codelens internally, the `roslyn-codelens` entry in [.mcp.json](.mcp.json) (lines 14-24) is no longer load-bearing for Claude Code's tool surface. Its only observable effect on Claude Code sessions is the "still connecting" deferred-tools reminder at session start that never resolves. Removing that entry would make Monitor the single Claude-Code-facing MCP launcher and eliminate the reminder-driven re-investigation.

Observation-only; not proposing the deletion itself or how it would interact with other MCP clients (Claude Desktop currently launches roslyn-codelens via the same `.mcp.json`-shaped path per [20260527-finding-62-roslyn-codelens-divergent-active-solution-across-clients.md](20260527-finding-62-roslyn-codelens-divergent-active-solution-across-clients.md)). Codex/operator can decide whether to scope the entry to Claude Code only, remove it entirely, or leave it pending a real consumer.

This finding is filed because the dashboard behavior would otherwise be tempting to re-investigate as a Claude Code MCP defect every time the deferred-tools reminder shows roslyn-codelens "still connecting". The reminder is accurate but misleading: in this architecture, "not bound to Claude Code" does not mean "not reachable".

Related: [20260527-finding-62-roslyn-codelens-divergent-active-solution-across-clients.md](20260527-finding-62-roslyn-codelens-divergent-active-solution-across-clients.md) — the cross-client divergence problem there is unaffected by the conclusion here; Monitor's embedded Roslyn surface and the live roslyn-codelens active-solution state are independent.
