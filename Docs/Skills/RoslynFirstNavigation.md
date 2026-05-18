# Roslyn First Navigation

Use when a C# task mentions symbols, references, callers, implementations, diagnostics, dependencies, or change impact.

## Rules

- Prefer Roslyn Tooling over text or grep search for C# symbol discovery.
- Use text search only for literal text, comments, strings, generated artifacts, non-C# files, or Roslyn failure fallback.
- Do not guess argument names. Read `tools/list` or the live schema.
- Start broad, then narrow: symbol search, type overview, references/callers/impact.
- Apply this per target file or edit cycle. Do not shortcut with "I already discovered this earlier" when the target file or coupled edit set changes.
- Treat empty `find_references` / `find_callers` as a result to verify, not proof of absence, before API renames or signature changes.

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
