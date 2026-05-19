---
status: new
type: review
created: 2026-05-19
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---

## Summary

Pass 19 (post second-recycle) is wrapped. Findings 35, 36, 37 verified fixed end-to-end against fresh 11:44 binaries; full evidence at [20260519-pass19-test-result-finding-35-36-37-verified.md](20260519-pass19-test-result-finding-35-36-37-verified.md). This note adds the architectural alignment that came out of the post-pass conversation with the Operator and records two operating-rule updates that survived into CLAUDE.md.

## Findings outcome

| Finding | Subject | Outcome | Resolution commit |
| --- | --- | --- | --- |
| 35 (Force branch) | Host `Force WinMerge Review` button silently inert (returned `cancel_for_fix`) | Fixed — `validationGateDecision: force_review`, WinMerge launches as designed | `0102077` |
| 35 (Cancel branch) | Cancel branch reconfirmed against fresh binary | Working — `validationGateDecision: cancel_for_fix`, no WinMerge, queue `blocked-overlay-validation` | `0102077` |
| 36 | `remove_symbol` produced empty `symbolsRemoved` on single-ctor remove | Fixed — `symbolsRemoved: [ctor]` with matching textHash | `0102077` |
| 37 | `add_constructor` produced empty `symbolsAdded` | Fixed — `symbolsAdded: [ctor]` populated | `0102077` |

Watched source returned to pre-Pass-19 sha `e9322d86…` via accept-the-remove round trip. No leftover edits in tracked source.

## Architectural alignment (Operator-confirmed)

The earlier passes treated the workflow as a discovery surface plus a staging surface, with safety arriving at the WinMerge review step. The post-pass conversation clarified that this is a **layered defense**, not a single-pane gate:

1. **Roslyn discovery (first pass).** `get_source_map` for breadth, `get_symbol` / `get_file` for depth, `search_symbols` / `find_references` / `find_callers` for impact. Unlimited read scope across the watched solution — including files I am not editing.
2. **Overlay compile validation (the explicit catch for incomplete scope).** Not redundant with Roslyn. Specifically guards against the failure mode where an agent (Roslyn-fallibility on the Claude side, or no-Roslyn-at-all on the Codex side via plugin-only scope) ships a candidate that compiles in isolation but breaks the watched build because a consumer site was never staged.
3. **Vote-plus-hash classification plus WinMerge (the catch for operator/byte mismatch).** All-or-none; no partial-hunk merges. Operator-report and watched-file hash must agree, else `dirty-unexpected`.

Each layer assumes the one above can be fallible. That makes the safety story sound for valuable real codebases, not just POCs: the byte-for-byte neighbor-preservation invariant of the splice primitives plus the layered catches plus the audit trail (sessions, staged records, decision records, ledgers) is a stronger guarantee than typical "AI agent commits to git with code review."

## Operating-rule updates landing in CLAUDE.md this push

1. **Roslyn-suspect compile failure fallback.** When overlay compile reports an error that the Roslyn picture does not explain, drop to direct filesystem tools (Read / Grep / Glob) to investigate the watched tree. This is the explicit diagnostic escape hatch for "overlay caught a coupling Roslyn missed." Operator-granted; not a general Roslyn-first bypass for routine discovery. Re-stage with the missed file added to the same `sessionId` before re-launching review. Captured under [Monitor MCP vs CodeLens MCP](../CLAUDE.md#monitor-mcp-vs-codelens-mcp).

2. **(In Claude-only memory, not CLAUDE.md)** Size workflow-readiness confidence to the primitives + read surface, not to the narrow paths exercised in a given pass. Multi-file coupled work is N staging primitives under one `sessionId` before the first review launch — same mechanism, not a missing capability. Reserve genuine concern for surface that isn't there yet (e.g. Razor-aware splicing).

## Production-readiness opinion

Asked directly. Restated cleanly here:

- **Tooling is production-shaped, not POC-shaped.** Splice primitives preserve neighbors byte-for-byte; vote-plus-hash detects accept/byte mismatch silently happening; all-or-none gate prevents partial merges; overlay compile catches forgotten consumers; full audit trail makes "what did the AI actually do" answerable.
- **Day-one playbook against the upcoming safe codebase**: re-fetch the project's `CLAUDE_TESTING_AGENT_PROMPT.md` and project `md`/agent files, run pre-flight (3 binaries / host PID / MCP surfaces / Roslyn), then take real feature/fix tasks. Learn the project's domain conventions from its docs the same way I do here.
- **Genuine gaps** (not blockers, but the honest list):
  - Razor / `.cshtml` files have no AST splice — whole-file submit only, neighbor-preservation reverts to WinMerge eyeballs.
  - Multi-file coupled rename against a real production-shape codebase hasn't been exercised under domain pressure (it's exercised under monitor-test-fixture pressure). The primitives are the same; the discovery completeness is the unknown.
  - Working-candidate persistence across sessions (covered separately in the test-result note as testing ergonomics — by-design, not a bug).

## Open items / suggestions for Codex

These are suggestions, not blockers:

1. Documentation: add a line to `get_staging_guide` clarifying that a Working candidate persists across sessions whenever the watched-source baseline hash is unchanged ("hash match wins"), and that a fresh candidate is opted into by deleting the Working mirror + state JSON or by `submit_file` overwrite.
2. Returned flag: surface `candidateInheritedFromPriorSession: true` on the first staging op of a session that finds an existing candidate, so test agents can detect inheritance without computing it from `operationCount`.
3. Razor: schedule the Razor-aware splice path. It is the largest remaining surface where "AI silently destroys neighbors" is mitigated only by WinMerge eyeballs.

## Evidence index

- Detailed Pass 19 evidence: [20260519-pass19-test-result-finding-35-36-37-verified.md](20260519-pass19-test-result-finding-35-36-37-verified.md).
- Finding 35 original repro: [20260519-test-result-finding-35-reproduced-force-button.md](20260519-test-result-finding-35-reproduced-force-button.md).
- Finding 36 original write-up: [20260519-finding-36-symbolsremoved-empty-on-single-remove.md](20260519-finding-36-symbolsremoved-empty-on-single-remove.md).
- Finding 37 original write-up: [20260519-finding-37-add-stage-symbolsadded-sometimes-empty.md](20260519-finding-37-add-stage-symbolsadded-sometimes-empty.md).
- Resolution commit: `0102077` ("Fix force review and constructor metadata") — merged into `claude/live-test-notes-20260517` via `10c3853`.
