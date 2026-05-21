# Index API Safety Findings (May 21, 2026)

## Scope

This review evaluates index-first read flow safety for MonitorBaseClaude using the current `get_source_map` selector artifacts and compact corpus indexes.

Questions reviewed:

1. Does the new index API preserve edit-target safety?
2. Are file hashes and stale checks sufficient before Claude uses indexed stable keys?
3. Should `get_solution_index` include file hash next to every symbol?
4. Are the SQLite queries bounded enough for large solutions?
5. What doc/skill changes are needed for index-first workflow?

## Findings

### 1. Edit-target safety is preserved if index-first is treated as discovery, not authority

Current contracts already frame selector/index data as discovery and require symbol reads plus staging-time baseline checks before mutation:

- `get_source_map` selector mode returns stable keys and symbol text hashes for target selection, and explicitly stays read-only.
- `get_symbol` is the body fetch gate before mutation.
- Candidate composition rejects on `candidate-baseline-stale` when watched source changed after Working baseline capture.
- Final classification remains vote-plus-hash in `record_diff_decision`.

Conclusion: the index-first workflow is safe when it keeps the existing narrowing chain:

```text
get_source_map/navigation -> get_source_map/selector or compact index -> get_symbol -> submit/stage -> record_diff_decision
```

### 2. File hashes and stale checks are mostly sufficient, with one practical guardrail to add in docs

The implementation already has layered protections:

- selector responses include file hash and symbol hash for identity/context.
- candidate baseline stale checks block staging from outdated watched baselines.
- session hash checks (`check_file_hash`) exist for pre-edit drift detection.

The missing piece is behavioral clarity for index-first clients: when a symbol selector is sourced from a cached index, clients should perform one freshness hop before editing:

- call file-scope `get_source_map(mode: "selector")`, or `check_file_hash`, for that file in the current session;
- then call `get_symbol` and continue.

This is primarily a workflow-doc enforcement item, not a new safety primitive.

### 3. `get_solution_index` should include file hash next to each symbol entry

Recommendation: yes.

Reasoning:

- Symbol keys are lexical and stable across many edits, so stale index rows can still resolve to a no-longer-intended body shape.
- Per-symbol co-located file hash gives a cheap, explicit freshness join key for clients and for future SQLite filtering.
- It avoids forcing clients to separately look up file-level metadata tables during first-pass pruning.

Suggested contract shape for each symbol row:

- `stableSymbolKey`
- `relativePath`
- `fileHash`
- `symbolTextHash`
- existing selector fields (`memberKind`, containing type/namespace, params/arity)

### 4. SQLite boundedness for large solutions is acceptable if queries are shaped and paged

For large-solution safety, require:

- explicit `LIMIT` and deterministic `ORDER BY` for all list endpoints.
- scoped predicates first (`solutionRoot`, project/folder/file, optional namespace).
- projection-only reads, with no large text columns in index endpoints.
- pagination cursor or `(offset, limit)` with a hard ceiling.
- max-result envelope in API response metadata.

Recommendation for default bounds:

- default page size: 100-250 symbols.
- hard max page size: 500.
- require narrowing when exceeded.

### 5. Doc/skill updates needed for index-first workflow

Required updates:

1. Add explicit freshness rule in workflow docs:
   - cached index selectors are advisory.
   - refresh file selector map or run `check_file_hash` before any submit/stage call.
2. Add index-first call chain examples to test script/docs:
   - `get_solution_index`/compact index -> file selector refresh -> `get_symbol`.
3. Add stale-index failure/recovery guidance:
   - if symbol not found/ambiguous or hash drift, refresh selector and rebuild selector JSON.
4. Add SQL boundedness checklist to planned-work docs for future SQLite rollout.

## Recommended Acceptance Criteria For Index-First Readiness

- Every index symbol row includes `fileHash` and `symbolTextHash`.
- Editing path requires a same-session freshness hop (`get_source_map` selector or `check_file_hash`) before body read and mutation.
- `get_symbol` remains required before `submit_symbol`/`remove_symbol`.
- All index list queries are bounded, ordered, and paginated with hard caps.
- MCP client playbooks include stale-index recovery path.

## Inline-Review Follow-Ups Applied

- Clarified that index-first safety depends on a live file freshness hop immediately before edit composition, not only at diff-decision time.
- Added explicit recommendation that index rows expose both `fileHash` and `symbolTextHash` so clients can detect drift before `get_symbol`.
- Added concrete SQLite endpoint constraints (`LIMIT`, deterministic `ORDER BY`, scope predicate, and hard page cap) so future `get_solution_index` behavior is testable.
