# Roslyn Tooling Teaching Spec

This spec defines how MonitorBaseClaude should teach Claude/Codex to use Roslyn Tooling as compiler-backed navigation instead of falling back to text search.

## Problem

Agents naturally reach for file search because it is universal and easy:

```text
grep DatabaseRepository
read matching files
guess call sites
```

That is useful for literal text discovery, but it is not compiler-backed navigation. For C# semantic questions the preferred path is:

```text
search_symbols -> get_type_overview -> find_references/find_callers/analyze_change_impact
```

The current failure mode is usually not lack of tools. It is that the agent does not know the exact argument shape or how to obtain the argument safely. Example: `find_references` requires `symbol`, not `symbolName`.

Authoritative rule: prefer Roslyn Tooling over text or grep search for C# symbol discovery. Do not edit watched source directly. All watched-source changes go through System Monitor staging, and diagnostics should be checked after staged edits compile before Operator review.

Immediate edit-safety vocabulary:

- **Coupled staged files**: files that must validate together before the first review launch.

For async, signature, API, or multi-file edits, the agent should stage every coupled file into one monitor session before the first review launch. Staging must preserve encoding, newline mode, and surrounding trivia unless the Operator requested formatting cleanup. Selector failure or ambiguity is a hard stop, not permission to widen the edit.

## Teaching Model

First improve the live tool descriptions and result hints. A model should get a useful next step from `tools/list` and from the previous response before it needs a separate recipe document.

Every Roslyn tool exposed to agents should eventually have a compact teaching card, but cards are the fallback/reference layer, not the first lever:

- **intent**: what question this tool answers
- **required arguments**: exact names and types from schema
- **how to get arguments**: prerequisite calls or source of truth
- **good example**
- **bad example**
- **next calls**: likely follow-up tools, mirrored in live result hints where possible
- **System Monitor handoff**: when to switch to staging/symbol reads

This starts with concise tool descriptions and `suggestedNextCalls`-style result hints. If observed Claude runs still choose grep or wrong argument names, promote the same content into a `get_roslyn_tool_recipe` or `get_tool_argument_help` helper.

## Claude Code Budget Constraints

Claude Code's MCP behavior shapes how large these recipes should be. Source: <https://code.claude.com/docs/en/mcp>.

- tool output warning appears at roughly 10,000 tokens
- default MCP output maximum is roughly 25,000 tokens
- `MAX_MCP_OUTPUT_TOKENS` can raise the client-side maximum, but that should not be the normal design assumption
- tools can declare `_meta["anthropic/maxResultSizeChars"]` for intentionally large text outputs, up to the documented 500,000 character ceiling
- Tool Search is enabled by default, so tool schemas are deferred and loaded on demand
- server instructions and tool descriptions are truncated around 2KB, so critical usage hints must be early and compact

Design implication: do not teach Claude by dumping every schema, every manifest, and every example at once. Teach it with small recipe cards and task recipes that lead to the next call. Treat any large manifest as a human reference or disk artifact, not the normal model payload.

Preferred output budgets:

| Output | Target |
| --- | ---: |
| single tool recipe | under 1,500 words |
| task recipe | under 2,000 words |
| schema card | exact schema plus 1-3 examples |
| live source-map navigation | below warning threshold |
| broad reports | write artifact to disk and return summary/path |

If a tool result may exceed the warning threshold, return a compact summary plus a file path or staged artifact reference. Do not force Claude to ingest giant result payloads unless the user explicitly asks for a large audit.

Tool Search also changes naming pressure. The helper tools must be obvious from their names and descriptions because Claude may discover them by search rather than by reading the entire server contract. Put the highest-value phrases first:

- "Roslyn argument recipe"
- "find references"
- "find callers"
- "change impact"
- "compiler-backed navigation"
- "System Monitor staged edit handoff"

The model-facing helpers should answer one narrow question per call:

```text
get_roslyn_tool_recipe(toolName: "find_references")
get_roslyn_task_recipe(task: "make method async")
get_roslyn_argument_help(toolName: "find_references", argumentName: "symbol")
```

Avoid a default `get_all_roslyn_recipes` shape. If a full catalog is useful for the Operator UI, expose it in the dashboard or write it to disk and return a path.

TODO: run Claude acceptance passes after the description/hint pass. Only add wordier example helpers if the live descriptions and next-call hints do not reliably produce the intended Roslyn-first workflow within the budget limits.

## Required Argument Acquisition Ladder

Agents should acquire arguments in this order:

1. Read the tool schema.
2. Prefer canonical symbols from prior Roslyn results.
3. Use `search_symbols(query)` to discover candidate types/members.
4. Use `get_type_overview(typeName)` to confirm namespace, members, file, and diagnostics.
5. Construct member symbols as `Type.Member` or fully qualified `Namespace.Type.Member` when needed.
6. Use System Monitor `get_source_map(..., mode: "selector")` when the target is a staged edit.
7. Use System Monitor `get_symbol(symbolSelectorJson)` when a body is needed.
8. Only then fall back to file search/full-file reads if the semantic tools cannot identify the target.

## Core Tool Cards

### `search_symbols`

Intent: discover candidate types, methods, properties, and fields by name.

Entry rule: use this before text or grep search for C# symbol discovery unless the user explicitly asks for literal text search.

Schema:

```json
{
  "required": ["query"],
  "properties": {
    "query": "Search query, substring match against symbol names"
  }
}
```

How to get arguments:

- Use the user-provided type/member/class/interface name.
- Strip punctuation and keep the meaningful symbol token.
- For `DatabaseRepository.GetAll`, search `DatabaseRepository` first, then `GetAll` if needed.

Good:

```json
{"query":"DatabaseRepository"}
```

Bad:

```json
{"symbolName":"DatabaseRepository"}
```

Next calls:

- `get_type_overview(typeName)` for a selected type.
- `find_references(symbol)` for selected type/member.

### `get_type_overview`

Intent: confirm a type and get a one-call semantic overview: namespace, base/interface shape, members, dependencies, hierarchy, and file diagnostics.

Schema:

```json
{
  "required": ["typeName"],
  "properties": {
    "typeName": "Type name, simple or fully qualified"
  }
}
```

How to get arguments:

- Use a selected result from `search_symbols`.
- Prefer fully qualified names when search returns ambiguity.

Good:

```json
{"typeName":"SchemaStudio.Data.DatabaseRepository"}
```

Acceptable:

```json
{"typeName":"DatabaseRepository"}
```

Bad:

```json
{"symbol":"DatabaseRepository"}
```

Next calls:

- `find_references(symbol: "DatabaseRepository.GetAll")`
- `find_callers(symbol: "DatabaseRepository.GetAll")`
- System Monitor `get_source_map` for the file when preparing edits.

### `find_references`

Intent: find all references to a type, method, property, field, or event across the solution.

Edit gate: do not edit affected files until references are confirmed. Watched-source edits go through System Monitor only.

Schema:

```json
{
  "required": ["symbol"],
  "properties": {
    "symbol": "Simple type, fully qualified type, or member such as MyClass.MyProperty"
  }
}
```

How to get arguments:

- Use a type name confirmed by `search_symbols` / `get_type_overview`.
- For methods/properties, construct `Type.Member`.
- If overloaded or ambiguous, use the type overview and System Monitor source map to identify the intended member before editing.

Good:

```json
{"symbol":"DatabaseRepository.GetAll"}
```

Good for type-wide references:

```json
{"symbol":"SchemaStudio.Data.DatabaseRepository"}
```

Bad:

```json
{"symbolName":"DatabaseRepository.GetAll"}
```

Next calls:

- `find_callers(symbol)` for method call-site focus.
- `analyze_change_impact(symbol)` for signature changes.
- System Monitor `get_symbol` for each affected edit body.

### `find_callers`

Intent: find every call site for a method.

Async note: when converting a method to async, callers may need conversion too. Recurse up the caller chain until the boundary is understood.

Schema:

```json
{
  "required": ["symbol"],
  "properties": {
    "symbol": "Method name as Type.Method, simple or fully qualified"
  }
}
```

How to get arguments:

- Use `Type.Method` from `get_type_overview` member list.
- Use this for methods, not broad type usage.

Good:

```json
{"symbol":"DatabaseRepository.GetAll"}
```

Bad:

```json
{"symbol":"DatabaseRepository"}
```

Next calls:

- `get_call_graph(symbol, direction: "callers")` when depth matters.
- System Monitor `get_source_map` on caller files before staging updates.

### `analyze_change_impact`

Intent: inspect blast radius before renaming, changing signatures, removing symbols, or converting sync APIs to async.

Edit gate: impact results must drive System Monitor staging. Do not edit watched source directly.

Schema:

```json
{
  "required": ["symbol"],
  "properties": {
    "symbol": "Symbol name to analyze, type name or Type.Method"
  }
}
```

How to get arguments:

- Use the same canonical symbol string as `find_references`.
- Prefer this before API shape changes.

Good:

```json
{"symbol":"DatabaseRepository.GetAll"}
```

Bad:

```json
{"query":"DatabaseRepository.GetAll"}
```

Next calls:

- System Monitor source maps for every affected file that must be edited.
- `get_diagnostics` after staging or after a validation run if the solution state changed.

### `get_call_graph`

Intent: inspect transitive callers/callees for a method without recursively calling `find_callers` or `analyze_method`.

Schema:

```json
{
  "required": ["symbol"],
  "properties": {
    "symbol": "Method symbol, e.g. Greeter.Greet or Namespace.Type.Method",
    "direction": "callees, callers, or both; default callees",
    "maxDepth": "integer, default 3",
    "maxNodes": "integer, default 500"
  }
}
```

Good:

```json
{"symbol":"DatabaseRepository.GetAll","direction":"callers","maxDepth":2}
```

Use when:

- a method is part of a workflow chain
- async propagation may need to move through multiple levels
- you need to understand upstream/downstream flow

### `get_diagnostics`

Intent: compiler warning/error inventory across solution or project.

Validation note: run diagnostics after staged edits compile to confirm no new errors before Operator review.

Schema:

```json
{
  "properties": {
    "project": "optional project name filter",
    "severity": "optional minimum severity: error or warning",
    "includeAnalyzers": "boolean, default false"
  }
}
```

Good:

```json
{"severity":"error","includeAnalyzers":false}
```

Use when:

- validating current solution health
- checking post-edit fallout
- deciding whether existing diagnostics are unrelated

System Monitor remains the staged candidate compiler gate. Roslyn diagnostics are the semantic navigation/health view.

### `find_implementations`

Intent: find concrete implementers of an interface or derived types of a base class.

Contract note: also run this when changing a concrete method that may implement an interface or override a base member.

Schema:

```json
{
  "required": ["symbol"],
  "properties": {
    "symbol": "Type name, simple or fully qualified"
  }
}
```

Good:

```json
{"symbol":"IDatabaseRepository"}
```

Use when:

- changing an interface/base class
- editing virtual/abstract methods
- understanding dispatch paths

## Task Recipes

### Find References To A Method

```text
search_symbols(query: "DatabaseRepository")
get_type_overview(typeName: "SchemaStudio.Data.DatabaseRepository")
find_references(symbol: "DatabaseRepository.GetAll")
```

Stop here for a pure question. Do not use System Monitor staging.

### Make A Method Async

```text
search_symbols(query: "DatabaseRepository")
get_type_overview(typeName: "SchemaStudio.Data.DatabaseRepository")
find_references(symbol: "DatabaseRepository.GetAll")
find_references(symbol: "DatabaseRepository.SaveAll")
analyze_change_impact(symbol: "DatabaseRepository.GetAll")
System Monitor get_source_map for defining file
System Monitor get_source_map/get_symbol for affected caller files
System Monitor staged edits
System Monitor compile validation and review/hash gate
```

Do not stage until the call sites and propagation boundary are understood.

### Add A Local Null Guard

```text
System Monitor get_source_map(path, scope: "file", mode: "selector")
System Monitor get_symbol(symbolSelectorJson)
System Monitor submit_symbol
System Monitor review/hash gate
```

Roslyn Tooling is optional unless the guard changes public behavior or affects callers.

### Change An Interface

```text
Roslyn Tooling search_symbols(query: "IThing")
Roslyn Tooling find_implementations(symbol: "IThing")
Roslyn Tooling find_references(symbol: "IThing")
Roslyn Tooling analyze_change_impact(symbol: "IThing")
System Monitor source maps/symbol reads for every affected file
System Monitor staged edits
```

## Support Needed In Tooling

### 1. `get_roslyn_tool_recipe`

Proposed helper exposed by the WinForms host or docs surface:

```json
{"tool":"find_references"}
```

Returns:

- intent
- exact schema
- required arguments
- how to obtain each argument
- example calls
- common wrong calls
- recommended next tools

This can be generated from a curated recipe file plus live schema verification.

Budget rule: return one tool recipe per call. Do not return the full recipe catalog by default.

### 2. `get_roslyn_task_recipe`

Proposed helper:

```json
{"task":"make method async"}
```

Returns a call plan with tool names, argument templates, stop points, and System Monitor handoff.

Budget rule: return only the requested task recipe and one or two nearest alternatives.

### 3. Schema Cards In Dashboard

The Roslyn Tooling tab should make the selected tool's argument contract visible:

- required fields highlighted
- sample JSON
- "How to get this argument" text
- copyable good example
- warning for common wrong aliases, such as `symbolName` where `symbol` is required

Dashboard schema cards can be verbose because they are not necessarily injected into Claude's context. Claude-facing recipe outputs should remain compact.

### 4. Argument Resolver Prompts

For high-risk tools, provide explicit prompt snippets:

```text
Before calling find_references, obtain the symbol string from search_symbols or get_type_overview. Use the argument name "symbol".
```

### 5. Dashboard Acceptance Checks

Add smoke expectations that can be judged from live MITM traffic:

- reference task should produce `search_symbols`, `get_type_overview`, `find_references`
- async-change planning should produce references/impact plus System Monitor source-map reads
- no semantic task should start with grep/file search unless literal text search is requested
- no edit should stage before references/source-map/body reads

## Negative Rules

- Do not grep class names to find semantic references when Roslyn Tooling is connected.
- Do not guess argument names. Read the schema or recipe.
- Do not use `symbolName` for Roslyn reference tools; use `symbol`.
- Do not use `query` for `find_references`, `find_callers`, or `analyze_change_impact`; use `symbol`.
- Do not use external Roslyn code actions as the watched-source write path.
- Do not apply Roslyn code fixes directly to watched source. Code fixes are advisory; convert them into System Monitor staged candidates.
- Do not stage edits before semantic impact is understood for API/signature changes.
- Do not silently expand mutation scope from one selected symbol to whole-file or unrelated-symbol edits.
- Do not re-vote a `dirty-unexpected` decision. Recover explicitly by refresh/rebase/restage or Operator reconcile.

## Documentation Phase Note

Generated method summaries and feature data-flow maps are later documentation artifacts. They should be external to source and generated after stabilization. They are not part of the live Roslyn Tooling argument-acquisition path.
