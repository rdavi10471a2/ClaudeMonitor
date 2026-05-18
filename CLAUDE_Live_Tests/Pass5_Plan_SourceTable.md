# Pass 5 Plan — Symbol-Level Staging on SourceTable.cs

Drafted 2026-05-18 in the diagnostic chat. Pick up here from a fresh Claude Code chat that has the MCP tools surfaced.

## Why this target

- File: `C:\Schema Studio - DBV2\SchemaStudio.SematicModel\Model\SourceTable.cs` (45 lines).
- Pure leaf model — namespace `SchemaStudio.SemanticModel.Model`, two POCO types (`JoinKey`, `SourceTable`) plus one enum (`JoinCardinality`).
- `SourceTable.ToString()` body is a pure debug-format method that just composes `Database/Schema/Table/Alias` — behavioural risk of editing it is near zero.
- `SchemaStudio.SematicModel` is the unexplored prime candidate flagged in Pass 4 (27 files, never touched by the Monitor workflow). First Monitor-mediated edit into this project.
- Project name has a typo (`SematicModel` vs `Semantic`) — that's the watched filename. Namespace in the file is the correct `SemanticModel`. Don't "fix" the typo as a side effect.

## Scope of the test

Three staging operations in ONE monitor session against this one file:

1. `submit_symbol` — replace `ToString()` body with an equivalent expression-bodied / `string.Join`-based variant. Same observable output, exercise the replace path.
2. `add_method` — add a new `string GetQualifiedName()` returning the same `$"{Database}.{Schema}.{Table}"` string without the alias suffix. Read-only helper, no consumers.
3. `add_property` — add a read-only computed `bool HasJoin => JoinKeys != null && JoinKeys.Count > 0`.

All three under the same `sessionId`. Overlay validation must compile the union of those three staged shapes into one syntactic file and report zero diagnostics. This is the primary post-`38c3db2` behaviour to validate.

WinMerge review is one diff (one file, three changes). Operator accept-all-or-none.

## Pre-flight checklist (must pass before staging)

- `mcp__monitor-base-claude__*` tools present in deferred-tool list at chat start.
- `get_workflow_status` returns non-error, reports WinMerge resolution.
- `get_monitor_status`, `get_tool_manifest`, `get_staging_guide` all return non-error.
- Roslyn `list_solutions` shows `Schema Studio.sln` active, `get_diagnostics(severity=error)` returns `[]`.
- WinForms host (`MonitorBaseClaude.exe`) running — needed for `launch_staged_diff`.

## Roslyn-first discovery before staging

For each target symbol, confirm zero consumers across the solution before staging — Finding 15 lesson, do not trust a single empty `find_references` result:

- `search_symbols` for `ToString` filtered to `SourceTable` type → expect one definition, zero external calls (it's an `override`, so it's reached via virtual dispatch — confirm no `SourceTable.ToString()` invocations text-wise too, but `ToString()` rewrites are inherently safe to vary).
- `search_symbols` for `GetQualifiedName` → expect zero matches (new symbol).
- `search_symbols` for `HasJoin` → expect zero matches (new symbol).
- `find_references` on the `SourceTable` type → just confirm we know who consumes the class itself (informational; the test doesn't change the type shape externally).

## Staging sequence

1. `start_monitor_session` → record returned `sessionId`.
2. `get_source_map(path, scope: file, mode: selector)` for stable selector keys.
3. `submit_symbol(sessionId, stableSymbolKey for ToString, newBody)`.
4. `add_method(sessionId, target type=SourceTable, methodSignature for GetQualifiedName, body)`.
5. `add_property(sessionId, target type=SourceTable, propertyDecl for HasJoin)`.
6. Confirm overlay compile validation returns zero diagnostics across all three staged operations.
7. `launch_staged_diff` for the file — WinMerge opens with all three changes visible in one diff.
8. Operator accepts-all in WinMerge.
9. `record_diff_decision(stagedRecordId, accepted)` → expect `accepted` or `accepted-normalized` classification, vote-plus-hash agreement.

## What "success" looks like

- Overlay validation across three symbol-staged operations on the same file reports zero diagnostics (validates the `38c3db2` fix end-to-end).
- WinMerge diff displays the union of three changes as one reviewable file delta, not three separate ones.
- `record_diff_decision` classifies cleanly (`accepted` / `accepted-normalized` / `rejected`), no `dirty-unexpected`.
- Watched repo still compiles clean after acceptance (`get_diagnostics(severity=error)` returns `[]`).
- No collateral damage to neighbouring symbols in `SourceTable.cs` (the byte-for-byte preservation property described in CLAUDE.md "Reason In Cloud, Compose Locally").

## What to file as findings if it goes sideways

- Overlay reports diagnostics for one staged op in isolation but not the union (or vice versa) — file as confusing/blocker.
- `add_property` adds the property but Roslyn diagnostics flag duplicate member, file as blocker.
- WinMerge shows three separate diffs instead of one merged file diff — file as confusing.
- `record_diff_decision` returns `dirty-unexpected` after a clean Operator accept — file as blocker.

Stay under the 5-finding cap.

## Cleanup

- If accepted, leave the changes in place. They're additive and benign; `ToString()` is functionally identical.
- If rejected, baseline is restored automatically by the all-or-none workflow.
