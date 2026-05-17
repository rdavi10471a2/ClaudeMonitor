# System Monitor Staging

Use when changing watched C# source.

The active safety mechanism is session overlay validation plus gated serial diff review. If files are coupled, stage all required candidates into one monitor session before launching the first review.

## Rules

- Do not edit watched source directly.
- All watched-source changes go through System Monitor staging.
- Use the smallest safe edit unit: symbol edit before whole-file replacement.
- Stage candidates, let System Monitor validate syntax/overlay compilation, then use Operator review.
- For coupled multi-file C# edits, stage every required file in one monitor session before the first review launch so overlay validation sees the whole proposed change.
- Record the Operator decision with `record_diff_decision`.
- Stop on `dirty-unexpected`; recovery is explicit refresh/rebase/restage or Operator reconcile.

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
Operator review
record_diff_decision
```

For multi-file work:

```text
start_monitor_session
stage file A with sessionId
stage file B with sessionId
overlay compile validates A+B together
launch/review file A
record decision for file A
launch/review file B
record decision for file B
```

For coupled edits, briefly name why the files must validate together before review.

Long reference: `Docs/AgentToolCallPlaybook.md`.

Debug-only reference: `get_smoke_test_catalog` / `Docs/SmokeTestCatalog.md` is for maintainers investigating or extending smoke coverage, not for normal Claude review or edit planning.
