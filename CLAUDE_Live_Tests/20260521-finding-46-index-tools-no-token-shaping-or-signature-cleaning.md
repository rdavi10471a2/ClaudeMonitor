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

The index family (`query_solution_index`, `find_indexed_symbols`, `get_solution_index`, `get_solution_index_tree`) returns responses that are **larger and dirtier than the equivalent source-map call** for the same scope. Two defects in combination cause this:

1. **No token-budget shaping.** Index responses do not include `estimatedTokenProxy`, `budgetLimit`, `wasTruncated`, or `suggestedNarrowing`. Capping via `maxSymbols` / `maxFiles` silently truncates without signal to the agent. By contrast, `get_source_map` budgets responses against an explicit `budgetLimit` and reports `wasTruncated` plus a narrowing path.
2. **Signatures are dirty.** Indexed symbol signatures include text that source maps consistently filter out: `AIFileContext` and `AIChange` attribute argument text, `#region` directives, leading comment blocks, and (worst case) commented-out code that gets extracted as a real symbol. Source maps honour the CLAUDE.md rule "AIFileContext and FileVersion remains visible; AIChange, AIHistory, AIInstructions, UserHistory are omitted." The index does not.

The combination means an agent following the documented "use the index for cheap discovery" guidance will spend more tokens than the existing path on the most common discovery scopes.

## Repro

Session `monitor-20260521130506-02a9a5e5b0aa41a1a`. Branch `claude-notes/20260521-pass21`, HEAD `6c315e5`.

### Token cost comparison

Same scope (namespace `SchemaStudio.Data`, 16 files, 219 symbols), two tools:

| Call | Bytes | ~tokens | Budget-shaped? |
|---|---|---|---|
| `query_solution_index(scope:namespace, value:"SchemaStudio.Data")` | 137,947 | ~34,487 | No |
| `get_source_map(scope:namespace, namespaceName:"SchemaStudio.Data", mode:navigation)` | 62,208 | ~15,552 (tool reports `estimatedTokenProxy: 13857` against `budgetLimit: 20000`, `wasTruncated: false`) | Yes |

Index response is 2.22× source map's response for the same data.

Capped variant `query_solution_index(…, maxSymbols:50, maxFiles:20)` returned ~45,000 chars inline but with **no `wasTruncated` flag, no `suggestedNarrowing`, no truncation signal of any kind.** A future agent doing this query would not know they got partial coverage.

### Signature pollution examples

All from `query_solution_index(scope:namespace, value:"SchemaStudio.Data", maxSymbols:50)`:

1. `Data\TargetScriptRepository.cs::TargetScriptRepository::field::_server`
   - Index signature: `"//------------------------------------------------------------ // SMO CACHE //------------------------------------------------------------ private Server? _server;"`
   - Source map signature would be: `"private Server? _server"`
2. `Data\TargetScriptRepository.cs::TargetScriptRepository::method::GetObjectScript(string,string,string)`
   - Index signature: `"#region Scripting Logic (DISPLAY ONLY) public string GetObjectScript(string databaseName, string schemaName, string objectName) { ... }"`
   - Region directive should not appear in a signature.
3. `Data\TargetScriptRepository.cs::TargetScriptRepository::method::UpsertColumnDescriptions(…)`
   - Index signature is **entirely commented-out code**: `"//public void UpsertObjectDescription( //    string databaseName, //    string schemaName, …"`
   - This is not a real callable symbol. It should not be indexed at all, or at minimum not surfaced with a signature derived from comment trivia.
4. `SchemaStudio.Data\DatabaseRepository.cs::DatabaseRepository`
   - Index signature is ~850 chars, ~750 of which are `[AIFileContext(…)]` and `[AIChange(…)]` attribute argument text. The actual class declaration is 14 chars. Identical pollution on `SchemaStudio.Data.DatabaseDefinition`.

Confirmed against the same content via `get_source_map` (see `20260521-test-result-index-tokens-head-to-head.md`): source map signatures for these symbols are clean.

## Expected

For the index to deliver its design intent ("cheap discovery via SQLite"), index responses should:

1. Include `estimatedTokenProxy`, `budgetLimit`, `wasTruncated`, and `suggestedNarrowing` fields with the same semantics as `get_source_map`. Cap namespace/solution responses against an explicit budget rather than the `maxSymbols`/`maxFiles` integer caps.
2. Strip `AIFileContext` and `AIChange`/`AIHistory`/`AIInstructions`/`UserHistory` attribute argument text from indexed signatures, identically to source map.
3. Strip `#region` / `#endregion` directives from signatures.
4. Strip leading comment blocks (`//` and `/* */`) that precede a symbol declaration.
5. Not extract commented-out code as a separate indexed symbol. The Roslyn syntax tree distinguishes a trivia-attached comment from a real declaration; the index extractor evidently uses a looser pass.

## Actual

See "Repro" above. None of (1)-(5) is currently honoured by the index.

## Suspected cause

Best guess (without reading the index-builder source): the index extractor reads the raw source text in a `[startLine, endLine]` window and stores that as the signature, instead of constructing a Roslyn-derived signature representation the way `get_source_map` does. That explains:

- Trivia (region, comments) sitting on adjacent lines gets included.
- Attribute argument text on the same logical declaration gets included.
- A comment block whose first line looks like a method declaration starts a fake symbol extraction.

If that's the cause, fixing it means switching the index extractor to a Roslyn-typed `SyntaxNode → signature-string` projection identical to what source map already uses, and applying the same attribute/region/comment filters.

## Minimal fix

Two paths, both yield real token wins:

1. **Cheaper** — add budget shaping and the trivia/attribute filter to the existing index extractor. Keep the index as a parallel agent-facing surface but make it abide by the same response contract as source map.
2. **Higher leverage** — make the index the implementation backbone of `find_file`, `get_source_map(scope:project|folder|namespace, mode:navigation)`, and Roslyn `search_symbols` (where freshness allows). The agent sees one discovery surface, the engine picks the cheapest valid source. Keep `get_indexed_symbol(stableSymbolKey)` exposed as a primitive cheap lookup; demote the rest of the current index API to internal-only.

Option 2 is the architecturally clean answer because it removes the "which discovery tool gives the better answer" decision from every pre-flight and centralises the budget/trivia filtering rules. It also addresses the orthogonal complaint in CLAUDE.md that the agent is supposed to "prefer get_source_map before full get_file for C# edits" — if get_source_map itself can be index-backed, that rule continues to deliver wins without a second-tier index API.

Option 1 is the strict minimum to make the standalone index a defensible choice. Without it, the documented "index-first" discovery path increases token cost.

## Severity

**confusing** — the tool surface is misleading. An agent reading the CLAUDE-memory entry `project_solution_index_tools.md` and the tool manifest would reasonably conclude the index is the cheap discovery layer. The measured reality on common scopes is that it costs more tokens than the existing path. The tool will not return wrong information (signatures are dirty but truthful; truncation is silent but the rows returned are real); it will silently return more tokens than necessary and hide truncation from the agent.

Promoting severity to **blocker** would be appropriate if the next round of work was going to depend on the index in production agent paths. For doc-only and rehearsal passes (Pass 21 included), the existing fallback to `get_source_map` is a safe out.

## Notes

- See pass-21 evidence note for the full head-to-head transcript: [20260521-test-result-index-tokens-head-to-head.md](20260521-test-result-index-tokens-head-to-head.md).
- Related memory: `project_solution_index_tools.md` (Claude-side auto-memory) — describes the index family as "discovery only" with a freshness-hop rule. The descriptor is correct; this finding is about the implementation, not the contract.
- The freshness-hop rule itself is fine and remains required regardless of which fix lands. Integration would just collapse the discovery layer; mutation prep still goes `discovery → freshness hop → get_symbol → stage`.
