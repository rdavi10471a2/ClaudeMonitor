# Troubleshooting Dashboard

Use when verifying that Claude/Codex is using the intended MCP surfaces.

## Rules

- The WinForms hub live stream is the first-class signal.
- Logs are durable receipts, not the primary live view.
- A healthy C# edit run shows both Roslyn Tooling and System Monitor traffic.
- If semantic work starts with grep while Roslyn Tooling is connected, treat it as an audit/correction cue, not an automatic hard failure. Correction: call search_symbols for the same query and confirm results before continuing.
- If grep/text search appears after overlay or build diagnostics found a missing call site, treat it as an allowed diagnostic fallback. The next healthy signal is staging the missed file into the same monitor session, not continuing with ad hoc text edits.
- For high-risk watched-source edits, the real gate is System Monitor staging and decision classification.

## Healthy Signals

Roslyn Tooling:

```text
list_solutions
tools/list
search_symbols
get_type_overview
find_references / find_callers / analyze_change_impact
```

System Monitor:

```text
get_monitor_status
get_workflow_status
get_source_map
get_symbol
submit_symbol / add_symbol / remove_symbol
record_diff_decision
```

Long reference: `Docs/AgentToolCallPlaybook.md`.
