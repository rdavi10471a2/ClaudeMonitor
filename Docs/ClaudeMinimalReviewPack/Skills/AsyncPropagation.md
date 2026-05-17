# Async Propagation

Use when making a C# method async, changing a signature, or updating callers.

## Rules

- Do not stage before references/callers/implementations are understood.
- Caller conversion may recurse upward through the call chain.
- Stop async propagation at explicit boundaries: UI event handlers, commands, background entry points, public API sync wrappers, or Operator-designated boundaries.
- At a stop boundary: stage all conversions up to that point, note the boundary in manifestJson, and surface to the Operator before propagating further.
- Run diagnostics after staged edits compile.
- For multi-file propagation, stage all coupled files in one monitor session before the first review launch.

## Usual Flow

```text
search_symbols(query)
get_type_overview(typeName)
find_references(symbol)
find_callers(symbol)
find_implementations(symbol)
analyze_change_impact(symbol)
System Monitor source maps/symbols for each edit target
stage bounded candidates through System Monitor
get_diagnostics after staged compile
```

Long reference: `Docs/AgentToolCallPlaybook.md`.
