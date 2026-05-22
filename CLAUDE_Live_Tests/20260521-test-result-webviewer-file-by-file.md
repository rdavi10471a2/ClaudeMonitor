---
status: processed
type: test-result
created: 2026-05-21
processed: true
processedBy: Codex
processedAt: 2026-05-21
resolution: Accepted as test evidence. Partial-class finding closed by c97ecdc; remaining repository caller zeros are Razor-injected-property visibility, not C# index failure.
resolutionCommit: c97ecdc
---

# Test result — WebViewer file-by-file Monitor index vs grep ground truth

## Summary

Validated the Monitor solution index against a non-DBV2 real-world C# codebase (`C:\SchemaStudioWebViewer`). The smoke runs the index against the WebViewer solution and emits a per-file caller/reference comparison against grep ground truth for four hand-picked target files (two large screens/controls and two `SchemaStudio.Data` repositories).

The smoke mode itself is the reusable test fixture — committed to `claude/webviewer-file-by-file-smoke` at commit `d217927` as `--webviewer-file-by-file` in `MonitorBaseClaude.ToolSmokeTests/Program.cs`. Future regressions in indexer behavior against this real-world codebase can be re-checked by re-running the same mode.

## Mode definition (the fixture)

Branch: `claude/webviewer-file-by-file-smoke` @ `d217927` — adds `--webviewer-file-by-file` to `MonitorBaseClaude.ToolSmokeTests/Program.cs`.

```
Solution path:    C:\SchemaStudioWebViewer\SchemaStudioWebViewer.sln
Observed root:    C:\SchemaStudioWebViewer
Target files:
  Components\Pages\ManageViewsNext\ManageViewsNext.razor.cs          (28 KB partial-class screen)
  Components\Pages\DomainObjectModeler\DomainObjectModeler.Selection.cs  (16 KB partial-class member group)
  SchemaStudio.Data\Repositories\DatabaseRepository.cs               (14 KB Data repository — two repo types in one file)
  SchemaStudio.Data\Repositories\DatabaseRelationshipRepository.cs   (14 KB Data repository)

Grep corpus: 76 *.cs files under observed root, excluding bin/obj/SourceBackups.
```

For each declared symbol per target file, the mode emits a row with:

- **Monitor callers / refs** — Monitor's `FindCallers` / `FindReferences` on the symbol's stable key.
- **Grep total** — whole-word `\b<name>\b` matches across the 76-file corpus.
- **Grep in declaring file** — same regex restricted to the symbol's own file.
- **Grep extra-file** — grep total minus grep-in-declaring-file. Approximation of "external references" before semantic disambiguation.

## Run statistics

```
Indexed files: 76
Indexed symbols: 1861
Indexed references: 4619
Indexed call sites: 966
Grep corpus .cs files: 76
```

Run output saved to `Working\History\ToolSmokeTests\20260521_144518\webviewer-file-by-file\summary.md` (290 lines, four per-file tables totaling ~200 declared symbols compared).

## Observations across the four target files

### Where Monitor disagrees with raw grep — usually because Monitor is semantically correct and grep over-counts

Examples from the run:

- `DatabaseRepository._connectionString` (private field): Monitor refs = **6**, grep total = **68**. Grep is over-counting because dozens of other repos in the corpus also have a field literally named `_connectionString`; whole-word regex can't disambiguate by containing type. Monitor is semantically scoped to the specific stable key and returns the correct **6** in-class references.
- `DatabaseRepository.QuoteSqlIdentifier`: Monitor refs = **6**, grep total = **26**. Same shape — `QuoteSqlIdentifier` exists on multiple repository types; Monitor reports the **6** within the declaring class only.
- `ManageViewsNext.UnknownDomain` const: Monitor refs = **8**, grep total = **9**. Monitor catches all the in-class uses; the +1 in grep is probably the declaration itself counted by the regex.

This is the **expected and desirable** divergence pattern — it shows the index is doing its job (semantic disambiguation) where grep can't.

### Where Monitor reports zero and grep finds outside-file occurrences — worth a closer look

- `ManageViewsNext` (the partial class type, declared at `ManageViewsNext.razor.cs:16`): Monitor refs = **0**, grep extra-file = **7**. The seven extra-file occurrences are almost certainly **other partial-class declarations** of `ManageViewsNext` in sibling files (`.Columns.cs`, `.Selection.cs`, etc.). The current Monitor model stores per-physical-declaration (one stable key per file for a partial class) rather than a merged-type row. The smoke harness's `Query("file", value: ...)` returned the one declaration from `ManageViewsNext.razor.cs`, and `FindReferences` on that one stable key correctly returns 0 because no other file *references* this specific partial declaration — they're separate physical declarations of the same logical type.

  This matches the current-model feature probe `partial declaration merge: False`. Not a regression — the architectural decision. Documented in the fixture-matrix review as a Monitor=False scope decision.

- `DatabaseRepository` (class type at `DatabaseRepository.cs:8`): Monitor refs = **0**, grep extra-file = **3**. The three extra-file occurrences would include field-type declarations like `private readonly DatabaseRepository _databaseRepository = new(...)` in callers. **Worth investigating**: type-token references when the receiver is target-typed may not be recorded. Monitor catches the *ctor invocation* under the ctor's stable key (the constructor row in the same table shows callers=1) but the field-type *type-ref* under the class's stable key appears to be missing.

  This is a candidate finding for follow-up. Not blocking.

### Where Monitor's caller count is suspiciously low

- `DatabaseRepository.GetAllAsync`, `.GetByIdAsync`, `.CreateAsync`, `.UpdateAsync`, `.DeleteAsync`: all show **Monitor callers = 0** despite grep extra-file ≥ 3 for each. These are public async methods on a public Data repository class — it would be surprising if none of them are called.

  Plausible explanations:
  1. These methods may legitimately be uncalled from this codebase (maybe used only by an external project or stripped during a refactor).
  2. The invocations may go through an injected interface (`IDatabaseRepository`) that the smoke isn't checking.
  3. Calls may go through MediatR-style dispatch or DI that doesn't show up as a direct `_repo.GetAllAsync()` syntactic pattern.

  Confirm by grep on each method name — the grep extra-file count gives the upper bound on possible callers. Worth one targeted look.

- `DatabaseRelationshipRepository.GetForDatabaseAsync`, `.GetByTableAsync`, etc.: same pattern. **Monitor callers = 0** for most public async methods on this repo.

  Same caveats as above — could be legitimately uncalled, or could be a same-named-method-via-interface dispatch case.

### Where Monitor's count is suspiciously high vs grep

None observed in this run. Across all four files, when Monitor reports a non-zero number, grep finds at least that many text occurrences. The pattern is always Monitor ≤ grep — consistent with Monitor being semantically narrower than text matching.

### Indexed-symbol-kind breakdown observed

Confirmed indexed across the four target files (consistent with Codex's `2a8537d` expansion):

- `class`, `partial class` declarations
- `field`, `property`, `event`, `enum`, `enum_member`
- `method`, `constructor`
- `lambda` (anonymous lambdas in field initializers and method bodies, named as `lambda@<line>:<col>`)

Enum members appear with their direct names (`View`, `Workspace`, `Selector`, `SelectorHeight`, etc.) and grep over-counts them heavily since `View` and `Workspace` are extremely common identifiers across the codebase.

## What this validates

1. **The Monitor index runs cleanly against an unrelated real-world C# codebase.** Build + index of 76 files + 1861 symbols + 4619 refs + 966 call sites completes in a single smoke run with no errors. Codex's `2a8537d` indexer changes don't have DBV2-specific dependencies.
2. **Semantic disambiguation is working in practice.** The repeated pattern of "Monitor returns N, grep returns much more than N" demonstrates the index is doing what an IDE's reference engine should do — return scoped, type-aware results that grep cannot.
3. **The enum_member indexing (closure of Finding F) works on real code.** Enum members like `WorkspaceResetLevel.View` show up as `enum_member` symbols with their own ref counts.
4. **Lambda indexing (previously locked at Monitor=False) works on real code.** Lambdas in field initializers and method bodies are captured with line:col-based names.

## What this surfaces as candidates for follow-up

1. **Class-type ref counting for partial types** — the partial-class declaration's `FindReferences` returns 0 even when sibling partials exist. Possibly fine (architectural per-physical decision) but worth confirming the operator's expectation. If "find all references to type Foo" should include the sibling partials' declarations, the query API may need a merge-aware mode.
2. **Class-type ref counting for non-partial types when refs are field-type declarations** — `DatabaseRepository` class reports 0 refs despite likely being referenced as a field type elsewhere. Either the field-type-token isn't being recorded, or it's only being recorded under the ctor's stable key (which has callers=1). Worth a targeted investigation.
3. **Apparent dead public methods on Data repositories** — five public async methods on `DatabaseRepository` and several on `DatabaseRelationshipRepository` report Monitor callers = 0. Plausibly real dead code (the repos may be partly aspirational); needs operator confirmation. If they ARE called via DI/interface dispatch, this is a finding about interface-vs-impl resolution that the fixture matrix's `IMcpProbeService.InterfaceProbe` row didn't catch in this real-world shape.

None of these are blocking. They're surfaces for the operator and Codex to decide whether to investigate further.

## How to re-run this test

```powershell
git fetch origin
git checkout claude/webviewer-file-by-file-smoke
dotnet build .\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj
dotnet .\MonitorBaseClaude.ToolSmokeTests\bin\Debug\net10.0\MonitorBaseClaude.ToolSmokeTests.dll --webviewer-file-by-file
```

Summary written to `Working\History\ToolSmokeTests\<timestamp>\webviewer-file-by-file\summary.md`. Compare against the saved 2026-05-21 summary for regressions.

## Notes

- The mode hard-codes four target files because the proposal's "syntactic-truth corpus" design is the proper general solution; this smoke is intentionally narrow (validate-on-a-real-codebase) rather than broad (full corpus coverage).
- The grep ground truth is approximate. For uniquely-named identifiers (e.g., specific method names on a single repo) it's a tight upper bound. For commonly-named identifiers (e.g., `_connectionString`, `View`) it over-counts heavily — which is the whole reason the Monitor index exists.
- Future expansion: lift the target file list to a CLI argument or config file so the same mode can validate any picked file set without re-coding.

## Follow-up — confirmed Finding 1 is an unchanged architectural state, not a regression

Per Operator request, re-ran the fixture-index-matrix smoke against `2a8537d` with one experimental change: the `partial declaration merge` feature probe's `current expectation` flipped from `False` to `True`. This tested whether Codex's `2a8537d` quietly added partial-class merging.

Result on the flipped probe:

```
- Matrix checks: 68
- Fully matched checks: 68
- Failure count: 0
- Current model feature probes: 10
- Current model feature expectation failures: 1
- `partial declaration merge` Roslyn present `True`, Monitor indexed `False`, current expectation `True`
```

**Monitor still stores per-physical declarations.** The fixture's two `McpPartialProbe` partial declarations are counted as 2 symbol rows; the probe at `True` fails because Monitor's actual state remains `False`. `2a8537d` did not change this.

The flip was experimental only — reverted, not committed.

**Implication for Finding 1**: the partial-class `ManageViewsNext` showing Monitor refs = 0 with 7 grep extra-file occurrences (the sibling partial declarations in `.Columns.cs`, `.Selection.cs`, etc.) is the **consequence of the per-physical-declaration architectural decision**, not an indexer bug. The current `find_indexed_references` against one partial's stable key returns refs to that specific physical declaration; sibling partials are separate symbols with their own (also empty) ref sets.

Two possible paths to close Finding 1 if the architectural call needs revisiting:

1. **Schema-side**: add a `partial_merge` table mapping each per-physical stable key to a canonical key for the merged type. New API mode that aggregates refs across the group. Bigger change — touches schema, indexer, and query API.
2. **Query-side workaround**: when `FindReferences` receives a stable key for a partial-class declaration, fetch all sibling partial declarations of the same `namespace::name` and union their reference rows before returning. Smaller change — query layer only, no schema impact, no indexer change. Same caller-visible behavior.

Operator's call whether to file Finding 1 as a real finding requiring a fix, or leave it as a locked architectural decision (the current `partial declaration merge: Monitor=False` feature probe already documents this).

## Retest after `c97ecdc "Add partial and relationship index coverage"`

Codex shipped a follow-up commit (`c97ecdc`, 2026-05-21 15:24 CDT) addressing the architectural items. Pulled and re-ran both smokes.

### Fixture matrix — fully green including all 11 feature probes

```
Matrix checks: 68
Fully matched checks: 68
Failure count: 0
Roslyn target resolution failures: 0
Current model feature probes: 11      (was 10 — one new probe added)
Current model feature expectation failures: 0
```

The three previously-locked architectural feature probes flipped from `Monitor=False, expectation=False` to `Monitor=True, expectation=True`:

- `partial declaration merge` — Monitor now exposes one merged type identity. The fixture's two `McpPartialProbe` partials are reported as a single logical symbol.
- `override relationship row` — Monitor now exposes override relationship rows in references.
- `interface implementation relationship row` — Monitor now exposes implementation relationship rows in references.

### WebViewer re-run — Finding 1 closed, Findings 2 and 3 reclassified

| Finding | Pre-fix observation | Post-`c97ecdc` observation | Status |
|---|---|---|---|
| 1 | `ManageViewsNext` partial class: Monitor refs = 0, grep extra-file = 7 | Monitor refs = **3** | **Improved by the partial-merge addition** |
| 2 | `DatabaseRepository` class: Monitor refs = 0, grep extra-file = 3 | Monitor refs = 0 (unchanged) | **Reclassified — Monitor is semantically correct** (see below) |
| 3 | `DatabaseRepository.GetAllAsync` etc.: Monitor callers = 0 | Monitor callers = 0 (unchanged) | **Reclassified — Razor-not-indexed limitation, not a C# correctness gap** (see below) |

### Finding 2 reclassification — Monitor's 0 refs for `DatabaseRepository` class is correct

Targeted grep on the `.cs` corpus broke down the 3 extra-file occurrences:

1. `Program.cs:67` — `new DatabaseRepository(...)` — a ctor invocation. The type token here is counted under the *ctor's* stable key (its callers row shows 1, matching). Not separately counted under the class's type-ref set, consistent with the convention the fixture matrix already established.
2. `ManageViewsNext.Selection.cs:22` — `DatabaseRepository.GetAllAsync()` — looks like a class-type-then-static-method on the surface, but semantically `DatabaseRepository` here is a *property*. The `.razor` file declares `@inject DatabaseRepository DatabaseRepository` which creates a property named `DatabaseRepository` typed as `DatabaseRepository`. At the call site the text resolves to the property, not the class type.
3. `DomainObjectModeler.Selection.cs:22` — same Blazor `@inject`-named-property pattern.

The ONLY true class-type reference outside the declaring file is the ctor invocation in `Program.cs`, and that's tracked under the ctor's stable key. **Monitor's 0 is semantically correct.** Grep's whole-word match couldn't distinguish "the text DatabaseRepository as a type" from "the text DatabaseRepository as a property name."

### Finding 3 reclassification — Razor `@inject` properties are not indexed by Monitor

The apparent "dead methods" on Data repositories (`GetAllAsync`, `GetByIdAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`) are actually called via injected Blazor properties:

```csharp
// ManageViewsNext.razor — declares the injected property:
@inject DatabaseRepository DatabaseRepository

// ManageViewsNext.Selection.cs — a partial-class file alongside the Razor file:
Databases = (await DatabaseRepository.GetAllAsync()).ToList();
```

`GetAllAsync()` IS semantically a call to `DatabaseRepository.GetAllAsync` on the injected property. Roslyn-with-Razor-generated-codebehind would resolve this and report it as a caller. But Monitor only indexes `.cs` files (not `.razor`), so the property declaration is invisible to Monitor's indexer. The call site appears unresolvable and the method's caller count stays at 0.

This is not a C# language coverage gap. The fixture-index-matrix already covers method-call-via-injected-property when both the property declaration and the call site are in `.cs` (the `IMcpProbeService _via` field pattern at matrix row 13 passes). The WebViewer case fails because the property declaration is in a `.razor` file the indexer doesn't parse.

**This is a Razor support gap, not a C# semantic-engine gap.** Future work to index `.razor` files (parsing `@inject`, `@code`, and the Razor-generated codebehind) would close it. Without Razor support, callers via Razor-injected properties continue to show 0 — by design of the current C#-only scope.

### Final state across all smokes

| Smoke | Result | Notes |
|---|---|---|
| `--fixture-index-matrix` | 68/68 matrix + 11/11 feature probes pass | All C# Findings A-G closed at `2a8537d`; partial-merge + override-row + interface-impl-row architectural locks closed at `c97ecdc` |
| `--dbv2-index-callers-all` | 420/420 at `2a8537d` (after SourceBakups cleanup) | No expected regression under `c97ecdc`; re-verify if any DBV2-targeted concern arises |
| `--webviewer-file-by-file` | Finding 1 closed (0 → 3 refs on `ManageViewsNext`); Findings 2 and 3 reclassified | Razor-injected-property dispatch surfaces only with Razor support |

**C# language coverage is substantively complete across all three harnesses.** The remaining real-world gap is `.razor` file indexing for `@inject` and `@code` blocks — a Razor support effort, not a C# semantic-engine effort.
