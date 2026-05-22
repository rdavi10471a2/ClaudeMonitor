---
status: open
type: doc-suggestion
created: 2026-05-21
processed: true
processedBy: Codex
processedAt: 2026-05-21
resolution: Parked. Useful if Razor-injected-property caller lookup becomes real workflow friction; current text/overlay-compile workflow is sufficient.
resolutionCommit:
---

> **Status note**: this proposal is OPEN but the Operator (2026-05-21) flagged it as added complexity that may not be needed — the current workflow handles .razor as text and the overlay-compile catches @code parse errors via the Razor SDK during staging. Pending real-world testing of whether the index's blindness to Razor-injected-property dispatch causes practical friction. See the closing "Operator's observation" section at the bottom.

# Proposal — Razor indexing (close the WebViewer Finding 3 class of gaps)

## Goal

Extend the Monitor solution index so it sees symbols and call sites declared by `.razor` files. The motivating case is the WebViewer Finding 3 reclassification: `DatabaseRepository.GetAllAsync()` shows Monitor callers = 0 because the call's receiver (`DatabaseRepository`) is a Blazor property declared by `@inject DatabaseRepository DatabaseRepository` in a `.razor` file the indexer doesn't parse. Same pattern applies to anything declared in `@code`, `@inject`, `@page`, `@attribute`, child content handlers, or two-way binding sites in any Blazor project.

This is a **Razor support** effort, separate from the C# semantic-engine work that closed Findings A-G. It's worth doing because WebViewer (and presumably other Blazor projects) won't get the full benefit of the index without it.

## Operator's observation that frames this

The Monitor overlay-compile (used during `submit_file` / `stage_candidate_for_review`) **already** sees Razor symbols indirectly. The overlay loads each project's `Compilation`, which invokes the Razor SDK source generator, which produces `.razor.g.cs` synthetic source trees containing every `@inject` property, every `@code` member, every event handler, and the codebehind glue. The overlay-Compilation can therefore resolve `DatabaseRepository.GetAllAsync()` in `ManageViewsNext.Selection.cs` correctly — it sees the Razor-injected property.

The index currently doesn't share that capability because it builds symbols from per-file `CSharpSyntaxTree.ParseText` only, with no Roslyn Compilation involved. The Razor SDK never runs during a `refresh_solution_index`, so the synthetic trees are never available to the indexer.

**The simplest framing of the proposal**: bring the index's view of source into alignment with the overlay's view. If the index used the same Compilation the overlay does, Razor would be visible for free.

## Three implementation paths in increasing investment order

### Path A — Index Razor-generated `.g.cs` from `obj/`

**What:** Extend the indexer's file walker to also include any `*.razor.g.cs` and `*.cshtml.g.cs` files under each project's `obj/` directory. Treat them as additional source the same way as hand-written `.cs` files.

**How it works:** After any `dotnet build` of the project, the Razor SDK leaves the generated codebehind on disk under `obj/<Configuration>/<TFM>/Razor/<relative-razor-path>.razor.g.cs`. These files contain the synthesized class declarations for each `.razor` file — partial class declarations with all `@inject` properties, all `@code` members, the BuildRenderTree method, parameters, etc. They're C# and parse cleanly with the existing `CSharpSyntaxTree.ParseText` path.

**Pros:**
- Minimum code change. ~30-50 lines extending the file walker plus a path-pattern check.
- No new dependencies.
- Symbol stable keys naturally include the `.g.cs` path, so call sites in sibling `.cs` partials resolve through normal name-binding.
- The generated code has the same Roslyn semantic shape as hand-written code — the existing walkers (`InvocationExpressionSyntax`, `MemberAccessExpressionSyntax`, etc.) just work.

**Cons:**
- Requires the project to have been built at least once. A freshly-cloned checkout has nothing in `obj/`.
- Generated paths vary by build configuration (`Debug` vs `Release`) and TFM. The walker needs to handle the cases.
- Out-of-date `.g.cs` (Razor source edited but project not rebuilt) produces stale symbol info. The index would need to track Razor-source mtime vs `.g.cs` mtime to detect staleness.
- The `.g.cs` shape is an implementation detail of the Razor SDK — version upgrades could change naming conventions.

**Effort:** ~1 day.

### Path B — Build a Roslyn `Compilation` per project during index refresh

**What:** Change `SolutionIndexService.Rebuild` to load each project via `MSBuildWorkspace` (or equivalent), let Roslyn invoke the Razor SDK source generator naturally, then enumerate symbols from the resulting Compilation's full set of `SyntaxTrees` (hand-written + generated).

**How it works:** This is what compilers, IDEs, and the existing overlay-compile do. `MSBuildWorkspace.OpenProjectAsync` returns a `Project` whose `GetCompilationAsync()` builds the full Compilation including all source generators that the project's MSBuild configuration declares. Razor SDK is a source generator; it produces the codebehind during compilation creation. Walk `compilation.SyntaxTrees` and you get every file Roslyn sees, source + generated alike.

**Pros:**
- Semantically complete. Anything Roslyn-with-the-actual-project-config can resolve, the index can resolve.
- Doesn't depend on prior builds — `MSBuildWorkspace` runs the SDK in-process.
- Picks up other source generators automatically (e.g. records, regex generators, custom analyzers).
- Future-proof against Razor SDK shape changes — the index gets whatever the SDK produces.

**Cons:**
- Workspace startup is heavy. Loading a 6-project DBV2-size solution via `MSBuildWorkspace` takes 5-15 seconds and uses several hundred MB. The current file-walker rebuild is ~6 seconds total with minimal memory.
- New dependency on `Microsoft.CodeAnalysis.Workspaces.MSBuild` (already present transitively in the McpServer project but not previously used for indexing).
- The first refresh after server startup is dramatically slower; subsequent incremental updates can be cheap if the workspace is kept alive between refreshes.
- More moving parts that can fail (MSBuild restore, target framework resolution, NuGet, analyzer load).

**Effort:** ~3-5 days including incremental-update wiring so the per-refresh cost stays bounded.

### Path C — Reuse the overlay-compile infrastructure (Operator's hinted path)

**What:** The overlay-compile already constructs Compilation objects per project for the staging validation path. Refactor the Compilation-building code into a shared service that both the overlay AND the index call. Index refresh uses the same Compilation the overlay does; symbols flow through one code path.

**How it works:** Extract the overlay's `BuildCompilationForProject` (or equivalent) into a `RoslynCompilationCache` service. Both `SolutionIndexService.Rebuild` and the overlay's validation path call it. The cache holds Compilations keyed by project path; invalidates on file watcher events; refreshes on demand. The index walks `compilation.SyntaxTrees` for symbol enumeration; the overlay queries the same Compilation for semantic validation.

**Pros:**
- Single source of truth for "what does Roslyn see for this project." Index and overlay agree by construction.
- Leverages existing investment — overlay already loads workspace and handles SDK invocation; index gets it for free.
- Razor coverage falls out of the refactor: anything the overlay sees, the index sees.
- The cache opens the door to other downstream consumers (potential future features like type-hierarchy queries, find-implementations across compilation, etc.) reusing the same Compilation rather than rebuilding it.
- Aligns the architecture with the operator's framing: "the overlay effectively does that indirectly" — make the *indirectly* into *directly* by sharing the path.

**Cons:**
- Refactor cost. The overlay's Compilation-building code isn't currently structured as a shared service; needs an extraction pass with care to preserve overlay's existing behavior.
- Same heavy startup as Path B for the first Compilation load. But that load already happens during the first overlay call, so the cost shifts rather than adds.
- Cache invalidation rules need to be explicit (file change → invalidate; project file change → reload; etc.).
- Risk that overlay's compilation has some session-specific or candidate-specific state baked in that the index doesn't want. Extraction needs to separate the project-level Compilation from the per-candidate overlay state.

**Effort:** ~4-7 days including extraction, cache lifecycle, and incremental refresh wiring.

## Recommendation

**Path C** is the right long-term answer because it aligns the architecture with the operator's intuition and avoids duplicated workspace-loading code. It also future-proofs against any other "the overlay can see this but the index can't" gaps that might surface later.

**Path A** is a pragmatic shortcut that closes the WebViewer Finding 3 class of gaps right now with minimum code. If shipping the Razor coverage faster is the priority, Path A is the V1 — it can land in a day, and Path C can be the V2 follow-up that supersedes it.

**Path B** is essentially Path C without the refactor — viable but leaves duplicated infrastructure. Don't recommend Path B over Path C unless the overlay's compilation code is structurally hard to extract.

## V1 / V2 split if shipping incrementally

**V1 — Path A:** Extend the indexer to ingest `obj/**/*.razor.g.cs` files when present. Update the file walker. Add staleness detection (compare Razor source mtime vs `.g.cs` mtime). Update memory rule for Claude: if working in a Blazor project, ensure the project has been built once before relying on the index for Razor-related queries.

Closes WebViewer Finding 3 immediately for built projects. Ships in a day.

**V2 — Path C:** Refactor the overlay's Compilation-building into a shared service; rebuild the index from that Compilation. Remove the V1 `obj/` walk in favor of the in-process SDK invocation. Update memory rule: no build precondition needed.

Closes the architectural shape concern. Ships in a week. After landing, the Razor coverage works on freshly-cloned checkouts too, and the index/overlay architecture is unified.

## Out of scope for this proposal

- Indexing the Razor source itself (`.razor` files as first-class) — the SDK-generated codebehind already covers everything the indexer needs. Direct Razor parsing would add complexity for no semantic gain.
- `.cshtml` (legacy MVC Razor) — the SDK handles these too; same mechanism applies.
- Razor language services like "find references to this `@page` route" or "rename a Razor parameter" — those are Razor-aware queries beyond the scope of the C# semantic surface.
- `_Imports.razor` global imports — handled by the SDK during codebehind generation; no separate indexer code needed.

## Open questions for Codex / Operator

- Which V1 timeline target? Path A (ship today) vs Path C (architectural win, ship in ~1 week).
- Whether `dotnet build` precondition for Path A is acceptable for the Operator's workflow. If the Operator typically builds frequently, Path A is fine for daily use; if the Operator wants index queries on a never-built checkout, only Path C provides that.
- Whether other source generators currently in use (besides Razor) produce code the index should pick up. If yes, Path C is more strongly favored because it handles all generators uniformly.
- Whether the cache-invalidation rules for Path C should hook into the existing file watcher Monitor uses for the `Working\` mirror, or whether `MSBuildWorkspace.WorkspaceChanged` events are sufficient.

## Notes

- The fixture-index-matrix already proves that the C# semantic engine handles property-receiver dispatch correctly (matrix row 13: `IMcpProbeService.InterfaceProbe(int)` via the `_via` interface variable passes). Razor just hides the property declaration in a non-`.cs` file; the dispatch resolution itself isn't the problem.
- The 4 hand-picked WebViewer target files used in `--webviewer-file-by-file` would automatically improve their Monitor caller/ref counts under either V1 or V2 — re-running that smoke after landing Razor support would be the natural regression check.
- This proposal is independent of the source-truth corpus proposal (`20260521-proposal-source-truth-corpus-smoke.md`). The corpus proposal could itself be extended to a Razor sub-corpus once Razor support lands, but they're separable.

## Operator's observation — may not be needed

The Operator (2026-05-21) flagged that they've been working on the WebViewer codebase without any Razor-specific tooling from Codex and the existing workflow holds up:

- AI edits `.razor` files as text. If the `@code` block parses, fine. If it doesn't, fine — overlay-compile catches it during staging.
- CSS files come through Monitor as regular text files. The same model applies to Razor: it's just another text format the overlay can validate semantically when it matters.
- Accept / reject / iterate is the standard loop. Razor errors surface in the overlay-compile diagnostics like any other compile error. The operator doesn't need the index to resolve Razor-declared symbols to make the edit workflow work.

So this proposal is **added complexity that may not be needed** — pending real-world testing of whether the index's blindness to Razor-injected-property dispatch actually causes practical friction.

The specific known limitation is: the index can't answer "find all callers of method X when callers go through an `@inject`-declared property in a `.razor` file." Whether that's a friction point depends on how often Claude/Codex/the Operator runs that query in practice. If real workflow testing shows it's hit frequently and the grep fallback is awkward, that's the trigger to revisit this proposal. If it rarely matters, the proposal stays parked.

Practical operating rule (saved to memory): when Claude is working on a Blazor project and `find_indexed_callers` returns 0 on a public method that the operator believes is used, suspect a Razor-injected-property call path. Grep the method name on `.razor` files in addition to `.cs` files before concluding the method is dead.
