# Finding 51 — Solution index includes files under `SourceBakups\` that Roslyn's active compilation does not

**Severity:** confusing (semantic mismatch with Roslyn ground truth)

**File/tool:** `mcp__monitor-base-claude__query_solution_index` / `find_indexed_symbols` / `find_indexed_references`

**Observed:** `query_solution_index(scope: folder, value: SourceBakups)` returns 2 indexed files:

- `SourceBakups\BehaviorSplit_20260410_105702\IntegrationsViewImportControl.Behavior.cs` (64442 bytes, lastWriteTimeUtc 2026-04-10, parseStatus `ok`)
- `SourceBakups\BehaviorSplit_20260410_105702\IntegrationsViewImportControl.cs` (20195 bytes)

These contain a full duplicate copy of the partial class `IntegrationsViewImportControl` (1594-line variant) from a pre-split snapshot. Symbols from these files appear in `find_indexed_symbols` results alongside the live UI versions. For example, `find_indexed_symbols("AddSelectedView", kind=method)` returns both the live `UI\MergedEditorSurface\IntegrationsViewImportControl.Persistence.cs::AddSelectedView` and the dead `SourceBakups\...\Behavior.cs::AddSelectedView`. `find_indexed_references` on `PropertyGridDataContext.ConnectionStringResolver` returns a duplicate row from `SourceBakups\...\IntegrationsViewImportControl.cs:140` alongside the live row at `UI\...\IntegrationsViewImportControl.cs:152`.

**Expected:** Either (a) the indexer respects `.csproj` `<Compile Remove="SourceBakups\**" />` (or equivalent solution-level exclusion) and skips these files, OR (b) reference/caller responses tag noise rows so the agent can filter.

**Minimal fix:** Decide whether the index should mirror Roslyn's active compilation set or include all on-disk `.cs` files; document the chosen contract. Today it does the latter without saying so.

**Evidence:** `query_solution_index(folder: SourceBakups)` and the `ConnectionStringResolver` head-to-head, both 2026-05-21 ~15:00 UTC.
