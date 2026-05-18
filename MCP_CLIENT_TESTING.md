# MCP Client Testing

This workspace has a project-scoped Claude Code MCP configuration in `.mcp.json`.

Claude-facing project rules live in `CLAUDE.md`. Codex/build-agent implementation rules live in `AGENTS.md`. Claude Desktop tests should paste or attach `CLAUDE.md` if Desktop does not read the file automatically.

For compact Claude workflow guidance, start with `get_staging_guide` or `Docs\Skills\SkillRouter.md`.

## Server

```text
C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.McpServer\bin\Debug\net10.0\MonitorBaseClaude.McpServer.exe
```

Build it before opening Claude Code:

```powershell
dotnet build C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.McpServer\MonitorBaseClaude.McpServer.csproj
```

For the Claude Code for VS Code bridge path, also build the hub bridge and Roslyn telemetry proxy:

```powershell
dotnet build C:\VSCodeProjects\MonitorBaseClaude\Tools\McpHubBridge\McpHubBridge.csproj
dotnet build C:\VSCodeProjects\MonitorBaseClaude\Tools\CodeLensTelemetryProxy\CodeLensTelemetryProxy.csproj
```

## Pre-flight

Before live MCP testing:

1. Build the solution or at least the MCP server, hub bridge, and Roslyn telemetry proxy.
2. Start `MonitorBaseClaude.exe` so the WinForms Host owns the MCP hub pipe.
3. Fully restart the VS Code window that owns Claude Code. `Developer: Reload Window` may not respawn MCP launchers or refresh tool bindings.
4. Confirm `monitor-base-claude` and `roslyn-codelens` reconnect.
5. Call `get_monitor_status`, `get_tool_manifest`, `get_staging_guide`, and `get_workflow_status`.
6. Confirm Roslyn `list_solutions` and `get_diagnostics` work before staging C# changes.

If the bridge reports that the WinForms hub stream closed or the pipe cannot be reached, restart `MonitorBaseClaude.exe` and then fully restart the VS Code window.

## Claude Code / VS Code Test

1. Open `C:\VSCodeProjects\MonitorBaseClaude` in VS Code.
2. Install/open Claude Code.
3. In the Claude Code panel, run `/mcp`.
4. Approve the project-scoped server if prompted.
5. Confirm `monitor-base-claude` is connected.
6. Ask Claude:

```text
Use the monitor-base-claude MCP server. Call get_monitor_status, then call get_tool_manifest, and summarize what monitor project is being ported.
```

7. Ask a source-map smoke prompt:

```text
Use the monitor-base-claude MCP server. Call get_source_map for Data\BaseTableRepository.cs with scope file and mode selector. Do not read the full file yet. Tell me which symbol you would read next before proposing an edit.
```

8. For related-file context, ask:

```text
Use find_file and get_source_map to inspect the related files for an edit under the watched project. Prefer get_symbol for bodies and use get_file only if the source map is not enough.
```

## Expected Tools

- `get_monitor_status`
- `get_tool_manifest`
- `find_file`
- `get_source_map`
- `get_symbol`
- `list_watched_projects` temporary discovery helper

The real migration rule is that the legacy monitor command/help surface becomes typed MCP tools. The manifest is the bridge document while the tools are ported one by one.

## Roslyn CodeLens Pairing

Use Roslyn CodeLens MCP for semantic questions such as diagnostics, references, callers, implementations, type hierarchy, dependency analysis, and generated code. Use Monitor MCP for staging, hashes, WinMerge review paths, ledgers, and `record_diff_decision`.

Use `Docs/RoslynToolingTeachingSpec.md` as the concrete recipe sheet for Roslyn arguments. The important discipline is schema-first argument acquisition: `search_symbols` uses `query`, while reference/caller/impact tools use `symbol`.

The expected C# context loop is:

```text
find_file -> get_source_map(mode: navigation) -> get_source_map(mode: selector) -> get_symbol -> get_file only when needed
```

Do not add source process markers or glyph/emoji anchors. Routine workflow state belongs in Monitor-owned staged records, sessions, ledgers, or review docs.

## Source-Map Corpus Smoke

Use this when you want DBV2-wide source-map coverage and token-pressure proxy analysis:

The old source-map corpus smoke harness has moved to the ignored `LocalSmokeTests\LegacyToolSmokeTests` workspace. It is local debug tooling, not part of the normal pushed project surface.

The command writes per-file source maps and corpus analysis under `Working\History\ToolSmokeTests\<timestamp>\source-map-corpus`.

Important outputs:

- `maps\...*.source-map.json`: full-fidelity per-file source-map artifacts for review/debug.
- `source-map-compact-index.json`: model-facing selector index with stable keys and hashes.
- `source-map-navigation-index.json`: minimal navigation index for choosing the next `get_symbol` or `get_source_map` call.
- `source-map-corpus-analysis.json` and `source-map-corpus-summary.md`: size ratios, token proxy estimates, symbol totals, diagnostics, and largest-file tables.

Use navigation mode for broad orientation. Use selector mode, the compact selector index, or file-level `get_source_map` only after the agent has chosen a target file or symbol. Use full mode for audit/debug, not broad Claude context.

Live `get_source_map` responses include ranked `suggestedNextCalls`. A good Claude answer should notice them as affordances, then choose the next call that matches the user's intent:

- navigation response -> choose a file and call `get_source_map(..., mode: "selector")`
- selector response -> choose a symbol and call `get_symbol` using `symbolSelectorJson`
- full response -> treat as audit/debug context, not the default broad orientation path

The corpus index files are aggregate smoke artifacts, not exact live `get_source_map` envelopes. Live responses include `scope`, `mode`, `modePurpose`, budget metadata, and ranked `suggestedNextCalls`; corpus indexes are for comparing source-map size and structure across DBV2.

## Claude Workflow Learning Script

Use this as the first real Claude acceptance pass. The goal is to see whether Claude learns the workflow contract before it is allowed to stage real DBV2 edits.

### Pass 1: Discovery Only

Ask Claude:

```text
Use the monitor-base-claude MCP server.
Call get_monitor_status, then call get_tool_manifest.
Do not stage or edit anything.
Summarize the workflow boundary between Model, Monitor Tool Server, Host/WinMerge, and Operator.
```

Expected behavior:

- Calls `get_monitor_status`.
- Calls `get_tool_manifest`.
- Says Monitor stages/verifies/classifies, not direct watched-source mutation.
- Says WinMerge is Host-owned review/save surface.
- Says Operator accepts all or rejects all.
- Says `record_diff_decision` classifies by vote-plus-hash agreement.

### Pass 2: Source-Map First Discipline

Ask Claude:

```text
Use the monitor-base-claude MCP server.
I want to add a null guard to LoadTable in EditorSurface\EditorSurfaceControl.cs.
Do not read the full file.
Show me the exact read/narrow sequence you will use before proposing an edit.
```

Expected behavior:

```text
get_source_map(path: "EditorSurface\EditorSurfaceControl.cs", scope: "file", mode: "selector")
get_symbol(path: "EditorSurface\EditorSurfaceControl.cs", symbolSelectorJson: structured selector or stableSymbolKey for LoadTable)
```

Fail behavior:

- Calls `get_file` first.
- Proposes code before reading the symbol body.
- Uses only `symbolName: "LoadTable"` for a future mutation when a structured selector is available.

### Pass 3: Narrow Body Read

Ask Claude after Pass 2:

```text
Now read only the symbol body you selected.
Do not stage a candidate yet.
Tell me whether you have enough context or whether you need one related symbol/file and why.
```

Expected behavior:

- Calls `get_symbol`.
- Explains whether the method body is enough.
- Requests a related symbol/source map only if the body references unknown helpers or state.
- Does not stage yet.

### Pass 4: Fixture Staging Only

Use a disposable fixture before allowing real DBV2 staging:

Use local-only smoke tests under `LocalSmokeTests` for fixture staging drills. New tests should be one class per test behind a small runner; use separate executables only for process/bridge/proxy lifecycle probes.

Then ask Claude to review the summary and explain:

```text
Which steps prove staged-only symbol surgery?
Which steps prove vote-plus-hash agreement?
Which states block more edits?
```

Expected behavior:

- Identifies `submit_symbol`, `add_symbol`, `remove_symbol`, `add_using`, and `remove_using` as staged-only candidates.
- Identifies accepted/rejected/dirty-unexpected classification.
- Treats dirty-unexpected as blocking, not recoverable by model guesswork.

## Updating The Rules From Observed Claude Behavior

After each Claude acceptance pass, update `CLAUDE.md`, this file, or the manifest only when observed behavior shows a stable lesson:

- If Claude repeatedly chooses the right tool, document the sequence as accepted.
- If Claude drifts, add a sharper negative rule and a concrete fail example.
- If Claude asks for too much context, strengthen the source-map/get-symbol-first wording.
- If Claude names wrong or stale tools, improve the manifest descriptions rather than adding hidden prompt assumptions.
- If Claude misunderstands WinMerge, repeat that WinMerge is review/save, not partial merge or repair.

Do not add broad rules based on one weird run. Prefer small examples tied to observed tool behavior.
