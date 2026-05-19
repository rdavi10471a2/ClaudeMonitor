---
status: fixed
type: finding
created: 2026-05-19
processed: true
processedBy: Codex
processedAt: 2026-05-19
resolution: Verified fixed in Pass 19 against fresh binaries (11:44). add_constructor → stage_candidate_for_review now reports symbolsAdded=[ctor]. See 20260519-pass19-test-result-finding-35-36-37-verified.md.
resolutionCommit: 0102077
---

## Title

`stage_candidate_for_review.serverDerivedMetadata.symbolsAdded` returns an empty array after an `add_constructor` op on an existing file, even though the candidate clearly adds the ctor

## Severity

confusing

## File/tool

`monitor-base-claude` MCP — `stage_candidate_for_review`'s `serverDerivedMetadata.symbolsAdded`. Pairs with sibling finding 36 (`symbolsRemoved` empty on single ctor remove).

## Observed

Session `monitor-20260519151325-d279e472530b41bc9` called `add_constructor` against `McpAddApiFixture.cs` to insert `public McpAddApiFixture(string parm)`. The op returned `status: candidate-updated`, `operationCount: 5` (Working candidate had been accumulating across sessions), overlay clean. `stage_candidate_for_review` returned `originalHash: e9322d86…` (1-ctor watched file) and `stagedHash: 0d743655…` (2-ctor candidate) — a real diff. But `serverDerivedMetadata.symbolsAdded: []` and `symbolsRemoved: []` were both empty.

Contrast: a `submit_file`-based new-file candidate (`McpOverlayGateFixture.cs`) in the same session correctly populated `symbolsAdded` with class + method entries. So the empty-metadata code path is specific to per-symbol composition on an existing file when the resulting diff is small.

## Expected

`symbolsAdded` should enumerate the added constructor entry, mirroring the new-file `submit_file` path that already works.

## Minimal fix

Likely the diff routine that emits `serverDerivedMetadata` compares against the candidate's prior staged-or-working state rather than the watched source baseline when `operationCount > 1` on the same path. Confirm and align on baseline = watched source for both add and remove sides.

## Evidence

- `add_constructor` response — `operationCount: 5`, `baselineHash: e9322d86…`, `candidateHash: 0d743655…`, no errors.
- `stage_candidate_for_review` response — record id `20260519_101458050_stage_candidate_for_review_McpAddApiFixture_87a621cc`, `serverDerivedMetadata.symbolsAdded: []`.
- Sibling positive case: `submit_file` new file `McpOverlayGateFixture.cs` → `symbolsAdded` lists class + method entries.
- Related: Finding 36 (single-op `remove_symbol` ctor → empty `symbolsRemoved`).
