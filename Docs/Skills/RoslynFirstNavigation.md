# Roslyn First Navigation

Use when a C# task mentions symbols, references, callers, implementations, diagnostics, dependencies, or change impact.

## Rules

- Prefer Roslyn Tooling over text or grep search for C# symbol discovery.
- Use text search only for literal text, comments, strings, generated artifacts, non-C# files, or Roslyn failure fallback.
- Do not guess argument names. Read `tools/list` or the live schema.
- Start broad, then narrow: symbol search, type overview, references/callers/impact.
- Apply this per target file or edit cycle. Do not shortcut with "I already discovered this earlier" when the target file or coupled edit set changes.
- Treat empty `find_references` / `find_callers` as a result to verify, not proof of absence, before API renames or signature changes.
- Before writing a call site to a referenced type, load that type's real callable surface with `search_symbols` and `get_type_overview` or equivalent Monitor source-map reads.
- If overlay/build diagnostics expose a missed call site that Roslyn did not find, use text search only as a diagnostic fallback, then return to Roslyn/Monitor structure and stage the missed file in the same session.

## Usual Flow

```text
search_symbols(query)
get_type_overview(typeName)
find_references(symbol)
find_callers(symbol), when behavior/signature changes
find_implementations(symbol), when contracts/overrides may matter
analyze_change_impact(symbol), before API/signature changes
```

Argument reminders:

- `search_symbols` uses `query`.
- `get_type_overview` uses `typeName`.
- Reference/caller/impact tools use `symbol`.

Long reference: `Docs/AgentToolCallPlaybook.md`.
