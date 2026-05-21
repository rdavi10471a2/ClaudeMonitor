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

## Update — extended matrix (50/50 pass on second run)

Per the Operator's observation that the fixture declares far more matrix-worthy surface than the 34 original rows cover, I extended `BuildFixtureMatrixChecks()` with 16 additional `MatrixCheck` rows targeting symbols the original matrix didn't score. After fixing two of my own answer-key off-by-ones (caught by the in-process Roslyn comparator — exactly the value-add of the three-leg design), all 50 rows pass with 0 failures, 0 Roslyn target resolution failures, 0 current-model-feature-probe expectation failures.

This widens the verified-correct surface for the post-`68c06ba` indexer to include: interface-dispatch vs impl-method resolution, virtual-vs-override dispatch, partial-class members across physical files, generated-file inclusion semantics, struct property write-in-ctor + read-from-receiver, base-class list references, extension class type as an unreferenced static class, and several 0-count shapes.

Both Finding 52 gap closures stay verified at this widened surface (target-typed `new(...)` ctor invocations correctly counted on `McpCallerProbeTarget(string)`, attribute usages correctly counted on `McpProbeMarkAttribute`).

### How the run went

First run: 50 attempted, **48 passed, 2 failed**. Both failures were my answer-key errors — in-process Roslyn and Monitor index agreed on the actual count, my expected number was wrong:

- `McpVirtualBase` — I expected refs=1, both engines returned 2. I missed `McpCallerProbeFixture.A.cs:82 public sealed class McpVirtualDerived : McpVirtualBase` (base-class list reference) in addition to the field type at `B:24`.
- `McpProbeStruct.Value` — I expected refs=1, both engines returned 2. I missed `McpCallerProbeFixture.A.cs:41 Value = value;` (the auto-property write inside the struct ctor body) in addition to the read at `B:48 _struct.Value`.

Both errors corrected to refs=2. Second run: **50 passed, 0 failed.**

This is what the smoke is *for* — the third leg (in-process Roslyn comparator) prevented a 2-leg bug where a wrong answer key would have flagged the Monitor index as broken when it was correct.

### The 16 new rows (verbatim, for Codex to lift into the product commit)

These were added below the existing `McpMetadataOnlyTarget.MetadataMethod()` row in `BuildFixtureMatrixChecks()` in `MonitorBaseClaude.ToolSmokeTests/Program.cs`. Also requires three local consts (`fileB`, `fileG`) and two helper functions (`KeyB`, `KeyG`) at the top of the method:

```csharp
const string fileA = "McpIndexProbes/McpCallerProbeFixture.A.cs";
const string fileB = "McpIndexProbes/McpCallerProbeFixture.B.cs";
const string fileG = "McpIndexProbes/McpGeneratedProbe.g.cs";
string Key(string containingType, string kind, string name)
{
    return $"{fileA}::SchemaStudio.SemanticModel.Tests::{containingType}::{kind}::{name}";
}
string KeyB(string containingType, string kind, string name)
{
    return $"{fileB}::SchemaStudio.SemanticModel.Tests::{containingType}::{kind}::{name}";
}
string KeyG(string containingType, string kind, string name)
{
    return $"{fileG}::SchemaStudio.SemanticModel.Tests::{containingType}::{kind}::{name}";
}
```

New rows (post-correction):

```csharp
new("IMcpFeatureContract", Key(string.Empty, "interface", "IMcpFeatureContract"), null, 2),
new("IMcpFeatureContract.ContractProbe()", Key("IMcpFeatureContract", "method", "ContractProbe()"), 1, 1),
new("McpFeatureContractImpl", Key(string.Empty, "class", "McpFeatureContractImpl"), null, 1),
new("McpFeatureContractImpl.ContractProbe()", Key("McpFeatureContractImpl", "method", "ContractProbe()"), 0, 0),
new("McpVirtualBase", Key(string.Empty, "class", "McpVirtualBase"), null, 2),
new("McpVirtualDerived", Key(string.Empty, "class", "McpVirtualDerived"), null, 1),
new("McpVirtualBase.VirtualProbe()", Key("McpVirtualBase", "method", "VirtualProbe()"), 1, 1),
new("McpVirtualDerived.VirtualProbe()", Key("McpVirtualDerived", "method", "VirtualProbe()"), 0, 0),
new("McpDerivedProbe", Key(string.Empty, "class", "McpDerivedProbe"), null, 0),
new("McpFeatureEnum", Key(string.Empty, "enum", "McpFeatureEnum"), null, 2),
new("McpProbeStruct.Value", Key("McpProbeStruct", "property", "Value"), null, 2),
new("McpPartialProbe.PartA()", Key("McpPartialProbe", "method", "PartA()"), 1, 1),
new("McpPartialProbe.PartB()", KeyB("McpPartialProbe", "method", "PartB()"), 1, 1),
new("McpGeneratedProbe", KeyG(string.Empty, "class", "McpGeneratedProbe"), null, 0),
new("McpGeneratedProbe.GeneratedMethod()", KeyG("McpGeneratedProbe", "method", "GeneratedMethod()"), 0, 0),
new("McpProbeExtensions", Key(string.Empty, "class", "McpProbeExtensions"), null, 0)
```

### Items potentially still outstanding (declared in fixture, not yet matrix-rowed)

Going through the generated fixture sources, here is what is declared but not scored even after my 16 additions:

- **Fields on `McpCallerProbeCallers`** — `_target`, `_targetExplicit`, `_targetTargetTyped`, `_impl`, `_via`, `_struct`, `_record`, `_delegate`, `_indexer`, `_operatorLeft`, `_operatorRight`, `_contract`, `_virtual` (13 fields). Counting refs for each is straightforward but adds 13 row-flavor rows whose signal-to-noise is low — most just count how many times each field is read or written in the same file. I left these off the matrix; if a future change is suspected to regress field-reference counting, these would be cheap to add.
- **`McpPartialProbe` (the type, in either physical file)** — I avoided adding a matrix row for the partial type itself because the current model decision (locked by the `partial declaration merge` feature probe) is "Monitor stores physical declarations" rather than a merged row. A row pinned to `fileA` would presumably get the type-refs Roslyn discovers, but it would be sensitive to which physical declaration Monitor uses as the resolution target. Worth deciding policy first, then adding.
- **`McpFeatureEnum.FeatureAlpha` / `FeatureNone` (enum members)** — explicitly locked at "Monitor indexed: False" by the `enum member declaration` feature probe. Not a useful matrix row until that policy changes.
- **Indexer / operator overload / conversion operator declarations on `McpIndexerProbe`, `McpOperatorProbe`** — explicitly locked at "Monitor indexed: False" by their respective feature probes.
- **`McpVirtualDerived` as a derived-of `McpVirtualBase`** — base/derived relationship rows are explicitly locked at "Monitor indexed: False" by the `override relationship row` and `interface implementation relationship row` feature probes.

In other words, the only matrix-worthy declared-symbol surface in the current fixture that I deliberately *didn't* turn into rows is either (a) the 13 caller-class fields (low signal), or (b) symbols that are already covered by the 10 current-model feature probes as locked "Monitor=False" cases.

The Operator's intuition that the fixture "contains pretty much full coverage of what can be added to a class declaration wise" looks correct after this exercise. The remaining gap is in the *matrix* (which symbols are scored vs which are merely declared), not in the *fixture* (which class-declaration shapes are exercised).

### Local Program.cs state

The 16-row addition and three-const + two-helper change live in `MonitorBaseClaude.ToolSmokeTests/Program.cs` on my local checkout, uncommitted (this notes branch is markdown-only per `CLAUDE.md`). Codex can `git diff` against `origin/main` in my checkout if helpful, or lift the row block above into a product commit on `main` directly. Build clean, all 50 + 10 = 60 checks (matrix + probes) pass.
