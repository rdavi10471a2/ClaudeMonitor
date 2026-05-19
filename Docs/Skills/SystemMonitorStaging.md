# System Monitor Staging

Use when changing watched C# source.

The active safety mechanism is session overlay validation plus gated serial diff review. If files are coupled, stage all required candidates into one monitor session before launching the first review.

## Rules

- Do not edit watched source directly.
- All watched-source changes go through System Monitor staging.
- Reason in the cloud; compose locally. Use Roslyn and Monitor selectors to describe the intended edit, then let the local tooling splice/stage the candidate.
- Use the smallest safe edit unit: symbol edit before whole-file replacement.
- Stage candidates, let System Monitor validate syntax/overlay compilation, then use Operator review.
- For coupled multi-file C# edits, stage every required file in one monitor session before the first review launch so overlay validation sees the whole proposed change.
- Record the Operator decision with `record_diff_decision`.
- Stop on `dirty-unexpected`; recovery is explicit refresh/rebase/restage or Operator reconcile.

## Choose The Staging Mode

| Intent | Preferred tool |
|---|---|
| Replace one method/property/field/type body or signature | `submit_symbol` |
| Add a method | `add_method` |
| Add a field | `add_field` |
| Add a property | `add_property` |
| Add a constructor | `add_constructor` |
| Add a nested type | `add_nested_type` |
| Remove a member | `remove_symbol` |
| Change using directives | `add_using` / `remove_using` |
| Make an existing type partial | `set_type_partial` |
| Create a brand-new file | `submit_file` |
| Regenerate or deliberately replace a whole file | `submit_file` |

Do not use `submit_file` for ordinary member-level edits just because you have the full file in context.

## Usual Flow

```text
get_monitor_status
get_workflow_status
get_tool_manifest when discovering the current tool contract
get_staging_guide when the client needs the staging and session-overlay rules
find_file, unless the full path was returned by a Roslyn or Monitor tool in this session
get_source_map(scope: "file", mode: "selector")
get_symbol for the smallest needed body
submit_symbol / set_type_partial / add_field / add_property / add_method / add_constructor / add_nested_type
add_symbol / remove_symbol / add_using / remove_using, when the narrow typed tools do not fit
stage_candidate_for_review
launch_staged_diff or Host/sidecar WinMerge review/save
Operator review
record_diff_decision
```

For multi-file work:

```text
start_monitor_session
compose Working candidate A with sessionId
stage_candidate_for_review for file A with sessionId
compose Working candidate B with sessionId
stage_candidate_for_review for file B with sessionId
candidate overlay validates current Working candidates together
launch/review file A
record decision for file A
launch/review file B
record decision for file B
```

For coupled edits, briefly name why the files must validate together before review.

## Discovery Discipline

Empty Roslyn reference/caller results are not proof that no consumers exist. Before treating an API/signature/rename as single-file, cross-check with at least one other signal: diagnostics, symbol search, public API surface, targeted source map, known UI fields/properties, or explicit Operator knowledge.

For whole-file staging, use Roslyn shape plus `get_file`; skip `get_source_map` unless you need stable selectors or structure. For symbol staging, use Roslyn shape plus `get_source_map`/`get_symbol`; skip `get_file` unless symbol context is insufficient.

Long reference: `Docs/AgentToolCallPlaybook.md`.

Debug-only reference: `get_smoke_test_catalog` / `Docs/SmokeTestCatalog.md` is for maintainers investigating or extending smoke coverage, not for normal Claude review or edit planning.
