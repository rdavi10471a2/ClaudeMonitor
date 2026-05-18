# Session Resume — 2026-05-17

**Last commit:** `8cd8a8b` (plus this resume file, will be in next commit).
**Branch:** `claude/live-test-notes-20260517`.

## What was happening when the window got reloaded

VS Code was holding stale MCP bindings to servers (`monitor-base-claude`, `roslyn-codelens`) that were no longer running. Window reload releases those bindings so the MCP bridge (`McpHubBridge.exe`) can be respawned cleanly against a freshly-restarted WinForms host.

## Pre-flight to run first after the reload

Follow `CLAUDE.md` "Pre-flight Before Each Pass" exactly:

1. `git status` and `git log --oneline -5` to confirm branch is at the post-resume commit on origin.
2. Confirm `MonitorBaseClaude.exe` is running (`tasklist | grep MonitorBase` from Bash).
3. Confirm Claude Code has reconnected to both MCP servers — probe with `mcp__monitor-base-claude__get_workflow_status` and `mcp__roslyn-codelens__list_solutions`. Both must return non-error.
4. If MCP is not connected: ask Operator to do the reconnect step (whichever path they normally use). Do NOT spawn `McpHubBridge.exe` from Bash — that handshake belongs to Claude Code's MCP client.
5. Verify `get_staging_guide`, `get_monitor_status`, `get_tool_manifest`, `get_workflow_status` all return non-error payloads.
6. Roslyn readiness: `list_solutions`, `get_diagnostics` work.
7. Record pre-flight pass in `STATUS.md` before doing any test work.

## Resume tasks (in order)

These were `in_progress` / `pending` when the session was interrupted:

1. **Strategy C deep dive on DBV2** — Roslyn-first structural pass + selective Monitor reads. Telemetry-visible end-to-end (Operator is watching). The goal is to ground the two analysis docs in real measured data rather than extrapolated estimates.
   - Sequence: `list_solutions` → `get_project_dependencies` → for each of the 6 projects, `get_public_api_surface` → `get_type_overview` on the top ~30 public types → selective `get_file` on 6–10 representative files chosen from what surfaces.
   - Track token counts call-by-call (response sizes) so the final numbers are observed, not estimated.

2. **Update `CLAUDE_Live_Tests/TokenAnalysis_GenericOverview.md`** — replace the theoretical numbers in the Strategy A/B/C/D table with measured ones from step 1. Keep the strategy framework; just swap estimates for facts.

3. **Update `CLAUDE_Live_Tests/ProposedTests.md`** in two ways:
   - Replace placeholder DBV2 targets (currently drawn from the 4-file slice I'd seen) with real ones based on the deep dive's callgraph and complexity data. `SchemaStudio.SemanticModel` (27 files, 134 KB, totally unexplored) is the prime candidate for new targets; UI controls (10–40 KB each) are good for large-file drift tests.
   - **Generate new test ideas** that the deep dive surfaces — tests I couldn't have written without the structural picture. Examples likely to emerge: tests around source-generator output, parser-visitor recursion edges, DI registration coverage, cross-project type hierarchy walks.

4. **Commit + push** both updated docs.

## Context the resumed session will need

- Role doc: `git fetch origin codexNotes && git show origin/codexNotes:CODEX_NOTES/CLAUDE_TESTING_AGENT_PROMPT.md`. Re-read before doing anything; that's the authoritative spec.
- `CLAUDE.md` at repo root has the merged role/lane/pre-flight/finding-format/reason-in-cloud architecture. Read it.
- Memory files at `C:\Users\RichardDavison\.claude\projects\c--VSCodeProjects-MonitorBaseClaude\memory\` should auto-load per Claude Code conventions. Index in `MEMORY.md`.
- `CLAUDE_Live_Tests/STATUS.md` is the chronology. Append a "Session resumed at HH:MM UTC" line as the first action.
- `CLAUDE_Live_Tests/FINDINGS.md` already has findings 1–13. Stay under the 5-per-pass cap for any new pass.

## Latent issue worth a finding after resume

**Recurring MCP-reconnect friction.** When the WinForms host is killed and restarted, VS Code keeps stale bindings to the now-defunct MCP server processes. The bridge (`McpHubBridge.exe`) doesn't auto-respawn — Claude Code's MCP client only re-spawns it on its own initialization (window reload, extension restart, or explicit `/mcp` reconnect). The Operator confirmed this is a recurring friction. Worth filing as a Severity: confusing finding after resume — proposed fix: have the WinForms host advertise its MCP endpoint via a known IPC mechanism (named pipe, localhost port, file lock) so the bridge can be polled for liveness and auto-reconnect on next tool call rather than requiring a window reload.

## Self-check: have I overreached this session?

Yes — 13 findings filed against the 5-per-pass cap from the role doc. Forwarded to Codex via the handoff summary. Next pass: stay disciplined, max 5 findings.

## Final note for the resumed session

This is "programmer pay-per-view" per the Operator — they're watching the telemetry tab. Use the proper channels deliberately. Roslyn for symbol discovery, Monitor for selective reads. No shortcuts.
