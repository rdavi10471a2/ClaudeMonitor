---
status: fixed
type: finding
created: 2026-05-19
processed: true
processedBy: Codex
processedAt: 2026-05-19
resolution: Verified fixed in Pass 19 against fresh binaries (11:44). stage_candidate_for_review now reports symbolsRemoved=[ctor] with matching textHash. See 20260519-pass19-test-result-finding-35-36-37-verified.md.
resolutionCommit: 0102077
---

## Title

`stage_candidate_for_review.serverDerivedMetadata.symbolsRemoved` returns an empty array when a single constructor was removed (Finding 33 still open and broader than originally scoped)

## Severity

confusing

## File/tool

`monitor-base-claude` MCP — `stage_candidate_for_review` response, specifically `serverDerivedMetadata.symbolsRemoved` and `symbolsAdded`. The staged candidate file content itself is correct.

## Observed

Session `monitor-20260519151325-d279e472530b41bc9` ran a single `remove_symbol` call against `McpAddApiFixture.cs` to drop the `McpAddApiFixture(string parm)` ctor overload. `remove_symbol` returned `status: candidate-updated`, `operationCount: 1`, overlay clean. `stage_candidate_for_review` then returned an empty `symbolsRemoved: []` array despite `originalHash` (0d743655…, 2-ctor file) differing from `stagedHash` (e9322d86…, 1-ctor file). The staged file at `Working\Staged\…\20260519_101917606_..._1bd2cfc8.cs` is correct — only the parameterless ctor remains. `record_diff_decision(accepted)` confirmed vote+hash agreement (`classification: accepted`).

This is broader than Finding 33 reported: Finding 33 said the constructor entry was *missing* from a populated array; here the entire array is empty after a single ctor remove.

## Expected

`symbolsRemoved` should enumerate every removed symbol — particularly when the candidate's only operation is a constructor removal that did change the file.

## Minimal fix

Audit the diff path that emits `serverDerivedMetadata.symbolsRemoved` for single-op constructor candidates. The same code-path likely affects `symbolsAdded` for the symmetric add-ctor case (see sibling finding `20260519-finding-37`).

## Evidence

- `remove_symbol` op response — `operationCount: 1`, `candidateHash: e9322d86…`, no errors.
- `stage_candidate_for_review` response — `originalHash: 0d743655…`, `stagedHash: e9322d86…`, `serverDerivedMetadata.symbolsRemoved: []`.
- Staged record id `20260519_101917606_stage_candidate_for_review_McpAddApiFixture_1bd2cfc8`.
- `record_diff_decision` — `classification: accepted`, `decisionMatchesClassification: true`.
- Related: prior Finding 33 (`20260519-finding-33-staged-metadata-symbols-removed-omits-constructors.md`).
