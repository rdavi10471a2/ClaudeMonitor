---
status: new
type: finding
created: 2026-05-29
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
supersedes: finding-63
---

## Summary

Razor *declarations* index well, but Razor *reference/caller* data is unreliable in two distinct ways depending on component form. This corrects Finding 63 ("Razor markup invocations not indexed"): markup invocations are sometimes captured and sometimes not, and when captured they land at generated-file coordinates while stamped with the real `.razor` path and hash — which reads as authoritative but is non-navigable. The highest-risk case is the split (two-file) component, where a markup-wired handler returns **zero callers/references**, indistinguishable from genuinely dead code.

Tested against the live monitor-owned index (header: Indexed 2026-05-28 12:42:50, Files 108, Symbols 3224, References 11372, Calls 2189, Relationships 70, Diagnostics 0, Stale 1) on watched solution `C:\SchemaStudioWebViewer V 1.1\SchemaStudioWebViewer.sln`.

## Repro A — inline `@code` component

File: `Components/Pages/ManageDatabases/DatabaseRelationshipsPanel.razor` (real file = 1376 lines).

1. `query_solution_index(scope: file, value: <path>)` → 128 symbols, all attributed to the real `.razor` path.
2. Spot-check declarations against the real file: index reports fields at lines 356-374; real file lines 356-374 are exactly those fields (`selectedTableName` @361, `statusText` @371). Declarations map to **real** coordinates.
3. `find_indexed_references(<statusText key>)` returns a row: `line: 467`, `snippet: "__builder.AddContent(110, statusText);"`.
4. Inspect real file line 467 → it is `    }` (a method's closing brace). The snippet is generated render-tree code never present in `.razor`.
5. Reference rows for `statusText` and `selectedTableName` cite lines up to 2544 and 2266 respectively, both well past the real file's 1376-line end.

## Repro B — split (two-file) component

Files: `Components/Pages/ManageViewsNext/ManageViewsNext.razor` (564 lines, markup only) + partial `.cs` companions `ManageViewsNext.razor.cs` (766 lines), `.Columns.cs`, `.Parser.cs`, `.Selection.cs`.

1. `query_solution_index(scope: file, value: .../ManageViewsNext.razor.cs)` → class `ManageViewsNext` reported at lines 16-766; real file is 766 lines; consts/fields at 28-47 match the real file exactly. Companion `.cs` declarations map to **real** coordinates. (Disproves an offset-shift-from-usings hypothesis: plain C# is read directly.)
2. `find_indexed_references(<field Databases key>)` → all rows in `ManageViewsNext.Selection.cs` at lines 22,24,30,32,179 with real snippets and the `Selection.cs` hash. Cross-partial `.cs` references map to **real** coordinates.
3. `query_solution_index(scope: file, value: .../ManageViewsNext.razor)` (markup) → a single symbol: the generated render class `AspNetCore_93fcc5f49990a38b6eef578b30eecf3aa9d376f8` (`public partial class ... : Microsoft.AspNetCore...`). No member symbols.
4. Real markup line 352 contains `@onclick="@ToggleViewTools"`. `ToggleViewTools` is declared in `ManageViewsNext.razor.cs` lines 129-132 (`find_indexed_symbols` confirms, `isGenerated: false`).
5. `find_indexed_callers(<ToggleViewTools key>)` → `[]`. `find_indexed_references(<ToggleViewTools key>)` → `[]`. The markup invocation is not captured anywhere.

## Expected

- Reference/caller rows should either point at the real source coordinate of the usage, or be clearly marked as non-source/generated so a consumer does not navigate to them as if they were real `.razor` positions.
- A markup-wired handler should not return an empty caller set that is indistinguishable from dead code. Either the markup call site is represented, or the result should signal "markup usage not covered" rather than silently empty.
- Path + hash stamping should not assert that a generated-coordinate row belongs to a file it does not index into.

## Actual

- Inline `@code`: markup and `@code` references are captured but at generated-file coordinates (line numbers exceed the real file length), each stamped with the real `.razor` path and real file hash. Non-navigable, false-authoritative.
- Split: companion `.cs` declarations and cross-`.cs` references are correct (real coordinates). The markup `.razor` carries only the generated render-class shell with no member symbols, and markup invocations of companion members return zero callers/references — a false-negative "dead code" signal.

## Evidence

- Index: `C:\VSCodeProjects\MonitorBaseClaude\Working\Indexes\SchemaStudioWebViewer V 1.1_946fe50f84d0\solution-index.sqlite`
- Repro A symbol key: `Components/Pages/ManageDatabases/DatabaseRelationshipsPanel.razor::SchemaStudioWebViewer.Components.Pages::DatabaseRelationshipsPanel::field::statusText`
  - reference row: `{"line":467,"snippet":"__builder.AddContent(110, statusText);","relativePath":"Components\\Pages\\ManageDatabases\\DatabaseRelationshipsPanel.razor","fileHash":"522dcef2..."}`
  - real file line 467 = `    }`
- Repro B handler key: `Components/Pages/ManageViewsNext/ManageViewsNext.razor.cs::SchemaStudioWebViewer.Components.Pages.ManageViewsNext::ManageViewsNext::method::ToggleViewTools()`
  - `find_indexed_callers` → `[]`; `find_indexed_references` → `[]`
  - real markup call site: `ManageViewsNext.razor:352` → `@onclick="@ToggleViewTools"`
- Repro B markup file symbol: generated class `AspNetCore_93fcc5f49990a38b6eef578b30eecf3aa9d376f8` (only symbol for `ManageViewsNext.razor`)

## Notes

Observation only; not a fix design. Practical consumer rule derived from this run:
- Discovering / jumping **to** a Razor `@code` or companion `.cs` member: index is trustworthy.
- Finding **where** a member is used when markup is involved: do not trust the indexed reference line, and do not trust an empty caller set. Grep the real `.razor` to locate the actual usage.

This supersedes Finding 63's framing. The two failure shapes (generated-coordinate references with real path/hash; silent empty callers for split markup handlers) are distinct and may have separate root causes in how the indexer treats the Razor-generated compilation unit versus the hand-written companion partials.
