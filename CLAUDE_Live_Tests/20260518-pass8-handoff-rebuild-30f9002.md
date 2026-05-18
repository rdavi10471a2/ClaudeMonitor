# 2026-05-18 — Pass 8 hand-off + rebuild after `30f9002`

**Status:** ready for new Claude Code chat. WF host DOWN, McpHubBridge DOWN, new MCP server dll built and on disk.

## Branch state

- `claude/live-test-notes-20260517` HEAD = `8db8edd` (merge of `origin/main` into notes branch, contains `30f9002`).
- Unpushed locally: `8db8edd` (the merge commit). Pass 8 notes commits `2a31fb1` and `b686382` are already on origin.
- `origin/main` tip = `30f9002 Promote candidate workflow tools` (committed 2026-05-18T21:02:15Z).
- Watched repo `C:\Schema Studio - DBV2`: unchanged from Pass 8 end — modified `ColumnBinding.cs` (`GetQualifiedColumn`), `SelectItem.cs`, `SourceTable.cs`; `ExportMappers.cs` restored to baseline; several `.bak` files in `SourceBakups/`.

## What `30f9002` changed

Files: `CLAUDE_Live_Tests/README.md`, `MONITOR_MCP_TOOL_MANIFEST.md`, `MonitorWorkflowService.cs` (+193), `Program.cs` (+132).

V1 surface expansion — promoted from legacy direct-staged to Working-candidate composition:

- `submit_symbol`
- `add_property`, `add_constructor`, `add_nested_type`
- `set_type_partial`
- `remove_symbol`
- `add_using`, `remove_using`

Every promoted tool gets a `_old` legacy escape hatch (`submit_symbol_old`, `add_property_old`, etc.). `launch_staged_diff` and `record_diff_decision` now take `stagedRecordId` only from `stage_candidate_for_review` or `_old` immediate-staging tools.

**Finding 26 fix landed in the same commit.** `MonitorWorkflowService.cs` now contains a helper that calls `File.Delete(statePath)` — Option A from the finding (clear the candidate state JSON when accept lands). Pass 9 should retest this explicitly.

**New lane rule in `CLAUDE_Live_Tests/README.md`:** legacy rollups (`STATUS.md`, `FINDINGS.md`, `SCRATCH.md`, `SESSION_RESUME.md`) are now historical. New findings go into dated files like this one. There is also a "Required Cleanup Pass" section at the start of a reporting session — inspect local scratch/restart notes, publish only useful current items into dated reports, archive or remove stale scratch copies.

## Rebuild

- `Tools\Rebuild-MonitorMcp.ps1 -Config Debug -ProjectOnly` succeeded in ~5 s.
- New dll: `C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.McpServer\bin\Debug\net10.0\MonitorBaseClaude.McpServer.dll`, **370176 bytes**, written 2026-05-18 16:11:45 local. (Pre-fix Pass 7 dll was 329728 bytes; ~40 KB growth reflects the V1 promotion of 6+ tools.)
- Both processes were DOWN before the build — no file-lock concern.

## Operator-side restart sequence

1. `MCP: Restart Server` in VS Code command palette for `monitor-base-claude` (the server should already be registered; just restart).
2. Restart the WF host so `launch_staged_diff` has a review surface.
3. Open a new Claude Code chat in this VS Code window. Mid-session tool-surface rebind is the Pass 5 hand-off / Finding 14 pattern — a new chat is what picks up the expanded V1 surface.

## Pass 9 plan suggestion (for the new chat)

**Pre-flight (per CLAUDE.md):** standard sequence + confirm `tools/list` exposes the newly promoted V1 surface and the corresponding `_old` legacy variants.

**Primary goal — V1 ideal demonstration on the expanded surface:**

Pick a small unmodified DBV2 file (e.g. `SchemaStudio.SematicModel\Model\ColumnBinding.cs` is already touched; try `ParsedQuery.cs` or another `Model\` leaf). One session, multiple ops, **one** `stage_candidate_for_review`, **one** WinMerge accept:

1. `add_property(HasValue => …)` ← post-promotion V1
2. `add_using("System.Linq")` ← post-promotion V1
3. `submit_symbol(...)` ← post-promotion V1 (rewrite an existing member body)
4. `remove_symbol(...)` ← post-promotion V1 (remove a member; mix add + remove in one candidate)
5. `set_type_partial(...)` ← post-promotion V1 (optional, only if a partial-split path is needed)

Verify all six ops compose into ONE Working candidate before `stage_candidate_for_review`. Verify `list_session_staged_records` returns count=0 before the snapshot, count=1 after.

**Secondary goal — Finding 26 retest:**

After accept on the above candidate, call `add_method` again on the same path in the same session. Expect SUCCESS (state JSON deleted by the new helper; candidate cycle starts fresh against the new watched baseline). If it still crashes opaquely, the fix isn't wired into the accept path — file as a Pass 9 finding.

**Tertiary goal — Finding 27 retest:**

`record_diff_decision` on a superseded staged record should now return the same structured `staged-record-superseded` refusal that `launch_staged_diff` returns. If still opaque, file follow-up.

## Open items to flag for next chat

- My Pass 8 notes landed in the legacy rollups (`STATUS.md`, `FINDINGS.md`) because they were the canonical lane when I started. The new rule (`30f9002` README addition) makes those historical. Pass 9 onward: dated files only. This hand-off file is the first one.
- The `Tools\Rebuild-MonitorMcp.ps1` script's `McpHubBridge.exe` running-check is still inverted (Pass 5 retest STATUS notes). Not a blocker; flagged for Codex.
- F20/F25 (non-ASCII string args mangled to U+FFFD) — no fix evidence in `30f9002`. Pass 9 could retest as a side observation.
