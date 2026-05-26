# Solution Index Scope

The solution index is a fast source navigation layer for code owned by the watched solution. It is not intended to model framework, NuGet, generated, or build-system internals as first-class source rows.

## Contract

- Index project source that belongs to the watched solution.
- Keep `dotnet build` as the final correctness gate.
- Preserve enough symbol, caller, reference, and relationship data for agents to navigate and edit source confidently.
- Treat external metadata symbols as display context unless source is physically present and indexed.
- Index generated-looking `.g.cs` files only when they are checked in or otherwise physically present under the observed source root.
- Surface incomplete or stale index state explicitly. A partial or diagnostic-bearing index is acceptable; silent confident wrong answers are not.

## Current Boundary

The current indexer builds one Roslyn compilation from C# files under the observed root, plus a Razor pre-pass for `.razor` files. This gives strong coverage for normal source semantics inside one source boundary, but it is not yet a full MSBuild project graph model.

Razor coverage (added 2026-05-26):

- `.razor` files are run through `Microsoft.AspNetCore.Razor.Language` (`RazorProjectEngine.Process`, `FileKinds.Component`); the generated C# is parsed, and symbol positions are projected back to the original `.razor` coordinates via `SourceMappings` so `stableSymbolKey`, `sourceAnchor`, and line/column point at user-written source.
- Symbols whose generated position lies in synthesized scaffolding (no covering source mapping) are dropped, so only `@code` members the developer wrote are indexed.
- Razor pipeline failures surface as `RAZOR0001` index diagnostics, not silent zero.
- `.cshtml` is not currently indexed. Indexing covers reads only — typed mutation tools (`submit_symbol`, `add_method`, `remove_symbol`, etc.) still gate to `.cs`; Razor edits flow through `replace_text_in_file` and `submit_file`.

Known project-system gaps:

- project identity per file and symbol
- true project-reference isolation and visibility rules
- SDK implicit usings from project properties
- per-project conditional symbols and target frameworks
- NuGet package metadata beyond display context
- generated code that is produced only during build and not present as source
- `.cshtml` indexing
- Razor-aware typed mutation tools (write surface)

## Testing Boundary

The external known-answer corpus under `C:\VSCodeProjects\MonitorBaseClaudeTests` is currently local-only and informational for project-system shape tests. Its asserted cases validate source semantics. Its informational cases document areas that should not be counted as hard MSBuild/project-system coverage yet.

Graduation criteria for project-aware indexing:

- load source according to solution/project compile items
- preserve project identity in indexed files and symbols
- bind cross-project source references without flattening unrelated projects into one anonymous compilation
- report incomplete project load or binding diagnostics rather than returning silent wrong answers
- keep `dotnet build` embedded in the workflow as the final validation step
