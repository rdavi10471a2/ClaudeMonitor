# Claude Skill: Roslyn Tooling + System Monitor

Use this when Claude Code is working in a C# project with both MonitorBaseClaude MCP servers connected.

## Purpose

Use compiler-backed Roslyn navigation before text search, then use System Monitor for protected staged edits. Do not directly edit watched source.

## First Calls

At the start of a real coding task, verify both surfaces:

```text
System Monitor: get_monitor_status
System Monitor: get_workflow_status
Roslyn Tooling: list_solutions
Roslyn Tooling: tools/list
```

Use `tools/list` to read live tool descriptions. The Roslyn descriptions include argument names and workflow hints.

## Golden Rules

- Prefer Roslyn Tooling over text or grep search for C# symbol discovery.
- Use text search only for literal text, comments, strings, generated artifacts, non-C# files, or when Roslyn cannot identify the target.
- Do not guess Roslyn argument names. Read the schema or tool description.
- Do not edit watched source directly.
- All watched-source changes go through System Monitor staging.
- Roslyn code fixes are advisory only; convert them into System Monitor staged candidates.
- For async/signature/API changes, inspect references, callers, implementations, and change impact before staging.
- After staged edits compile, run diagnostics again before Operator review.

## Semantic Read Flow

For "where is this used?" or "what would this change affect?":

```text
search_symbols(query)
get_type_overview(typeName)
find_references(symbol)
find_callers(symbol), when changing behavior/signature
find_implementations(symbol), for async/signature changes or when contracts/overrides may be involved
analyze_change_impact(symbol), before API/signature changes
```

Argument reminders:

- `search_symbols` uses `query`.
- `get_type_overview` uses `typeName`.
- `find_references`, `find_callers`, `find_implementations`, `analyze_change_impact`, and `get_call_graph` use `symbol`.
- Do not use `symbolName` or `query` for reference/caller/impact tools.

## Protected Edit Flow

For edits:

```text
System Monitor find_file, if path is uncertain
System Monitor get_source_map(scope: "file", mode: "selector")
Roslyn Tooling references/callers/impact checks, if behavior/signature can affect other code
System Monitor get_symbol for the smallest needed body
System Monitor submit_symbol/add_symbol/remove_symbol/add_using/remove_using/submit_file
System Monitor compile validation result
Roslyn Tooling get_diagnostics after staged compile when solution health matters
Operator review in WinMerge
record_diff_decision
```

Do not stage until the affected edit set is understood.

For coupled multi-file C# edits, start or reuse one monitor session and stage every required file before the first review launch. The overlay compile check must see the proposed files together, even though Operator diffs are reviewed one file at a time.

## Immediate Edit Safety Notes

- For async, signature, API, or multi-file edits, stage all coupled files into the same session before review.
- Preserve existing encoding, newline mode, and surrounding trivia unless the Operator explicitly requests formatting cleanup.
- In existing files, add new members after the closest related member, or near the bottom of the containing type when no obvious neighbor exists.
- Do not reorganize existing declarations or add new regions during functional edits.
- For brand-new generated C# files, use a stable layout: fields, constructors, properties, public methods, protected/internal members, private helpers, nested types. Use regions only when comparable project files already use them.
- Selector resolution failure or ambiguity must stop staging. Do not fall back to name-only mutation or widen the edit scope.
- Mutation tools must not silently expand from one symbol to whole-file or unrelated-symbol edits.
- If System Monitor returns `dirty-unexpected`, stop that file's edit flow. Recovery is explicit: refresh/rebase/restage or Operator reconcile. Do not call `record_diff_decision` again to force a different outcome.

## Async Change Flow

For "make method async and update callers":

```text
search_symbols(query: "<type or method>")
get_type_overview(typeName: "<selected type>")
find_references(symbol: "<Type.Method>")
find_callers(symbol: "<Type.Method>")
find_implementations(symbol: "<Type.Method or interface method>")
analyze_change_impact(symbol: "<Type.Method>")
System Monitor source maps and symbols for each edit target
stage bounded candidates through System Monitor
get_diagnostics after staged compile
```

Run `find_implementations` for async changes even when starting from a concrete method. If there are no implementations or derived contract impacts, record that and move on.

Caller conversion may recurse upward. Stop at an explicit boundary such as an event handler, UI command, background entry point, or public API where a sync wrapper is intentionally kept.

## Dashboard Evidence

A healthy run shows:

- Roslyn Tooling traffic for `list_solutions`, `tools/list`, `search_symbols`, `get_type_overview`, `find_references`, and related semantic tools.
- System Monitor traffic for status, source maps, symbol reads, staging, and decision recording.
- No direct watched-source writes outside System Monitor.

If a semantic C# task starts with grep/file search while Roslyn Tooling is connected, treat the dashboard as an audit signal and correction cue. The Operator may redirect the model back to Roslyn Tooling when practical, but this is not a hard runtime gate. Future hub enforcement should focus on staging gates for high-risk edits, not on blocking every search.
