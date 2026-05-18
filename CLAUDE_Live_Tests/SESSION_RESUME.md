# Session Resume — 2026-05-18 (latest at top)

**Branch:** `claude/live-test-notes-20260517`.
**Tip after merge:** `dbb3f4f` Merge `origin/main` into `claude/live-test-notes-20260517` (will be followed by Pass 7 note commits).
**Origin/main at tip:** `b0d071e` "Promote Working candidate composition flow" — the new V1 single-file edit mode is live.

## State machine for the next session (Pass 7 testing)

**On open, do NOT redo any of the following — they're already done on disk:**

1. `origin/main` merged. Three new commits picked up:
   - `ede0a47` — Organize Claude report lanes; new `CLAUDE_Live_Tests/README.md` lane spec (date-stamped per-report files with YAML headers). Archived ~12 KB of stale Docs.
   - `694e076` — Block superseded staged candidates; physical archive to `Working\Staged\Superseded\<yyyyMMdd>\<recordId>\…`; `launch_staged_diff` and `record_diff_decision` reject superseded ids.
   - `b0d071e` — **Working candidate composition flow** (the new mode). Details in `project_working_candidate_composition_flow.md` auto-memory and in CLAUDE.md.
2. `MonitorBaseClaude.McpServer` rebuilt (Debug). DLL at `MonitorBaseClaude.McpServer\bin\Debug\net10.0\MonitorBaseClaude.McpServer.dll`.
3. WinForms host rebuilt (Debug) and started. PID was 22112 in the prior session. Verify it's still alive:
   ```powershell
   Get-Process MonitorBaseClaude -ErrorAction SilentlyContinue | Select-Object Id, MainWindowTitle
   ```
   If gone, restart with:
   ```powershell
   Start-Process -FilePath 'c:\VSCodeProjects\MonitorBaseClaude\bin\Debug\net10.0-windows\MonitorBaseClaude.exe'
   ```
4. `McpHubBridge.exe` launchers were live at 24800 + 27368 in the prior session (Operator started them). If they're dead, the Operator needs to start them via VS Code Command Palette → MCP: Start Server for `monitor-base-claude` and `roslyn-codelens`.
5. CLAUDE.md updated with the new "Working Candidate Composition Flow (V1, post-b0d071e)" section.
6. `CLAUDE_Live_Tests/STATUS.md` Pass 7 entry written.
7. Date-stamped restart-note `20260518-claude-code-mid-session-mcp-rebind.md` filed.
8. Auto-memory updated: new `project_working_candidate_composition_flow.md`; `feedback_remind_vs_code_restart_on_mcp_failure.md` extended with the mid-session rebind lesson.

## Why a new session is needed

Mid-session MCP rebind does NOT work in Claude Code. The prior session's `ToolSearch` for `mcp__monitor-base-claude__*` and `mcp__roslyn-codelens__*` returned "No matching deferred tools found" even after the launchers came up. The harness only binds MCP servers at session start. Operator was going to toggle disable+enable on the MCP servers before opening this resume — that may or may not surface the tools; if not, a full Claude Code restart with launchers already up is the fallback.

## Pre-flight to run first (abridged — full version in CLAUDE.md)

1. `git status` and `git log --oneline -5` — confirm tip and clean tree.
2. `Get-Process McpHubBridge,MonitorBaseClaude` — confirm both alive.
3. `ToolSearch select:mcp__monitor-base-claude__get_monitor_status,mcp__monitor-base-claude__get_tool_manifest,mcp__monitor-base-claude__get_staging_guide,mcp__monitor-base-claude__get_workflow_status`. All four must load schemas.
4. Call each and confirm non-error payloads. Confirm `tools/list` exposes the new `stage_candidate_for_review` plus `submit_file_old` / `add_symbol_old` / `add_field_old` / `add_method_old`.
5. `ToolSearch select:mcp__roslyn-codelens__list_solutions,mcp__roslyn-codelens__get_diagnostics`. Call each.
6. `get_workflow_status` must report a WinMerge resolution. If not, do not call `launch_staged_diff`.
7. Append a "Pass 7 resumed at HH:MM UTC — MCP up" line to `STATUS.md` before any test work.

If MCP tools still aren't surfacing after pre-flight: file an additional finding referencing `20260518-claude-code-mid-session-mcp-rebind.md` and stop. Do not waste tokens trying to rebind from the agent side.

## Pass 7 test plan — Working-candidate composition flow

Target file: pick a small, easy member-level target in DBV2. `SelectItem.cs` from Pass 6 already has uncommitted changes — pick a different file (e.g. one in `C:\Schema Studio - DBV2\SchemaStudio.SemanticModel\` that's not currently modified). Use Roslyn `get_type_overview` + Monitor `get_source_map` to pick the target.

Five scenarios, in order:

1. **Single-op single-file member edit through V1 path.**
   - `start_monitor_session`.
   - Choose a small method body via Roslyn discovery + `get_source_map` (selector mode) + `get_symbol`.
   - `submit_symbol` is NOT yet on the candidate path; pick a target suitable for `add_method` (insert a new method) instead.
   - After `add_method`: confirm a file at `Working\<observedRootKey>\<relative path>` contains the candidate. Confirm `list_session_staged_records` shows NO staged record yet.
   - `stage_candidate_for_review(path, sessionId)`. Confirm one StagedEditRecord appears.
   - `launch_staged_diff(stagedRecordId)`. Operator reviews in WinMerge. `record_diff_decision`. Verify vote-plus-hash classification.

2. **Multi-op same-file composition.**
   - On a fresh path, call `add_field` then `add_method` under one session.
   - Confirm the second op composes against the first's Working candidate, not against fresh watched source.
   - Confirm only one Working file exists and contains both insertions.
   - `stage_candidate_for_review`. Confirm exactly one StagedEditRecord (not two).

3. **`candidate-baseline-stale` behavior.**
   - Start a candidate (one `add_field`).
   - Manually touch the watched source out-of-band (e.g. add a space and save in VS Code — Operator's hand, not via MCP).
   - Call a second candidate op on the same path. Confirm it refuses with `candidate-baseline-stale`.
   - Document the recovery path (start a new session? abandon candidate? — observe what the server actually returns).

4. **Superseded-record handling (post-694e076).**
   - Stage a candidate via `stage_candidate_for_review`. Capture stagedRecordId-A.
   - Immediately stage a corrected candidate for the same file. Capture stagedRecordId-B.
   - Verify record A's `QueueStatus == "superseded-by-later-same-file-candidate"`. Verify record A's staged file moved to `Working\Staged\Superseded\<yyyyMMdd>\<recordId-A>\…`.
   - Try `launch_staged_diff(recordId-A)`. Confirm `staged-record-superseded` and the error message about `list_session_staged_records`.
   - Try `record_diff_decision(recordId-A, accepted)`. Confirm refusal.

5. **`_old` variant smoke (compatibility path).**
   - On a separate path, call `submit_file_old` (or `add_method_old`). Confirm the legacy behavior: an immediate staged record, no Working-candidate composition.
   - Don't make this a primary test path — just confirm the escape hatch works.

## Per-finding cap reminder

Max 5 findings per pass, max 150 words each. Filing format per `CLAUDE.md` "Finding Format And Limits". For new findings, use date-stamped files per `CLAUDE_Live_Tests/README.md` (e.g. `20260518-finding-26-add-method-Working-baseline-edge.md`). Append-mode entries to the legacy `FINDINGS.md` are still acceptable but the date-stamped lane is preferred for post-2026-05-18 reports.

## Context the resumed session will need

- Role doc (canonical): `git fetch origin codexNotes && git show origin/codexNotes:CODEX_NOTES/CLAUDE_TESTING_AGENT_PROMPT.md`. Re-read before any tool calls.
- `CLAUDE.md` at repo root — read top-to-bottom; the new Working-candidate section is between "Reason In Cloud" and "Report And Memory Lanes".
- Manifest: `MonitorBaseClaude.McpServer/MONITOR_MCP_TOOL_MANIFEST.md` — authoritative tool descriptions; the V1 candidate semantics are documented under `submit_file`, `stage_candidate_for_review`, `add_symbol`, `add_field`, `add_method`, `submit_file_old`, `add_symbol_old`, `add_field_old`, `add_method_old`.
- Auto-memory should load via the harness. Key entries: `project_working_candidate_composition_flow.md`, `feedback_remind_vs_code_restart_on_mcp_failure.md` (extended), `reference_testing_agent_role.md`.
- `CLAUDE_Live_Tests/STATUS.md` Pass 7 entry — the rest of what was done this session.
- `CLAUDE_Live_Tests/20260518-claude-code-mid-session-mcp-rebind.md` — restart-note that motivated this resume.

## Latent open items (carried from prior sessions, not addressed this session)

- Strategy C deep dive on DBV2 (token-counted Roslyn + Monitor pass) — still useful but secondary to Pass 7 V1-flow testing.
- `TokenAnalysis_GenericOverview.md` numbers are still estimates, not measurements.
- `ProposedTests.md` placeholders still need to be replaced with real DBV2 targets.

## Self-check before the next pass

- Did I re-read the role doc this session? If not, do that first.
- Am I about to use a `_old` tool variant? If yes, stop and verify why — the V1 candidate path is the target.
- Am I about to stage a multi-file coupled edit? Make sure every coupled file is staged under one `sessionId` before the first `launch_staged_diff`. The coupling rule is unchanged.
- Did `get_workflow_status` report a WinMerge resolution? If not, don't call `launch_staged_diff`.

---

# Older resume notes (preserved for history)

## Session Resume — 2026-05-17

**Last commit:** `8cd8a8b` (plus this resume file, will be in next commit).
**Branch:** `claude/live-test-notes-20260517`.

### Update — 2026-05-17, after first reload attempt

**Window reload alone was NOT sufficient.** Diagnosis from the post-reload pickup session:

- WinForms host was not running after the reload. I (Claude) launched it from `c:\VSCodeProjects\MonitorBaseClaude\bin\Debug\net10.0-windows\MonitorBaseClaude.exe` via `Start-Process`. It ran successfully (process name `MonitorBaseClaude`, window title `MonitorBaseClaude MCP Client`). **Operator is killing it before the full VS Code restart for a clean slate, so the next session will start with no host running.** Launch path: `Start-Process -FilePath 'c:\VSCodeProjects\MonitorBaseClaude\bin\Debug\net10.0-windows\MonitorBaseClaude.exe'`. Verify `Get-Process MonitorBaseClaude` shows it up before probing MCP.
- Even with the host up, Claude Code's MCP client never re-spawned the PowerShell launcher scripts. Proof: `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\logs\mcp-server-monitor-base-claude.log` and `...mcp-server-roslyn-codelens.log` had **zero new entries** since the prior session's `Server transport closed` events on 2026-05-16T22:11Z and 2026-05-17T03:48Z respectively. The launchers were never invoked this session.
- `/mcp` slash command is **not available** in this Claude Code VS Code-native environment. The Operator's chosen recovery: full VS Code restart (not just window reload).
- Spawn chain otherwise verified healthy: `.mcp.json` references the launcher scripts; `Tools\McpHubBridge\bin\Debug\net10.0\McpHubBridge.exe` exists (built today 17:55); `Schema Studio.sln` resolves at `C:\Schema Studio - DBV2\Schema Studio.sln`.

**After the full VS Code restart, the next session should:**

1. Verify `MonitorBaseClaude` process is still running. Skip starting a new one.
2. Probe MCP via `ToolSearch select:mcp__monitor-base-claude__get_workflow_status` etc. — if tools register, pre-flight can proceed normally per the section below.
3. If tools still don't register, tail the two log files for new entries — that tells us whether Claude Code attempted to spawn at all.
4. **File a finding** (Severity: confusing) once tools are up: "Window reload does not respawn MCP servers; full VS Code restart required." This is the same friction noted in the original "Latent issue worth a finding after resume" section, now with concrete evidence (the log gaps).
5. Resume tasks per the unchanged list below — start with Strategy C deep dive on DBV2.

### Resume tasks (in order — superseded by Pass 7 plan above as of 2026-05-18)

These were `in_progress` / `pending` when the original session was interrupted, before Pass 7 reset the priority. Kept here for reference:

1. **Strategy C deep dive on DBV2** — Roslyn-first structural pass + selective Monitor reads. The goal is to ground the two analysis docs in real measured data rather than extrapolated estimates.
2. **Update `CLAUDE_Live_Tests/TokenAnalysis_GenericOverview.md`** — replace theoretical numbers with measured ones.
3. **Update `CLAUDE_Live_Tests/ProposedTests.md`** — replace placeholder DBV2 targets with real ones based on the deep dive.
4. **Commit + push** both updated docs.

### Latent issue worth a finding after resume

**Recurring MCP-reconnect friction.** When the WinForms host is killed and restarted, VS Code keeps stale bindings to the now-defunct MCP server processes. The bridge (`McpHubBridge.exe`) doesn't auto-respawn — Claude Code's MCP client only re-spawns it on its own initialization. NOW SUPERSEDED by the 2026-05-18 restart-note which extends this finding: even when the launchers are running, mid-session binding does not happen — Claude Code only binds MCP at session start.
