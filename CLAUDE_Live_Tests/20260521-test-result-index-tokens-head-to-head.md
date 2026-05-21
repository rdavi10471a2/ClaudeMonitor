---
title: Index-tools vs existing-tools — token-usage head-to-head
date: 2026-05-21
branch: claude-notes/20260521-pass21
session: monitor-20260521130506-02a9a5e5b0aa41a1a
status: passed-with-1-new-finding
---

# Index-tools vs existing-tools — token-usage head-to-head

Operator question: *can the new index be used as-is, or does it need to be integrated into the existing tools? With attention to whether the new index, when used, will decrease token usage.*

## Test design

Pick a realistic discovery question and solve it twice, once with the new index tools alone, once with the existing discovery tools. Measure response sizes (rough tokens = chars / 4) and verifier-quality of the result.

**Question:** "List all classes in the watched DBV2 solution whose name ends with `Repository`, and report whether each one has an `Async`-named method."

This is the shape of a real pass pre-flight — the kind of question an agent asks before deciding which file to open and stage.

## Path A — index-only

| # | Call | Args | Response size | ~tokens |
|---|---|---|---|---|
| A1 | `find_indexed_symbols` | `text:"Repository", kind:"class"` | 4,815 chars | ~1,204 |
| A2 | `query_solution_index` | `scope:"namespace", value:"SchemaStudio.Data"` | **137,947 chars** | **~34,487** |

A2 exceeded the harness inline-output budget and was dumped to a `tool-results/*.txt` file requiring chunked reading to summarize. Functionally unusable in one shot.

Capped variant `query_solution_index(scope:namespace, maxSymbols:50, maxFiles:20)` returned ~45,000 chars inline but **silently truncates** — no `wasTruncated` flag, no narrowing suggestion, no signal to the agent that they got partial coverage.

## Path B — existing tools

| # | Call | Args | Response size | ~tokens |
|---|---|---|---|---|
| B1 | Roslyn `search_symbols` | `query:"Repository"` | 3,134 chars | ~784 |
| B2 | `get_source_map` | `scope:"namespace", namespaceName:"SchemaStudio.Data", mode:"navigation"` | 62,208 chars | ~15,552 (tool reports `estimatedTokenProxy: 13857` against `budgetLimit: 20000`, `wasTruncated: false`) |

B1 returns noise (6 field references, 6 duplicate class entries with `file:""` and `line:0`, 1 EPPlus metadata false-positive on top of the 7 real class hits) but is half the size of A1. B2 is **2.22× smaller** than A2 for the same scope on the same data and self-reports against an explicit budget.

## File-scope drill (the realistic mutation-prep scope)

| Call | Response size | ~tokens | Notes |
|---|---|---|---|
| `query_solution_index(scope:"file", value:"…DatabaseDomainRepositoryAsync.cs")` | ~5,650 chars | ~1,413 | Leaner per-symbol: 11 fields per symbol |
| `get_source_map(scope:"file", mode:"selector")` | ~10,000 chars (reports `estimatedTokenProxy: 2188`) | ~2,188 | Richer per-symbol (modifiers, **structured `isAsync: true` flag**, returnType, parameterTypes, parameterNames) plus 8 `suggestedNextCalls` |

At file scope the two tools are within a small factor of each other. The index is leaner per row; the source map carries more structured signal per row plus suggestion bloat. **The index "wins" by ~30-40% on raw bytes but loses on structured-data quality** (signature string-matching for `async` vs. `isAsync: true` boolean).

## Signal-quality problems in the index payload

Three concrete quality issues observed in `query_solution_index` payloads that would force an agent into extra work or wrong conclusions:

1. **Comments and `#region` directives leak into signatures.** Examples from `Data\TargetScriptRepository.cs`:
   - `_server` field signature: `"//------------------------------------------------------------ // SMO CACHE //------------------------------------------------------------ private Server? _server;"` — the SMO header comment block is concatenated into the field signature.
   - `GetObjectScript` method signature begins with `"#region Scripting Logic (DISPLAY ONLY) public string GetObjectScript(…)"` — region directive rolled into signature.
2. **Commented-out code is extracted as a real symbol.** `Data\TargetScriptRepository.cs::UpsertColumnDescriptions` exists in the index with a signature that is entirely commented-out code (`"//public void UpsertObjectDescription( //    string databaseName, …"`). Source map omits this.
3. **`AIFileContext` / `AIChange` attribute text dominates class signatures.** `SchemaStudio.Data.DatabaseRepository` signature in the index is ~850 chars, of which ~750 are attribute argument text. Source map's documented contract (CLAUDE.md "AIFileContext and FileVersion remains visible in source-map output; legacy workflow-history attributes such as AIChange, AIHistory, AIInstructions, and UserHistory are omitted") is honoured by `get_source_map` and ignored by `query_solution_index`. Same goes for `DatabaseDefinition`.

These three classes of pollution apply to source-map at file-scope as well in principle, but the source map's filter rules consistently strip them. The index does not.

## Summary table (token economy)

| Scope | Index path | Source-map / Roslyn path | Index ratio | Verdict |
|---|---|---|---|---|
| Symbol name search (class-kind, "Repository") | 1,204 tokens | 784 tokens | 1.54× | **Existing wins** — index has `AIFileContext` bloat in signatures |
| Namespace navigation (16 files, 219 symbols) | 34,487 tokens (un-shaped) | 13,857 tokens (budget-shaped) | 2.49× | **Existing wins decisively** — index is uncapped, source map self-shapes |
| File selector inspection | ~1,413 tokens | ~2,188 tokens | 0.65× | **Index wins on bytes**, source map wins on structured `isAsync` flag |

## Answer to the operator's question

**The new index, used naively, INCREASES token usage on the common discovery paths an agent would actually take.** The only scope where the index is cheaper in bytes is single-file inspection — and at that scope the agent normally needs `get_source_map(file, selector)` anyway because it produces the freshness-hopped stable selectors required for mutation. So the index doesn't save tokens on the mutation path; it adds a tool the agent has to remember.

To deliver on the promise of "cheap discovery via SQLite," the index needs one of:

1. **Token-budget shaping parity with `get_source_map`** — add `estimatedTokenProxy`, `budgetLimit`, `wasTruncated`, and `suggestedNarrowing` to all index responses. Cap the namespace/solution scopes against an explicit budget. Strip `AIFileContext`/`AIChange`/`AIHistory` attribute text and `#region` / comment trivia from indexed signatures so the index produces the same shape source maps already do.
2. **Integration as the implementation backbone of existing tools** (preferred) — `find_file`, `get_source_map(scope:project|folder|namespace, mode:navigation)`, and `search_symbols` (Roslyn) become index-backed when the index is fresh, with the same budget/truncation/suggestion contract. The agent sees one discovery surface, the engine picks the cheapest valid source. `get_indexed_symbol(stableSymbolKey)` stays exposed as a primitive lookup; the rest of the index API becomes internal.

Both fixes deliver real token wins. Option 2 is preferred because it removes a parallel surface the agent has to know about and removes the "which tool gives the better answer" decision from every pre-flight. The current standalone surface is in the worst-of-both-worlds: it's available, it looks cheap, and naïve use of it costs more than the existing path.

## Recommendation

Treat `find_indexed_symbols`, `query_solution_index`, `get_solution_index`, and `get_solution_index_tree` as **internal implementation tools, not Tier-1 agent tools**. Reroute `find_file` and `get_source_map(navigation)` through them where freshness allows. Keep `get_indexed_symbol`, `refresh_solution_index*`, and `get_solution_index_status` as agent-visible operations (the first for narrow cheap lookups, the rest for maintenance). Until that integration ships, document the budget gap so the agent doesn't choose the index by default for navigation work.

[Finding 46](20260521-finding-46-index-tools-no-token-shaping-or-signature-cleaning.md) records the concrete defects (budget shaping, signature pollution) that need to be fixed before integration is worth attempting.

## Open items

- This test did not exercise `get_solution_index_tree` directly. It is the smallest-response shape in the index family and could be the cheap-navigation foothold the index family needs. Worth a separate measurement.
- This test did not measure latency. The index is local SQLite (fast), source map is live Roslyn (slower for cold-cache hits). For an agent in a long session the latency difference may be small relative to round-trip time; this is a follow-up measurement.
- This test did not exercise the freshness-hop interaction. If the integration option (option 2 above) lands, the freshness-hop rule still applies between discovery and mutation; integration only collapses the discovery layer.

## Addendum — pollution-subtracted ("ignoring the AI attributes") comparison

Operator follow-up: *if you ignore the AI attributes, is the index actually better?*

Short answer: **the AI attributes alone are not the main cost. Even with every defensible cleanup applied, the index is still ~2× source map at navigation scope — but the index DOES win on bulk-discovery scope by ~49%.** The right framing isn't "index vs source-map," it's "which task shape does each tool win on?"

### What's in the index pollution, by actual byte cost

Measured against the 137,947-byte persisted `query_solution_index(scope:namespace, value:"SchemaStudio.Data")` response:

| Category | Bytes | % of response |
|---|---|---|
| `AIFileContext` attribute argument text (2 occurrences) | 1,454 | 1.05% |
| `AIChange` attribute argument text (2 occurrences) | 397 | 0.29% |
| `AIHistory` / `AIInstructions` / `UserHistory` (1 occurrence) | 189 | 0.14% |
| **AI attributes subtotal** | **2,040** | **1.48%** |
| Decorative attributes (`[Browsable]`, `[Category]`, `[DisplayName]`, `[PropertyOrder]`, `[ValueRequired]`, etc.) — 169 occurrences | 8,123 | 5.89% |
| ` { ... }` body placeholders (148 occurrences) | 1,184 | 0.86% |
| ` => ...` expression-body placeholders (16 occurrences) | 192 | 0.14% |
| Redundant per-symbol `fileHash` (64-char hex, 219 occurrences) — same hash is already in the per-file `sha256` field | 16,863 | 12.22% |
| **Total cleanable overhead** | **28,402** | **20.59%** |

So the **AI attributes are 1.5%, not the main cost.** The bigger costs are the decorative attribute brackets (which the source map's "compact contract signatures" rule strips) and the redundant per-symbol `fileHash` (which is structurally avoidable — the file's hash is already in `files[]`).

### Cleaned ratio at namespace scope

| Tool | Bytes | Cleaned bytes | ~tokens |
|---|---|---|---|
| `query_solution_index(scope:namespace)` | 137,947 | 109,545 | ~27,386 |
| `get_source_map(scope:namespace, navigation)` (inner JSON) | 55,432 | 54,065 | ~13,516 |

**Even fully cleaned, index is 2.03× source map at navigation scope.** Why? Source map navigation is intentionally low-density: it returns parse status, namespaces, file shape, and class/member names with line spans, but **omits signatures, hashes, modifiers, and parameter info** at this scope. The index returns full detail at every scope. They're not measuring the same thing.

### When the index actually wins: bulk discovery

If the agent's task needs per-symbol detail (signatures, hashes, stable selectors) **across an entire scope**, the index's one-shot beats source-map's navigate-then-drill:

| Path | Bytes | ~tokens |
|---|---|---|
| `query_solution_index(scope:namespace)` (cleaned) | 109,545 | ~27,386 |
| `get_source_map(navigation)` + 16 × `get_source_map(file, selector)` (~10K each) | 215,432 | ~53,858 |

**For bulk discovery, cleaned index is 49% cheaper than nav+drill.** This is the real niche the index family was designed for and it's a real win once the cleanup ships.

### Refined verdict

- For **"show me the project structure"** (no symbol detail needed): source map or `get_solution_index_tree` wins. The tree returns ~5,400 bytes for the whole 84-file solution — by far the cheapest shape, no signatures, just namespace → file mapping.
- For **"find symbols matching name X"**: Roslyn `search_symbols` wins (more compact than `find_indexed_symbols` even after AI cleanup, because the AI bloat lives in *signatures* and `search_symbols` returns no signature).
- For **"give me all symbols + stable keys + signatures in this scope at once"**: cleaned index wins by ~49%. Current dirty index is borderline (eaten by the 20% pollution overhead and budget gaps).
- For **"inspect this one file"**: source map (selector) wins on data quality (structured `isAsync`, modifiers, parameter names). Index wins on raw bytes but ties or loses once the agent has to string-match signatures.

So the index isn't a slam-dunk loss. It's a tool with a real niche that today's implementation **fails to deliver on** because the response is 20% pollution and has no budget shaping. Clean it up and the bulk-discovery win is real.

### Updated recommendation

Keep two index tools agent-visible:

1. **`get_solution_index_tree`** — cheapest project-shape map, ~5,400 bytes for the whole solution. Genuinely useful when source-map navigation at project scope would be overkill.
2. **`get_indexed_symbol(stableSymbolKey)`** — cheapest single-symbol lookup primitive.

Demote `query_solution_index` / `find_indexed_symbols` to internal use for now. Before re-exposing them as agent-visible, fix Finding 46 defects (budget shaping + dirty signatures + redundant hash dedup). Once fixed, **they earn their place as the bulk-discovery option that source map navigation can't match.**

Or — preferred — integrate `query_solution_index` as the backend for a new `get_source_map(scope:namespace, mode:detail)` density that exposes the bulk-discovery shape through the existing tool surface. Same budget contract, same trivia/attribute filter rules, no parallel API to learn. Agent picks density via `mode`, engine picks storage source.

## Addendum 2 — apples-to-apples comparison (the operator's "doesn't Claude need the attributes" check)

Operator follow-up: *doesn't Claude need to know the attributes on a field/class?*

Yes. The earlier "strip all attribute brackets" recommendation was wrong and is retracted. Decorative-looking attributes like `[Browsable]`, `[Category]`, `[DisplayName]`, `[PropertyOrder]`, `[Description]`, `[Required]`, `[ValueRequired]`, `[Key]`, `[Column]`, `[Obsolete]`, `[ReadOnly]`, etc. are part of the symbol's contract:

- PropertyGrid behavior depends on `[Browsable]`, `[Category]`, `[DisplayName]`, `[PropertyOrder]`, `[Description]`. Adding a new property without these (when the file's convention uses them) produces a property that displays incorrectly.
- Validation depends on `[Required]`, `[StringLength]`, `[ValueRequired]`, `[TextLength]`, `[Range]`. Adding a property without the right validation contract changes runtime behavior.
- ORM mapping depends on `[Key]`, `[Column]`, `[NotMapped]`, `[ForeignKey]`, `[DBIgnore]`. Wrong attribute → wrong column → silent persistence bug.
- Deprecation depends on `[Obsolete]`. An agent calling an obsolete member without warning is a real bug.
- CLAUDE.md specifically requires `[AIFileContext]` and `[FileVersion]` to be preserved and updated.

So an agent reading a file before mutation needs to see these attributes. Stripping them would make the index unsafe for mutation prep.

Source-map's documented rule (from the tool manifest) is correct and narrower than what I initially recommended:

> "Durable file-header metadata such as `AIFileContext` and `FileVersion` remains visible in source-map output; legacy workflow-history attributes such as `AIChange`, `AIHistory`, `AIInstructions`, and `UserHistory` are omitted. All attributes remain untouched in source files…"

So source map keeps **all** attributes EXCEPT the four workflow-history ones (`AIChange`, `AIHistory`, `AIInstructions`, `UserHistory`). My earlier byte tally lumped `[Browsable]/[Category]/…` (8,123 bytes) into "pollution" — those are not pollution and should NOT be stripped. The honest cleanup is much narrower:

| Cleanup (revised) | Bytes saved | % |
|---|---|---|
| `AIChange` / `AIHistory` / `AIInstructions` / `UserHistory` attribute argument text | ~586 (the AIChange 397 + AIHistory 189 measured earlier; `AIFileContext` 1,454 STAYS per the manifest) | 0.4% |
| Trivia leaked into signatures (`#region`, leading `//` comments, commented-out code extracted as a symbol) | ~600 bytes (small but high-impact for parser correctness) | 0.4% |
| ` { ... }` body placeholders (debatable — keep for visual readability or strip for tokens) | 1,184 | 0.9% |
| Redundant per-symbol `fileHash` (same hash already in `files[].sha256`) | 16,863 | 12.2% |
| **Total defensible cleanup** | **~19,233** | **~14%** |

Revised cleaned index size: ~118,714 bytes (~29,679 tokens). Not 109,545.

### Apples-to-apples comparison

For the *real* equivalent — both tools returning per-symbol detail with contract attributes preserved at namespace scope:

| Tool | Bytes / tokens | Notes |
|---|---|---|
| Cleaned `query_solution_index(scope:namespace)` (with defects fixed, attributes preserved) | ~118,714 / **~29,679 tok** | Single call |
| `get_source_map(scope:namespace, mode:detail)` | **refuses to render — `wasTruncated:true`** | Reports `estimatedTokenProxy: 40531` against `budgetLimit: 20000`. Returns `suggestedNarrowing` with 10 high-symbol files instead. Agent would need to drill those 10 files individually. |

When source map's budget refusal is followed (drill the 10 narrowed files individually, ~2,500-5,000 tokens each at detail mode), total Path B is ~30k-50k tokens — comparable to or worse than the cleaned index's single call.

**So the verdict updates to:** for the bulk-detail use case (give me all symbols with full contract data in this scope), **the index is actually the right answer** — it has the bandwidth source map's detail mode can't fit in its budget. The defects in Finding 46 (no budget shaping, redundant hashes, trivia in signatures) are the blockers; fix those and the index has a clear niche that source map doesn't compete on.

### Revised recommendation

1. Keep `get_solution_index_tree` agent-visible (cheap project structure, ~5,400 bytes for whole solution).
2. Keep `get_indexed_symbol(stableSymbolKey)` agent-visible (cheap single-symbol lookup).
3. **Keep `query_solution_index` and `find_indexed_symbols` agent-visible** after fixing Finding 46 defects (budget shaping, trivia stripping, fileHash dedup; keep contract attributes). The earlier "demote to internal" recommendation was based on assuming we'd strip contract attributes — that's not safe.
4. The integration path (`get_source_map(detail)` backed by the index) is still attractive but is now a "consistency / one-surface" choice rather than a "fix the budget overrun" necessity, since cleaned `query_solution_index` is already inside a fixable budget envelope.
