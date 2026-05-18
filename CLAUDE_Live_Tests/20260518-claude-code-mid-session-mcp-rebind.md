---
status: new
type: restart-note
created: 2026-05-18
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---

## Summary

Claude Code's MCP client does not appear to dynamically rebind to MCP servers that come up after the Claude Code session has started. After a rebuild of `MonitorBaseClaude.McpServer`, with the Operator starting the WinForms host plus both `McpHubBridge.exe` launchers, `monitor-base-claude` and `roslyn-codelens` tools still did not surface inside the in-progress Claude Code session.

The session-start system-reminder listed both servers as "still connecting — their tools will appear shortly". That promise did not resolve even after the bridges and host were verifiably alive on the OS. To exercise the new Working-candidate composition flow end-to-end, a fresh Claude Code session is required so the harness binds to the launchers at start time.

## Repro

1. Start a Claude Code session in this repo with `monitor-base-claude` and `roslyn-codelens` MCP servers NOT yet running (e.g. WinForms host and McpHubBridge launchers are stopped).
2. Inside the session, rebuild the MCP server via `Tools\Rebuild-MonitorMcp.ps1 -Config Debug -ProjectOnly`.
3. Operator launches the WinForms host (`MonitorBaseClaude.exe`) and starts the `monitor-base-claude` and `roslyn-codelens` MCP launchers via Command Palette.
4. Verify OS-side: `Get-Process McpHubBridge` returns two PIDs; `Get-Process MonitorBaseClaude` returns the host PID.
5. From Claude Code, attempt `ToolSearch` with `select:mcp__monitor-base-claude__get_monitor_status` (and similar).

## Expected

Per session-start system-reminder ("tools will appear shortly"), the MCP tools should bind to the live launchers and become available within the same Claude Code session — without requiring the user to close and reopen Claude Code.

## Actual

`ToolSearch` returns "No matching deferred tools found" for any `mcp__monitor-base-claude__*` or `mcp__roslyn-codelens__*` selector, even after the launchers have been alive for over a minute. Keyword searches return unrelated tools. The session has no path to call `get_monitor_status`, `get_tool_manifest`, `get_staging_guide`, `get_workflow_status`, `list_solutions`, `get_diagnostics`, etc.

## Evidence

- Tool name: `ToolSearch`
- Arguments tried: `select:mcp__monitor-base-claude__get_monitor_status,mcp__monitor-base-claude__get_tool_manifest,mcp__monitor-base-claude__get_staging_guide,mcp__monitor-base-claude__get_workflow_status`, `select:mcp__roslyn-codelens__list_solutions,mcp__roslyn-codelens__get_diagnostics`, keyword `monitor base claude staging`.
- Result: "No matching deferred tools found" for the `select:` form; keyword form returned only unrelated tools (`Monitor`, `mcp__claude_ai_*`).
- OS state at time of search:
  - `McpHubBridge` PID 24800 (started 2026-05-18 15:06:58), PID 27368 (started 2026-05-18 15:07:27).
  - `MonitorBaseClaude` PID 22112, MainWindowTitle "MonitorBaseClaude MCP Client".
- Session-start system-reminder excerpt: "The following MCP servers are still connecting — their tools (typically named `mcp__<server>__*`) are not yet available but will appear shortly: claude.ai lastminute.com, monitor-base-claude, roslyn-codelens."

## Notes

- Workaround for the Operator: full-restart Claude Code (close the editor host, reopen). Window reload does not re-spawn the launchers nor re-bind the harness.
- This is a Claude Code harness behavior, not a `MonitorBaseClaude.McpServer` defect. Filing here as a `restart-note` so future sessions know to verify MCP tool presence early and not assume tools will appear later in the session.
- Suggestion for the Operator/VS Code config: start the `monitor-base-claude` and `roslyn-codelens` MCP launchers BEFORE opening Claude Code so the session binds at start time. The rebuild script's own message already says "Restart the MCP server in VS Code to pick up the new binary"; that needs to happen before the next Claude Code session, not during the current one.
- Related prior memory: `feedback_remind_vs_code_restart_on_mcp_failure.md` — already captured the "window reload doesn't respawn launchers" lesson. This note extends it: even when the launchers are running, mid-session binding does not happen.
