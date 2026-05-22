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

The current indexer builds one Roslyn compilation from C# files under the observed root. This gives strong coverage for normal source semantics inside one source boundary, but it is not yet a full MSBuild project graph model.

Known project-system gaps:

- project identity per file and symbol
- true project-reference isolation and visibility rules
- SDK implicit usings from project properties
- per-project conditional symbols and target frameworks
- NuGet package metadata beyond display context
- generated code that is produced only during build and not present as source

## Testing Boundary

The external known-answer corpus under `C:\VSCodeProjects\MonitorBaseClaudeTests` is currently local-only and informational for project-system shape tests. Its asserted cases validate source semantics. Its informational cases document areas that should not be counted as hard MSBuild/project-system coverage yet.

Graduation criteria for project-aware indexing:

- load source according to solution/project compile items
- preserve project identity in indexed files and symbols
- bind cross-project source references without flattening unrelated projects into one anonymous compilation
- report incomplete project load or binding diagnostics rather than returning silent wrong answers
- keep `dotnet build` embedded in the workflow as the final validation step
