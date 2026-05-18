# Pass 6 Plan — Composition + post-c95c313 verifications on SelectItem.cs

Drafted 2026-05-18 in the diagnostic chat. SourceTable.cs (Pass 5 retest target) already carries those three changes — Operator wants a fresh composition test against a new file.

This pass simultaneously validates:

1. **Finding 17** — composition still works after the c95c313 telemetry-hardening commit (regression check).
2. **Finding 18** — the new `list_session_staged_records` MCP tool surfaces session staged records (closes the visibility gap that `get_monitor_session.files` left).
3. **Finding 19** — `get_source_map` selector signature now includes property initializers (`= new()`, `= JoinCardinality.Unknown`, etc.).
4. **Finding 22** — the telemetry FileShare race is gone. The Timer-based tail-reader was removed and remaining reads use `FileShare.ReadWrite | FileShare.Delete`. The host should not crash during the diff cycle.

## Why SelectItem.cs

- File: `C:\Schema Studio - DBV2\SchemaStudio.SematicModel\Model\SelectItem.cs` (70 lines).
- Pure POCO with one debug-format `ToString()` at lines 62–68. Behavioural risk of editing it is near zero.
- Has `internal ColumnBinding Binding { get; set; } = new();` at line 30 — exactly the kind of property where Pass 5 Finding 19 reproduced. Source-map signature for that property should now include the `= new();` initializer.
- Same project as SourceTable.cs (`SchemaStudio.SematicModel`), so the unexplored-prime-candidate scope continues without crossing project boundaries.
- 13 properties already — room for one new property and one new method without crowding.

## Pre-flight checklist

- `mcp__monitor-base-claude__*` present in deferred-tool list (including the new `list_session_staged_records`).
- `get_workflow_status`, `get_monitor_status`, `get_tool_manifest`, `get_staging_guide` all non-error.
- Roslyn: `list_solutions` shows `Schema Studio.sln` active, `get_diagnostics(severity=error)` returns `[]`.
- WinForms host (`MonitorBaseClaude.exe`) running — needed for `launch_staged_diff` and verifies the post-c95c313 telemetry view doesn't crash through normal workflow.

## Roslyn-first discovery

- `search_symbols("HasAlias")` → expect `[]` (new symbol).
- `search_symbols("GetEffectiveName")` → expect `[]` (new symbol).
- `find_references("SchemaStudio.SemanticModel.Model.SelectItem")` → likely `[]` again per Findings 11/15/21 pattern. Cross-check with `search_symbols("SelectItem")` to confirm the type's real consumers (expected: `ParsedQuery.SelectItems`, `ParsedQuery.ToColumns`, `SelectItem.ExpressionNode` AST handle). Same outcome as Pass 5: not blocking because we're adding members, not renaming.

## Staging sequence

Single monitor session, three symbol ops. Insert a `list_session_staged_records` probe between each op to validate Finding 18 fix.

1. `start_monitor_session(purpose: "Pass 6 composition + post-c95c313 verifications on SelectItem.cs")` — keep ASCII-only purpose; per the AGENTS.md rule added in c95c313 and Finding 20.
2. `get_source_map(SelectItem.cs, scope: file, mode: selector)` — capture selectors AND verify the `Binding` property signature now includes `= new();`. (Finding 19 verification.)
3. `submit_symbol(SelectItem.ToString)` with ternary rewrite:
   ```csharp
   public override string ToString() =>
       !string.IsNullOrWhiteSpace(Alias) ? $"{Expression} AS {Alias}" : Expression;
   ```
   Behaviourally identical to the existing 7-line version.
4. `list_session_staged_records(sessionId)` → expect 1 record, `QueueStatus: staged`. (First proof point for Finding 18.)
5. `add_property(SelectItem, "public bool HasAlias => !string.IsNullOrWhiteSpace(Alias);", afterSymbol: "Alias")` — read-only computed.
6. `list_session_staged_records(sessionId)` → expect 2 records, op 1 should now be `superseded-by-later-same-file-candidate`, op 2 should be `staged`. (Validates composition + supersede pairing visible from the new tool.)
7. `add_method(SelectItem, "public string GetEffectiveName() => string.IsNullOrWhiteSpace(Alias) ? Expression : Alias;", afterSymbol: "ToString")` — non-redundant with `ToString` (no quoting; just one of two).
8. `list_session_staged_records(sessionId)` → expect 3 records, ops 1+2 superseded, op 3 staged. (Full verification.)

## Verification before review

- Read op 3's staged file on disk directly. Expect all three changes co-present: ternary `ToString`, `HasAlias`, `GetEffectiveName`.
- Confirm op 1 and op 2 record JSONs show `QueueStatus: superseded-by-later-same-file-candidate`.
- Confirm `list_session_staged_records` matches.

## Review and decision

9. `launch_staged_diff(op 3 record id)` → WinMerge shows ONE diff with three changes.
10. Operator accept-all or reject (whatever they prefer).
11. `record_diff_decision(op 3 record id, accepted|rejected)` → expect classification `accepted` (exact byte match) or `accepted-normalized` (BOM/EOL only), with `decisionMatchesClassification: true`.
12. `get_diagnostics(severity=error)` post-accept → expect `[]`.

## Success criteria

- ✓ Composition: op 3 staged file contains all three changes (Finding 17 regression-clean).
- ✓ Supersede markers: ops 1+2 `superseded-by-later-same-file-candidate`, op 3 `staged`.
- ✓ `list_session_staged_records` returns correct record counts at each probe (Finding 18 fixed).
- ✓ `get_source_map` signature for `Binding` includes `= new();` (Finding 19 fixed).
- ✓ No WF host crash during the staging+diff cycle (Finding 22 fixed).
- ✓ Watched DBV2 compiles clean post-accept.

## Failure-mode findings to file (max 5; reserve cap for actual surprises)

- Composition regression: file as blocker.
- `list_session_staged_records` returns empty or wrong status: file as blocker.
- `get_source_map` still strips `Binding`'s `= new();`: file as confusing.
- WF crash recurrence on telemetry: file as blocker, note c95c313 changes that should have prevented it.
- Anything new / unexpected.

## Cleanup

- If accepted, leave the changes in DBV2. They're additive and behaviourally consistent.
- If rejected, all-or-none means baseline restored automatically.

## Hand-off state at draft time

- Branch `claude/live-test-notes-20260517` local-only with merged `c95c313` and merged `ce8e500`. NOT pushed (per Operator's "don't push code" rule).
- Comms branch `origin/Claude_Workflow_Communication` carries Findings 1–22 and prior pass notes (not Pass 6 yet — this plan added after the comms push).
- DBV2 watched repo has the Pass 5 retest changes (ToString ternary + GetQualifiedName + HasJoin on SourceTable.cs) accepted.
- WF host (`MonitorBaseClaude.exe`) running, fresh-built (post-c95c313). Both McpHubBridge bridges alive. New Pass 6 chat will reconnect on first probe.
