# Claude Desktop And Code Setup

This note captures the intended Claude setup for MonitorBaseClaude.

## Expected Clients

- Claude Code in VS Code or terminal for local project-aware work.
- Claude Desktop for conversation and MCP experiments where available.
- Monitor MCP Tool Server for workflow, staging, hashes, ledgers, and review classification.
- Roslyn CodeLens MCP for semantic .NET code intelligence.

Claude Code official MCP docs describe project `.mcp.json` files, `/mcp` status checks, project-scoped server approval prompts, and MCP prompts/resources. Roslyn CodeLens setup pages advertise Claude Code and Claude Desktop usage with stdio MCP configuration, including `claude mcp add roslyn-codelens -- roslyn-codelens-mcp` or a `.mcp.json`/desktop config entry.

Sources used during setup review:

- https://code.claude.com/docs/en/mcp
- https://code.claude.com/docs/en/settings
- https://conare.ai/marketplace/mcp/roslyn-codelens/setup
- https://conare.ai/marketplace/mcp/roslyn-codelens
- https://thedailyworkflow.com/mcp/server/roslyn-codelens-mcp

## Claude Code Project Setup

Machine-local paths live in `appsettings.json`. Start from `appsettings.template.json`, then set:

- `MonitorClient:WatchedSolutionPath`
- `MonitorClient:CodeLensSolutionPath`
- `WorkflowSettings:ObservedRoot`

Relative paths in `appsettings.json` are resolved from the config file folder. The project `.mcp.json` calls repo-local PowerShell launch scripts under `Tools`, so it does not need a user-specific `C:\Users\...\roslyn-codelens-mcp.exe` path.

Build the Monitor MCP server first:

```powershell
dotnet build .\MonitorBaseClaude.McpServer\MonitorBaseClaude.McpServer.csproj
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

## Claude Desktop Setup

Claude Desktop may not automatically read `CLAUDE.md` the same way Claude Code does. Paste or attach the relevant rules when testing Desktop, especially:

- Use Monitor MCP for staging and vote-plus-hash classification.
- Use `find_file -> get_source_map(mode: navigation) -> get_source_map(mode: selector) -> get_symbol -> get_file only when needed`.
- Do not directly edit watched source.
- Do not use source process markers or glyph anchors.
- Diff review is all-or-none.

Desktop MCP configuration should point to the built Monitor MCP server executable or use the same stdio command pattern as `.mcp.json`. Machine-local absolute paths belong in local config, not shared docs, unless the doc is explicitly for this workstation.

For Claude Desktop, if relative `.mcp.json` script paths are not resolved from the project root, use absolute script paths to:

```text
<repo>\Tools\Start-MonitorBaseClaudeMcp.ps1
<repo>\Tools\Start-RoslynCodeLensMcp.ps1
```

## Roslyn CodeLens Pairing

Roslyn CodeLens is valid as a Claude-facing MCP tool because its public setup material explicitly targets Claude clients. Use it beside Monitor MCP, not instead of Monitor MCP.

Use CodeLens for:

- diagnostics
- references
- callers
- type hierarchy
- project dependencies
- source generator inspection
- generated code inspection
- code actions in preview mode

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
