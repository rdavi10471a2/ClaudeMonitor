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
- Retry Pass 2 partial-split staging against a fresh server binary with the WinForms host running.

## Pass 2 — 2026-05-17 — DatabaseDomainRepository Async + Static SQL Dict (aborted)

Detail in `Pass2_DatabaseDomainRepository_Async.md`. Summary:

- Intended scope: convert `DatabaseDomainRepository` to async, extract the three SQL literals to a named static dictionary, allow partial-class split, introduce C# regions as insertion-anchor context, demonstrate the create-file path via `submit_file` against a new watched path.
- Discovery (Roslyn + Monitor) found a pre-existing `DatabaseDomainRepositoryAsync` class with the same shape, both classes have zero callers in the solution, and neither file uses regions today.
- Staging partial-split: modify-existing-path succeeded (record `20260517_174813445_submit_file_DatabaseDomainRepository_4bc6cba3` with `CS0103: The name 'Sql' does not exist` from overlay validation, as expected). New-path `submit_file` for the partial companion failed twice with an opaque error.
- Operator halted Pass 2 mid-run: the MCP server was not rebuilt and the WinForms host was not started, so any `launch_staged_diff` would have returned `host_unavailable` rather than a real Operator decision. Pass 2 staging outcomes are not valid evidence of current server behavior.
- New findings filed: 6 (`submit_file` new-path opaque failure), 7 (setup docs do not say to start the WinForms host), 8 (test-validity gate I should have caught up-front).
- Next step: rebuild `MonitorBaseClaude.McpServer`, start the WinForms host, then rerun Pass 2 against a fresh binary.

## Pass 2 Rerun — 2026-05-17 ~18:18 UTC — DatabaseDomainRepository async + static SQL dict (single file)

Detail in `Pass2_DatabaseDomainRepository_Async.md` under the "Pass 2 rerun" subsection. Summary:

- Recovered from a killed chat session; both MCP servers verified alive after the rebuild. `get_staging_guide` now exposed in `tools/list` (Finding 2 marker is gone).
- Scope: same file (`SchemaStudio.Data\DatabaseDomainRepository.cs`), single-file shape, no new file / no partial class. Async API + static `IReadOnlyDictionary<string,string> Sql` + regions (Fields / Constructors / SQL Statements / Public Methods). I initially misread "minimal changes" as absolute-minimum and proposed null guards; Operator corrected back to the original async + SQL-dict scope.
- New monitor session `monitor-20260517231612-66ef0e2bfc0b4eae9`. Old session's pre-launch record `20260517_174813445_..._4bc6cba3` is orphaned per Operator choice (not explicitly rejected).
- `submit_file` staged record `20260517_181844880_submit_file_DatabaseDomainRepository_f921afa8`. Overlay validation: 82 syntax trees compiled, **0 diagnostics**. This is the key improvement over the original Pass 2: the same-file SQL dict resolves the `CS0103: The name 'Sql' does not exist` errors that blocked the partial-split attempt.
- `launch_staged_diff` returned `winmerge-launched`. First attempt cancelled by Operator (combined async + dict + regions + null guards diff was unreadable in WinMerge). Recorded `rejected`, restaged a simplified candidate (dict + async only, no regions, no extra null guards), relaunched.
- Operator saved the simplified candidate. `record_diff_decision(accepted)` → classification **`accepted-normalized`** (baseline had mixed `\n` / `\r\n`, normalized on save). Vote-plus-hash agreed. End-to-end pipeline validated.

## Pass 3 — 2026-05-17 ~18:33 UTC — SchemaObjectRepository async-only

Detail in `Pass3_SchemaObjectRepository_Async.md`. Summary:

- Operator asked for a simpler test on a different repository file: async only, no SQL dict.
- Target: `SchemaStudio.Data\SchemaObjectRepository.cs`, 3838 bytes, 5 public methods.
- Roslyn-first discovery: `search_symbols`, `get_type_overview`, `find_callers` × 4. All callers empty. `find_references` also empty despite `search_symbols` flagging a `_schemaObjectRepository` field in `IntegrationsViewImportControl` — see Finding 11.
- Staged record `20260517_183111241_submit_file_SchemaObjectRepository_03226b7b`. Overlay validation: 82 syntax trees, **2 overlay files** (wider consumer slice), 0 diagnostics. Confirmed the consumer field doesn't invoke any renamed methods.
- Operator accepted in WinMerge. `record_diff_decision` → **`accepted-normalized`**, decisionMatchesClassification true, normalized hashes match (`05318b53...`).
- Findings filed: 10 (Claude Roslyn-first inconsistency), 11 (`find_references` empty where `search_symbols` shows a real type usage), 12 (token waste calling both source_map and get_file for whole-file rewrites).

## Pass 4 — 2026-05-17 — Session Resume After Full VS Code Restart

### Pre-flight

- VS Code window reload (prior session) did **not** respawn the MCP server launchers — log gap evidence captured in `SESSION_RESUME.md`. Full VS Code restart was required.
- Post-restart MCP probe: both servers up. `MonitorBaseClaude.exe` PID 174204, `MonitorBaseClaude.McpServ` PID 132324. `get_workflow_status`, `get_monitor_status`, `get_staging_guide`, `get_tool_manifest` all returned non-error payloads. Roslyn `list_solutions` shows `Schema Studio.sln` active with 6 projects, status `ready`.
- Roslyn `get_diagnostics(severity=error)` returned **8 compile errors** in the watched solution: 1 in `SchemaStudio.Data\DatabaseDomainTypeConverter.cs` (CS0411 type-arg inference on `ImmutableArrayExtensions.Select` — `GetByDatabaseAsync` returns `Task<T>` not `IEnumerable<T>`), and 7 CS1061 errors across `UI\DatabaseDomainManagerForm.cs`, `UI\MergedEditorSurface\IntegrationsViewImportControl.Loading.cs`, and `IntegrationsViewImportControl.Persistence.cs`. Pattern: consumers call pre-rename method names (`SaveAll`, `GetByDatabase`, `GetBySource`, `Insert`, `Update`).
- Watched repo (`C:\Schema Studio - DBV2`) shows 3 files modified uncommitted: the two repos Pass 2/3 edited, plus `DatabaseDomainTypeConverter.cs` (partial migration — calls new `GetByDatabaseAsync` but treats `Task<T>` as `IEnumerable<T>`).
- **Operator framing (refined twice across this pass):** "write set is dead — you should have been using the new protocol for multi file edits." The canonical multi-file protocol is the one already in `get_staging_guide`: there is no pre-declared WriteSet, no `declare_writeset()` step, and no `get_file(sessionId)` anchor pass. The session bag is populated **by the staging calls themselves** — N `submit_symbol(sessionId)` calls populate N entries. A coupled rename must stage the repository AND every consumer fix under the same `sessionId` before the first `launch_staged_diff`; overlay validation then sees the union. CLAUDE.md "Reason In Cloud, Compose Locally" previously described the abandoned anchor-step model and was updated this pass to reference `get_staging_guide` as canonical and drop the WriteSet language.
- **Two root causes for Pass 2/3 build break:** (1) Discovery false-negative — `find_references` returned empty for `_schemaObjectRepository` in Pass 3, and I read empty as "no consumers" rather than "discovery may be incomplete." (2) Stale protocol — even with the right WriteSet from discovery, I staged single-file, accepted, ended the session. Both passes did this.
- **Operator direction:** do not stage the consumer fixes this pass. File Findings 14 + 15, update CLAUDE.md, end the test pass. Operator's preferred shape for the eventual fix is sync bridge methods on the repositories (re-add `GetByDatabase`, `GetBySource`, `Insert`, `Update`, `SaveAll` as thin sync-over-async wrappers) — preserves consumer call sites, async stays primary. **Operator will apply the fix manually**, not via Codex or Monitor workflow.

### Findings This Pass

- Finding 14: VS Code window reload does not respawn MCP server launchers; full window restart required.
- Finding 15: empty `find_references` produced an undersized single-file WriteSet for a coupled rename; CLAUDE.md "anchor every file" protocol was the dead version of the rule.
- Finding 16: `get_type_overview` returns ~10× more data on WinForms `UserControl`-derived types than on POCOs because the inherited `System.Windows.Forms` interface stack is enumerated; ~25% of a Strategy C pass on UI-heavy DBV2 is this metadata repeated per UI-type call. Proposed minimal fix: opt-in flag to skip inherited metadata interfaces.

### Work This Pass

- CLAUDE.md "Reason In Cloud, Compose Locally" rewritten: dropped WriteSet/anchor-step language and the open-question paragraph; added "Multi-File Coupled Edits — Canonical Protocol" pointing at `get_staging_guide` as authority; added "Discovery Discipline" subsection codifying the empty-result cross-check rule.
- Strategy C deep dive **executed** end-to-end on DBV2 with measured token counts: `get_public_api_surface` 31.5K tokens, `get_project_health` 4.2K, 10× `get_type_overview` ~25K (2 UI types ≈ 17K of that). Total survey ~73K no-bodies / ~84K with 6 sampled file bodies. Original Strategy C estimate (37K) was ~2× understated, attributable to (a) per-WinForms-type interface bloat (Finding 16) and (b) `get_public_api_surface` being a single 31K-token call rather than the ~5K assumed.
- `TokenAnalysis_GenericOverview.md` updated: measurement provenance, per-call measurement table, revised Strategy A/B/C/D cost sections, revised practicability and cost estimates ($0.20–$0.57 per pass range, not $0.07–$0.67), measurement caveats refreshed. Strategy B remains the only un-measured strategy.
- `ProposedTests.md` content **nixed** at Operator direction. Framing error: prior catalog drifted toward testing DBV2's API surface rather than the Monitor workflow itself. File replaced with a short deprecation note; original content recoverable via git history. Rewrite deferred to a future pass under the correct frame (Monitor-workflow-only probes, DBV2 files as test substrate not subject).

## Pass 5 — 2026-05-18 — Symbol-Level Staging Behavior (halted at pre-flight)

### Intended Scope (per Codex `CLAUDE_TESTING_AGENT_PROMPT.md` "Next Pass Suggestion")

- Test symbol-level staging: `submit_symbol`, `add_method`, `add_field`, `add_property`, `remove_symbol`, `set_type_partial` (only if needed).
- Especially relevant now that `38c3db2` ("Include symbol-staged files in overlay validation") landed — Pass 5 is the first chance to exercise overlay validation across symbol-staged files.
- Watched repo (`C:\Schema Studio - DBV2`) compiles clean (Operator applied Pass 2/3 consumer fixes manually). Roslyn `get_diagnostics(severity=error)` returned `[]` at start of pass.

### Pre-flight Outcome — HALTED

Pre-flight failed before any staging work. State at halt (2026-05-18, ~mid-morning):

- Branch: `claude/live-test-notes-20260517` at `694fe06`, 13 commits ahead of `origin/main`, clean working tree (untracked `.claude/` and `ProjectDocs.zip` only).
- WinForms host: UP (`MonitorBaseClaude.exe` PID 70588, started 08:59 today).
- McpHubBridge: UP (PID 140068, started 09:00 today).
- Roslyn-codelens MCP: connected, `list_solutions` + `get_diagnostics` working.
- **Monitor MCP: NOT connected on the Claude Code client side.** `mcp__monitor-base-claude__*` absent from the deferred-tool list; `ToolSearch` queries for `monitor-base-claude`, `monitor staging workflow`, and `submit_symbol add_method get_source_map` all returned no matches.
- Confirming evidence: `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\logs\mcp-server-monitor-base-claude.log` last write **2026-05-16 17:11** — Claude Code's MCP client never spawned the launcher this session.

This is the same Finding 14 friction (window reload doesn't respawn MCP launchers). Operator electing **full VS Code restart**; this status line is the marker before the restart so the resumed session can pick up cleanly.

### Resume Plan

1. After VS Code restart, re-probe `ToolSearch` for `monitor-base-claude` tools.
2. If present: complete pre-flight per CLAUDE.md (verify `get_staging_guide`, `get_monitor_status`, `get_tool_manifest`, `get_workflow_status` all return non-error; confirm `get_workflow_status` reports WinMerge resolution). Then proceed with symbol-level staging tests.
3. If still absent: file as Pass 5 finding (the existing Finding 14 already documents window-reload friction; a *full restart* still failing to respawn would be a separate, worse symptom).
4. Choose a small DBV2 target for the symbol-level staging exercise (TBD — likely a single member-level edit on a leaf class with no consumers, to keep blast radius zero while exercising the tools).

### Update — 2026-05-18 ~09:20 — MCP brought up via VS Code native UI; chat tool surface stale

Findings during the restart cycle, distinct from Finding 14:

- Post-restart, **Claude Code 2.1.143 delegates MCP entirely to VS Code's native MCP gateway** — the extension's `package.json` mentions `mcp` only as a capability keyword, no settings/commands of its own. Log files now appear at `%APPDATA%\Code\logs\<session>\window1\mcpServer.workspace-dot-mcp.0.<server>.log`, and `mcpGateway.log` is a single `Initialized` line; the old per-server logs at `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\...` are stale from the standalone Claude desktop app and no longer relevant.
- VS Code native MCP **discovers `.mcp.json` but does not auto-start workspace servers** — they show as "stopped" in `MCP: List Servers`. Operator started both manually (right-click → Start Server). Logs then show `Connection state: Running`, `Discovered 38 tools` at 09:15:22 for monitor-base-claude; two `McpHubBridge.exe` processes alive (one per server).
- The launcher chain itself is healthy: manual `powershell -File .\Tools\Start-MonitorBaseClaudeMcp.ps1` spawned `McpHubBridge.exe --server monitor` cleanly. So the path that failed earlier this session was specifically VS Code's native-MCP autostart, not the .mcp.json or launcher scripts.
- VS Code exposes the following MCP commands (extracted from the workbench bundle): `workbench.mcp.startServer`, `stopServer`, `restartServer`, `showInstalledServers`, `showOutput`, `skipAutostart`, plus `openWorkspaceMcpJson`/`openUserMcpJson`. `code` CLI does NOT support `--command`, so a pure-PowerShell stop/start helper isn't possible from outside the VS Code process. Restart-after-rebuild requires triggering the command from inside VS Code (Command Palette or a bound key).
- **Tool-surface lag:** even after both servers reported Running with 38 tools, this chat's deferred-tool list never refreshed — `ToolSearch select:mcp__monitor-base-claude__*` continued to return no match. Claude Code apparently does not re-poll the MCP tool surface mid-conversation for servers that come online after session start. A new chat is required.

### Out-of-lane Write (Operator-granted, 2026-05-18)

- `Tools\Rebuild-MonitorMcp.ps1` written under explicit Operator grant. Builds the slnx (default) or just `MonitorBaseClaude.McpServer.csproj` with `-ProjectOnly`. Guards against running with `McpHubBridge.exe` alive (would file-lock the binary). After build, instructs Operator to invoke `MCP: Start Server` / `MCP: Restart Server` from the Command Palette. Not committed; awaiting Codex review or first-use validation in the next chat.
- This is outside the Claude testing-agent writable lane (`Tools/` is product tooling). Recording here as the audit trail.

### Hand-off To Next Chat

Open a new Claude Code chat in the same VS Code window. Expected state:

- Branch `claude/live-test-notes-20260517` at commit `694fe06`, 13 ahead of `origin/main`. Working tree has uncommitted `Tools\Rebuild-MonitorMcp.ps1` and this STATUS update.
- WinForms host `MonitorBaseClaude.exe` running (PID changes per session).
- VS Code MCP servers `monitor-base-claude` and `roslyn-codelens` already started — should appear in the new chat's deferred-tool list at session start.
- DBV2 watched repo compiles clean (Operator applied Pass 2/3 sync bridge fix manually).

Then resume the Pass 5 scope: one symbol-level staging test (`submit_symbol` or `add_method`) on a DBV2 leaf with no consumers, exercising the post-`38c3db2` overlay-validation-includes-symbol-staged-files improvement.

**Target picked and plan drafted:** [Pass5_Plan_SourceTable.md](Pass5_Plan_SourceTable.md). Three symbol-level staging operations (`submit_symbol` + `add_method` + `add_property`) on `SchemaStudio.SematicModel\Model\SourceTable.cs` in one session — exercises all the relevant Pass 5 tools at once AND validates the post-`38c3db2` overlay-includes-symbol-staged-files improvement under one WinMerge diff. Pre-flight checklist, Roslyn discovery, and the staging sequence are all in the plan file — new chat should hit go immediately.

## Pass 5 (resumed) — 2026-05-18 ~14:38 UTC — Symbol-level staging composition gap

Detail in [Pass5_Plan_SourceTable.md](Pass5_Plan_SourceTable.md) for the original plan; this section is the outcome.

### Pre-flight

- New chat in same VS Code window. MCP tool surface present at start: both `mcp__monitor-base-claude__*` and `mcp__roslyn-codelens__*` showed up in the deferred-tool list immediately (Operator manually started both MCP servers via VS Code's command palette before the chat opened; STATUS hand-off note was correct).
- `get_workflow_status` / `get_monitor_status` / `get_staging_guide` / `get_tool_manifest` / Roslyn `list_solutions` + `get_diagnostics(severity=error)` all clean. Watched repo compiles with `[]` errors (Operator's sync-bridge fix from Pass 4 is in place).
- `get_staging_guide` now exposes the "Choose The Staging Mode" intent→tool table and the "Discovery Discipline" subsection. Codex merged Finding 13 between Pass 4 and Pass 5.

### Discovery

- Roslyn first: `get_type_overview(SourceTable)`, `search_symbols(GetQualifiedName)` empty, `search_symbols(HasJoin)` empty, `find_references(SourceTable)` **empty** despite 8 demonstrable consumers via `search_symbols(SourceTable)` (`ExportMappers.ToSourceTableDtos(IEnumerable<SourceTable>?)`, `ParsedQuery.SourceTables` of `List<SourceTable>`, etc.). Third reproduction of the Finding 11/15 `find_references` gap — filed as Finding 21.
- Cross-check via `search_symbols` is what Operator's "discovery discipline" rule prescribes. Empty `find_references` was treated as "verify another way," not "no consumers." That's the rule working as intended.
- Source map (selector mode) on `SourceTable.cs`: 18 symbols, 4283 estimatedTokenProxy, parseStatus ok, 0 diagnostics on the file. Stable selector key for `ToString` obtained.

### Staging — three ops, one session, one file

Session `monitor-20260518143852-0d6557c0eeed4f20a`.

1. `submit_symbol(ToString)` — ternary-expression rewrite of the 3-branch `ToString`, behaviourally identical. Staged record `20260518_093901840_..._3b4d567b`. Syntax + overlay clean, `overlayFileCount: 1`.
2. `add_method(GetQualifiedName)` — read-only helper returning `$"{Database}.{Schema}.{Table}"`. Staged record `20260518_093909940_..._6e68bb9f`. `symbolsAdded: [GetQualifiedName]` at line 46. Syntax + overlay clean, `overlayFileCount: 1`.
3. `add_property(HasJoin)` — `=> JoinKeys != null && JoinKeys.Count > 0`. Staged record `20260518_093929841_..._eb3615ef`. `symbolsAdded: [HasJoin]` at line 35. Syntax + overlay clean, `overlayFileCount: 1`.

### Key observation — composition gap

The three staging calls did NOT compose within the session. Direct inspection of staged files on disk:

- Op 1's staged file contains the `ToString` rewrite. No `HasJoin`. No `GetQualifiedName`.
- Op 3's staged file contains `HasJoin`. Original 3-branch `ToString` unchanged. No `GetQualifiedName`.
- All three staged-record JSONs report identical `OriginalHash` (`5c4ffbe...` = unchanged watched baseline). Each `StagedHash` is different.
- `get_monitor_session` returns `"files": []`; `list_monitor_sessions` shows `fileCount: 0` for the Pass 5 session despite three staging calls bound to it.

Each `submit_symbol` / `add_method` / `add_property` call reads the watched-source baseline, applies its single mutation, and writes a fresh staged candidate. Subsequent ops do not see prior ops' work. The "stage A then stage B then overlay sees A+B" model from `CLAUDE.md` and `get_staging_guide` does not match observed behaviour.

Operator's hypothesis "multiple sessions creating multiple files" was ruled out by reading the staged-record JSONs — all three carry `SessionId: monitor-20260518143852-0d6557c0eeed4f20a` and the same `SourceFilePath`.

### Decision — stopped at staging, no diffs launched

Operator direction: file the bug report and stop, no WinMerge cycles. Watched source untouched. Three staged candidates remain in `Working\Staged\` as evidence; harmless because Monitor never writes watched source directly.

### Findings filed this pass

- Finding 17: symbol-level staging does not compose within a session (blocker for the documented protocol).
- Finding 18: `get_monitor_session.files` empty after staging — likely same root cause as 17.
- Finding 19: source-map signature strips property initializers (`= new()`, `= JoinCardinality.Unknown`).
- Finding 20: non-ASCII chars in `start_monitor_session.purpose` mangled to `U+FFFD` on the wire.
- Finding 21: `find_references` returns `[]` for a type with demonstrable type-position consumers — third reproduction.

### Fix sketch for Findings 17 + 18 (handed off to Codex)

Conceptual root cause: each staging tool re-reads `watchedSourcePath` from disk at call time, rather than the most-recent staged content for that file under the active session. Composition therefore never accumulates.

Suggested shape of the fix:

1. Add a per-session per-file in-memory "current staged content" cache on the server.
2. The first staging call for `(sessionId, watchedRelPath)` populates the cache from the watched file. The `OriginalHash` recorded in the staged-record JSON is the watched baseline (unchanged from today, preserves baseline-trace fidelity for vote-plus-hash).
3. Each subsequent staging call for the same `(sessionId, watchedRelPath)` reads from the cache, applies its Roslyn mutation, writes back to the cache, AND writes the now-composed result to a fresh staged candidate file on disk.
4. Surface the cache's keys via `get_monitor_session.files` and `list_monitor_sessions.fileCount`.
5. Overlay validation already runs per-call against the project graph; with composition in place, op 3's overlay validates `baseline + op1 + op2 + op3`, which is the union the plan expected.

`launch_staged_diff` semantics under this model: launch against the LATEST staged record for a file in a session, since that record's stagedFile is the cumulative state. Prior records become point-in-time snapshots rather than independent candidates.

Edge case to think through: if op 2 fails syntax validation, op 3 should fail-safe too (the cache should not advance through invalid intermediate states). Easiest rule: only update the cache when staging produced `syntaxValidation.hasErrors: false`.

### Watched repo state at end of pass

- `C:\Schema Studio - DBV2`: unchanged (Operator's pre-pass clean state preserved). `git status` would show only the same uncommitted files as Pass 4 left.
- Notes branch `claude/live-test-notes-20260517`: this STATUS update and the FINDINGS.md additions are the only new changes.

## Pass 5 retest — 2026-05-18 ~15:28 UTC — Composition fix verified end-to-end

### Setup

- Codex shipped `ce8e500 Compose same-file staged edits within sessions` on `origin/main` after Pass 5 closed. `git merge origin/main` brought it into the notes branch (clean merge, single commit, 79 lines in `MonitorWorkflowService.cs`).
- Rebuild via `Tools/Rebuild-MonitorMcp.ps1 -ProjectOnly -NoCheck` — dll moved from 327168 bytes / 8:56 AM (pre-fix) to 329728 bytes / 10:27 AM (post-fix). `ResolveEditBasePath` and `SupersedePriorSameFileSessionRecords` confirmed in the working tree.
- **Workflow correction:** Operator clarified the rebuild rule. Build with the WinForms host **and** McpHubBridge running, then VS Code's `MCP: Restart Server` swaps to the new binary. `Tools/Rebuild-MonitorMcp.ps1`'s `McpHubBridge` guard had this inverted and needed `-NoCheck` to bypass. The script should drop or invert that guard.
- **First retest attempt failed silently** because the source wasn't merged yet — `dotnet build` saw no changed input and produced a 1.25 s incremental no-op. Bridge loaded the pre-fix dll; staging produced identical hashes to the broken Pass 5 run. Diagnosed by checking the dll `LastWriteTime` against the rebuild time and grepping for `ResolveEditBasePath` in the working tree.

### Codex's fix mechanism (matches commit message)

- New `ResolveEditBasePath(context, sessionId)` finds the most-recent same-file staged record in the session with status `staged` or `force-review-launched` and returns its staged file path; falls back to watched source if none.
- Every typed staging tool (`submit_symbol`, `add_*` via `add_symbol`, `add_using`, `remove_using`, `set_type_partial`, `remove_symbol`) now calls `ParseCompilationUnit(context, editBasePath)` — reading the prior staged candidate when composing, watched source otherwise.
- `SupersedePriorSameFileSessionRecords` marks earlier same-file records in the session as `QueueStatus: superseded-by-later-same-file-candidate` after each successful new stage.
- Overlay validation skips superseded records, so it sees the cumulative latest file once. `launch_staged_diff` against the latest record shows the union.
- Uses the on-disk staged record as the durable "cache" — survives MCP reconnects, no separate in-memory state.

### Retest run (session `monitor-20260518152843-fa42e45ca57a40ff9`)

Target: `SchemaStudio.SematicModel\Model\SourceTable.cs`. Same three staging ops as the original Pass 5 plan.

1. `submit_symbol(ToString)` — ternary expression-bodied rewrite. Staged record `20260518_102901240_..._953a2306`. StagedHash `0f7f228a...` (same as broken run — op 1 always reads watched baseline, no prior staged record to compose on).
2. `add_method(GetQualifiedName)` after `ToString`. Staged record `20260518_102910450_..._7aff15fd`. StagedHash `c3fbbcad...` (**different** from broken run's `54a76628...` — proves op 2 read op 1's staged file, not the watched source). `symbolsAdded.startLine: 42`, **not 46** — because op 1's ternary `ToString` shrank the file by 4 lines.
3. `add_property(HasJoin)` after `JoinKeys`. Staged record `20260518_102921575_..._602f8dc3`. StagedHash `c6b4ec85...`. `symbolsAdded` now lists **both** `HasJoin` (line 35) **and** `GetQualifiedName` (line 44) — the server is doing baseline-vs-staged delta against the watched source and sees both new members.

### Verification of composition

Direct read of op 3's staged file confirmed all three changes co-present:
- Line 35: `public bool HasJoin => JoinKeys != null && JoinKeys.Count > 0;`
- Lines 39–42: ternary `ToString` rewrite
- Line 44: `public string GetQualifiedName() => $"{Database}.{Schema}.{Table}";`

Record JSON statuses:
- Op 1: `QueueStatus: superseded-by-later-same-file-candidate` ✓
- Op 2: `QueueStatus: superseded-by-later-same-file-candidate` ✓
- Op 3: `QueueStatus: staged` (the live record) ✓

### Review and decision

`launch_staged_diff` on op 3 → WinMerge PID 147604 → Operator accept. `record_diff_decision(accepted)` returned:

- `classification: accepted` (not `accepted-normalized` — exact byte match)
- `decisionMatchesClassification: true`
- `currentHash == stagedHash == c6b4ec85b4013f97d15aadbeb187862ff42de42d3d67a7796233a3b111ca88e0`
- `originalHash: 5c4ffbe887945e...` (the unchanged baseline, different from current — confirming the change actually landed)
- Decision record at `Working\Staged\Decisions\20260518\20260518_102921575_..._103247367_accepted.json`

Post-accept `mcp__roslyn-codelens__get_diagnostics(severity=error)` returned `[]`. DBV2 still compiles clean. Watched source now carries the ternary `ToString`, `GetQualifiedName()`, and `HasJoin`.

### Findings 17 + 18 status

- **Finding 17 (composition gap):** fixed by `ce8e500`. Verified end-to-end through the Claude Code MCP path.
- **Finding 18 (`get_monitor_session.files` empty):** not retested here, but the composition fix shares the same on-disk records lookup, so the symptom may persist (the fix uses `ReadSessionStagedRecordEntries` for composition, but `get_monitor_session.files` reads a different surface). Worth a Pass 6 check.

### New symptom — Pass 6 candidate (FileShare contention on McpTelemetry)

Mid-diff-review the WinForms host threw an unhandled exception dialog:

> The process cannot access the file `'C:\VSCodeProjects\MonitorBaseClaude\Working\History\McpTelemetry\RoslynCodeLens\responses.jsonl'` because it is being used by another process.

Cause: the McpTelemetry layer opens `responses.jsonl` without `FileShare.ReadWrite`. The Roslyn-codelens McpHubBridge writes the file while the WF host telemetry view tries to read it (or vice versa).

Minimal fix: change both the writer and the reader to open with `FileShare.ReadWrite`:
- writer: `new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)`
- reader: `new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)`

Buffering would paper over the symptom; the structural fix is the share-flag.

Filed as **Finding 22** at Operator direction during the retest (over the 5-per-pass cap; Operator-authorized). Operator's chosen resolution: strip the WF host's telemetry tail-reader, keep current-run state in process memory, leave the McpTelemetry jsonl files as write-only audit logs. That removes the contender and collapses the race; the FileShare flag fix becomes unnecessary.

### Outstanding `Tools/Rebuild-MonitorMcp.ps1` issue

Per Operator's clarification, the script's `McpHubBridge.exe` running-check is backwards. The correct workflow rebuilds while the bridge is up; the file lock on the dll resolves because the managed JIT releases the handle after load. Either:
- Drop the check entirely.
- Or invert it: warn if the bridge is **not** running, since that means the new dll won't be picked up by a subsequent `MCP: Restart Server`.

I won't touch the script in this pass (already-flagged as out-of-lane in Pass 4). Note for Codex.

### Watched repo state at end of retest

- `C:\Schema Studio - DBV2`: one new modified file, `SchemaStudio.SematicModel\Model\SourceTable.cs`, carrying the three accepted Pass 5 changes. Other uncommitted state from Pass 2/3 sync-bridge fix preserved.
- Notes branch `claude/live-test-notes-20260517`: merged `origin/main` (commit `ce8e500` pulled in); this STATUS update is the only further new change.

## Pass 6 — 2026-05-18 ~16:45 UTC — Composition + post-c95c313 verifications on SelectItem.cs

Plan in [Pass6_Plan_SelectItem.md](Pass6_Plan_SelectItem.md). Target was a new file (`SelectItem.cs` in same project as Pass 5's `SourceTable.cs`) to give the composition fix a fresh substrate.

### Pre-flight

- Branch `claude/live-test-notes-20260517` at `5c83324` (Pass 5 retest merge of `origin/main`), no commits ahead/behind `origin/main` aside from notes branch. `git fetch origin` confirmed no upstream movement.
- WinForms host UP: `MonitorBaseClaude.exe` PID 48724 (started 11:24:39 local).
- McpHubBridge: 4 instances running (2 per server is current architecture).
- Monitor MCP: all 4 status tools clean. `get_staging_guide` returns the post-Finding-13 "Choose The Staging Mode" + "Discovery Discipline" payload.
- Roslyn: `Schema Studio.sln` active with 6 projects, status `ready`. `get_diagnostics(severity=error)` returned `[]`.

### Roslyn-first discovery

- `search_symbols("HasAlias")` and `search_symbols("GetEffectiveName")` both `[]` (new symbols, as expected).
- `find_references("SchemaStudio.SemanticModel.Model.SelectItem")` returned **17 usages** — populated, not empty. This breaks the Findings 11/15/21 pattern (`find_references` empty on `_repository` / `SourceTable`). Not filed as a new finding; documented here as a counter-example. The bug is selective, not universal — possibly tied to type properties accessed inside method bodies (which `SelectItem` has plenty of via `query.SelectItems`) vs collection generic-args / parameter types (which `SourceTable` has many of).
- `get_source_map(SelectItem.cs, scope: file, mode: selector)`: 22 symbols, 5078 estimatedTokenProxy, parseStatus ok, 0 diagnostics.

### Finding 19 verified fixed

Source map signature for `Binding` property: `"... internal ColumnBinding Binding { get; set; } = new();"` — initializer present. Pass 5 reproduction is now closed; Codex fix landed between Pass 5 retest and Pass 6.

### Staging — three ops, one session, one file

Session `monitor-20260518164521-1419765fc0a340799`.

1. `submit_symbol(ToString)` — ternary rewrite of the 2-branch `ToString` body (verified behaviourally identical via `get_symbol` before staging). Record `20260518_114544188_..._39d72901`. Syntax + overlay clean, `overlayFileCount: 1`. `list_session_staged_records` returned count=1, op 1 `staged`. ✓
2. `add_property(HasAlias, afterSymbol: Alias)` — `=> !string.IsNullOrWhiteSpace(Alias)`. Record `20260518_114554893_..._e870a6d8`. `symbolsAdded.startLine: 14`. `list_session_staged_records` returned count=2, op 1 `superseded-by-later-same-file-candidate`, op 2 `staged`. ✓ Composition pairing visible from the new tool.
3. `add_method(GetEffectiveName, afterSymbol: ToString)` — `=> string.IsNullOrWhiteSpace(Alias) ? Expression : Alias`. Record `20260518_114615903_..._1f4616c8`. `symbolsAdded` listed BOTH `HasAlias` (line 14) AND `GetEffectiveName` (line 69) — server doing baseline-vs-staged delta against watched source and seeing both new members. `list_session_staged_records` returned count=3, ops 1+2 `superseded-by-later-same-file-candidate`, op 3 `staged`. ✓ Full Finding 17+18 verification.

### Composition verification (Finding 17)

Direct read of op 3's staged file (`...\20260518_114615903_..._1f4616c8.cs`) confirmed all three changes co-present:
- Line 14: `public bool HasAlias => !string.IsNullOrWhiteSpace(Alias);`
- Lines 64–65: ternary `ToString` rewrite
- Line 69: `public string GetEffectiveName() => string.IsNullOrWhiteSpace(Alias) ? Expression : Alias;`

### Two cosmetic issues spotted before review launch

- **Line 65 indentation**: ToString lambda continuation pasted at 4-space indent vs the surrounding 8-space class-member context. The whole declaration sits visually mis-aligned. Filed as **Finding 23**.
- **Lines 66–68 duplicated `// DISPLAY` banner**: `add_method(afterSymbol: ToString)` apparently cloned ToString's leading-trivia comment block ahead of the new method. Original banner before `ToString` still present (correct), but a second identical banner now sits before `GetEffectiveName`. Filed as **Finding 24**.

Both are C#-syntactically valid (overlay validation clean), so I disclosed them to Operator and launched the diff anyway.

### Review and decision

`launch_staged_diff(op 3)` returned `winmerge-launched` PID 35560. Operator accepted in WinMerge. `record_diff_decision(accepted)` returned:

- `classification: accepted` (exact byte match — NOT `accepted-normalized`)
- `decisionMatchesClassification: true`
- `currentHash == stagedHash == 0b389f79b989cfaf87f575dc52a474adb825ad8c7fe423672046b37da2c6a937`
- `originalHash: 6486ebf414ee...` (baseline differs from current — change actually landed)

Post-accept `get_diagnostics(severity=error)` returned `[]`. DBV2 still compiles clean.

### Bonus reproduction — Finding 20 expands

The decision-record `note` I sent contained an em-dash. The response echoed the note with `�` (U+FFFD) where the em-dash was. F20's encoding bug is on the MCP stdio reader, not on a specific tool argument — every string arg has the same exposure. Filed as **Finding 25** so F20 doesn't get marked fixed for `purpose` only.

### Finding 22 — telemetry FileShare race did NOT reproduce

The WinForms host (PID 48724) stayed up through the full staging + diff + accept cycle. No unhandled-exception dialog. F22's Operator resolution (strip the tail-reader entirely, c95c313) is verified end-to-end through the Claude Code MCP path.

### Pass 6 success scorecard

| Goal | Result |
|------|--------|
| F17 composition still works post-c95c313 | ✓ verified |
| F18 `list_session_staged_records` surfaces session records correctly | ✓ verified |
| F19 source-map signature includes property initializers | ✓ verified (`Binding` shows `= new();`) |
| F22 telemetry FileShare race gone | ✓ verified (no crash through full cycle) |
| Watched repo compiles clean post-accept | ✓ verified |

### New findings this pass

- Finding 23: `submit_symbol` doesn't re-indent multi-line member declarations.
- Finding 24: `add_method` / `add_property` clone `afterSymbol`'s leading-trivia comment block.
- Finding 25: F20 reproduces on `record_diff_decision.note` — same root cause, broader scope.

3 findings, well under the 5-per-pass cap.

### Watched repo state at end of pass

- `C:\Schema Studio - DBV2`: two modified files now — Pass 5 retest's `SourceTable.cs` plus this pass's `SelectItem.cs` carrying the three Pass 6 changes.
- Notes branch `claude/live-test-notes-20260517`: this STATUS update, the three Pass 6 findings in FINDINGS.md, and `Pass6_Plan_SelectItem.md` are the new changes since Pass 5 retest commit.

## Pass 7 — 2026-05-18 — Working-candidate composition mode landed on main; pre-flight + setup

### Role recap

- Re-read `origin/codexNotes:CODEX_NOTES/CLAUDE_TESTING_AGENT_PROMPT.md`. Role unchanged: tester/config-helper. Writable lane: `CLAUDE.md` plus `CLAUDE_Live_Tests/**/*.md`. Off-limits: product source, `Docs/Skills/**`, manifest, README, MCP_CLIENT_TESTING, AGENTS.
- Findings still capped at 5 per pass, 150 words each.

### Branch state

- Was on `claude/live-test-notes-20260517` at branch tip `b8811f1` (Pass 6 notes).
- `git fetch origin codexNotes main` — picked up three new commits on `origin/main` since the last merge base `c95c313`:
  - `ede0a47` "Organize Claude report lanes and archive stale docs" (new `CLAUDE_Live_Tests/README.md` lane spec; archived stale `Docs/`).
  - `694e076` "Block superseded staged candidates" (physical archive of superseded staged files under `Working/Staged/Superseded/<yyyyMMdd>/<recordId>/…`; `launch_staged_diff` and `record_diff_decision` reject `superseded-*` records).
  - `b0d071e` "Promote Working candidate composition flow" — the new single-file edit mode.
- Merged `origin/main` into `claude/live-test-notes-20260517` (`dbb3f4f`). Conflicts in `CLAUDE.md` and `CLAUDE_Live_Tests/README.md` resolved:
  - `CLAUDE.md`: kept my Agent-Role/Pre-flight/Coupled-Edits sections AND added main's Report-And-Memory-Lanes section. Added a new Working Candidate Composition Flow section documenting the V1 path.
  - `CLAUDE_Live_Tests/README.md`: took main's new date-stamped per-report lane spec. Legacy in-folder files (STATUS.md, FINDINGS.md, etc.) noted as preserved as-is.

### Build

- McpHubBridge and MonitorBaseClaude both not running at start — clean to rebuild.
- `Tools\Rebuild-MonitorMcp.ps1 -Config Debug -ProjectOnly` — succeeded in 6.25 s. Output `MonitorBaseClaude.McpServer.dll` at `bin\Debug\net10.0\`.
- `dotnet build MonitorBaseClaude.csproj --configuration Debug` (WinForms host) — succeeded in 2.78 s.
- WinForms host started, PID 22112, MainWindowTitle "MonitorBaseClaude MCP Client".

### MCP rebind — PRE-FLIGHT BLOCKER for this session

- Session-start system-reminder listed `monitor-base-claude` and `roslyn-codelens` as "still connecting — tools will appear shortly".
- After rebuild + host start, Operator started the MCP launchers (two `McpHubBridge.exe` PIDs 24800 + 27368 live).
- `ToolSearch` for `mcp__monitor-base-claude__*` and `mcp__roslyn-codelens__*` still returns "No matching deferred tools found". Keyword searches surface only unrelated tools.
- Conclusion: Claude Code's MCP client doesn't dynamically rebind to MCP servers that come up mid-session. The "tools will appear shortly" promise from the system reminder did not resolve even after the bridges came up.
- Tested flow validation **cannot proceed in this session**. Deferred to next Claude Code session (which should pick up the new tool surface at start time).
- Filing a separate restart-note: `20260518-claude-code-mid-session-mcp-rebind.md`.

### New flow summary — for next session

- `submit_file`, `add_symbol`, `add_field`, `add_method` now compose into `Working\<observedRootKey>\<relative path>`. Multiple ops accumulate; no staged record created until `stage_candidate_for_review`.
- Baseline rule: first op snapshots watched-source hash/length/timestamp. Later ops refuse with `candidate-baseline-stale` if watched source changed.
- Legacy escape hatches retained as `submit_file_old`, `add_symbol_old`, `add_field_old`, `add_method_old`.
- Not yet promoted to the candidate path: `add_property`, `add_constructor`, `add_nested_type`, `submit_symbol`, `remove_symbol`, `set_type_partial`, `add_using`, `remove_using` — still create staged records directly.
- Superseded staged records physically move to `Working\Staged\Superseded\…`; `launch_staged_diff` / `record_diff_decision` reject them with explicit messages.

### Next Claude pass — when MCP tools are bound at session start

1. Confirm `tools/list` exposes `submit_file`, `add_symbol`, `add_field`, `add_method`, `stage_candidate_for_review`, plus the `_old` variants.
2. Walk a single-file member edit through the Working-candidate path on a small DBV2 file: `submit_file` or `add_method`, verify `Working\<observedRootKey>\<path>` contains the candidate, no staged record yet, then `stage_candidate_for_review`, `launch_staged_diff`, `record_diff_decision`.
3. Walk a multi-op same-file edit through the Working candidate (e.g. `add_field` then `add_method` against one path) and confirm the second op composes against the first, not a fresh baseline.
4. Verify `candidate-baseline-stale` behavior by mutating the watched file out-of-band between two candidate ops.
5. Verify superseded behavior: stage a candidate, immediately stage a corrected candidate, confirm the first record's QueueStatus reads `superseded-by-later-same-file-candidate` and its staged file is moved under `Working\Staged\Superseded\…`. Try `launch_staged_diff` against the superseded id and confirm the `staged-record-superseded` error.

### Watched repo state at end of pass

- `C:\Schema Studio - DBV2`: still the two modified files from Pass 6 (`SourceTable.cs`, `SelectItem.cs`). No new staged edits this pass.
- Notes branch `claude/live-test-notes-20260517`: merge commit `dbb3f4f`, updated `CLAUDE.md` (new Working-candidate section), main-canonical `CLAUDE_Live_Tests/README.md`, this STATUS Pass 7 entry, and a new date-stamped restart-note.

## Pass 8 — 2026-05-18 ~20:17 UTC — Working-candidate composition flow end-to-end + supersession + recovery

### Pre-flight

- Notes branch `claude/live-test-notes-20260517` at `3c43336` (Pass 7 commit), 0 ahead / 19 behind only because origin/main was pulled to dbb3f4f; the 19-ahead count is the cumulative notes branch.
- WinForms host UP (PID 22112). 2 `McpHubBridge.exe` PIDs alive. Monitor MCP tool surface visible at session start — Finding 14 / Pass 7 rebind blocker did NOT recur; tools loaded cleanly.
- `get_workflow_status` returns `winMergePath: C:\Program Files\WinMerge\WinMergeU.exe`. `get_monitor_status`, `get_tool_manifest`, `get_staging_guide` all non-error. `get_staging_guide` exposes the post-Finding-13 "Choose The Staging Mode" + multi-file flow + Discovery Discipline sections.
- Roslyn `list_solutions` shows `Schema Studio.sln` active, status `ready`. `get_diagnostics(severity=error)` returned `[]`.
- Re-read `origin/codexNotes:CODEX_NOTES/CLAUDE_TESTING_AGENT_PROMPT.md`. Role unchanged.

### Target

`SchemaStudio.SematicModel\Model\ColumnBinding.cs` (355 bytes, 5 string auto-properties, 1 type-position consumer via `SelectItem.Binding`). Then `ExportMappers.cs` for multi-op + supersession test.

### Step 1 — V1 single-file walk (ColumnBinding)

Session `monitor-20260518201734-0fd9ef4e65cf4327a`.

- `add_method(ColumnBinding, GetQualifiedColumn => $"{Database}.{Schema}.{Table}.{Column}")` → `status: candidate-updated`, op count 1, baseline `58cb0ce`, candidate `aa172ed1`. Working candidate at `Working\<observedRootKey>\<rel>\ColumnBinding.cs` exists. State JSON at `Working\.state\Candidates\...\ColumnBinding.cs.candidate.json` records baseline + operation count.
- `list_session_staged_records` returned `count: 0` — V1 design verified: no staged record before `stage_candidate_for_review`.
- `stage_candidate_for_review(ColumnBinding)` → staged record `20260518_151823037_..._d947656a`, `symbolsAdded: [GetQualifiedColumn(method, line 15)]`, overlay + syntax clean.
- `launch_staged_diff` → WinMerge PID 21556. Operator accepted. `record_diff_decision(accepted)` → **`classification: accepted`** (exact byte match), `currentHash == stagedHash == aa172ed1`.

### Step 2/3 — Multi-op composition (ExportMappers, fresh session)

Session `monitor-20260518202213-8d14c1fe83594d74a`. **Note on workflow shape:** V1 ideal is N changes → 1 stage → 1 merge. I deliberately staged twice here to also exercise step 4's supersession; a normal 3-change V1 workflow would be one `stage_candidate_for_review` after all three ops.

- Op 1 `add_method(Probe)`, op 2 `add_field(ProbeTag)`, op 3 `add_method(ProbeTwo)` — op count 1→2→3, baseline preserved, candidate advances `f41c94a → 40e8221f → 8d598efe`.
- `stage_candidate_for_review` after op 2 → S1 `..._19146557` with `symbolsAdded: [Probe, ProbeTag]`.
- `stage_candidate_for_review` after op 3 → S2 `..._9237a36e` with `symbolsAdded: [Probe, ProbeTwo, ProbeTag]` — cumulative state.

### Step 4 — Supersession verified

- `list_session_staged_records` after S2: S1 `queueStatus: superseded-by-later-same-file-candidate`, S2 `queueStatus: staged`.
- S1 `stagedFilePath` rewritten to `Working\Staged\Superseded\20260518\<recordId>\...`; physical move confirmed via filesystem.
- `launch_staged_diff(S1)` → **`status: staged-record-superseded`** with clean recovery message pointing at `list_session_staged_records`. Documented refusal works.

### Step 5 — `candidate-baseline-stale` observed (post-accept transition)

Implicit observation: after step 1 accept landed in watched, the Working candidate state JSON for ColumnBinding was **not** cleared and still recorded `BaselineHash: 58cb0ce` (the pre-accept hash, now stale; watched moved to `aa172ed1`). Subsequent V1 ops on that path opaquely crashed (see Finding 26). The stale-baseline transition is real and detectable — the refusal path is just unimplemented.

Did NOT explicitly mutate watched out-of-band between two ops in a single candidate session, because the post-accept case already exercised the same baseline-mismatch path. If the structured refusal is implemented in a future build, an explicit external-mutation test should re-run.

### Step 6 — Recovery via remove_symbol (Operator accepted S2 in WinMerge unintentionally)

Operator accepted S2's probe code in WinMerge instead of rejecting. Watched `ExportMappers.cs` carried `Probe`, `ProbeTwo`, `ProbeTag`. Recovered via legacy direct-staged path:

- Session `monitor-20260518202753-51e42b71c07844bda`. Three sequential `remove_symbol` calls composed correctly on the legacy path (each reads the prior staged file). R3's `symbolsRemoved` lists all three.
- `launch_staged_diff(R3)` → Operator accept → `classification: accepted-normalized`, `currentHash: 76a5dd6...` (original pre-Pass-8 baseline). Exact recovery.
- **Workflow-cost asymmetry to flag:** 3 adds via V1 = 1 stage / 1 merge. 3 removes via legacy `remove_symbol` = 3 stages / 1 merge (R1+R2 auto-superseded, only R3 reviewed). Expected per CLAUDE.md transitional list, but worth seeing the cost in numbers. Once `remove_symbol` is V1-promoted, removes will collapse to 1 stage too.

### Findings filed this pass

- Finding 26: V1 `add_method`/`add_field` crash opaquely when candidate state JSON exists with a stale baseline (e.g. post-accept). No structured `candidate-baseline-stale` payload; bridge-level wire error. **Blocker** for any multi-pass workflow on the same path within a session.
- Finding 27: `record_diff_decision` on a superseded staged record crashes opaquely instead of returning the documented `staged-record-superseded` refusal. `launch_staged_diff` handles it correctly — sibling-tool inconsistency.

### Notable positives (no finding needed)

- V1 composition works as designed when state is clean: Working candidate accumulates, baseline preserved, op count increments, overlay validates the cumulative file every call.
- `serverDerivedMetadata.symbolsAdded` / `symbolsRemoved` correctly enumerate cumulative deltas on every staged record, including across composition.
- Legacy `remove_symbol` path inherits the post-`ce8e500` composition behavior.
- `launch_staged_diff` refusal on superseded records includes a clean human-readable recovery hint.
- WinForms host stayed up through the full pass; Finding 22 (telemetry FileShare race) did not reproduce.

### Watched repo state at end of pass

- `C:\Schema Studio - DBV2`: three modified files at pass end — `ColumnBinding.cs` (new `GetQualifiedColumn`), `SelectItem.cs` (Pass 6 leftovers), `SourceTable.cs` (Pass 5 leftovers). `ExportMappers.cs` returned to the original baseline via the remove_symbol recovery — no longer in `git status`. Several `.bak` files appeared in `SourceBakups/` (Host-generated pre-accept snapshots; not part of the commit).
- Notes branch `claude/live-test-notes-20260517`: this STATUS Pass 8 entry, two new FINDINGS entries.

### Next pass suggestion

1. After Finding 26 fix: explicit external-mutation `candidate-baseline-stale` test. Mutate watched between two V1 ops, expect a structured refusal payload, not a crash.
2. Test the V1 `submit_file` whole-file path against a new file (i.e. file creation through the candidate flow rather than member-level composition).
3. Test V1 composition across mixed tool kinds (`submit_file` baseline followed by `add_method` composition on the same path).
4. Test `start_monitor_session(purpose: "...")` with non-ASCII Unicode in `purpose` again to confirm F20/F25 status post any encoding fix.
5. Optionally: explore the Operator workflow for the Working candidate state JSON lifecycle — should it be cleared on accept? Cleared on session end? Stay until rebaselined? The expected behavior is undocumented and the current behavior is the crash-source for Finding 26.

## Token comparison Variants A/B/C — 2026-05-18 — closed for now

Three head-to-head measurements producing the same target file (`SchemaStudio.Data\SchemaObjectColumnRepositoryAsync.cs`):

| Variant | Mode | Outbound bytes | vs raw `Write` baseline |
|---|---|---|---|
| A | Monitor workflow, new file from scratch | ~14,360 actual / ~11,690 ideal | **1.59×–1.96× worse** |
| B | Raw `Read` + `Write` baseline, new file | ~8,760 / ~7,510 construction-only | 1× (baseline) |
| C | Monitor workflow, incremental adds + 1 bug+fix iteration on existing file | ~3,360 | **0.42× (C 2.4× cheaper)** at K=0; **0.14× at K=2** |

Conclusion: **iteration amortizes the new-file/complete-rewrite overhead** — yes, for now. The workflow's per-iteration marginal (~1 KB via `submit_symbol`) is ~7.6× smaller than raw `Write` (~8 KB whole-file re-emit). New files lose; existing-file iteration wins decisively. Crossover at K=0.53 iterations, so any non-trivial change favors the workflow.

Findings filed: 30 (CLAUDE.md stale on `submit_symbol` V1 promotion).

Details:
- A/B: [20260518-token-comparison-plan-schemaobjectcolumn-async.md](20260518-token-comparison-plan-schemaobjectcolumn-async.md)
- C: [20260518-variant-c-iteration-existing-file.md](20260518-variant-c-iteration-existing-file.md)
- Finding 30: [20260518-finding-30-submit-symbol-claude-md-stale-on-v1-promotion.md](20260518-finding-30-submit-symbol-claude-md-stale-on-v1-promotion.md)

Watched repo: `SchemaObjectColumnRepositoryAsync.cs` is the C end-state (8,057 bytes, sha `e197ee19...`, compiles clean).
