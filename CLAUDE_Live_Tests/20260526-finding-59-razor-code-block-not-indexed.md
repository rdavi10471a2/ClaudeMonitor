---
status: fixed
type: finding
created: 2026-05-26
processed: true
processedBy: claude
processedAt: 2026-05-26
resolution: Read surface closed in commit ab0d483 (2026-05-26). `.razor` `@code` members are indexed via `RazorProjectEngine.Process` + `SourceMappings`, surfaced with correct original-`.razor` coordinates through all index query tools. Verified end-to-end against `C:\SchemaStudioWebViewer - Copy` (80→108 files, 1886→3224 symbols, 0 diagnostics; `OpenColumnMergeReviewAsync` and `ColumnReconciliationDialog.razor` members return correctly). Write surface (typed mutation tools still gate to `.cs`) tracked separately in Finding 60.
resolutionCommit: ab0d483
---

## Summary

The Monitor solution index recognizes `.razor` files but does not surface the C# members declared inside their `@code` blocks. Sibling `.razor.cs` partial-class companions are indexed normally, but logic that lives only inside a Razor file's `@code` is invisible to `find_indexed_symbols`, `find_indexed_references`, `find_indexed_relationships`, `get_indexed_symbol`, and `query_solution_index`. Net effect: typed symbol-composition tools (`submit_symbol`, `remove_symbol`, `add_method`, etc.) can't target Razor `@code` members today, forcing fallback to text-based `replace_text_in_file` for what is otherwise plain C#.

## Repro

Watched solution: `C:\SchemaStudioWebViewer - Copy\SchemaStudioWebViewer.sln` (current `main`).

Three queries:

1. `find_indexed_symbols(text: "OpenColumnMergeReviewAsync")`
   - Defined in `Components/Pages/ManageViewsNext/ManageViewsNext.razor` line 605, inside an `@code` block, as `private async Task OpenColumnMergeReviewAsync()`.
   - Result: `[]` (empty).

2. `find_indexed_symbols(text: "ComputeReconciliationCounts")`
   - Defined in `Components/Pages/ManageViewsNext/ManageViewsNext.razor.cs` line 164 (the partial-class companion `.cs` file).
   - Result: full metadata returned — signature, line range, stableSymbolKey, selectorJson, etc.

3. `query_solution_index(scope: "file", value: "Components\\ColumnReconciliation\\ColumnReconciliationDialog.razor")`
   - Returns `indexMissing: false, files: [], symbols: []` — file is known to the index but contributes zero symbols. The file's `@code` block contains roughly two dozen declared C# members (constants, properties, methods, nested types like `ReconciliationCandidate`); none of them appear.

## Expected

C# symbols declared inside `.razor` `@code` blocks (and the equivalent inside `.cshtml`) should be indexed identically to `.cs` members and surfaced through the same indexed-symbol tools, since they are plain C# that Roslyn already parses as part of the Razor SDK compile path. `query_solution_index(scope: "file", value: "...some.razor")` should return the file's `@code` members.

If indexing `@code` is intentionally out of scope, the documented coverage boundary (`Docs/SolutionIndexScope.md`) should say so explicitly so agents do not waste a query against `find_indexed_symbols` before realizing they must fall back to text tools.

## Actual

`.razor` files are recognized (`indexMissing: false`) but contribute zero symbols. `@code` members are unreachable through any indexed-symbol surface.

## Evidence

- Tool: `mcp__monitor-base-claude__find_indexed_symbols`
  - Args: `{ "text": "OpenColumnMergeReviewAsync" }`
  - Result: `[]`
- Tool: `mcp__monitor-base-claude__find_indexed_symbols`
  - Args: `{ "text": "ComputeReconciliationCounts" }`
  - Result: 1 row, fully populated (signature `private (int NeedsReview, int Added, int Removed, int Unchanged) ComputeReconciliationCounts()`, lines 164–193 of `ManageViewsNext.razor.cs`)
- Tool: `mcp__monitor-base-claude__query_solution_index`
  - Args: `{ "scope": "file", "value": "Components\\ColumnReconciliation\\ColumnReconciliationDialog.razor" }`
  - Result: `{ "scope":"file", "value":"...ColumnReconciliationDialog.razor", "indexMissing":false, "files":[], "symbols":[] }`
- Reference files:
  - `C:\SchemaStudioWebViewer - Copy\Components\Pages\ManageViewsNext\ManageViewsNext.razor` (~47KB, large `@code` block, indexed as a file but zero symbols surfaced)
  - `C:\SchemaStudioWebViewer - Copy\Components\ColumnReconciliation\ColumnReconciliationDialog.razor` (~50KB, two dozen `@code` C# members — `BuildCandidates`, `RefreshViewState`, nested `ReconciliationCandidate` class, etc. — none indexed)
  - `C:\SchemaStudioWebViewer - Copy\Components\Pages\ManageViewsNext\ManageViewsNext.razor.cs` (sibling partial — fully indexed for comparison)

## Notes

- Workflow impact: during the recent Phase A/B refactor of `ManageViewsNext`, all edits to `ColumnReconciliationDialog.razor` had to use `replace_text_in_file` because the `@code` members weren't reachable as indexed symbols. Same file gets per-symbol diff metadata if its members were in a `.razor.cs` companion. Operator preference is to either close this gap or pay the tokens to separate `@code` into `.razor.cs` partials project-wide — closing the indexer gap is the cheaper outcome.
- Razor `@code` is the same C# Roslyn compiles for the page class. The Razor SDK emits an intermediate `.razor.g.cs` (or in-memory equivalent) that contains the page class plus the `@code` body. If the indexer's intake can reach that generated source (or re-parse the `@code` content via the Razor language services), it can surface the members with stable keys keyed to the original `.razor` file path so the existing typed tools (`submit_symbol`, `remove_symbol`, `add_method`, etc.) work unchanged.
- This finding pairs with [[feedback_prefer_typed_symbol_tools_over_text_replace]] — the operator-side rule says "use typed tools for C# symbol edits"; this finding documents the one place where that rule can't be applied today because the tooling doesn't cover the surface.
- Existing project memory note `project_razor_handled_as_text` says "Razor-specific indexing parked as added complexity that may not be needed." This finding is the concrete cost evidence for unparking it.
- Not blocking the recent refactor — confirmed `.razor` text-edit path works, build came back clean. Filed for design attention, not as a regression.

## Exact indexer locations to change

The gap is three hardcoded lines in `Services/SolutionIndexService.cs`. Not architectural — the indexer simply never opens `.razor` files:

- **Line 2677** — full-rebuild file enumeration is `"*.cs"` only:
  ```csharp
  return Directory.EnumerateFiles(observedRoot, "*.cs", options)
      .Where(path => !IsExcludedPath(observedRoot, path))
      .OrderBy(path => Path.GetRelativePath(observedRoot, path), StringComparer.OrdinalIgnoreCase);
  ```
- **Lines 2699–2702** — per-file refresh path (`ResolveSourceFilePath`) explicitly throws on non-`.cs`:
  ```csharp
  if (!fullPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
  {
      throw new InvalidOperationException("Only C# files can be indexed by file refresh.");
  }
  ```
- **Line 840** — content parsing in `BuildFileIndex` is plain `CSharpSyntaxTree.ParseText`, which would produce garbage syntax errors on Razor markup if a `.razor` file slipped through:
  ```csharp
  SyntaxTree tree = CSharpSyntaxTree.ParseText(text, path: sourceFilePath);
  ```

## Suggested fix shape

1. Extend the enumeration glob at line 2677 to include Razor:
   - `Directory.EnumerateFiles(observedRoot, "*.cs", options)`
   - `.Concat(Directory.EnumerateFiles(observedRoot, "*.razor", options))`
   - `.Concat(Directory.EnumerateFiles(observedRoot, "*.cshtml", options))`
2. Relax the refresh-path gate at line 2699 to accept `.razor` / `.cshtml`.
3. In `BuildFileIndex`, branch on extension before parsing:
   - **`.cs`**: existing `CSharpSyntaxTree.ParseText` path unchanged.
   - **`.razor` / `.cshtml`**: extract C# content via one of two routes:
     - **Razor-SDK route (preferred for correctness)**: pull in `Microsoft.AspNetCore.Razor.Language` (NuGet), build a `RazorProjectEngine`, call `Process` on the source, take `CodeDocument.GetCSharpDocument().GeneratedCode`, then `CSharpSyntaxTree.ParseText(generatedCode, path: sourceFilePath)`. Resulting symbol locations need a small adjustment because the generated document is a complete synthesized class around the original `@code`; line mapping data is available via `CodeDocument.GetCSharpDocument().SourceMappings` to project anchors back to the original `.razor` line numbers.
     - **Text-extract route (cheap, brittle on edge cases)**: scan the Razor source for `@code { … }` blocks (and `@functions { … }` for cshtml), concatenate their contents wrapped in a synthetic `partial class { … }` declaration that matches the generated component class name, parse that as `CSharpSyntaxTree.ParseText(synthetic, path: sourceFilePath)`, and add an offset translation so symbol line numbers map back to the original `.razor` positions. ~50 lines, no new dependencies. Misses computed/expression-only members but covers `@code` correctly.
4. `stableSymbolKey` format already uses relative file path + namespace + container + member — no schema change needed; keys for Razor symbols would just resolve to `.razor` paths and the existing typed-symbol tools (`submit_symbol`, `remove_symbol`, `add_method`) would work unchanged so long as the matching path-routing in those tools also accepts `.razor`.
5. The overlay-validation path (which currently produces the razor-as-cs misparse noise we saw throughout this session) likely shares the same plain-C# assumption and could be fixed at the same time so the gate logic stops blocking Razor edits.
