---
status: new
type: test-result
created: 2026-05-22
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---

## Summary

Post-rebuild review of the Codex updates that landed between 2026-05-21 and 2026-05-22 (commits `015fb3d` through `3d18e42`). The high-value items from `20260521-doc-suggestion-claude-md-skills-rework.md` shipped, plus several additions that exceeded the original suggestion. Build green; rules clear; closed-loop index refresh wired up. Hardened on the tested primitives. New tools and the cross-file summary-form claim need one live edit pass to fully validate.

## What Landed In CLAUDE.md

- **Design principle section at line 13–18** — explicit "reason in the cloud, edit locally" with both channels named (inbound = solution index + source maps + symbols; outbound = smallest safe composition tool).
- **`@Docs/Skills/SkillRouter.md` import at line 9** — always-live skill router; task-specific cards remain on-demand.
- **Solution index leads the read workflow** — step 2 of Required Edit Loop puts `get_solution_index_tree` / `query_solution_index` / `find_indexed_symbols` / `find_indexed_references` / `find_indexed_callers` / `find_indexed_relationships` ahead of source-map and body reads.
- **`refresh_file` cold-session entry** — step 5 wires `refresh_file` + chunked Read as the only path for large files; `get_file` reserved for small files.
- **Warm-session no-re-read rule** — "If the file is already in context in the current session, do not re-read it; call the narrow edit tool directly with `expectedOldText` or a hash guard from that in-context text."
- **Razor section updated** — `replace_text_in_file` / `replace_span_in_file` preferred; full-file only as fallback for new files or broad rewrites.
- **IndexRefresh status visible** — `record_diff_decision` returns refresh status; single-file accepts refresh immediately, multi-file sessions defer until the chain completes.

## New Tools Beyond The Suggestion

- **`replace_text_in_file`** — exceeds the original `find_text_span` proposal. Server finds + replaces in one call with `expectedMatches: 1` uniqueness guard. No line/column counting needed.
- **`find_text_span`** — dry-run variant that returns 1-based coordinates for `replace_span_in_file` when bounds are needed for diagnostics.
- **`find_indexed_relationships`** — first-class structural surface for `partial_declaration`, `inherits_from`, `derived_type`, `overrides`, `overridden_by`, `implements_interface_member`, `implemented_by`, with `incoming`/`outgoing`/`both` direction filter.
- **`IndexRefresh` on `record_diff_decision`** — keeps the inbound channel current automatically. Without this, summary-form queries would drift after every accept.

## Findings Closed In Code/Docs

- **54** — `GetOffsetFromLineColumn` column parameter naming improved.
- **55** — Redundant `EnsureCandidateBaselineIsCurrent` call removed.
- **56** — `refresh_file` as cold-session entry now in CLAUDE.md; large-file threshold suggestion pending (see `20260522-doc-suggestion-large-file-32kb-threshold.md`).
- **57** — `refresh_file` now "records refresh state, and clears any existing candidate state for that file" (manifest line 253), eliminating the stale `.candidate.json` workflow hazard.

## Index Tech Stack

The solution index is Roslyn-native:

- `Microsoft.CodeAnalysis.CSharp` v4.14.0 (Roslyn compiler API)
- `Microsoft.CodeAnalysis.CSharp.Workspaces` v4.14.0 (Workspaces layer)
- `Microsoft.Data.Sqlite` v10.0.0 (persistence)
- `ModelContextProtocol` v1.3.0 (transport)

The index builds via raw `CSharpCompilation.Create(...)` (not `MSBuildWorkspace`) with `SemanticModel.GetSymbolInfo(...)`. These are the same APIs the official C# compiler uses, which is why index API claims can be validated against Roslyn's own test patterns. Per Operator: a new single-file MCP-claim test set tests each index claim against the real tools' own tests, and they all pass.

This changes the earlier "summary-form inbound channel untested" framing: the underlying index data is Roslyn-equivalent. What still needs a live pass is the cloud-side workflow — model reasoning from summary-form context to a correct cross-file edit, end-to-end.

## Closed-Loop Architecture

The full loop is now sustainable without manual maintenance:

1. **Index built from Roslyn semantic model**, persisted to SQLite.
2. **Inbound queries** (`find_indexed_*`, `query_solution_index`, `get_source_map`) read from that current index.
3. **Outbound edits** use smallest safe primitive (`replace_text_in_file` / `replace_span_in_file` / symbol-level tools / `submit_file` only when warranted).
4. **Vote-plus-hash gate** classifies the WinMerge outcome.
5. **`record_diff_decision` triggers `IndexRefresh`** on accept — the index is rebuilt with the new state before the next inbound query.

Single-component failure modes are all caught: stale candidate (`refresh_file` clears it), wrong span (`expectedOldText` / `expectedMatches: 1` rejects it), wrong-file accept (vote-plus-hash detects `dirty-unexpected`), drifted index (`record_diff_decision` rebuilds it).

## Razor Markup — Acknowledged Gap

Razor markup edits remain text-based (`replace_text_in_file`). No Roslyn-equivalent semantic surface exists for the markup portion; the Razor SDK parses and compiles but doesn't expose clean symbol-level edit primitives the way C# does. `expectedMatches: 1` plus Razor-SDK overlay compile is the safety floor. This is an unavoidable workaround until Razor SDK exposes symbols (or until someone wires the Razor SDK syntax tree into the index the way C# is wired in now).

## Pending Doc-Suggestion: 32KB Large-File Threshold

Filed on branch `claude-notes/20260522-doc-suggestion-32kb-threshold`. CLAUDE.md previously said "do not call `get_file` for large files" without a number. Suggested 32KB based on empirical pass/fail evidence from the 2026-05-21 session (~36KB ManageViews.razor passed inline; ~86KB BaseViewCreator.razor returned API 500). Sits below known-good boundary with margin for JSON envelope overhead.

## Status

- **Hardened on what's been tested**: span/text edit primitives, refresh workflow, stale-candidate clearing, vote-plus-hash classification, build green, 0 warnings, 0 errors.
- **Backed by passing tests**: index API contract (per the new single-file MCP-claim test set using Roslyn's own test patterns).
- **Needs a live edit pass**: `replace_text_in_file`, `find_indexed_relationships`, `IndexRefresh`-on-accept, summary-form context for a real cross-file edit.

## Next Test

When ready, exercise the new tools on a real edit:

1. Pick a small cross-file C# change (working file + 2–3 referenced types in other files).
2. Use only `query_solution_index` / `find_indexed_symbols` / `find_indexed_relationships` for dependency surfaces — do not load dependency file bodies.
3. Use `replace_text_in_file` or `submit_symbol` for the edits.
4. Verify `IndexRefresh` status after accept.
5. Re-query the index to confirm the new state is visible without manual refresh.

If that pass succeeds, the summary-form inbound claim is no longer design intent.
