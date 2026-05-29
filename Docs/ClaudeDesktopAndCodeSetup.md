# Claude Desktop And Code Setup

This note captures the intended Claude setup for MonitorBaseClaude.

## Expected Clients

- Claude Code in VS Code or terminal for local project-aware work.
- Claude Desktop for conversation and MCP experiments where available.
- Monitor MCP Tool Server for workflow, staging, hashes, ledgers, and review classification.
- Roslyn-derived semantic .NET code intelligence through Monitor-owned index/source-map/hub surfaces.

Claude Code official MCP docs describe project `.mcp.json` files, `/mcp` status checks, project-scoped server approval prompts, and MCP prompts/resources. Roslyn CodeLens setup pages advertise direct Claude bindings, but this project does not use a direct `roslyn-codelens` binding in the normal VS Code workflow; Monitor is the single Claude-facing MCP server.

Sources used during setup review:

- https://code.claude.com/docs/en/mcp
- https://code.claude.com/docs/en/settings
- https://conare.ai/marketplace/mcp/roslyn-codelens/setup
- https://conare.ai/marketplace/mcp/roslyn-codelens
- https://thedailyworkflow.com/mcp/server/roslyn-codelens-mcp

## Claude Code Project Setup

Machine-local paths live in `appsettings.json`. Start from `appsettings.template.json`, then set:

- `MonitorClient:WatchedSolutionPath`
- `WorkflowSettings:ObservedRoot` only as a legacy/fallback folder when no watched solution path is configured.

`MonitorClient:WatchedSolutionPath` is the shared solution identity for Monitor and Roslyn-derived context. The watched project does not need to be a sibling of the Monitor repository; it can live anywhere the local machine can read.

Relative paths in `appsettings.json` are resolved from the config file folder. The project `.mcp.json` binds one Claude-facing MCP name, `monitor-base-claude`, directly to the repo-local hub bridge executable. The PowerShell scripts under `Tools` are developer convenience wrappers, not the canonical Claude/VS Code binding path.

Build the solution first:

```powershell
dotnet build .\MonitorBaseClaude.slnx
```

Open the project root:

```text
C:\VSCodeProjects\MonitorBaseClaude
```

Claude Code should read:

- `.mcp.json` for project MCP server configuration.
- `CLAUDE.md` for project workflow rules.
- `MCP_CLIENT_TESTING.md` for a quick connection smoke.

Codex/build-agent implementation notes live separately in `AGENTS.md`. Those notes are for building MonitorBaseClaude, not for redefining Claude's runtime MCP workflow.

Inside Claude Code, run:

```text
/mcp
```

Approve the project-scoped server if prompted. Confirm the Monitor MCP server is connected.

Recommended first prompt:

```text
Use the monitor-base-claude MCP server. Call get_monitor_status, get_tool_manifest, and get_source_map for Data\BaseTableRepository.cs with scope file and mode selector. Summarize the safe edit loop before proposing any code change.
```

## Claude Code Usage Snapshot

Claude Code can expose live model, cost, context-window, token, rate-limit, and session fields through its status line JSON. MonitorBaseClaude reads a local snapshot written by `Tools/ClaudeStatusLine/Write-ClaudeStatusSnapshot.ps1` and displays it in `View -> Claude Info`.

Configure Claude Code statusline to call the script from this repository:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File C:/VSCodeProjects/MonitorBaseClaude/Tools/ClaudeStatusLine/Write-ClaudeStatusSnapshot.ps1
```

The script writes:

```text
Working\History\ClaudeCode\statusline-latest.json
Working\History\ClaudeCode\statusline-history.jsonl
```

This is the supported path for Claude token/context feedback. MCP request/response byte counts remain separate proxy telemetry and should not be treated as Claude token counts.

## Claude Desktop Setup

Claude Desktop may not automatically read `CLAUDE.md` the same way Claude Code does. Paste or attach the relevant rules when testing Desktop, especially:

- Use Monitor MCP for staging and vote-plus-hash classification.
- Use `find_file -> get_source_map(mode: navigation) -> get_source_map(mode: selector) -> get_symbol -> get_file only when needed`.
- Do not directly edit watched source.
- Do not use source process markers or glyph anchors.
- Diff review is all-or-none.

Desktop MCP configuration should also point directly to the built bridge executable when you want all MCP traffic visible in the WinForms dashboard. Do not launch the servers through PowerShell wrappers from Desktop's MSIX build; that path can break stdio forwarding before the MCP `initialize` request reaches the server.

Use this workstation-local shape after building the solution and starting `MonitorBaseClaude.exe`:

```json
{
  "mcpServers": {
    "monitor-base-claude": {
      "command": "C:\\VSCodeProjects\\MonitorBaseClaude\\Tools\\McpHubBridge\\bin\\Debug\\net10.0\\McpHubBridge.exe",
      "args": ["--server", "monitor"]
    }
  }
}
```

The bridge is the stdio process Claude binds. Starting the WinForms app starts the named-pipe hub, not the bridge process. Claude/VS Code or Claude Desktop starts `McpHubBridge.exe`; the bridge connects to the already-running hub, and the hub starts or reconnects the real Monitor server process behind the pipe. `MonitorClient:WatchedSolutionPath` is the single watched solution path.

## Roslyn Context

The normal Claude-facing setup exposes only Monitor MCP. Roslyn-derived answers are still available through Monitor's solution index, source maps, overlay compile validation, and hub-owned internals. A direct Roslyn CodeLens MCP binding is reserved for explicit experiments, not the standard workflow.

Use Monitor/Roslyn-derived context for:

- diagnostics
- references
- callers
- type hierarchy
- project dependencies
- source generator inspection
- generated code inspection
- code-action analysis only when explicitly surfaced through an approved experimental route

Do not use CodeLens `apply_code_action` as the normal write path for the watched solution if a direct Roslyn binding is manually attached. If CodeLens suggests a refactoring or fix, use it as analysis input, then stage the resulting watched-source change through Monitor MCP so the Operator review and vote-plus-hash gate still apply.

Use Monitor MCP for:

- watched source discovery
- source maps used by the Monitor loop
- staged candidates
- WinMerge review paths
- decision records
- ledgers
- vote-plus-hash classification

## Tool-Use Prompt Pattern

For C# edits, ask Claude to follow this pattern:

```text
Before editing, use Monitor MCP find_file if needed, then get_source_map in navigation mode for broad folder/project context and selector mode for the chosen file. Use get_symbol for the exact member body. Read related files when the source map shows dependencies, partial classes, or suggestedNextCalls. Stage a complete candidate only after the related context is understood.
```

For impact questions, add:

```text
Use Roslyn CodeLens for references/callers/type hierarchy/diagnostics, then return to Monitor MCP for staging and review.
```

## Token/Usage Notes

Claude Code exposes usage/context information in its own UI commands such as `/context`, `/cost`, and `/usage`. MonitorBaseClaude should not depend on scraping those values. The Host can record them manually or display them beside Monitor telemetry later.

The practical token strategy is tool choice:

- Prefer `get_source_map` for structure.
- Prefer `get_symbol` for bodies.
- Use `get_file` only when the full file is genuinely needed.
- Ask CodeLens semantic questions instead of pulling large related files blindly.

## Enterprise Or Team Accounts

Team or enterprise accounts may have managed settings, MCP restrictions, approval prompts, or usage policies outside this repository. Treat those as account policy. The local Monitor workflow should still be valid if the client is allowed to launch project MCP servers.
