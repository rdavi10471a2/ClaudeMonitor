---
status: new
type: finding
created: 2026-05-28
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---

## Summary

The Monitor solution index now covers Razor declarations via the Razor SDK (commit ab0d483, 2026-05-26): `find_indexed_symbols` resolves `@code`-declared members and the synthesized `__GeneratedComponent::AspNetCore_<hash>` component class. But the call-graph surface has a markup blind spot: invocations and method-group references that originate inside Razor markup attributes (`@onclick`, `@bind`, etc.) do not appear in the references or callers tables. Roslyn's semantic model resolves them in the synthesized `.razor.g.cs`; the index walker is dropping or hiding them. This makes "what Razor markup wires this method?" force a `.razor` grep fallback even after the SDK is in play.

## Repro

1. Pick a method defined in a `.cs` (or `.razor.cs`) partial that is invoked from `.razor` markup. Example used: `RunViewToolAsync(Func<Task>)` in `ManageViewsNext.razor.cs:134`, wired from `ManageViewsNext.razor:362` via `@onclick="@(() => RunViewToolAsync(OpenColumnMergeReviewAsync))"`.
2. Confirm the method is indexed:
   ```
   find_indexed_symbols(text: "RunViewToolAsync", kind: "method")
   ```
   → 1 hit, stable key `Components/Pages/ManageViewsNext/ManageViewsNext.razor.cs::SchemaStudioWebViewer.Components.Pages.ManageViewsNext::ManageViewsNext::method::RunViewToolAsync(Func<Task>)`.
3. Call:
   ```
   find_indexed_callers(stableSymbolKey: "<the key above>")
   ```
4. Repeat for a method passed as method-group from the same markup attribute:
   ```
   find_indexed_references(stableSymbolKey: "Components/Pages/ManageViewsNext/ManageViewsNext.Columns.cs::SchemaStudioWebViewer.Components.Pages.ManageViewsNext::ManageViewsNext::method::OpenColumnMergeReviewAsync()")
   ```

## Expected

At least one row in `find_indexed_callers(RunViewToolAsync)` anchored to `ManageViewsNext.razor:362`, and at least one row in `find_indexed_references(OpenColumnMergeReviewAsync)` anchored to the same line. Both invocations are real, present in Roslyn's semantic model via the Razor SDK's emitted `.razor.g.cs`, and known to the compiler.

## Actual

Both queries return `[]`. Confirmed with both HTML-encoded and bare `<` in the stable key for the `RunViewToolAsync(Func<Task>)` lookup — same result either way.

## Evidence

- Razor SDK is indexing declarations correctly: `find_indexed_symbols("IsNavigationHidden")` returned the `@code` property at `ColumnReconciliationDialog.razor:338` under `__GeneratedComponent::AspNetCore_cbf1d1936f9f6afa82789f82d4b1d0571d7d8759`, signature `private bool IsNavigationHidden { get; set; } = true;`.
- `query_solution_index(scope: file, value: "Components\Pages\ManageViewsNext\ManageViewsNext.razor")` returns the generated component class `__GeneratedComponent::AspNetCore_93fcc5f49990a38b6eef578b30eecf3aa9d376f8 : ComponentBase`. So the file IS in the index, and the Razor SDK IS producing user-visible symbols for it.
- The markup line that should produce the missing call/reference rows:
  ```razor
  @onclick="@(() => RunViewToolAsync(OpenColumnMergeReviewAsync))"
  ```
  at `C:\SchemaStudioWebViewer V 1.1\Components\Pages\ManageViewsNext\ManageViewsNext.razor:362`.
- Empty results from:
  - `find_indexed_callers("Components/Pages/ManageViewsNext/ManageViewsNext.razor.cs::SchemaStudioWebViewer.Components.Pages.ManageViewsNext::ManageViewsNext::method::RunViewToolAsync(Func<Task>)")` → `[]`
  - `find_indexed_references("Components/Pages/ManageViewsNext/ManageViewsNext.Columns.cs::SchemaStudioWebViewer.Components.Pages.ManageViewsNext::ManageViewsNext::method::OpenColumnMergeReviewAsync()")` → `[]`
- Index freshness: rebuilt 2026-05-28T17:42:50 at the end of session `monitor-20260528173631-3c3a42af121f4635a`; 108 files, 3224 symbols, 11372 references, 2189 call sites, 0 stale files, 0 diagnostics. Razor files are walked (declarations prove it); call/reference rows from markup invocations are simply not landing.

## Notes

Operator-facing context (Operator observation 2026-05-28): "the compiler knows that via references" — fair user expectation. Once `RazorProjectEngine.Process` emits the synthesized `.razor.g.cs`, the markup attribute becomes a literal C# expression like `EventCallback.Factory.Create<...>(this, () => RunViewToolAsync(OpenColumnMergeReviewAsync))`, and Roslyn's semantic model resolves both the invocation and the method-group as references against the user symbols. So the underlying knowledge exists in the compilation; the gap is between Roslyn's compilation graph and the index's persisted reference/caller tables.

Likely shape of the issue (observation, not design): the Razor SDK's `SourceMappings` project declarations back to `.razor` line/column but the attribute-expression body in the synthesized component method has its own mapping kind that the index walker may not be honoring, so reference sites whose Roslyn position lives in the `.razor.g.cs` may be (a) dropped because they have no clean user-source anchor, or (b) persisted under a generated-file path that downstream queries filter out. Either path is a fixable choice in the walker; both options preserve user-visible anchors via `SourceMappings`. Codex to decide the right approach.

Knock-on effect documented in memory `project_razor_handled_as_text` (updated 2026-05-28): for any "who calls this?" question on a Blazor symbol, grep `.razor` is still required as a sanity fallback. Closing this finding would remove that fallback.

Related: [20260528-roslyn-codelens-binding-via-monitor-works-as-is.md](20260528-roslyn-codelens-binding-via-monitor-works-as-is.md) — separate observation; doesn't affect this one.
