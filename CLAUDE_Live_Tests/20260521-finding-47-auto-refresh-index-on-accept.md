---
status: new
type: finding
created: 2026-05-21
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---

## Summary

`record_diff_decision` should auto-trigger `refresh_solution_index_file` for the path it just classified as `accepted` (or `accepted-normalized`). The Monitor server already knows the watched path, the new watched hash, and that the file just mutated. One internal refresh call after the vote-plus-hash classification keeps the index hot continuously, eliminating the brief stale window between accept and the next deliberate index refresh.

This becomes especially clean given that **every watched-source mutation in this workflow flows through staging.** The operator does not hand-edit watched source ("I don't edit files manually, everything goes through AI"). So the staging system has perfect knowledge of "which files just changed" and exactly when. There is no "human edit underneath" scenario to defend against; the index can stay synchronously hot.

## Repro / context

Session `monitor-20260521130506-02a9a5e5b0aa41a1a`, branch `claude-notes/20260521-pass21`, current HEAD `ee5f409`.

Pass 21 demonstrated the current behavior. The forward accept of the `_ = parm;` ctor body change landed at `record_diff_decision(accepted)` with `currentHash: ac6ebb93…`. After that:

- Watched source: `ac6ebb93…`
- Index `find_indexed_symbols("McpAddApiFixture")` would have continued returning `fileHash: 6592eb30…` (the pre-edit hash) until a manual `refresh_solution_index*` call.
- The cleanup-revert cycle that followed *did* trigger a manual `submit_symbol` against the post-accept baseline, which incidentally pulled the live watched bytes back into the Working mirror — but the SQLite index was not touched.

The mismatch is caught by the freshness-hop on the *next* mutation attempt (the agent's next `submit_symbol` records the live baseline hash, which would not match the index's reported `fileHash`), so this is not a correctness bug. It is an unnecessary stale-window.

## Expected

`record_diff_decision` with `classification` in `{accepted, accepted-normalized}` should:

1. Compute the new watched hash (already done — `currentHash` in the response).
2. Call `refresh_solution_index_file(path)` internally before returning.
3. Include the updated index status in the response (optional but useful: `indexFileHashAfterRefresh`, `indexFreshAsOfUtc`).

For `classification` in `{rejected}` the index does not need a refresh — watched source did not change. For `{dirty-unexpected, blocked-overlay-validation}` the index should not be refreshed either, but the staged record's queue state should be enough signal.

## Actual

`record_diff_decision` does not currently touch the index. The agent (or operator) must remember to call `refresh_solution_index_file` themselves, or accept that the next index query will return stale data until the next full `refresh_solution_index` runs (e.g., from a build hook).

## Why this is safe now (post-architecture clarification)

I had previously assumed the freshness-hop needed to be the load-bearing defense because random watched-source edits might occur outside the staging flow (e.g., human edits, git pulls, IDE refactors). The operator clarified on 2026-05-21 that **all watched-source mutations go through AI staging** — they do not hand-edit. That removes the "concurrent edit" failure mode from the threat model. The remaining "external" mutation paths are:

- `git pull` / branch switch — should be paired with a full `refresh_solution_index` anyway.
- External tools (one-off scripts, designer-regen) — rare; freshness-hop catches them at next staging attempt.
- Build artifacts changing files — out of scope (Working/Staged/Indexes are monitor-owned).

So the index can be kept continuously hot via the staging accept hook. Freshness-hop drops from "primary safety mechanism" to "defense-in-depth for the edge cases above."

## Minimal fix

In the `record_diff_decision` server handler, after vote-plus-hash classification produces `accepted` or `accepted-normalized`:

```pseudocode
if classification in {accepted, accepted-normalized}:
    indexResult = refresh_solution_index_file(stagedRecord.SourceFilePath)
    response.indexRefreshed = true
    response.indexFileHashAfterRefresh = indexResult.fileHash
    response.indexFreshAsOfUtc = indexResult.lastIndexedAtUtc
```

If `refresh_solution_index_file` is expensive enough that the operator notices the latency on accept, run it in a background task and report `indexRefreshScheduled = true` instead.

## Severity

**suggestion** — the index is correct, just slower-to-update than necessary. The freshness-hop catches the stale-window today. The fix is a small mechanical improvement that takes advantage of information the server already has.

## Notes

- This finding pairs with the architectural direction in `20260521-test-result-index-vs-sourcemap-winforms.md`: get Roslyn symbol info into the DB and let `get_source_map` be the agent-facing transform. Combined with auto-refresh on accept, the index stays hot continuously for the source-map-transform layer to read from.
- Compatible with a future build hook (`refresh_solution_index` on post-build event). The build hook handles bulk changes (branch switches, multi-file refactors that didn't go through staging); the accept hook handles the routine single-file mutation that *does* go through staging.
- The agent-side CLAUDE-memory entry `project_solution_index_tools.md` documents the freshness-hop rule today. After this finding ships, that memory should be updated to "freshness-hop is defense-in-depth; index is auto-refreshed on accept" rather than implying the agent must hop on every query.
