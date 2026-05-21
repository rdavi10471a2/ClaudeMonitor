---
status: new
type: finding
created: 2026-05-21
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---

## Summary

The Roslyn-symbol-info-in-DB direction (Finding 46 / WinForms test result / Finding 47) closes declaration-search failures by construction — every class/interface/method/property/field declared in every parsed syntax tree gets a deterministic index row. But the DBV2 monitor has historically been bitten by a *different* class of search failure: `find_references` and `find_callers` returning empty for symbols that demonstrably have cross-file usages (Finding 11 is the canonical case).

To eliminate those failures, the index schema needs to extend beyond declarations. Two additional tables, populated by the same build-time Roslyn walk:

1. **`symbol_references`** — every `IdentifierName` / `MemberAccess` / `NameSyntax` node that resolves to a known declaration via the semantic model, stored as `(symbol_id, file, line, column, reference_kind)`.
2. **`call_sites`** — every `InvocationExpressionSyntax` whose target resolves to a known method symbol, stored as `(method_symbol_id, caller_file, caller_line, caller_method_symbol_id?)`.

With those tables in place, `find_references(symbol)` becomes `SELECT * FROM symbol_references WHERE symbol_id = ?` — deterministic, no workspace-loading timing risk, no false negatives from stale semantic models.

## Repro / context

Two pre-existing findings document the today behavior:

- **Finding 11** (per `CLAUDE.md` writable-lane reference) — `find_references` returned empty for a field that had a demonstrable usage in another file. The pass author fell back to grep/text search to find the missed reference, which is exactly the doctrine-violating workaround `CLAUDE.md` warns against (CLAUDE.md "Discovery Discipline" section: "Roslyn discovery negative results … must be treated as 'discovery may be incomplete', not as 'no consumers exist'.")
- **Finding 15** (referenced in `CLAUDE.md`) — the downstream cost of treating an empty `find_references` as authoritative: single-file rename WriteSet missed consumer call sites, watched build broke post-accept.

Roslyn live queries fail for several reasons that an index built at build time would not share:

- Workspace-load timing (first query after a workspace reload often returns empty until projects finish loading)
- Cross-project references where the dependent project's compilation hasn't completed
- Stale semantic models that haven't been invalidated after an edit
- Partial classes where one partial fragment didn't parse

These are *runtime state* failures, not algorithmic failures. A reference walk performed once over a successfully-built workspace, with results persisted to SQLite, doesn't have a "workspace state at query time" — the walk already completed when the build succeeded.

## Expected

Schema additions (illustrative, not prescriptive):

```sql
CREATE TABLE symbol_references (
    symbol_id        INTEGER NOT NULL,  -- foreign key to existing symbols table
    file_id          INTEGER NOT NULL,
    line             INTEGER NOT NULL,
    column           INTEGER NOT NULL,
    reference_kind   TEXT    NOT NULL,  -- 'read', 'write', 'invocation', 'typeof', 'inherits', 'implements', etc.
    PRIMARY KEY (symbol_id, file_id, line, column)
);

CREATE INDEX idx_references_by_symbol ON symbol_references (symbol_id);
CREATE INDEX idx_references_by_file   ON symbol_references (file_id);

CREATE TABLE call_sites (
    callee_symbol_id INTEGER NOT NULL,
    caller_symbol_id INTEGER,            -- nullable for top-level / static-init call sites
    file_id          INTEGER NOT NULL,
    line             INTEGER NOT NULL,
    column           INTEGER NOT NULL,
    PRIMARY KEY (callee_symbol_id, file_id, line, column)
);

CREATE INDEX idx_calls_by_callee ON call_sites (callee_symbol_id);
CREATE INDEX idx_calls_by_caller ON call_sites (caller_symbol_id);
```

Reference walker pseudocode:

```pseudocode
for syntaxTree in workspace.SyntaxTrees:
    semanticModel = compilation.GetSemanticModel(syntaxTree)
    for node in syntaxTree.GetRoot().DescendantNodes():
        if node is IdentifierName or MemberAccessExpression:
            symbol = semanticModel.GetSymbolInfo(node).Symbol
            if symbol and symbol.IsInSource():
                emit symbol_references(symbol_id_for(symbol), file_id, line, col, classify_reference(node))
        if node is InvocationExpressionSyntax:
            symbol = semanticModel.GetSymbolInfo(node).Symbol
            if symbol is IMethodSymbol and symbol.IsInSource():
                enclosing = closest enclosing method/property symbol
                emit call_sites(symbol_id_for(symbol), symbol_id_for(enclosing), file_id, line, col)
```

This is the same walk a Roslyn `SymbolFinder.FindReferencesAsync` does, but performed once over the stable build state and persisted.

New (or replacement) MCP tools the agent calls:

- `find_indexed_references(stableSymbolKey, maxResults?)` — returns all reference sites.
- `find_indexed_callers(methodStableSymbolKey, maxResults?)` — returns all call sites.

Existing Roslyn `find_references` / `find_callers` either get rerouted through the DB or get demoted to a "live fallback" path used only when the index file hash for the target symbol's declaring file doesn't match watched (freshness-hop violation).

## Actual

Today's index stores **declarations only**. `find_indexed_symbols(text:"X")` returns declared symbols matching the name. There is no per-symbol reverse index of usages. Agents that need to find usages still go through Roslyn's live API and inherit its workspace-state failure modes.

## What this fixes (closing the loop with prior findings)

- **Finding 11 root cause** — false-negative `find_references` results — is structurally eliminated by the persisted reference table. The walker runs once per build; queries are SQL lookups.
- **Finding 15 downstream cost** — missed consumer call sites in a rename WriteSet — becomes recoverable because the rename agent can rely on the DB's reference table.
- **The CLAUDE.md "discovery discipline" rule** ("Roslyn discovery negative results must be treated as 'may be incomplete'") can be RETIRED for the references/callers case once the index covers them. Today the rule exists because Roslyn's negative results are unreliable; with a deterministic build-time walk, they become reliable.

## What this doesn't fix (inherent static-analysis limits)

These remain agent-side concerns regardless of which tool serves the query:

- Reflection-based usage (`Type.GetMethod("X")`)
- DI/IoC string lookups (`services.Get<I>()` via assembly scan, attribute-based registration with deferred resolution)
- Cross-language references (Razor, XAML, .resx, T4 templates, external `*.json` config)
- Dynamic invocation, expression trees built from strings
- Generated-code references where the generator emits AFTER index build

For these, the agent still needs to apply judgement. The index merely promises that **what Roslyn could in principle see** is reliably reported. What Roslyn semantically cannot see, no tool can.

## Minimal fix

1. Add `symbol_references` and `call_sites` tables to the SQLite schema.
2. Extend the index builder's Roslyn walk to populate them (described above).
3. Expose `find_indexed_references` and `find_indexed_callers` as MCP tools.
4. Either reroute Roslyn `find_references` / `find_callers` through the DB, or keep both with the DB as primary and live Roslyn as fallback when freshness is unverifiable.
5. Update `CLAUDE.md` "Discovery Discipline" guidance: empty index-references results are reliable (when index hash matches watched); empty Roslyn live results remain "may be incomplete."

## Severity

**suggestion** — the index works correctly for declarations today. This finding is about extending its scope to close a *separate* class of search failure that has bitten prior passes. Severity could be promoted to **confusing** if pass evidence accumulates of agents still hitting empty `find_references` results in production work; today it's primarily a forward-looking improvement.

## Notes

- Depends on Finding 46's Roslyn-derived-extractor direction. The reference walk and the declaration walk should share the same Roslyn workspace pass for efficiency.
- Pairs with Finding 47 (auto-refresh on accept). After an accept the file's declarations refresh; the references walk for that file should also re-run because new code may have added/removed references. Per-file refresh is more expensive than declaration-only refresh, but still cheap relative to a full solution rebuild.
- The references table also enables call-graph queries (`get_call_graph` in Roslyn CodeLens has a similar shape) and rename-impact analysis. Worth scoping these as natural follow-ons once the table exists.
