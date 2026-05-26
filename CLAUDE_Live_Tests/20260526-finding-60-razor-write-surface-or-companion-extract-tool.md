---
status: new
type: proposal
created: 2026-05-26
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
relatedFindings: ["20260526-finding-59-razor-code-block-not-indexed"]
---

## Summary

Finding 59's commit landed the **read surface** for `.razor` `@code` indexing only. Typed mutation tools (`submit_symbol`, `add_method`, `add_field`, `add_property`, `add_constructor`, `add_nested_type`, `remove_symbol`, `add_using`, `remove_using`, `set_type_partial`) still gate to `.cs` via `MonitorWorkflowService.ResolveCSharpFileContext` and reject any `.razor` path. So Razor `@code` members are now reachable for discovery and impact analysis, but editing them still falls through to `replace_text_in_file`. This proposal sketches two ways to close that gap and flags Operator preference.

## Repro

`MonitorBaseClaude.McpServer/MonitorWorkflowService.cs:1579-1588`:

```csharp
private MonitorFileContext ResolveCSharpFileContext(string sourceFilePath, string toolName)
{
    MonitorFileContext context = ResolveFileContext(sourceFilePath);
    if (!context.SourceFilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"{toolName} currently supports C# source files only.");
    }
    return context;
}
```

All ten typed-symbol composition tools route through this. After the indexer change in commit `ab0d483`, you can call `find_indexed_symbols("OpenColumnMergeReviewAsync")` and get back a stable key keyed to `ManageViewsNext.razor`, but feeding that key (or its file path) into `submit_symbol` / `remove_symbol` / `add_method` throws `submit_symbol currently supports C# source files only.`

## Option A — Extend the typed tools to Razor

Make `ResolveCSharpFileContext` accept `.razor`. Then each tool needs Razor-aware composition machinery:

- **`submit_symbol`, `remove_symbol`**: the index gives `TextSpanStart` / `TextSpanLength` in original `.razor` coordinates already. Splice the new Roslyn-formatted member text into the `.razor` at the indexed span. Indentation has to be preserved by reading what's in the `.razor` at the splice point, not by Roslyn formatting.
- **`add_method` / `add_field` / `add_property` / `add_constructor` / `add_nested_type`**: locate the end of the `@code` block (last `}` of `@code { ... }`) and insert before it. Razor SDK reports `@code` boundaries reliably via `RazorCodeDocument.GetCSharpDocument().SourceMappings`; text-scanning is fragile against multiple `@code` blocks, `@functions` blocks, and brace-in-string edge cases.
- **`add_using` / `remove_using`**: `.razor` uses `@using Foo`, not `using Foo;`. Either implement a separate `@using` directive handler or keep these on the `.cs` gate.
- **`set_type_partial`**: the generated component class is implicitly partial — Razor users can't change that. Keep on the `.cs` gate.

Cost: ~200-400 lines of new code in `MonitorWorkflowService.cs`, plus careful indentation preservation, plus splice-side test coverage that has to exercise the same fragility classes that already make Razor edits dicey today.

Operator concern: the splicing/brace-finding machinery is exactly the kind of "may be subtly wrong in real `.razor` files" surface that already cost a session debugging the indexer. Multiplying that risk across ten tools is unappealing.

## Option B — Companion-extract tool (Operator-preferred)

Add **one** new tool that splits a `.razor` file into:
1. The `.razor` file with its `@code` block removed (or emptied to `@code { }`).
2. A new `Foo.razor.cs` companion file containing a `partial class` declaration with all the extracted `@code` members.

After the split, **every existing typed mutation tool just works** against the `.razor.cs` because it's plain C#. No mutation-side Razor awareness anywhere.

### Proposed tool shape

- Name (suggestion): `extract_razor_code_to_companion`
- Argument: `sourceFilePath` (the `.razor` file)
- Behavior: opens a monitor session that produces two Working candidates:
  - The original `.razor` with the `@code` content removed
  - A new sibling `<filename>.razor.cs` containing:
    ```csharp
    namespace <namespace inferred from folder layout + project default>;

    public partial class <component class name from filename> : ComponentBase
    {
        <extracted @code content, dedented one level>
    }
    ```
- Returns: the session id and both staged candidate paths so the operator can launch_staged_diff for each.
- Idempotent: if a `.razor.cs` already exists, refuse with a clear error so the operator can resolve it manually.

### Inference rules to design

- **Component class name**: filename stem. `ColumnReconciliationDialog.razor` → `ColumnReconciliationDialog`.
- **Namespace**: Razor SDK can already compute this. Easiest path: `RazorProjectEngine.Process` the file, take the `GeneratedCode`'s `namespace` declaration. Sanity-check it matches a `.razor.cs` companion that already exists for any sibling `.razor` in the same folder.
- **Base class**: default `ComponentBase` unless the `.razor` declares `@inherits SomeBaseClass`, in which case extract that.
- **Existing partial keyword**: if the component is already partial (rare but possible via a companion that exists), error out — the split is a no-op.
- **Imports**: `@using` directives in the `.razor` stay in the `.razor`. The companion gets the equivalent `using Foo;` for any `@using` directive it would otherwise need; if all `@code` body references resolve via the `.razor`'s `@using` set already, the companion needs no `using` lines. Razor SDK's generated C# resolves this for us — copy `using` lines from the generated code that aren't synthesized framework imports.
- **`@inject` properties**: the simplest correct path is to leave `@inject` in the `.razor`. They're not C# members the way `@code` body declarations are — they're directive sugar that the Razor SDK expands. Don't try to split them out.

### Verification plan

Run against `C:\SchemaStudioWebViewer - Copy\Components\ColumnReconciliation\ColumnReconciliationDialog.razor` (rich `@code`, no `@page`, no existing companion). Acceptance shape:

1. `extract_razor_code_to_companion(sourceFilePath: ".../ColumnReconciliationDialog.razor")` produces two candidates and returns a session id.
2. WinMerge review accepts both.
3. `dotnet build C:\SchemaStudioWebViewer - Copy\SchemaStudioWebViewer.sln` succeeds with 0 errors / 0 warnings.
4. `refresh_solution_index` — Razor file now contributes far fewer (or zero) symbols; `.razor.cs` contributes the extracted members.
5. `find_indexed_symbols("BuildCandidates")` — returns the `.razor.cs` row, not the `.razor` row.
6. `submit_symbol` against `BuildCandidates` in the `.razor.cs` — works, no Razor branch needed.
7. App launches at `http://localhost:5214/manage-views` and the reconciliation dialog behaves identically (visual + interactive smoke).

### Proposed `splitRazorFile` helper — concrete shape

Helper lives in a new file `Services/RazorCompanionSplitter.cs` and is called by the MCP tool wrapper. Pure function over file text; no Working folder writes inside the helper itself (the tool wrapper owns staging).

```csharp
public sealed record RazorSplitResult(
    string OriginalRazorText,   // .razor with @code removed
    string CompanionCsText,     // .razor.cs content
    string CompanionFileName,   // e.g. ColumnReconciliationDialog.razor.cs
    IReadOnlyList<string> Warnings);

public sealed record RazorSplitOptions(
    bool LeaveEmptyCodeBlock = false,   // emit `@code { }` placeholder vs strip entirely
    string DefaultBaseClass = "ComponentBase",
    string? OverrideNamespace = null);

public static class RazorCompanionSplitter
{
    public static RazorSplitResult Split(
        string observedRoot,
        string razorRelativePath,
        string razorText,
        RazorSplitOptions options);
}
```

Algorithm (in order, each step has a clean failure mode):

1. **Parse via Razor SDK first.** `RazorProjectEngine.Process(item, FileKinds.Component)`. If Process throws, return a warning and stop — do not fall through to text-scan. Razor parse failure means the file isn't safe to mutate by any path.
2. **Locate `@code` directives via SourceMappings + the generated syntax tree.**
   - Iterate `CodeDocument.GetCSharpDocument().SourceMappings`.
   - For each mapping, locate the corresponding span in the generated C#.
   - Filter mappings to those whose generated-side text parses as one or more C# `MemberDeclarationSyntax` nodes (use `SyntaxFactory.ParseCompilationUnit` on the slice). This naturally rejects expression mappings (`@if`, `@foreach` slices), `@inject` directive mappings, and `@page`/`@layout` directives — only `@code { ... }` and `@functions { ... }` content matches.
3. **Resolve component class identity.**
   - `className` = Razor SDK's generated class name from the document (anchored to filename stem in practice).
   - `namespaceName` = `options.OverrideNamespace` ?? SDK-derived namespace ?? folder-walk fallback (`<project root namespace>.<folder path>`). If none can be resolved with confidence, return a warning and stop.
   - `baseClass` = explicit `@inherits` directive content ?? `options.DefaultBaseClass`.
4. **Detect blocking conditions, refuse early with a precise warning:**
   - A sibling `<filename>.razor.cs` already exists → refuse (`existing-companion`).
   - The component is declared `partial` in the `.razor` (rare; only via explicit `@inherits` + `@implements` sometimes paired with a directive) → refuse (`already-partial`).
   - Mappings include `@functions { ... }` content (`.cshtml`-shaped) → refuse (`cshtml-functions-block-unsupported` — out of scope for v1).
   - Generated namespace doesn't match a sibling `.razor.cs` companion's namespace in the same folder → warning, allow but flag.
5. **Build companion C# text.** Template:
   ```csharp
   <usings copied from generated C#, filtered to non-framework>
   namespace <namespaceName>;

   public partial class <className> : <baseClass>
   {
   <each extracted @code body, dedented one level relative to its source>
   }
   ```
   - **`using` lines**: copy from the generated C#'s top-level `using` directives. Filter out the synthesized framework imports (`Microsoft.AspNetCore.Components.*`, `Microsoft.AspNetCore.Components.Rendering`, etc., plus any `using` that's already a Razor-implicit). The filter is a static allow/deny list keyed to the SDK version; if any `using` falls outside the deny list it stays.
   - **Member dedent**: read the generated C# member text via Roslyn, get its leading trivia, compute the indentation level of the surrounding `@code { ... }` brace, and subtract that prefix from each member line. Don't rely on Razor SDK indentation — it can be inconsistent.
   - **Member order**: preserve source order from the `.razor`. Do not reorder.
   - **XML doc comments / attributes**: copy along with their member (they're already part of the member's leading trivia in Roslyn).
6. **Build new `.razor` text.**
   - Walk source mappings backward (highest line first) so byte offsets don't shift mid-edit.
   - For each `@code { ... }` block: replace the entire `@code` directive (from `@code` keyword to matching `}`) with either nothing (`options.LeaveEmptyCodeBlock = false`) or `@code { }` (`options.LeaveEmptyCodeBlock = true`).
   - Preserve surrounding blank-line conventions: if the block was preceded by a blank line and the block was at end-of-file, trim the trailing blank line; otherwise leave whitespace untouched.
   - Do not touch `@page`, `@layout`, `@inject`, `@using`, `@inherits`, `@implements`, `@namespace`, or any markup. Those stay in the `.razor`.
7. **Validate the output.**
   - Parse the new companion C# via Roslyn; if it has any errors, the split is incorrect — return the parse errors as warnings and refuse to produce the result.
   - Parse the modified `.razor` through Razor SDK again; if Process throws or the generated C# has new errors that weren't there before, refuse.
   - Both checks are gates, not warnings.

Edge cases the algorithm explicitly handles:

| Case | Outcome |
|---|---|
| Multiple `@code { ... }` blocks in one file | All extracted into the companion in source order. Warning if more than one. |
| `@code` block contains only field declarations | Works the same. |
| `@code` block contains a nested type | Works — Roslyn member parser handles nested types. |
| `@code` body uses `this.X` where `X` is markup-bound | Works — the generated class is still partial with the markup half. |
| `@inherits Foo<T>` with generic | Carried into `baseClass`. |
| `@inject` properties referenced from `@code` | Stay in the `.razor` as `@inject`. The companion sees them as instance properties of the same partial class because the `.razor` half declares them. |
| File has `@page "/route"` | Untouched. Stays in `.razor`. |
| File has no `@code` block at all | Refuse (`no-code-block`). |
| File is in a non-Blazor `.razor` (MVC view) | Refuse early via `FileKinds.Component` parse check. |
| `@functions { ... }` block (cshtml-shaped) | Refuse for v1. File a follow-up if needed. |
| Generated namespace contains `__GeneratedComponent` synthetic | Don't use that. Fall back to folder-walk inference. |

What the helper deliberately does **not** do:

- Touch any tool's `ResolveCSharpFileContext` gate. The gate stays. Post-split, mutations target the `.razor.cs` and the gate's `.cs`-only rule is correct.
- Reformat the companion beyond Roslyn's default member formatting. No `dotnet format` pass — that's an Operator decision.
- Recursively walk folders. One file per call.
- Auto-`@inject` migration. `@inject` stays in `.razor` for v1.

### Tool wrapper around the helper

The MCP tool wraps the helper with the standard Monitor staging idiom:

```csharp
[McpServerTool, Description("Split a .razor file's @code into a sibling .razor.cs partial-class companion. Produces two Working candidates under one session: the .razor with @code removed and the new .razor.cs.")]
public MonitorMultiFileEditResult split_razor_file(
    [Description("Watched .razor file path.")] string sourceFilePath,
    [Description("Optional namespace override; default is the Razor SDK-derived namespace.")] string? @namespace = null,
    [Description("If true, leave an empty `@code { }` placeholder in the .razor. Default false (strip the block).")] bool leaveEmptyCodeBlock = false,
    [Description("Optional monitor session id; one is created if omitted.")] string? sessionId = null);
```

Returns the session id, the two staged candidate paths (`.razor` + new `.razor.cs`), and the helper's warnings. Operator then `launch_staged_diff` each, accepts both via WinMerge, and `record_diff_decision` per file.

### Test corpus to validate against

Before shipping, run the helper against this set and assert correct splits + clean build:

1. `ColumnReconciliationDialog.razor` (rich `@code`, no `@page`, no existing companion, ~50KB, nested `ReconciliationCandidate` class).
2. `ManageViewsNext.razor` (has a `.razor.cs` companion already — must refuse).
3. A small `.razor` with one method in `@code` — sanity case.
4. A `.razor` with `@inject` properties referenced from `@code` — verify they stay in the `.razor` and the companion compiles against them.
5. A `.razor` with `@inherits Foo<TItem>` — verify generic carry-over.
6. A `.razor` with no `@code` — must refuse with `no-code-block`.
7. A malformed `.razor` (deliberate `@code {` with no matching `}`) — must refuse via Razor SDK parse failure, not crash.

Each test asserts:
- Helper output's two texts produce a clean `dotnet build` of the watched solution.
- `find_indexed_symbols` for the extracted members points to the `.razor.cs` post-split, not the `.razor`.
- `submit_symbol` against an extracted member in the `.razor.cs` succeeds (write-surface gate is now satisfied).

### Why this is safer than Option A

- One tool, one well-bounded transformation, one place for the brace-finding / Razor-SDK logic to live.
- Re-uses every existing typed tool unchanged on the `.razor.cs` output.
- Reversible by hand if extraction is wrong — operator can merge the companion back manually.
- Indexing of the post-split state needs no further tooling — `.razor.cs` is already covered.
- The `add_using` / `set_type_partial` semantic mismatches disappear because they apply to the `.cs` companion, not the `.razor`.
- Costs at most a few hundred lines of one extraction routine instead of carrying Razor splice logic in ten mutation tools.

### Trade-offs to weigh

- Forces a structural decision on the watched project: `@code` lives in `.razor.cs` from then on. Operator who prefers in-file `@code` for small components has to opt out.
- The extraction itself is a watched-source mutation — must follow the same stage / WinMerge / vote-plus-hash gate as any other edit. Two files per session = the existing multi-file overlay validation has to pass.
- Doesn't help one-off small `@code` tweaks that operator might want to do in-place. For those, `replace_text_in_file` is still the right tool — same as today. The extraction tool is a workflow upgrade, not a replacement for `replace_text_in_file`.
- If Razor SDK's generated `namespace` doesn't match repo convention for the project, the inference rule has to fall back to folder + project root namespace, which can drift.

## Recommendation

Option B (extraction tool) is the simpler, safer mutation path. Option A (per-tool Razor awareness) can be revisited if the project adopts a convention of keeping small components' `@code` in-file. Either way, the read surface stays as just-shipped — Finding 59's commit `ab0d483` is independent of which option (if any) closes the write gap.

## Notes

- Tool surface is open for Codex to design. The arg shape, naming, and inference rules above are sketches.
- This finding pairs with [[20260526-finding-59-razor-code-block-not-indexed]] — the indexer fix shipped, this is the unresolved write-side half.
- The existing project memory `project_razor_handled_as_text` should be updated after either option (A or B) lands. Until then, the write-side gate on `.razor` stands and `replace_text_in_file` remains the fallback for `.razor` `@code` edits.
