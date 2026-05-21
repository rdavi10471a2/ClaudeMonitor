---
status: new
type: handoff
created: 2026-05-21
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---

# Claude response — review of fixture-index-matrix smoke and `68c06ba`

This responds to `CLAUDE_Live_Tests/20260521-claude-review-fixture-index-matrix.md`. Pulled `main` clean to `68c06ba`, built the smoke project, ran both smokes, read `Services/SolutionIndexService.cs` and `MonitorBaseClaude.ToolSmokeTests/Program.cs` diffs in full.

## Local build/run verification

- `dotnet build .\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj` → `Build succeeded. 0 Warning(s) 0 Error(s) Time Elapsed 00:00:05.88`.
- `dotnet .\MonitorBaseClaude.ToolSmokeTests\bin\Debug\net10.0\MonitorBaseClaude.ToolSmokeTests.dll --fixture-index-matrix` → **`Passed: True`**, 34/34 matrix checks, 10/10 current-model feature probe expectations, 0 Roslyn target resolution failures. Both Finding 52 gaps closed and verified:
  - `McpCallerProbeTarget(string)` ctor — Monitor callers `2/2` (both explicit `new(...)` and target-typed `new(...)`), Monitor refs `2/2`.
  - `McpProbeMarkAttribute` — Monitor refs `2/2` (both attribute usages).
- `dotnet ... --dbv2-index-callers-all` → `Passed: False`, 327/420 fully matched, 93 failures. Classification below; **this looks like smoke-config drift on my watched DBV2 checkout, not an index regression** — see "DBV2 smoke divergence" section.

## Review against your six focus points

### 1. Are the fixture answer-key counts semantically correct?

Yes. Spot-verified each MatrixCheck row in `BuildFixtureMatrixChecks()` against the generated `McpCallerProbeFixture.A.cs` and `.B.cs` content (and `McpGeneratedProbe.g.cs`). Notable choices that look correct:

- `StaticGate(int)` has 1 caller / 2 references — the static method is called once but the type-receiver `McpCallerProbeTarget.StaticGate(...)` produces a type-ref row plus the invocation, so refs = 2. Consistent with how `referenceCount` treats type-mentions distinct from call-site rows.
- `McpCallerProbeTarget(string)` has 2 callers / 2 references — the explicit `new McpCallerProbeTarget("explicit")` and the target-typed `new("targettyped")` each count as one call site AND one reference. Asymmetric kinds-of-counting would have been worth a comment.
- `McpCallerProbeTarget` (type) has 8 references — adds up: 3 field types + 2 explicit-`new` type tokens + 2 static-receiver mentions + 1 cross-type ref I couldn't immediately pin without running grep again. The 8 vs my hand-counted 5 in Finding 52 reflects Codex's expanded fixture having more reference sites for the same type. Both sides of the matrix (Roslyn comparator and Monitor index) returned 8, which is the more important fact.
- `McpMetadataOnlyTarget.MetadataMethod()` has 0 callers / 1 ref — the metadata-only target is reached via `typeof(...)`/`nameof(...)` so the method should never have a real invocation. 0 callers + 1 ref is the correct shape for the `nameof(MetadataMethod)` site.

### 2. Is the Roslyn comparison independent enough from the Monitor index?

**Mostly yes — with one observation worth naming.** `BuildRoslynFixtureMatrix` builds its own `CSharpCompilation` from the fixture source files, uses Roslyn's `GetSymbolInfo`/`GetTypeInfo` directly, and counts references and callers in an entirely separate code path from `SolutionIndexService`. That is good independence at the symbol-resolution layer — much better than relying on the Roslyn MCP server (which separately had `find_callers` returning `[]` for new files even after `rebuild_solution` during my exploratory work earlier today).

The shared-blind-spot risk: both `BuildRoslynFixtureMatrix` and the post-`68c06ba` `SolutionIndexService` walk the same three AST node families — `SimpleNameSyntax`, `ImplicitObjectCreationExpressionSyntax`, and `AttributeSyntax`. If a fourth AST shape were a real callsite (e.g. `nameof(...)` arguments treated as a reference, primary-constructor parameters, `record` ctor calls in deconstruction, collection-expression conversions) and both walkers ignored it, both would silently agree at 0 and the answer-key column would be the only thing catching the gap.

The answer-key column already pins those values explicitly, so for the rows in the current matrix this is fine. For future expansion: if Codex adds a new row whose Roslyn-side counter is also new, that pair could pass without anyone noticing the underlying behavior is wrong on both sides. Not a current bug — just a property of the symmetric design worth keeping in mind when extending the matrix.

### 3. Are the current-model feature probes clearly named?

Yes. Each probe in the "Current model feature probes" section names what Roslyn thinks vs what Monitor currently indexes, and ends with an explicit `current expectation` field that locks the *current* Monitor behavior, not the "right" behavior. The `note` field for each probe explains why Monitor currently doesn't index it (e.g. "current Monitor symbol model has no indexer kind").

A reader skimming the smoke output should not confuse these with permanent gaps in the fixture — the section header "Current model feature probes" plus the `current expectation` framing make it obvious these are scope decisions, not test failures. The 10 probes also serve as a checklist of "if you implement this, flip the expectation and the test will tell you whether you did it right."

One nit: the `generated-file policy` probe records `current expectation: True`, locking the current behavior that `.g.cs` files ARE indexed. Worth a one-line note on whether that lock is intentional permanent or a placeholder until a generated-file exclusion arrives — the doc reads as the latter but the test enforces the former.

### 4. Is target-typed constructor indexing correct?

Yes, against this fixture. Read the `ImplicitObjectCreationExpressionSyntax` walker in `SolutionIndexService.cs:665-697`:

- `model.GetSymbolInfo(creation)` correctly returns the IMethodSymbol for the implicit ctor invocation.
- `GetStableKeyForSymbol(symbol, filesByRelativePath)` requires the target to be in the indexed file set, which is the correct scope guard.
- Uses `creation.NewKeyword.Span` for line/column — points at the `new` keyword, which is sensible for human review. (One alternative would be the entire argument list span, but the keyword position is more compact.)
- `referenceKind = "construction"` is distinct from explicit-new's classification — that's useful so a downstream caller could differentiate the two construction syntaxes if needed.
- Dedup key `{target}|{path}|{line}|{column}|{referenceKind}` prevents the same site being recorded twice.

The matrix verifies the behavior end-to-end. `McpCallerProbeTarget(string)` now correctly returns 2 callers for `new McpCallerProbeTarget("explicit")` and `new("targettyped")`.

### 5. Does attribute usage indexing correctly map `[Name]` to `NameAttribute`?

Yes. The helper `GetAttributeTypeSymbol(SemanticModel, AttributeSyntax)` in `SolutionIndexService.cs:1151-1160`:

```csharp
private static ISymbol? GetAttributeTypeSymbol(SemanticModel model, AttributeSyntax attribute)
{
    ISymbol? symbol = GetBestSymbol(model.GetSymbolInfo(attribute));
    if (symbol is IMethodSymbol method)
    {
        return method.ContainingType;
    }

    return symbol ?? model.GetTypeInfo(attribute).Type;
}
```

This is the right shape. Roslyn's `GetSymbolInfo` on an `AttributeSyntax` returns the *constructor* the attribute resolves to (because `[Foo(x)]` is a constructor invocation under the hood). Unwrapping to `ContainingType` gives you the attribute class, which is what a user querying "references to FooAttribute" expects. The fallback to `GetTypeInfo(attribute).Type` handles the no-constructor-resolution edge case.

The matrix verifies the mapping: `[McpProbeMark]` shorthand on both the class (`A:26`) and the method (`A:51`) correctly attribute to `McpProbeMarkAttribute` and produce `2/2` Monitor refs.

The reference-kind label `"attribute"` is helpful for callers who want to distinguish attribute usages from regular type references in result-handling code.

### 6. Is any generated fixture category misleading, or should become a first-class indexed model feature?

Two observations:

- **`generated-looking .g.cs files`**: the probe locks current behavior that the indexer ingests `McpGeneratedProbe.g.cs`. That's correct for now (Monitor doesn't have a generated-file exclusion), but I'd expect this category to flip the moment a `<Compile Remove="**\*.g.cs" />` or a `.AssemblyAttributes.cs`-style exclusion arrives. Suggest renaming the probe label to something like `generated-file inclusion (pending exclusion policy)` so the intent reads correctly when someone later implements the exclusion and changes `current expectation: True → False`.
- **`partial declaration merge`**: currently locked at `Monitor indexed: False` — Monitor stores one row per physical declaration. The DBV2 codebase has heavy partial-class usage (`IntegrationsViewImportControl.*.cs`), and the fact that `find_indexed_callers` on a partial-class method correctly returns rows means the *current* behavior works for the common case. Worth noting: "should become a first-class indexed model feature" only if there is a real user-visible request for *merged* symbol queries (e.g. "find all members of the merged type X"). Until that's a request, the current behavior is the right scope decision.

The other 8 probes (indexer, operator-overload, conversion-op, enum-member, local-fn, lambda-caller-identity, override-row, interface-impl-row) all read as honest scope decisions worth locking — not bugs.

## DBV2 smoke divergence

My `--dbv2-index-callers-all` run failed with 93 failures vs your `146/146 pass`. After classifying every failure, **all 93 are smoke-config drift, not index regressions**:

- **17 "Missing" entries** — all in `SourceBakups/BehaviorSplit_20260410_105702/IntegrationsViewImportControl*.cs`. These are the legacy backup files Finding 51 correctly excluded from the index. The smoke's hand-pinned expected-list still expects them and reports Missing.
- **76 "Unexpected" entries** — all in `UI/MergedEditorSurface/IntegrationsViewImportControl.{Behavior,Loading,Persistence,ParsedMergePreview,ParserSync,SelectionStatus,UiFactories,Validation}.cs`. These are the *real* live callers in the 2026-04-10 partial-class split. The index correctly captures them; the smoke's expected-list was pinned to a DBV2 state that predates the split.

The "Cross Section" table at the top of the summary makes the pattern obvious — `UI`: targets 156, expected callers 148, actual callers 278. Actual is nearly 2x expected because the split moved code that used to live in one file into ~9 partial-class files. The index correctly indexes the post-split state; the smoke's expected list is stale.

**Suggested follow-up for the DBV2 smoke** (not in scope for this review, just a flag): consider deriving the expected caller list from an in-process Roslyn pass over the watched DBV2 source at test time (the same approach `BuildRoslynFixtureMatrix` uses), rather than hand-pinning. That would make the smoke self-correcting against any partial-class split or rename in the watched checkout, while still catching real index regressions. The current hand-pinned design is brittle to upstream churn — your own note ("Your DBV2 file/symbol counts may differ if your watched DBV2 checkout differs, but the smoke should still pass") acknowledges this, and my checkout proves the brittleness.

Alternatively if hand-pinning is intentional (i.e. the smoke is meant to detect upstream churn AS a signal), the failure format could surface the cross-section deltas as the headline instead of 93 per-target failures — easier to triage at a glance.

## Side observations not blocking the review

- The `2026-05-21 fixture I staged via Monitor` at `SchemaStudio.SematicModel/Tests/McpIndexProbes/McpCallerProbeFixture.{A,B}.cs` is still in the watched DBV2 source on `main` (it landed via Monitor accept earlier today, before `68c06ba`). It does not conflict with the smoke's generated fixture, which lives under `Working/History/ToolSmokeTests/<timestamp>/fixture-index-matrix/FixtureProject/McpIndexProbes/`. The two co-exist cleanly. Per your instruction not to copy generated fixtures into the watched DBV2, no action needed there.
- Stale Working-mirror leftovers (`Working/Schema Studio - DBV2_*/SchemaStudio.SematicModel/Model/McpOverlayGateFixture{,3,5}.cs`) caused overlay-compile force-review to fire during my fixture staging earlier today. I cleaned the three `.cs` files mid-session but the gate decision appeared to carry forward to subsequent launches in the same session. Logging here for context — separate workflow finding, not something the current review touches.
- The Roslyn MCP server returned `[]` for `find_callers`/`find_references` on the new fixture symbols even after `rebuild_solution`. `search_symbols` saw them, find-callers couldn't reach them. Hypothesis: needs a host-process restart, not just project re-parse. The in-process Roslyn comparator in `BuildRoslynFixtureMatrix` is unaffected (it builds its own Compilation) — which is part of why that design choice is better than relying on the MCP server for this comparison.

## Recommendation

`68c06ba` looks ready to consider merged. Both Finding 52 gaps are closed in semantically correct ways, the fixture-matrix smoke provides a durable regression check for the closure plus a clearly-named locking surface for the 10 known model limitations, and the Roslyn comparator is independent enough at the resolution layer.

The DBV2 smoke divergence I observed does not indicate any index problem; it indicates the smoke's hand-pinned expected list has drifted from the watched checkout. Worth a separate followup if you want the smoke to be CI-stable across heterogeneous DBV2 states.
