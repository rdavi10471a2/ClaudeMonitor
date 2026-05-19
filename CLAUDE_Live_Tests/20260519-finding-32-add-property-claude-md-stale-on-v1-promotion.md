---
status: new
type: finding
created: 2026-05-19
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---

## Title

CLAUDE.md "Working Candidate Composition Flow" lists `add_property` as not-yet-promoted, but observed behavior is V1 candidate-flow

## Severity

stale

## File/tool

`CLAUDE.md`, "Working Candidate Composition Flow (V1, post-b0d071e)" section. The transitional list of tools that "continue to create staged records directly" includes `add_property` alongside `add_constructor`, `add_nested_type`, `submit_symbol`, `remove_symbol`, `set_type_partial`, `add_using`, `remove_using`.

## Observed

In Pass 11 session `monitor-20260519134259-142cbd6a5a794b3fb`, `add_property(ViewSourcedColumnDefinition, HasBaseLineage)` returned `status: candidate-updated`, `operationCount: 1`, V1 candidate fields (`candidateFilePath: Working\<observedRootKey>\<rel>`, `candidateStatePath: Working\.state\Candidates\...`, `candidateHash`, `baselineHash`), and **no** staged-record id. `list_session_staged_records` returned `count: 0` immediately after the call — confirming the V1 contract (no record until `stage_candidate_for_review`). The tool's MCP schema description itself states "Add one C# property to the monitor-owned Working mirror candidate. Does not create a staged record."

## Expected

Either CLAUDE.md removes `add_property` from the not-yet-promoted list (right outcome if it was actually promoted, which the tool description and Pass 11 evidence both indicate), or the server should still return a staged-record response (if the promotion was unintended). This is the same drift Finding 30 reported for `submit_symbol`; both can be fixed in one CLAUDE.md edit.

## Minimal fix

Remove `add_property` from the transitional list in CLAUDE.md's "Working Candidate Composition Flow" section. While editing, audit the rest of the list (`add_constructor`, `add_nested_type`, `remove_symbol`, `set_type_partial`, `add_using`, `remove_using`) against the current binary — the V1 promotion commits `b0d071e`, `30f9002`, `944f522` may have brought more tools over without a CLAUDE.md sync.

## Evidence

Pass 11 step 1 response payload (sessionId `monitor-20260519134259-142cbd6a5a794b3fb`):

- `add_property` returned `status: candidate-updated`, `operationCount: 1`, `baselineHash: c51d358a...`, `candidateHash: b8cfcecc...`, `candidateStatePath: Working\.state\Candidates\Schema Studio - DBV2_6c4e124c9922\SchemaStudio.SematicModel\Model\ViewSourcedColumnDefinition.cs.candidate.json`. Overlay validation clean (83 syntax trees, 0 diagnostics).
- `list_session_staged_records(sessionId)` returned `count: 0, records: []`.
- Subsequent `stage_candidate_for_review` produced staged record `20260519_084343444_..._9968791a` whose `serverDerivedMetadata.symbolsAdded` correctly named `HasBaseLineage (property, line 221)`.
- See also Finding 30 ([20260518-finding-30-submit-symbol-claude-md-stale-on-v1-promotion.md](20260518-finding-30-submit-symbol-claude-md-stale-on-v1-promotion.md)) for the sibling drift on `submit_symbol`.
