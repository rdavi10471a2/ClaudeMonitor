# Claude Desktop And Code Setup

This note captures the intended Claude setup for MonitorBaseClaude.

## Expected Clients

- Claude Code in VS Code or terminal for local project-aware work.
- Claude Desktop for conversation only unless local MCP support is re-verified on this workstation.
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

Relative paths in `appsettings.json` are resolved from the config file folder. The project `.mcp.json` uses repo-local PowerShell scripts for Claude Code and developer shells.

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

## Claude Desktop Status

Claude Desktop local MCP access is not currently a verified path for this workstation. Prior attempts indicated Desktop could not access the local Monitor MCP server reliably, so the supported live testing path is Claude Code in VS Code with the project MCP binding.

Do not use Desktop for Monitor MCP workflow validation until a fresh local MCP connection test proves it can call the server. If Desktop is used for conversation or external review, paste or attach the relevant rules manually because it may not read `CLAUDE.md` the same way Claude Code does.

The removed direct-exe Desktop configuration is historical only. Keep the active instructions focused on VS Code/Claude Code unless Desktop local MCP support becomes demonstrably reliable.

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

Do not use CodeLens `apply_code_action` as the normal write path for the watched solution. If CodeLens suggests a refactoring or fix, use it as analysis input, then stage the resulting watched-source change through Monitor MCP so the Operator review and vote-plus-hash gate still apply.

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
