---
status: new
type: finding
created: 2026-05-18
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---

## Title

CLAUDE.md "Working Candidate Composition Flow" lists `submit_symbol` as not-yet-promoted, but observed behavior is V1 candidate-flow

## Severity

stale

## File/tool

`CLAUDE.md`, "Working Candidate Composition Flow (V1, post-b0d071e)" section. The transitional list of tools that "continue to create staged records directly" includes `submit_symbol`.

## Observed

In Variant C session `monitor-20260519000019-138ecd02021d43d7b`, `submit_symbol(SchemaObjectColumnRepositoryAsync\CountByObjectAsync, fixed body)` returned `status: candidate-updated`, `operationCount: 3`, V1 candidate fields (`candidateFilePath`, `candidateStatePath`, `candidateHash`), and **no** staged-record id. It composed against the existing Working candidate produced by two prior `add_method` calls.

## Expected

Either CLAUDE.md removes `submit_symbol` from the not-yet-promoted list (the right outcome if it was actually promoted), or the server should still return a staged-record response (if the promotion was unintended).

## Minimal fix

Remove `submit_symbol` from the transitional list in CLAUDE.md's "Working Candidate Composition Flow" section. Cross-check against the V1 promotion commits since `b0d071e`.

## Evidence

See [20260518-variant-c-iteration-existing-file.md](20260518-variant-c-iteration-existing-file.md) "Workflow Path Actually Taken" row 9 and "Observations" section. Response payload from op 3 in the linked test-result contains the V1 candidate fields without any staged-record id.
