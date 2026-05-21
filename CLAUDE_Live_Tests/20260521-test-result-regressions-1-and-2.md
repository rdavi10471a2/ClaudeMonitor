---
status: new
type: test-result
created: 2026-05-21
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---

## Summary

README "Current Assignment" regression checks #1 and #2 both PASS against the current main-only binary (origin/main HEAD `b4e7ac8`, McpServer rebuilt per pre-flight in `20260521-pass20-real-form-partial-class.md`).

- **Regression #1** (Finding 33 — `symbolsRemoved` enumerates overloaded constructors): **PASS**. Mixed add+remove candidate with 2 ctor adds and 1 ctor remove (alongside field/property/method removes) correctly populates both `symbolsAdded` and `symbolsRemoved` arrays.
- **Regression #2** (Finding 35 — force-review modal Cancel branch): **PASS**. `validationGateDecision: cancel_for_fix`, status `overlay-errors-review-cancelled`, no `processId` returned (WinMerge not launched), staged record queue status correctly transitions to `blocked-overlay-validation`.

## Regression #1 — Repro

Session: `monitor-20260521053422-7bfba9bdd892481b8`. Target: `SchemaStudio.SematicModel/Model/McpAddApiFixture.cs`.

Baseline (post-Pass 19): 1 field (`_field`), 1 property (`Property`), 1 ctor `()`, 1 method (`Method`), 1 nested class (`Nested`). Hash `e9322d86`.

One-session candidate composed via 6 calls under `sessionId`:

| # | Tool | Selector / args | Result |
|---|---|---|---|
| 1 | `remove_symbol` | `McpAddApiFixture::constructor::McpAddApiFixture()` | candidate-updated, opCount=1 |
| 2 | `remove_symbol` | `McpAddApiFixture::method::Method()` | candidate-updated, opCount=2 |
| 3 | `remove_symbol` | `McpAddApiFixture::property::Property` | candidate-updated, opCount=3 |
| 4 | `remove_symbol` | `McpAddApiFixture::field::_field` | candidate-updated, opCount=4 |
| 5 | `add_constructor` | `(string parm)` empty body | candidate-updated, opCount=5 |
| 6 | `add_constructor` | `(string parm, int count)` empty body | candidate-updated, opCount=6 |

`stage_candidate_for_review` returned (staged record `20260521_003508878_..._894f039f`, originalHash `e9322d86`, stagedHash `6592eb30`):

```json
"serverDerivedMetadata": {
  "symbolsAdded": [
    {"name":"McpAddApiFixture","kind":"constructor","startLine":7,"endLine":7,"textHash":"ea4ee1ae..."},
    {"name":"McpAddApiFixture","kind":"constructor","startLine":8,"endLine":8,"textHash":"999fe2ec..."}
  ],
  "symbolsRemoved": [
    {"name":"_field","kind":"field","startLine":6,"endLine":6,"textHash":"f6dbba25..."},
    {"name":"Property","kind":"property","startLine":7,"endLine":7,"textHash":"c23d3d47..."},
    {"name":"McpAddApiFixture","kind":"constructor","startLine":8,"endLine":8,"textHash":"23a6ea05..."},
    {"name":"Method","kind":"method","startLine":9,"endLine":9,"textHash":"ca88d3ad..."}
  ],
  "usingsAdded": [],
  "usingsRemoved": []
}
```

**Constructor enumeration verified on BOTH sides**: 2 ctor entries in `symbolsAdded`, 1 ctor entry in `symbolsRemoved` (between `_field`/`Property` and `Method`). Finding 33 fixed; Finding 37 fixed (add-side empty array on constructor); Finding 36 was already marked fixed in Pass 19 and remains fixed (single-ctor remove was probed implicitly here too).

Operator saved in WinMerge. `record_diff_decision(accepted)` → `classification: accepted`, `decisionMatchesClassification: true`, `currentHash == stagedHash == 6592eb30`.

## Regression #1 — Minor observation (NOT a regression failure)

Constructor entries in `symbolsAdded`/`symbolsRemoved` carry `name` + `kind` + `startLine`/`endLine` + `textHash`, but **no `parameterTypes`**. For overloaded constructors with the same `name`, two add-side entries with identical `name: "McpAddApiFixture", kind: "constructor"` are only differentiated by `startLine`/`endLine` and `textHash`. An audit consumer that wants to programmatically resolve overloads to their signatures has to either:

1. Look at the staged file content directly at the reported line, or
2. Match `textHash` against pre-staging selector hashes.

Both work. Not a defect — just an observation about the audit trail's resolution. If `parameterTypes` were included for constructor and method entries, overload disambiguation would be possible from the metadata alone. Worth considering for a future enhancement; not filing as a finding.

## Regression #2 — Repro

Session: `monitor-20260521053731-f25217863499416ba`. Target: `SchemaStudio.SematicModel/Model/McpAddApiFixture.cs` (post-Reg#1 state, hash `6592eb30`).

| # | Tool | Action | Result |
|---|---|---|---|
| 1 | `submit_symbol` | Replace `McpAddApiFixture(string parm)` ctor body with `var x = ThisIsADeliberateMissingTypeForRegression2.UndefinedField;` | candidate-updated, opCount=1, overlay reports new CS0103 in `McpAddApiFixture.cs:9:17` (alongside 3 inherited stale-fixture errors) |
| 2 | `stage_candidate_for_review` | — | staged record `20260521_003747397_..._214b2881`, stagedHash `a2478351`, overlay still reports 4 errors (1 mine + 3 stale) |
| 3 | `launch_staged_diff` | — | **`status: overlay-errors-review-cancelled`**, `validationGateStatus: completed`, **`validationGateDecision: cancel_for_fix`**, `validationGateMessage: "Operator cancelled review so the agent can fix overlay compile errors first."`, `nextStep: "Review was not launched. Fix the reported issue, then retry launch_staged_diff before calling record_diff_decision."`, **no `processId` field** (WinMerge NOT launched) |
| 4 | `list_session_staged_records` | — | `queueStatus: blocked-overlay-validation` — confirms server-side queue block per CLAUDE.md / Pass 16 doctrine |

Compare against Force WinMerge Review branch (exercised earlier this session in Pass 20 + Regression #1):

| Branch | `status` | `processId` | `validationGateDecision` |
|---|---|---|---|
| Force WinMerge Review | `winmerge-launched` | present (e.g. 2232, 45620, 46500) | `force_review` |
| Cancel Review | `overlay-errors-review-cancelled` | **absent** | `cancel_for_fix` |

Both modal branches now confirmed to produce distinct, correct responses.

## Regression #2 — Cleanup state

Watched file `McpAddApiFixture.cs` remains at post-Reg#1 hash `6592eb30` (operator clicked Cancel, no WinMerge save). Staged record `20260521_003747397_..._214b2881` remains in `blocked-overlay-validation` state as evidence; deliberately left in place. The blocked record affects only its own session; other sessions are unaffected. Recovery path (restage corrected candidate) was not exercised — the regression check's stated scope is the modal branches, not recovery; the corresponding doctrine ("until the blocked item is fixed or explicitly force-reviewed") was verified in Pass 16 already.

## Evidence pointers

- Reg #1 staged record JSON: `Working\Staged\Records\20260521\20260521_003508878_stage_candidate_for_review_McpAddApiFixture_894f039f.json`
- Reg #1 decision record: `Working\Staged\Decisions\20260521\20260521_003508878_stage_candidate_for_review_McpAddApiFixture_894f039f_003638298_accepted.json`
- Reg #2 staged record JSON: `Working\Staged\Records\20260521\20260521_003747397_stage_candidate_for_review_McpAddApiFixture_214b2881.json`
- Reg #2 has no decision record (review was never launched; `record_diff_decision` was not called)
- Session JSONs: `Working\Sessions\monitor-20260521053422-7bfba9bdd892481b8.json`, `Working\Sessions\monitor-20260521053731-f25217863499416ba.json`

## Status

Both regression checks #1 and #2 PASS. Regression check #3 (source-map selector affordances) PASSED earlier in Pass 20 and is reported in `20260521-pass20-real-form-partial-class.md`.

Outstanding from the README "Current Assignment": none on the main-only surface. The `origin/codex/session-inspector-telemetry-refresh` branch (4 commits adding watched-solution SQLite index tools) is NOT merged into `origin/main` as of `b4e7ac8` and cannot be exercised from the current binary.
