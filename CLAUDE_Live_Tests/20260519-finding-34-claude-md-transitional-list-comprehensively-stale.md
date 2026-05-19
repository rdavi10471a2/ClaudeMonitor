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

CLAUDE.md "Working Candidate Composition Flow" transitional list is comprehensively stale — every tool listed as not-yet-promoted has been V1-promoted

## Severity

stale

## File/tool

`CLAUDE.md`, the "Working Candidate Composition Flow (V1, post-b0d071e)" section. The transitional list reads:

> Tools that have not yet been promoted (`add_property`, `add_constructor`, `add_nested_type`, `submit_symbol`, `remove_symbol`, `set_type_partial`, `add_using`, `remove_using`) continue to create staged records directly.

## Observed

Pass 13 (session `monitor-20260519140105-cdf28125ff0749d49`) and Pass 14 (session `monitor-20260519140517-e8bc275066a040f0b`) walked every tool in the transitional list. Every single one returned `status: candidate-updated`, monotonically increasing `operationCount`, the V1 candidate fields (`candidateFilePath`, `candidateStatePath`, `candidateHash`), and **no** staged-record id. All composed against the same Working candidate. After each pass, `list_session_staged_records` returned 0 records until the explicit `stage_candidate_for_review` call.

| Tool | Pass / op | Observed status |
|---|---|---|
| `add_using` | Pass 13 op 1 | `candidate-updated`, opCount 1 |
| `add_using` | Pass 13 op 2 | `candidate-updated`, opCount 2 |
| `set_type_partial` | Pass 13 op 3 | `candidate-updated`, opCount 3 |
| `add_property` | Pass 13 op 6 | `candidate-updated` (also confirmed Pass 11 / Finding 32) |
| `add_property` | Pass 13 op 7 | `candidate-updated` |
| `add_constructor` | Pass 13 op 8 | `candidate-updated` |
| `add_constructor` | Pass 13 op 9 | `candidate-updated` |
| `add_nested_type` | Pass 13 op 12 | `candidate-updated` |
| `add_nested_type` | Pass 13 op 13 | `candidate-updated` |
| `submit_symbol` | Pass 13 op 14 | `candidate-updated` (also confirmed Variant C / Finding 30) |
| `remove_using` | Pass 14 op 1 | `candidate-updated`, opCount 1 |
| `remove_symbol` | Pass 14 ops 2-6 | `candidate-updated`, opCount 2-6 |

Every tool also has its own MCP schema description that explicitly states "Does not create a staged record; call stage_candidate_for_review when all edits are complete." The doc and the implementation agree with each other; CLAUDE.md is the only place still claiming the legacy contract.

## Expected

Remove the entire transitional list from CLAUDE.md's "Working Candidate Composition Flow" section. The list of "tools that have not yet been promoted" is empty. The compose-then-stage shape now applies uniformly to all member-level and namespace-level edit tools.

The same section should retain its other statements — baseline-hash rule, `candidate-baseline-stale` precondition, `submit_file`/`add_symbol`/`add_field`/`add_method` (the originally-promoted four) — but the paragraph naming the eight transitional tools should be deleted, not edited.

## Minimal fix

In `CLAUDE.md`, replace the paragraph:

> Legacy immediate-staged behavior is preserved as `submit_file_old`, `add_symbol_old`, `add_field_old`, `add_method_old`. Do not call the `_old` variants unless explicitly testing the legacy path. Tools that have not yet been promoted (`add_property`, `add_constructor`, `add_nested_type`, `submit_symbol`, `remove_symbol`, `set_type_partial`, `add_using`, `remove_using`) continue to create staged records directly. Treat that as transitional, not as the target shape.

with:

> Legacy immediate-staged behavior was previously preserved as `submit_file_old`, `add_symbol_old`, `add_field_old`, `add_method_old`; commit `944f522` removed those variants. Every member-level and namespace-level edit tool now composes into the Working candidate and does not create a staged record until `stage_candidate_for_review` is called.

Or simpler still: delete both sentences and leave the surrounding "Baseline rule" / "new-file candidates" paragraphs intact.

## Evidence

- Pass 13 walked 14 ops total (`add_using` ×2, `set_type_partial`, `add_field` ×2, `add_property` ×2, `add_constructor` ×2, `add_method` ×2, `add_nested_type` ×2, `submit_symbol`) against a single Working candidate, ending with one staged record at hash `223346ce`. The session never produced a staged record until step 15's explicit `stage_candidate_for_review` call.
- Pass 14 walked 6 ops (`remove_using` ×1, `remove_symbol` ×5) against a single Working candidate, ending with one staged record at hash `e9322d86`. Same pattern: no staged record until explicit stage.
- Commits that performed the promotions: `b0d071e` (V1 design intro, first 4 tools), `30f9002` (promote candidate workflow tools), `944f522` (remove legacy `_old` variants — implying the V1 path is the only path).
- Sibling findings already filed: Finding 30 (submit_symbol), Finding 32 (add_property). This finding supersedes both by widening the scope to every tool in the transitional list. Codex can resolve all three with one CLAUDE.md edit.
