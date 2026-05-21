---
status: ready-for-review
type: handoff
created: 2026-05-21
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---

# Claude Review Task - Fixture Index Matrix

Please review current `main` after pulling the latest commit. Do not merge or replay
`claude-notes/20260521-index-findings-49-51` over current `main`; that branch was based
before the latest index fixes and can revert real product changes.

## Files to review

- `Services/SolutionIndexService.cs`
- `MonitorBaseClaude.ToolSmokeTests/Program.cs`

## What changed

- `SolutionIndexService` now records target-typed `new(...)` as constructor call sites.
- `SolutionIndexService` now records attribute usages as references to the attribute type.
- `MonitorBaseClaude.ToolSmokeTests` now has `--fixture-index-matrix`.
- The fixture smoke generates a temporary three-file C# fixture and compares:
  - fixed answer-key counts,
  - in-process Roslyn semantic counts,
  - Monitor SQLite index counts.
- The fixture covers regular search behavior plus current-model feature probes:
  - indexer declarations,
  - operator overloads,
  - conversion operators,
  - enum members,
  - local functions,
  - lambda bodies,
  - partial declarations,
  - generated-looking `.g.cs` files,
  - override shapes,
  - implicit interface implementation shapes.

## Git cleanup before testing

Please make your local checkout match current `origin/main` before running the review:

```powershell
git fetch origin
git checkout main
git pull --ff-only origin main
git status --short --branch
```

Expected status before running tests: clean `main` at `origin/main`.

If you are still on `claude-notes/20260521-index-findings-49-51`, switch back to `main`.
Do not merge that notes branch into `main`. Its report text can be useful context, but its
code diffs are stale.

## Build

Run from the repository root:

```powershell
dotnet build .\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj
```

Use the DLL directly for smoke runs so Windows apphost locking cannot accidentally run stale
bits:

```powershell
dotnet .\MonitorBaseClaude.ToolSmokeTests\bin\Debug\net10.0\MonitorBaseClaude.ToolSmokeTests.dll --fixture-index-matrix
dotnet .\MonitorBaseClaude.ToolSmokeTests\bin\Debug\net10.0\MonitorBaseClaude.ToolSmokeTests.dll --dbv2-index-callers-all
```

## Where the generated fixtures are

`--fixture-index-matrix` writes a fresh generated project under:

```text
Working\History\ToolSmokeTests\<timestamp>\fixture-index-matrix\FixtureProject\McpIndexProbes
```

The generated files are:

- `McpCallerProbeFixture.A.cs`
- `McpCallerProbeFixture.B.cs`
- `McpGeneratedProbe.g.cs`

These files are generated test artifacts. They should not be copied into the watched DBV2
project unless a separate task explicitly asks for that.

## Expected results

The latest local verification passed with:

- Fixture matrix: `34/34` matrix checks, `10/10` current-model feature probe expectations,
  `0` Roslyn target resolution failures.
- DBV2 all-callers: `146/146` target method/constructor checks, `0` failures.

Your DBV2 file/symbol counts may differ if your watched DBV2 checkout differs, but the smoke
should still pass.

## Review focus

Please check:

- whether the fixture answer-key counts are semantically correct,
- whether the Roslyn comparison is independent enough from the Monitor index,
- whether current-model feature probes are clearly named and not confused with permanent
  missing fixture coverage,
- whether target-typed constructor indexing is correct,
- whether attribute usage indexing correctly maps `[Name]` to `NameAttribute`,
- whether any generated fixture category is misleading or should become a first-class
  indexed model feature.
