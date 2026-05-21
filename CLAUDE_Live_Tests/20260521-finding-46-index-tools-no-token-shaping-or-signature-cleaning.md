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

The index family (`query_solution_index`, `find_indexed_symbols`, `get_solution_index`, `get_solution_index_tree`) returns responses that have **token-budget and signature-extraction defects** absent from the equivalent source-map call:

1. **No token-budget shaping.** Index responses do not include `estimatedTokenProxy`, `budgetLimit`, `wasTruncated`, or `suggestedNarrowing`. Capping via `maxSymbols` / `maxFiles` silently truncates without signal to the agent. By contrast, `get_source_map` budgets responses against an explicit `budgetLimit` and reports `wasTruncated` plus a `suggestedNarrowing` list.
2. **Signatures contain extraction trivia and redundant data.** Specifically: `#region` / `#endregion` directives leaked into signatures; leading `//` comment blocks concatenated into signatures; commented-out code occasionally extracted as a real indexed symbol; and `AIChange` / `AIHistory` workflow attributes that source map strips per CLAUDE.md. There is also one redundancy issue — the 64-char `fileHash` is emitted on every symbol row even though the same hash is already in `files[].sha256` (12% of the response).

**Important correction from an earlier draft of this finding:** the decorative `[Browsable]`, `[Category]`, `[DisplayName]`, `[PropertyOrder]`, `[ValueRequired]`, `[TextLength]`, `[Required]`, `[Key]`, `[Obsolete]`, etc. attributes are **contract**, not pollution. Source map keeps them; the index should too. An agent doing mutation needs to see them to follow file conventions (PropertyGrid ordering, validation rules, ORM mapping). The earlier recommendation to "strip all attribute brackets" was wrong and is retracted in the "Expected" section.

With these defects, the documented "use the index for cheap discovery" guidance can mislead. At narrow scopes (file, single-symbol) the index is competitive or cheaper. At broad scopes (namespace, solution) the index returns more data than `get_source_map(navigation)` because the source map's navigation mode intentionally strips signatures for that scope — they're measuring different things. The fair contrast is `query_solution_index(namespace)` vs `get_source_map(scope:namespace, mode:detail)`. When that contrast is run (see the test result note), the source map refuses to render at all (`wasTruncated:true` against the 20k budget, would have been ~40,531 tokens) — proving that **for full per-symbol detail in a namespace, the index's one-shot is the right shape**, just with the defects above blocking it from being usable.

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
2. Strip the **AI workflow-history attributes only** — `AIChange`, `AIHistory`, `AIInstructions`, `UserHistory` — from indexed signatures, identically to source map. Per the manifest, source map already strips these. AI attributes are only ~1.5% of the response so this is small, but the principle matters for parity.
3. Strip `#region` / `#endregion` directives from signatures.
4. Strip leading comment blocks (`//` and `/* */`) that precede a symbol declaration.
5. Not extract commented-out code as a separate indexed symbol. The Roslyn syntax tree distinguishes a trivia-attached comment from a real declaration; the index extractor evidently uses a looser pass.
6. **Dedupe redundant per-symbol `fileHash`** — the file-level `sha256` in `files[]` already carries this value; emitting it again on every symbol row costs ~16,863 bytes (12% of the response) for zero new information.

**What NOT to strip:** keep **contract attributes** — `[Browsable]`, `[Category]`, `[DisplayName]`, `[Description]`, `[PropertyOrder]`, `[ValueRequired]`, `[TextLength]`, `[Required]`, `[Key]`, `[Column]`, `[NotMapped]`, `[DBIgnore]`, `[Obsolete]`, `[TypeConverter]`, `[DefaultProperty]`, `[ReadOnly]`, `[CallerMemberName]`, `[AIFileContext]`, `[FileVersion]`, etc. These are part of the symbol's API contract and an agent doing mutation needs to see them to follow convention (PropertyGrid ordering, validation, ORM mapping). Source map keeps these. The index should too. An earlier draft of this finding recommended stripping all attribute brackets — that was wrong and is retracted here.

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

**confusing** — the tool surface has fixable defects (budget shaping + trivia in signatures + redundant fileHash). The data returned is truthful; truncation is silent but the rows returned are real. Fix-the-defects rather than abandon-the-API is the right move. The bulk-discovery niche the index was designed for is real (per the test-result addendum, source-map detail-mode refuses to render the same scope because it would cost ~40k tokens; the index could fit it in ~29k after the defects are fixed).

Promoting severity to **blocker** would be appropriate if the next round of work was going to depend on naïve namespace-scope index queries in production agent paths. For Pass 21's index-first chain at file scope, the index is already usable end-to-end (the freshness hop works, the stable selectors compile against `get_symbol` + `submit_symbol`, the staging chain works).

## Notes

- See pass-21 evidence note for the full head-to-head transcript: [20260521-test-result-index-tokens-head-to-head.md](20260521-test-result-index-tokens-head-to-head.md).
- Related memory: `project_solution_index_tools.md` (Claude-side auto-memory) — describes the index family as "discovery only" with a freshness-hop rule. The descriptor is correct; this finding is about the implementation, not the contract.
- The freshness-hop rule itself is fine and remains required regardless of which fix lands. Integration would just collapse the discovery layer; mutation prep still goes `discovery → freshness hop → get_symbol → stage`.
