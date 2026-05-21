---
title: Index vs source-map on WinForms files (Designer.cs + Form.cs) + architectural direction
date: 2026-05-21
branch: claude-notes/20260521-pass21
session: monitor-20260521130506-02a9a5e5b0aa41a1a
status: passed-with-recommendation
---

# Index vs source-map on WinForms files + architectural direction

Operator's follow-up requests:

1. *"If the db is rebuilt on build (somehow) then next run can fetch the index and the entire tool can use that index — and sourcemap can go the way of the dodo, or do we duplicate the functionality of source map with the index?"*
2. *"We should be able to get the Roslyn symbol info into the DB."*
3. *"Test with a winforms cs file."*
4. *"Remember we are also filtering out interfaces in the current tool — in case the new index includes/excludes them as well — for apples-to-apples."*

## Apples-to-apples check on interface inclusion

Both tools were called on `SchemaStudio.SematicModel\Providers\IViewDefinitionProvider.cs` (a pure interface declaration file, 438 bytes, 1 interface + 5 methods).

| Tool | Returned interface? | Returned methods? | Notes |
|---|---|---|---|
| `query_solution_index(file)` | Yes (`kind:"interface"`) | Yes (5 methods, each `kind:"method"`) | No filter applied |
| `get_source_map(file, selector)` | Yes (`kind:"interface"`, `syntaxKind:"InterfaceDeclaration"`) | Yes (5 methods with full parameter info, `isAsync:false`, etc.) | No filter applied |

**Both tools include interfaces and interface members at file scope, selector mode.** Apples-to-apples confirmed for this dimension. If interface filtering is meant to live elsewhere (a UI tree, a different mode, a future filter), it isn't active in either tool today at this scope.

The index's interface method signature is `"void ClearCache();"` (with semicolon, because the index window-extracts the source); source map's is `"void ClearCache()"` (without, because it Roslyn-derives the contract signature). Both convey the same info.

## Head-to-head on `UI\DatabaseManagerForm.cs` (real WinForms form, 13 KB, 20 symbols)

Both tools returned the same 20 symbols (1 partial class + 9 fields + 1 ctor + 9 methods). The file has no `[Browsable]`/`[Category]` attribute decoration (the fields are private UI controls, not data-bound properties), so the attribute-pollution concern from the data-class scenario doesn't apply here. But two other pollution types DO appear:

### Pollution example 1 — leading comment concatenated into signature

`VerifyDatabaseExists` method signature:

- Source map: `"private string VerifyDatabaseExists(DatabaseDefinition db)"` (clean) + structured fields `returnType:"string"`, `parameterTypes:["DatabaseDefinition"]`, `parameterNames:["db"]`
- Index: `"// --- NEW: SMO Existence Check --- private string VerifyDatabaseExists(DatabaseDefinition db) { ... }"` (leading C-style comment captured into the signature)

### Pollution example 2 — leading comment marker concatenated

`InitializeComponentCustom` method signature:

- Source map: `"private void InitializeComponentCustom()"` (clean)
- Index: `"// ... [Keep InitializeComponentCustom, CreateStyledButton, and LoadData as provided] ... private void InitializeComponentCustom() { ... }"` (a code-annotation comment from the source file was captured)

### Token cost on this file

| Tool | Approx response bytes | Tool-reported tokens | Per-symbol char cost (avg) |
|---|---|---|---|
| Source map | ~14,000 (with 16 `suggestedNextCalls`) | `estimatedTokenProxy: 4142` | ~700 (rich structured fields) |
| Index | ~9,500 | ~2,400 (rough at 4 chars/token) | ~475 (leaner per row) |

At file scope on a WinForms file the index is ~40% leaner on raw bytes. But it pays for that with dirty signatures on 2 of 20 symbols — and those dirty signatures cost the agent reasoning time, not just tokens. An agent reading `"// --- NEW: SMO Existence Check --- private string VerifyDatabaseExists(...)"` has to mentally strip the comment to find the actual method signature. Source map's `"private string VerifyDatabaseExists(...)"` is the contract directly.

## Head-to-head on `SchemaStudio.Designer.cs` (the WinForms designer pattern, 2.4 KB, 5 symbols)

This is the file pattern operator asked about most directly. Hand-shaped designer, partial of `SchemaStudio` form, contains the `InitializeComponent()`, the `Dispose(bool)` override, and two fields. Pattern is universal across all WinForms designer files.

**Source map signatures (Roslyn-derived, clean):**

```text
partial class SchemaStudio
private System.ComponentModel.IContainer components = null
protected override void Dispose(bool disposing)
private void InitializeComponent()
private SchemaViewer MainViewer
```

Plus structured fields: `hasDocumentation:true` on `components`, `Dispose`, and `InitializeComponent` (so the agent knows the XML doc exists); `isOverride:true` on `Dispose`; `modifiers:["partial"]` on the class; etc.

**Index signatures (text-window extracted, dirty):**

```text
partial class SchemaStudio { ... }
/// <summary> /// Required designer variable. /// </summary> private System.ComponentModel.IContainer components = null;
/// <summary> /// Clean up any resources being used. /// </summary> /// <param name = "disposing">true if managed resources should be disposed; otherwise, false.</param> protected override void Dispose(bool disposing) { ... }
#region Windows Form Designer generated code /// <summary> /// Required method for Designer support - do not modify /// the contents of this method with the code editor. /// </summary> private void InitializeComponent() { ... }
#endregion private SchemaViewer MainViewer;
```

**4 of 5 signatures are polluted.** The pollution sources:

1. **XML doc comments** (`/// <summary>...</summary>`) get concatenated. Source map flags `hasDocumentation:true` separately; the index dumps the doc text into the signature string.
2. **`#region` directive** prepended to `InitializeComponent`. The WinForms designer code-folding marker is part of the canonical WinForms file shape — every designer has it.
3. **`#endregion` directive** prepended to `MainViewer` field. Same pattern.

The XML doc inclusion on `Dispose` alone roughly doubles that symbol's signature byte cost (~240 chars vs ~50). On `InitializeComponent`, the combined region+doc pollution roughly triples it. **This is the highest-density pollution case I've measured.**

### Token cost on Designer.cs

| Tool | Approx response bytes | Tool-reported tokens |
|---|---|---|
| Source map | ~4,500 (with 5 `suggestedNextCalls`) | `estimatedTokenProxy: 1151` |
| Index | ~4,400 | ~1,100 (rough) |

On this small file the byte totals are about the same — the per-symbol bloat from comment/region pollution offsets what the index would otherwise save by skipping `suggestedNextCalls` and structured-field decoration. **The index loses its "leaner per row" advantage entirely on designer files** because every designer member has XML doc + region trivia attached.

## Architectural answer: Roslyn into DB, source-map stays as the agent-facing transform

The Designer.cs result settles the architectural question. The index's pollution problem is **inherent to the current extraction strategy** (read raw text in `[startLine, endLine]` window) — not a bug to be patched but a wrong approach. Every WinForms designer file will have this pollution. Every well-documented domain class with XML doc comments will have this pollution. Every file using `#region` for code organization will have this pollution.

The fix is structural: **the index extractor should produce its signature column the same way source map produces signatures today** — by walking the Roslyn `SyntaxNode` for each declaration and generating a clean contract signature, separating doc comments into a `hasDocumentation` flag, separating attributes into structured fields. That is exactly what "we should be able to get the Roslyn symbol info into the DB" means.

Once that lands:

1. The DB stores **the same cleaned, Roslyn-derived signature** that source map computes today.
2. The DB additionally stores the structured fields source map computes (`modifiers`, `returnType`, `parameterTypes`, `parameterNames`, `isAsync`, `isOverride`, `hasDocumentation`, `hasAttributes`, `baseTypes`, `syntaxKind`).
3. `get_source_map` becomes the **agent-facing transform layer**: it reads cleaned rows from the DB when the file hash matches, falls back to live Roslyn when not. All the existing response-shaping rules (mode density, budget shaping, narrowing suggestions, namespace-surface affordances) stay in source map.
4. `query_solution_index` and `find_indexed_symbols` either get the same Roslyn-derived signatures (free upgrade) or get demoted to internal-maintainer tools. They are no longer needed as agent-facing tools because source-map now reads from the same data.
5. Build hooks call `refresh_solution_index` post-build; per-file refresh on save handles between-build drift; the freshness-hop hash check catches anything that slipped through.

This answers the operator's question directly: **source map doesn't go away. It becomes the agent-facing tool whose backend got faster (SQLite) and richer (cleaner Roslyn-derived rows).** No duplication of source-map functionality in the index family — instead, the index becomes the storage layer that source-map already needed.

### What this means for Finding 46

Finding 46 listed five defects in the current index (no budget shaping, dirty signatures with region/comment/commented-out-code, redundant per-symbol fileHash, AI workflow attribute leakage). Three of those defects (region, leading comments, commented-out code) **vanish for free** once the extractor switches to Roslyn-syntax-derived signature generation, because Roslyn's syntax tree treats trivia and declarations as distinct nodes. The other two (budget shaping, fileHash dedup) are still needed regardless of extraction strategy.

So the implementation order is:

1. Switch index extractor to Roslyn-derived signature generation (clears 3/5 of Finding 46 defects + the WinForms designer pollution case + the data-class XML-doc case).
2. Dedup per-symbol fileHash (recovers ~12% of bytes).
3. Add budget shaping (estimatedTokenProxy / budgetLimit / wasTruncated / suggestedNarrowing) to the index responses, OR collapse them into source-map's transform layer per the architectural plan above.
4. Build/save hooks to keep the DB fresh.

## Bottom line

- **Interface handling: same in both tools** at file scope. No apples-to-oranges concern there.
- **WinForms field-heavy files: index ~40% leaner per row, but pollutes 10% of signatures with concatenated comments.**
- **WinForms designer files: index ties or loses on bytes AND pollutes 80% of signatures.** Every designer file in the repo would show this.
- **Architectural direction: Roslyn-derived rows in the DB, source-map remains the agent-facing transform.** Don't retire source-map, don't duplicate it — make the index its faster storage.
