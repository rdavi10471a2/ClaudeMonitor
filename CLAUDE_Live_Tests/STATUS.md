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
