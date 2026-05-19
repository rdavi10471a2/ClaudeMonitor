---
status: spec
type: system-test-spec
created: 2026-05-19
authoredBy: Claude testing agent (Pass 11 wrap-up + Pass 12/13/14 design)
reusable: true
---

# System Test Spec — Add/Remove Tool-Surface Walk

A reusable end-to-end test for the Monitor MCP add-* and remove-* tool surface.
Exercises every typed insertion/removal tool in three named passes against one
synthetic fixture file. Designed so a future agent (Claude or otherwise) can
regenerate it from this file alone, without re-deriving the parameters.

## Why this test exists

Prior passes covered the tool surface piecemeal: Pass 8 hit `add_method` +
`add_field` + `remove_symbol` (legacy path); Variant A planned `add_using` /
`add_method` × 5 but the typed adds failed and the pass fell back to whole-file
`submit_file` (see Finding 29); Variant C hit `add_method` + `submit_symbol`
on an existing file; Pass 11 hit `add_property` alone. **No single pass walked
the whole add-* surface, and the remove side is largely untested on the V1
candidate path.** This spec consolidates that coverage into one regenerable
recipe.

The pass shape is deliberately:

```text
Pass 12: create fixture file (submit_file) -> accept -> Operator commit
Pass 13: exhaustive add-* (one Working candidate) -> accept -> Operator commit
Pass 14: exhaustive remove-* (one Working candidate) -> accept
```

The two commit boundaries are part of the test — they exercise the F26 path
("post-accept baseline transition") at scale.

## Pre-flight (every pass)

Standard pre-flight per `CLAUDE.md` "Pre-flight Before Each Pass" plus:

- WinForms Host running (Operator confirms).
- `tools/list` exposes the full surface: `submit_file`, `add_using`,
  `set_type_partial`, `add_field`, `add_property`, `add_constructor`,
  `add_method`, `add_nested_type`, `submit_symbol`, `stage_candidate_for_review`,
  `launch_staged_diff`, `record_diff_decision`, `remove_using`, `remove_symbol`.
- `Working\.state\Candidates\<observedRootKey>\<rel>` does NOT contain a
  `McpAddApiFixture.cs.candidate.json` (Finding 26 risk avoidance).
- Discovery is Roslyn-first (`search_symbols`, `get_type_overview`,
  `get_source_map`). Grep only as a compiler-gate fallback when
  `find_references` returns empty for a symbol that demonstrably has consumers
  (Finding 11 under-reporting fallback).

## Fixture

- Path: `SchemaStudio.SematicModel\Model\McpAddApiFixture.cs`
- Project: `SchemaStudio.SematicModel`
- Namespace: `SchemaStudio.SemanticModel.Model`
- Type: `McpAddApiFixture` (becomes `partial` in Pass 13)
- Naming convention: every paired member uses a uniform `_Remove` suffix on
  the to-remove partner. The keep partner uses the natural name; the remove
  partner is `<naturalName>_Remove`. Pass 14 targets every `*_Remove` member
  for deletion. Constructors are disambiguated by parameter signature instead
  of name (constructor name is fixed to the class name); the to-remove
  constructor takes a single `string parm_Remove` argument so the selector
  payload still encodes the `_Remove` intent. Using directives have no
  member-name component; the to-remove using is `System.Linq` by convention,
  documented in the table.

## Pass 12 — Fixture file creation

Single tool call into a fresh Working candidate plus one stage/diff/accept.

| Step | Tool | Argument shape |
|---|---|---|
| 1 | `start_monitor_session` | `purpose: "Pass 12 add/remove API walk - fixture creation"` |
| 2 | `submit_file` | `path: "SchemaStudio.SematicModel/Model/McpAddApiFixture.cs"`, `sessionId: <from step 1>`, `content:` see "Pass 12 initial body" below |
| 3 | `stage_candidate_for_review` | same `path`, same `sessionId` |
| 4 | `launch_staged_diff` | `stagedRecordId` from step 3 |
| 5 | Operator | Save in WinMerge to accept; close without saving to reject |
| 6 | `record_diff_decision` | `stagedRecordId` from step 3, `decision: "accepted"`, `sessionId` |
| 7 | Operator | Commit the new file to the watched repo before Pass 13 |

### Pass 12 initial body

```csharp
namespace SchemaStudio.SemanticModel.Model;

public class McpAddApiFixture
{
}
```

Three lines of body, deliberately empty. The `submit_file` payload should be
the smallest valid C# file the Monitor + Roslyn pipeline will parse cleanly.
Overlay validation must be `compiled` with zero diagnostics — that is the
Pass 12 success gate.

### Pass 12 success criteria

- `submit_file` returns `status: candidate-updated`, `baselineHash: "<new-file>"`,
  `operationCount: 1`, overlay diagnostics empty.
- `stage_candidate_for_review` returns a staged record with empty
  `serverDerivedMetadata.symbolsAdded` (because the class is empty) or with
  the empty `McpAddApiFixture` class listed depending on server-derived metadata
  granularity. Whichever the implementation chooses, record the observed shape.
- `launch_staged_diff` opens WinMerge (Host required).
- `record_diff_decision(accepted)` returns `classification: accepted` (exact)
  or `accepted-normalized` (BOM/EOL only) with `decisionMatchesClassification:
  true`.
- Post-accept: `Working\.state\Candidates\<observedRootKey>\SchemaStudio.SematicModel\Model\McpAddApiFixture.cs.candidate.json` does NOT exist (state JSON cleaned up). If it does exist, Finding 26 reproduces.

## Pass 13 — Exhaustive add-* walk

Fresh session. **All 14 ops compose into one Working candidate.** Single
`stage_candidate_for_review` after the final op, one `launch_staged_diff`, one
`record_diff_decision`.

| # | Tool | Required args | Pair role |
|---|---|---|---|
| 1 | `add_using` | `path`, `namespace: "System.Collections.Generic"`, `sessionId` | keep |
| 2 | `add_using` | `path`, `namespace: "System.Linq"`, `sessionId` | remove (Pass 14 — usings have no member name; `System.Linq` is the convention) |
| 3 | `set_type_partial` | `path`, `containingType: "McpAddApiFixture"`, `isPartial: true`, `sessionId` | transformation, no pair |
| 4 | `add_field` | `path`, `containingType: "McpAddApiFixture"`, `declaration: "private int _field = 1;"`, `sessionId` | keep |
| 5 | `add_field` | `path`, `containingType: "McpAddApiFixture"`, `declaration: "private int _field_Remove = 2;"`, `afterSymbol: "_field"`, `sessionId` | remove |
| 6 | `add_property` | `path`, `containingType: "McpAddApiFixture"`, `declaration: "public int Property { get; set; }"`, `afterSymbol: "_field_Remove"`, `sessionId` | keep |
| 7 | `add_property` | `path`, `containingType: "McpAddApiFixture"`, `declaration: "public int Property_Remove { get; set; }"`, `afterSymbol: "Property"`, `sessionId` | remove |
| 8 | `add_constructor` | `path`, `containingType: "McpAddApiFixture"`, `declaration: "public McpAddApiFixture() { }"`, `afterSymbol: "Property_Remove"`, `sessionId` | keep |
| 9 | `add_constructor` | `path`, `containingType: "McpAddApiFixture"`, `declaration: "public McpAddApiFixture(string parm_Remove) { _field_Remove = parm_Remove?.Length ?? 0; }"`, `afterSymbol: "McpAddApiFixture"`, `sessionId` | remove (overload; disambiguated by `parameterTypes: [\"string\"]`) |
| 10 | `add_method` | `path`, `containingType: "McpAddApiFixture"`, `declaration: "public int Method() => _field + Property;"`, `sessionId` | keep |
| 11 | `add_method` | `path`, `containingType: "McpAddApiFixture"`, `declaration: "public int Method_Remove() => _field_Remove + Property_Remove;"`, `afterSymbol: "Method"`, `sessionId` | remove |
| 12 | `add_nested_type` | `path`, `containingType: "McpAddApiFixture"`, `declaration: "public sealed class Nested { public int Value { get; set; } }"`, `sessionId` | keep |
| 13 | `add_nested_type` | `path`, `containingType: "McpAddApiFixture"`, `declaration: "public sealed class Nested_Remove { public int Value { get; set; } }"`, `afterSymbol: "Nested"`, `sessionId` | remove |
| 14 | `submit_symbol` | `path`, `symbolSelectorJson:` for `Method` from a `get_source_map(scope: file, mode: selector)` taken AFTER ops 1-13, `code: "public int Method() => _field + Property + 1;"`, `sessionId` | body replacement on Keep |
| 15 | `stage_candidate_for_review` | `path`, `sessionId` | — |
| 16 | `launch_staged_diff` | `stagedRecordId` from step 15 | — |
| 17 | Operator | Save in WinMerge | — |
| 18 | `record_diff_decision` | `stagedRecordId`, `decision: "accepted"`, `sessionId` | — |
| 19 | Operator | Commit before Pass 14 | — |

### Pass 13 success criteria

- Every `add_*` op returns `status: candidate-updated` with monotonically
  increasing `operationCount` (1 .. 14).
- Overlay validation stays `compiled` with zero diagnostics across all 14 ops.
  If any op surfaces a CS-error, that is the bug — record it and stop.
- The single staged record at step 15 carries `serverDerivedMetadata` that
  enumerates all 13 added symbols (op 14's body replacement should appear as
  a body change, not a new add).
- WinMerge opens with one diff for the full fixture.
- `record_diff_decision(accepted)` returns `classification: accepted` (or
  `accepted-normalized`) with `decisionMatchesClassification: true`.
- Post-accept: candidate state JSON cleaned up.

### Discovery between ops

After each add-call, optionally read `get_source_map(scope: file, mode: selector)`
to confirm placement and get the `stableSymbolKey` for op 14's `submit_symbol`
selector. This is read-only — it costs tokens but produces audit-quality
selectors for `remove_symbol` in Pass 14.

## Pass 14 — Exhaustive remove-* walk

Fresh session. All remove ops compose into one Working candidate. Single
stage/diff/accept.

Pass 14 removes every `*_Remove`-suffixed member plus the `System.Linq`
using. After Pass 14 commits, the fixture contains only the keep partners.

**Execution order is leaf-first.** The intermediate-state overlay validates after every op, and the `(string parm_Remove)` constructor body references `_field_Remove`, while `Method_Remove` references both `_field_Remove` and `Property_Remove`. Removing fields/properties before their consumers would surface `CS0103` at the intermediate overlay check — the gate would refuse the candidate composition mid-flow. The order below removes leaf consumers first so every intermediate state compiles.

| # | Tool | Required args |
|---|---|---|
| 1 | `start_monitor_session` | `purpose: "Pass 14 add/remove API walk - remove _Remove members"` |
| 2 | `remove_using` | `path`, `namespace: "System.Linq"`, `sessionId` |
| 3 | `remove_symbol` | `path`, `symbolSelectorJson:` method `Method_Remove` (depends on `_field_Remove` and `Property_Remove`; remove first), `sessionId` |
| 4 | `remove_symbol` | `path`, `symbolSelectorJson:` constructor `McpAddApiFixture(string)` (depends on `_field_Remove`; use `parameterTypes: ["string"]` to disambiguate from the parameterless overload), `sessionId` |
| 5 | `remove_symbol` | `path`, `symbolSelectorJson:` field `_field_Remove` (now safe — no remaining consumers), `sessionId` |
| 6 | `remove_symbol` | `path`, `symbolSelectorJson:` property `Property_Remove` (now safe), `sessionId` |
| 7 | `remove_symbol` | `path`, `symbolSelectorJson:` nested type `Nested_Remove` (no consumers ever), `sessionId` |
| 8 | `stage_candidate_for_review` | `path`, `sessionId` |
| 9 | `launch_staged_diff` | `stagedRecordId` from step 8 |
| 10 | Operator | Save in WinMerge |
| 11 | `record_diff_decision` | `stagedRecordId`, `decision: "accepted"`, `sessionId` |

The `symbolSelectorJson` for each `remove_symbol` call can be constructed directly without a `get_source_map` round-trip: `{"name": "<member>", "memberKind": "<field|property|method|class|constructor>", "containingType": "McpAddApiFixture"}` plus `"parameterTypes": [...]` only when overload disambiguation matters (the `(string)` ctor in step 4). Pass 14 in this session confirmed direct-construction is sufficient.

### Pass 14 success criteria

- Every `remove_*` op returns `status: candidate-updated` (V1 contract) with
  monotonically increasing `operationCount`. If any op returns an immediate
  staged-record id, that is the legacy path; record which tools are still
  legacy and file as a finding sibling to Finding 30 / Finding 32.
- Overlay validation stays `compiled` with zero diagnostics across all 6 ops.
- The single staged record at step 8 carries `serverDerivedMetadata.symbolsRemoved`
  that enumerates all 5 removed symbols plus the removed `System.Linq` using
  in `usingsRemoved`.
- `record_diff_decision(accepted)` returns `classification: accepted` (or
  `accepted-normalized`) with `decisionMatchesClassification: true`.

## Reproducibility notes

- The exact `sessionId` returned by `start_monitor_session` is random; pin it
  in the per-run sidecar (`CLAUDE_Live_Tests/<date>-pass-NN-mcp-add-api-fixture.md`).
- `baselineHash` and per-step `candidateHash` are deterministic functions of
  file content; record them in the sidecar so a re-run can confirm bit-identical
  output.
- `serverDerivedMetadata.symbolsAdded` line numbers depend on whitespace; the
  spec records the structural ordering (keep before remove for each pair),
  not exact line numbers.
- The `afterSymbol` arguments are intentional and produce the order shown in
  the spec table. A regen with different `afterSymbol` arguments is a
  different test.

## Known-risk callouts before running

- Finding 26 (V1 op opaque crash on stale state JSON): mitigated by choosing
  a fresh path with no candidate state JSON history. The post-accept cleanup
  observed in Pass 11 should keep the path clean between passes, but verify
  state-JSON absence between passes as a pre-flight step.
- Finding 27 (`record_diff_decision` opaque crash on superseded record): avoid
  by **not** calling `stage_candidate_for_review` twice within a pass. The
  V1 batching rule (one stage per pass) is the natural mitigation.
- Finding 28 (overlay partial-class not substituted): may surface on Pass 13
  step 3 when `set_type_partial` activates partial on a class with no
  companion file. Record overlay diagnostics; do not block if validation
  still reports `compiled`.
- Finding 29 (`add_method` opaque error on non-watched file paths): the
  fixture is inside the watched root so this should not fire. If it does,
  the path-resolution code regressed.
- Finding 32 (CLAUDE.md V1-promotion list stale): Pass 13 and Pass 14 produce
  authoritative V1-vs-legacy data for every tool in the transitional list.
  Use the per-call `status` field (`candidate-updated` = V1; staged-record
  id immediately = legacy) to update CLAUDE.md after the walk.

## Reporting

Each pass appends to `STATUS.md` with:

- Pre-flight block (branch, hashes, host/Roslyn/WinMerge readiness, state-JSON
  scan).
- Target / scope.
- Step-by-step results table mirroring the table in this spec.
- Findings filed.
- Notable positives.
- Watched-repo state at pass end.
- Next-pass suggestion.

Per-run sidecar at `CLAUDE_Live_Tests/2026MMDD-pass-NN-mcp-add-api-fixture.md`
captures full tool-call payloads and response payloads for audit.

## Future evolution

When the V1 promotion list closes, this spec should also be the regression
test for the promotion. Drop pairs from this spec only when the corresponding
tool is removed from the product (e.g., legacy `_old` variants in commit
`944f522`); add new tools to the table as they appear.
