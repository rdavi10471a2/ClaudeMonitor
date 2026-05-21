---
status: new
type: test-result
created: 2026-05-21
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---

# Finding 52 — Fixture-based 3-way correctness test for the Monitor solution index

## Summary

Staged a deliberately-shaped two-file fixture into the watched solution to exercise every C# symbol kind that matters for `find_indexed_callers`/`find_indexed_references` against a fully-known answer key. The fixture compiles, every callsite and reference is documented inline, and the expected count of each is hard-coded in a comment block inside the fixture so the test is rerunnable.

Result: **the Monitor solution index returned the correct count for 21 of 23 tested cases.** Two confirmed gaps remain. A third observation — Roslyn `find_callers`/`find_references` cannot reach the new fixture symbols even after `rebuild_solution` — is the Operator's hypothesis (Roslyn app probably needs a full restart, not just a rebuild) and is recorded here for Codex follow-up.

The fixture is committed under `SchemaStudio.SematicModel/Tests/McpIndexProbes/` so future index regressions can rerun the same comparison against the same answer key.

## Method

For every symbol declared in the fixture's File A, run three lookups against the same identifier:

1. **Grep** — raw text occurrences in `C:\Schema Studio - DBV2\**\*.cs`, excluding `SourceBakups\` so the data is comparable with the Monitor index (which correctly excludes that folder per Finding 51).
2. **Roslyn** — `mcp__roslyn-codelens__find_callers` / `find_references` after `rebuild_solution`.
3. **Monitor** — `mcp__monitor-base-claude__find_indexed_callers` / `find_indexed_references` using the stable symbol keys returned by `query_solution_index(scope: file)` on each fixture file.

Roslyn dropped out: see "Roslyn observation" below.

## Fixture placement and state

- Branch: `claude-notes/20260521-index-findings-49-51`
- Watched-source path A: `C:\Schema Studio - DBV2\SchemaStudio.SematicModel\Tests\McpIndexProbes\McpCallerProbeFixture.A.cs` (file hash `c4f3f3341a88a6b344bd990fb09394bde1a2e428eab4c866468cbea80bf97561`)
- Watched-source path B: `C:\Schema Studio - DBV2\SchemaStudio.SematicModel\Tests\McpIndexProbes\McpCallerProbeFixture.B.cs` (file hash `5e9c9e87e5645b8aa5e83240519d8b27434c71ac7410b47d1f0dd78dff56d4f9`)
- Project: `SchemaStudio.SematicModel\SchemaStudio.SemanticModel.csproj` (SDK-style, implicit globbing picks up the new subfolder)
- Namespace: `SchemaStudio.SemanticModel.Tests`
- Both files staged and accepted via Monitor session `monitor-20260521164302-599c161b96244fb08`. Decisions: both `accepted` with `decisionMatchesClassification: true`.
- Solution index rebuilt after acceptance: 82 → 84 files, 981 → 1023 symbols, 3926 → 3982 references, 745 → 760 call sites.

## Fixture source — File A (declarations)

`SchemaStudio.SematicModel\Tests\McpIndexProbes\McpCallerProbeFixture.A.cs`:

```csharp
using System;
using SchemaStudio.AIHelpers;

namespace SchemaStudio.SemanticModel.Tests;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class McpProbeMarkAttribute : Attribute { }

public enum McpProbeKind
{
    None = 0,
    Alpha = 1,
    Beta = 2
}

public interface IMcpProbeService
{
    int InterfaceProbe(int seed);
}

public sealed class McpProbeServiceImpl : IMcpProbeService
{
    public int InterfaceProbe(int seed) => seed + 1;
}

[McpProbeMark]
[FileVersion("1.0")]
[AIFileContext(
    "SchemaStudio.SematicModel/Tests/McpIndexProbes/McpCallerProbeFixture.A.cs",
    "Declares known symbols (methods, ctors, properties, events, generics, overloads, extension method, interface, attribute, enum) used by McpCallerProbeFixture.B.cs to drive an apples-to-apples grep/Roslyn/Monitor cross-reference test.",
    Responsibilities = "Hold every probe symbol's declaration and the intra-file caller for the private helper. Each symbol's expected caller/reference count is documented in the answer-key block of McpCallerProbeFixture.B.cs.",
    Nuances = "Do not change identifier names or add new callsites here without updating the expected-count table in File B. McpProbeKind and GetResolverInvoke are intentionally never called externally to test 0-count paths.",
    RelatedFiles = "McpCallerProbeFixture.B.cs",
    LastReviewed = "2026-05-21")]
public sealed class McpCallerProbeTarget
{
    public int PublicIncrement(int value) => value + 1;

    private int PrivateHelper(int v) => v * 2;

    public int CallsPrivateHelper(int v) => PrivateHelper(v);

    public int OverloadedAdd(int a) => a;

    public int OverloadedAdd(int a, int b) => a + b;

    public T GenericIdentity<T>(T value) => value;

    public static int StaticGate(int seed) => seed * 10;

    [McpProbeMark]
    public int MarkedMethod() => 7;

    public static Func<int>? ResolverProperty { get; set; }

    public static int GetResolverInvoke() => ResolverProperty?.Invoke() ?? 0;

    public event EventHandler? ProbeCompleted;

    public void RaiseProbeCompleted() => ProbeCompleted?.Invoke(this, EventArgs.Empty);

    public McpCallerProbeTarget()
    {
    }

    public McpCallerProbeTarget(string label)
    {
        Label = label;
    }

    public string? Label { get; }
}

public static class McpProbeExtensions
{
    public static int ToProbeDoubled(this int v) => v * 2;
}
```

## Fixture source — File B (callers + answer key)

`SchemaStudio.SematicModel\Tests\McpIndexProbes\McpCallerProbeFixture.B.cs`:

```csharp
using System;
using SchemaStudio.AIHelpers;

namespace SchemaStudio.SemanticModel.Tests;

[FileVersion("1.0")]
[AIFileContext(
    "SchemaStudio.SematicModel/Tests/McpIndexProbes/McpCallerProbeFixture.B.cs",
    "Invokes every probe symbol declared in McpCallerProbeFixture.A.cs with a known, deliberately-shaped call site. Each invocation is labeled with the symbol it targets so grep/Roslyn/Monitor results can be compared against a fixed answer key.",
    Responsibilities = "Hold every cross-file caller/reference site needed by the apples-to-apples grep/Roslyn/Monitor cross-reference test. Tests target-typed `new()` ctor invocation deliberately at `_targetTargetTyped`.",
    Nuances = "Do not add or remove call sites without updating the expected-count answer-key block below. _target and _targetExplicit use explicit `new McpCallerProbeTarget(...)`; _targetTargetTyped uses target-typed `new(...)` to isolate the target-typed-new gap.",
    RelatedFiles = "McpCallerProbeFixture.A.cs",
    LastReviewed = "2026-05-21")]
public sealed class McpCallerProbeCallers
{
    // EXPECTED-COUNT ANSWER KEY (apples-to-apples ground truth).
    // Symbol                                                | Callers | References (incl. callers + non-call refs)
    // ----------------------------------------------------- | ------- | ------------------------------------------
    // PublicIncrement(int)                                  | 2       | 2
    // PrivateHelper(int)                                    | 1       | 1
    // CallsPrivateHelper(int)                               | 1       | 1
    // OverloadedAdd(int)                                    | 1       | 1
    // OverloadedAdd(int,int)                                | 1       | 1
    // GenericIdentity<T>(T)                                 | 1       | 1
    // StaticGate(int)                                       | 1       | 1
    // MarkedMethod()                                        | 1       | 1
    // ResolverProperty                                      | n/a     | 2 (read at File A GetResolverInvoke + write here at WritesProperty)
    // GetResolverInvoke()                                   | 0       | 0 (declared, intentionally never called)
    // ProbeCompleted (event)                                | n/a     | 2 (raise at File A RaiseProbeCompleted + subscribe here at SubscribesAndRaises)
    // RaiseProbeCompleted()                                 | 1       | 1
    // McpCallerProbeTarget() parameterless ctor             | 1       | 1 (explicit new at _target)
    // McpCallerProbeTarget(string)                          | 2       | 2 (1 explicit new at _targetExplicit, 1 target-typed new(...) at _targetTargetTyped)
    // Label property                                        | n/a     | 1 (write inside ctor(string) body)
    // ToProbeDoubled(this int)                              | 1       | 1
    // IMcpProbeService.InterfaceProbe(int)                  | 1       | 1 (called via _via interface variable)
    // McpProbeServiceImpl.InterfaceProbe(int)               | 1       | 1 (called directly on _impl)
    // McpProbeServiceImpl ctor (parameterless)              | 1       | 1 (explicit new at _impl)
    // McpProbeMarkAttribute                                 | n/a     | 2 attribute usages (on McpCallerProbeTarget class + on MarkedMethod)
    // McpProbeKind                                          | n/a     | 0
    // IMcpProbeService                                      | n/a     | 2 type refs (impl base list + _via field type)
    // McpProbeServiceImpl                                   | n/a     | ~1 type ref at _impl field type, plus its ctor invocation tracked separately

    private static readonly McpCallerProbeTarget _target = new McpCallerProbeTarget();
    private static readonly McpCallerProbeTarget _targetExplicit = new McpCallerProbeTarget("explicit");
    private static readonly McpCallerProbeTarget _targetTargetTyped = new("targettyped");
    private static readonly McpProbeServiceImpl _impl = new McpProbeServiceImpl();
    private static readonly IMcpProbeService _via = _impl;

    public int SimpleCaller() => _target.PublicIncrement(1);

    public int OtherCaller() => _target.PublicIncrement(2);

    public int CallsBothOverloads() => _target.OverloadedAdd(1) + _target.OverloadedAdd(1, 2);

    public int CallsGeneric() => _target.GenericIdentity<int>(42);

    public int CallsStatic() => McpCallerProbeTarget.StaticGate(3);

    public int CallsMarked() => _target.MarkedMethod();

    public int CallsInterfaceViaInterfaceVar() => _via.InterfaceProbe(5);

    public int CallsInterfaceImplDirect() => _impl.InterfaceProbe(7);

    public int CallsExtension() => 9.ToProbeDoubled();

    public void WritesProperty()
    {
        McpCallerProbeTarget.ResolverProperty = () => 11;
    }

    public void SubscribesAndRaises()
    {
        _target.ProbeCompleted += (_, _) => { };
        _target.RaiseProbeCompleted();
    }

    public int CallsPrivateHelperWrapper() => _target.CallsPrivateHelper(10);
}
```

## Apples-to-apples results (grep vs Monitor index)

Roslyn column omitted because every Roslyn lookup returned `[]` for fixture symbols — see "Roslyn observation" section below. Grep counts are after excluding `SourceBakups\` matches and comment-only matches in the answer-key block.

| # | Symbol | Kind | Answer key (callers / refs) | Grep | Monitor callers | Monitor refs | Match? |
|---|---|---|---|---|---|---|---|
| 1 | `PublicIncrement(int)` | public method | 2 / 2 | 2 | 2 (B:49, B:51) | 2 (same) | ✓ |
| 2 | `PrivateHelper(int)` | private, intra-file | 1 / 1 | 1 | 1 (A:41) | — | ✓ |
| 3 | `CallsPrivateHelper(int)` | cross-file public | 1 / 1 | 1 | 1 (B:78) | — | ✓ |
| 4 | `OverloadedAdd(int)` | overload #1 | 1 / 1 | 1 | 1 (B:53 col 48) | — | ✓ overload disambig |
| 5 | `OverloadedAdd(int,int)` | overload #2 | 1 / 1 | 1 | 1 (B:53 col 75) | — | ✓ |
| 6 | `GenericIdentity<T>(T)` | generic | 1 / 1 | 1 | 1 (B:55) | — | ✓ |
| 7 | `StaticGate(int)` | public static | 1 / 1 | 1 | 1 (B:57) | — | ✓ |
| 8 | `MarkedMethod()` | method with attribute | 1 / 1 | 1 | 1 (B:59) | — | ✓ |
| 9 | `GetResolverInvoke()` | declared-only | 0 / 0 | 0 | 0 | — | ✓ 0-count |
| 10 | `RaiseProbeCompleted()` | method | 1 / 1 | 1 | 1 (B:75) | — | ✓ |
| 11 | `McpCallerProbeTarget()` | parameterless ctor | 1 / 1 | 1 (B:43 explicit) | 1 invocation B:43 | — | ✓ |
| 12 | **`McpCallerProbeTarget(string)`** | **ctor — target-typed-new test** | **2 / 2** | **2** (B:44 explicit + B:45 target-typed) | **1** (B:44 only) | — | ✗ **GAP A** |
| 13 | `IMcpProbeService.InterfaceProbe(int)` | interface method via interface var | 1 / 1 | 1 | 1 (B:61) | — | ✓ |
| 14 | `McpProbeServiceImpl.InterfaceProbe(int)` | impl method direct | 1 / 1 | 1 | 1 (B:63) | — | ✓ |
| 15 | `ToProbeDoubled(this int)` | extension method | 1 / 1 | 1 | 1 (B:65) | — | ✓ |
| 16 | `ResolverProperty` | static property | n/a / 2 | 2 (A:56 read + B:69 write) | — | 2 (both) | ✓ catches read AND write |
| 17 | `ProbeCompleted` | event | n/a / 2 | 2 (A:60 raise + B:74 subscribe) | — | 2 (both) | ✓ catches raise AND subscribe |
| 18 | `Label` | property | n/a / 1 | 1 (A:68 write) | — | 1 ref, `referenceKind: "write"` | ✓ kind labeled |
| 19 | **`McpProbeMarkAttribute`** | **attribute type** | **n/a / 2** | **2** (A:26 + A:51) | — | **0** | ✗ **GAP B** |
| 20 | `McpProbeKind` | unreferenced enum | n/a / 0 | 0 | — | 0 | ✓ 0-count |
| 21 | `IMcpProbeService` | interface type | n/a / 2 | 2 (A:21 impl base + B:47 field) | — | 2 (both) | ✓ |
| 22 | `McpProbeServiceImpl` | impl class | n/a / 2 | 2 (B:46 field + B:46 ctor token) | — | 2 — one `referenceKind: "type"` + one `referenceKind: "construction"` | ✓ |
| 23 | `McpCallerProbeTarget` (type) | main class type | n/a / 5 | 5 (B:43,44,45 field types + B:57, B:69 static receivers) | — | 5 (all five) | ✓ |

**Score: 21 / 23 exact match.**

## Gap A — target-typed `new(...)` ctor invocations not counted

Stable key: `SchemaStudio.SematicModel/Tests/McpIndexProbes/McpCallerProbeFixture.A.cs::SchemaStudio.SemanticModel.Tests::McpCallerProbeTarget::constructor::McpCallerProbeTarget(string)`

Source has two invocations of this ctor:

```text
McpCallerProbeFixture.B.cs:44    private static readonly McpCallerProbeTarget _targetExplicit = new McpCallerProbeTarget("explicit");
McpCallerProbeFixture.B.cs:45    private static readonly McpCallerProbeTarget _targetTargetTyped = new("targettyped");
```

`find_indexed_callers` returns only one row:

```json
{
  "targetStableSymbolKey": ".../McpCallerProbeTarget::constructor::McpCallerProbeTarget(string)",
  "relativePath": "SchemaStudio.SematicModel\\Tests\\McpIndexProbes\\McpCallerProbeFixture.B.cs",
  "referenceKind": "invocation",
  "line": 44,
  "column": 72,
  "snippet": "private static readonly McpCallerProbeTarget _targetExplicit = new McpCallerProbeTarget(\"explicit\");"
}
```

The target-typed `new("targettyped")` at line 45 is absent. The same pattern was visible in the earlier real-codebase wave-1 head-to-head test for `SchemaObjectColumnRepository(string)`, whose only invocation in source uses target-typed `new(AppConfig.Current.ConnectionString)` and returned 0 callers from both Monitor and Roslyn. The fixture now isolates the failure mode: explicit `new ClassName(...)` is counted; target-typed `new(...)` is not.

## Gap B — attribute usages not counted as references to the attribute type

Stable key: `SchemaStudio.SematicModel/Tests/McpIndexProbes/McpCallerProbeFixture.A.cs::SchemaStudio.SemanticModel.Tests::::class::McpProbeMarkAttribute`

Source has two attribute usages:

```text
McpCallerProbeFixture.A.cs:26    [McpProbeMark]                  (on class McpCallerProbeTarget)
McpCallerProbeFixture.A.cs:51    [McpProbeMark]                  (on method MarkedMethod)
```

`find_indexed_references` returns `[]` for the attribute type. Both the `[McpProbeMark]` shorthand usages should appear as references; neither does.

Note this is consistent with Monitor's behavior on `[McpProbeMark]` in this fixture — the index never recorded the attribute-usage syntactic positions as references to the underlying `McpProbeMarkAttribute` type.

## Roslyn observation (Operator's hypothesis — not concluded by this test)

Roslyn `find_callers` / `find_references` returned `[]` for every fixture symbol in three separate phases:

1. Initial run, before any solution rebuild — expected (Roslyn hadn't seen the new files yet).
2. After `mcp__roslyn-codelens__rebuild_solution` reported "Rebuild complete. 6 project(s) compiled in 4.7s" — still `[]`.
3. After fully-qualified names like `SchemaStudio.SemanticModel.Tests.McpCallerProbeTarget.PublicIncrement` — still `[]`.

However `mcp__roslyn-codelens__search_symbols` after the rebuild *did* find both fixture types:

```json
[
  {"type":"class","fullName":"SchemaStudio.SemanticModel.Tests.McpCallerProbeTarget","file":"C:\\Schema Studio - DBV2\\SchemaStudio.SematicModel\\Tests\\McpIndexProbes\\McpCallerProbeFixture.A.cs","line":35,"project":"SchemaStudio.SemanticModel","isGenerated":false,"origin":{"kind":"source"}},
  {"type":"class","fullName":"SchemaStudio.SemanticModel.Tests.McpCallerProbeTarget","file":"","line":0,"project":"","isGenerated":false,"origin":{"kind":"source"}}
]
```

The duplicate hit (one with file path, one with empty file) is itself suspicious — `find_callers` may be resolving the empty-file phantom and finding no callers there. The Operator's working hypothesis is that the Roslyn MCP host needs a full process restart for new source files to participate fully in find-reference semantics, and that `rebuild_solution` re-parses but doesn't fully rebind the call-site graph. This finding records the observation; verifying that hypothesis (restart Roslyn process, rerun the same `find_callers` calls, see if `[]` becomes populated rows) is left for Codex.

If the restart hypothesis turns out to be correct, this becomes a usability finding on `rebuild_solution`'s contract: "rebuild" should imply call-graph rebind, not just project recompile.

## Side-channel observations (not part of the scored test)

- Overlay-compile pollution: `submit_file` and `stage_candidate_for_review` reported `overlayValidation.status: "compiled-with-errors"` with diagnostics in `McpOverlayGateFixture/3/5.cs`. Those files don't exist on disk under `C:\Schema Studio - DBV2\` — they live only as stale Working-mirror leftovers from a 2026-05-19 staging session. The overlay compile pulled those phantom files into the syntax tree set, forcing the Host force-review gate to fire on a clean fixture. Cleanup of the three stale `.cs` files inside the Working mirror was performed mid-test (`McpOverlayGateFixture4.cs` was preserved as it does exist on disk), but `launch_staged_diff` for File B still reported `validationGateDecision: "force_review"` afterward — either the gate decision was carried forward from the prior call in the same session, or the overlay still cached the stale syntax trees. Worth a separate workflow finding; not part of the index-correctness scoring above.
- `query_solution_index(scope: file)` on `IntegrationsViewImportControl.cs` returned a 68,683-character response during prior exploratory work and exceeded the MCP tool output limit — driven by `selectorJson` field redundancy. Worth a separate finding; the fixture files are small enough that this didn't reproduce here.

## Reproducing this test

After any change to the Monitor indexer, the test can be rerun without rewriting the fixture:

1. `mcp__monitor-base-claude__refresh_solution_index` — ensure the fixture is indexed.
2. For each of the 23 stable keys (the ones documented in the fixture answer-key block above), call `find_indexed_callers` and/or `find_indexed_references`.
3. Compare row counts and snippet text against the answer-key block in File B.
4. Any row count delta against the documented expected number is a regression (or a closure if the target was one of the two gaps above).

Roslyn comparison: per the Operator's hypothesis above, restart the Roslyn MCP host before running `find_callers` / `find_references` on these symbols.

## Notes

- The fixture is intentionally kept simple and self-contained. No external dependencies, no MSBuild magic, no generated-code paths.
- Identifiers are unique enough to grep without false positives across the real codebase (`PublicIncrement`, `OverloadedAdd`, `GenericIdentity`, `MarkedMethod`, `ResolverProperty`, `ProbeCompleted`, `RaiseProbeCompleted`, `ToProbeDoubled`, `InterfaceProbe`, `McpCallerProbeTarget`, `McpProbeMarkAttribute`, `McpProbeKind`, `IMcpProbeService`, `McpProbeServiceImpl`, `McpProbeExtensions`).
- The two ctor sites (explicit-new + target-typed-new) sit on adjacent lines (B:44 and B:45) specifically so a diff between the explicit-new caller row and the missing target-typed-new caller row is one line apart in source.
