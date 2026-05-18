---
status: new
type: test-result
created: 2026-05-18
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---

## Summary

Pass 9 verifies the expanded V1 surface from commit `30f9002` end-to-end. Five newly promoted candidate-flow tools (`add_property`, `add_using`, `submit_symbol`, `remove_symbol`, `set_type_partial`) compose into one Working candidate, snapshot via one `stage_candidate_for_review`, and accept via one WinMerge cycle. Finding 26 (state JSON not cleared on accept) and Finding 27 (`record_diff_decision` crash on superseded record) are both confirmed fixed.

## Setup

- Branch `claude/live-test-notes-20260517` at `8db8edd` (merge of `origin/main` containing `30f9002`).
- MCP server dll `MonitorBaseClaude.McpServer.dll` 370176 bytes, written 2026-05-18 16:11:45 (per Pass 8 hand-off note).
- WinForms host PID 29264 started 2026-05-18 16:17:26.
- Both MCP servers loaded by the new chat after VS Code `MCP: Start/Restart Server`.
- Watched repo `C:\Schema Studio - DBV2` compiles clean at pass start (`get_diagnostics(severity=error)` returned `[]`).

## Target

`SchemaStudio.SematicModel\Model\ColumnBinding.cs`, reused from Pass 8 so its leftover candidate state JSON (`BaselineHash: 58cb0ce...` vs current watched `aa172ed1...`) could be characterized. Operator elected to delete the stale JSON manually before staging so the Pass 9 primary plan ran against a clean baseline — see Finding 28 for the broader problem this surfaced.

## Pass 9 Primary — Five-Tool V1 Composition

Session `monitor-20260518212420-b873f09ec39d4d809`. All five ops added to one Working candidate, baseline `aa172ed1...` preserved across the chain.

| Op | Tool | Symbol | opCount | candidate hash | overlay |
|---|---|---|---|---|---|
| 1 | `add_property` | `HasSourceColumn` after `SourceColumn` | 1 | `aef3a5ee` | 82/3 clean |
| 2 | `add_using` | `System` | 2 | `fcd06f1e` | 82/3 clean |
| 3 | `submit_symbol` | `SourceColumn` body, add `= string.Empty` | 3 | `cd7f09c5` | 82/3 clean |
| 4 | `remove_symbol` | `GetQualifiedColumn` (no consumers in DBV2) | 4 | `5144920c` | 82/3 clean |
| 5 | `set_type_partial` | `ColumnBinding`, `isPartial: true` | 5 | `04e8ba39` | 82/3 clean |

`list_session_staged_records(sessionId)` returned `count: 0` before `stage_candidate_for_review` — V1 invariant verified.

`stage_candidate_for_review` → staged record `..._5a34781d`, `originalHash: aa172ed1...`, `stagedHash: 04e8ba39...`, overlay clean. `launch_staged_diff` → WinMerge PID 30756 → Operator accept → `record_diff_decision(accepted)` → `classification: accepted` (exact byte match, not normalized), `decisionMatchesClassification: true`, `currentHash == stagedHash == 04e8ba39...`.

Direct read of watched after accept confirmed all five changes co-present: `using System;`, `public partial class ColumnBinding`, `SourceColumn { get; set; } = string.Empty;`, `HasSourceColumn` property, `GetQualifiedColumn` removed.

DBV2 compiles clean post-accept (`get_diagnostics(error)` → `[]`).

## Finding 26 Verification — Forward Direction

State JSON path: `Working\.state\Candidates\Schema Studio - DBV2_6c4e124c9922\SchemaStudio.SematicModel\Model\ColumnBinding.cs.candidate.json`.

Pre-accept: present. Post-accept: **absent**. The `30f9002` helper deletes the state JSON on accept as advertised.

Same verification re-ran after the S2 accept later in the pass — JSON gone again. Helper fires reliably on both accept paths exercised here.

## Finding 26 Verification — Fresh Op After Accept

Critical follow-on: with state JSON cleared, a fresh V1 op on the same path in the same session must record the current watched as the new baseline, not crash opaquely.

Result: `add_method(DescribeSource)` against `ColumnBinding.cs` returned `status: candidate-updated`, `baselineHash: 04e8ba39...` (post-accept watched), `operationCount: 1` (fresh candidate cycle), overlay clean. No crash.

F26 is fixed end-to-end through the Claude Code MCP path.

## Finding 27 Verification — Superseded Record Refusal

After the F26 follow-on op, the candidate was staged (S1 = `..._d0ef3203`), one more op added (`add_property HasSourceTable`), and staged again (S2 = `..._2e5fc9b3`). S1 was correctly marked `superseded-by-later-same-file-candidate`, its staged file physically moved to `Working\Staged\Superseded\20260518\...`.

`record_diff_decision(S1, accepted)` returned:
- `classification: staged-record-superseded`
- `decisionMatchesClassification: false`
- `note: "This staged record was superseded by a later same-file candidate. Use list_session_staged_records to locate the current staged record for this session."`
- Decision record file: `..._staged-record-superseded.json` (distinct from accepted/rejected paths).

Pre-`30f9002` this crashed opaquely. Now matches the `launch_staged_diff` refusal shape — sibling-tool consistency restored. F27 fixed.

## Outstanding S2 Accept

Operator elected to `launch_staged_diff(S2)` and accept in WinMerge. S2 added `HasSourceTable` (property) and `DescribeSource` (method) on top of the Pass 9 primary state. `record_diff_decision(accepted)` → `classification: accepted` (exact byte match), `currentHash == stagedHash == ae63cf5f...`.

## Final Watched State

```csharp
using System;
namespace SchemaStudio.SemanticModel.Model
{
    public partial class ColumnBinding
    {
        public string SourceAlias { get; set; }
        public string SourceDatabase { get; set; }
        public string SourceSchema { get; set; }
        public string SourceTable { get; set; }
        public bool HasSourceTable => !string.IsNullOrWhiteSpace(SourceTable);
        public string SourceColumn { get; set; } = string.Empty;
        public bool HasSourceColumn => !string.IsNullOrWhiteSpace(SourceColumn);
        public string DescribeSource() => SourceTable + "." + SourceColumn;
    }
}
```

`get_diagnostics(error)` clean.

## Scorecard

| Goal | Result |
|---|---|
| 5-tool V1 composition into one Working candidate | passed |
| Baseline preserved across all 5 ops | passed |
| Overlay validation clean each step (82 trees / 3 overlay files) | passed |
| `list_session_staged_records` count=0 pre-snapshot, increments post-snapshot | passed |
| One stage_candidate_for_review → one WinMerge → exact-byte accept | passed |
| F26 forward direction (state JSON cleared on accept) | passed |
| F26 follow-on (fresh V1 op post-accept records new baseline) | passed |
| F27 (record_diff_decision on superseded record returns structured refusal) | passed |
| DBV2 compiles clean post-accept | passed |

## Side Observations (Not New Findings)

- `find_references(SchemaStudio.SemanticModel.Model.ColumnBinding)` returned `[]` despite `QueueBinder.cs`, `ColumnBinder.cs`, and `BasicSelectVisitor.cs` referencing `SourceAlias` via the type and `SelectItem.Binding` being a type-position consumer. Finding 21 / 15 / 11 pattern recurring — discovery had to fall back to text grep to find the production references. Cross-check via grep is what Discovery Discipline already prescribes.
- Three orphan candidate state JSONs (`Enums.cs`, `ExportMappers.cs`, `ParsedQuery.cs`) survived in `Working\.state\Candidates\…` from prior passes. Pre-Pass-9 cleanup deleted only `ColumnBinding.cs.candidate.json` manually. Operator may want to sweep the rest. No finding filed — `30f9002`'s delete-on-accept fix covers the forward path; cleaning prior-pass leftovers is housekeeping.

## Pass 9 Watched Repo State

`C:\Schema Studio - DBV2`:
- `ColumnBinding.cs` — Pass 8's `GetQualifiedColumn` plus Pass 9's `partial`, `using System`, `SourceColumn = string.Empty`, `HasSourceColumn`, `HasSourceTable`, `DescribeSource` (and `GetQualifiedColumn` removed).
- `SelectItem.cs` — Pass 6 leftovers.
- `SourceTable.cs` — Pass 5 leftovers.

Notes branch `claude/live-test-notes-20260517`: this dated test-result file plus two finding files.

## Next Pass Suggestion

1. Mixed-tool V1 composition across two coupled files (multi-file coupled edit, currently exercised only via legacy `_old` direct staging).
2. F20/F25 retest with non-ASCII characters in `start_monitor_session.purpose` and `record_diff_decision.note` to confirm the Unicode mangling status under the post-`30f9002` binary.
