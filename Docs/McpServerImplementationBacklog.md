# MCP Server Implementation Backlog

This is the list of documentation discoveries that should become real MCP server behavior, tools, or tests. Keep this practical: if an oracle fixture teaches reusable edit logic, either fold it into `MonitorWorkflowService` or make it a smoke regression.

## Already Folded Into MCP Server

- Localized Roslyn formatting for touched nodes through `Microsoft.CodeAnalysis.CSharp.Workspaces`.
- `set_type_partial` staged declaration modifier operation.
- Typed member insertion tools are exposed:
  - `add_field`
  - `add_property`
  - `add_method`
  - `add_constructor`
  - `add_nested_type`
- `add_symbol` insertion trivia handling:
  - empty type insertion avoids phantom blank lines
  - same-kind insertion can borrow next-peer leading trivia
  - related-member insertion can borrow previous-peer leading trivia
  - inserted members get trailing EOL trivia
- Source maps skip monitor/process attributes such as `AIFileContext`, `AIChange`, and `FileVersion`.
- `launch_staged_diff` overlay-error gate:
  - compile-failed staged candidates remain available for agent correction
  - WinMerge launch is blocked by default when overlay validation has errors
  - MCP server asks the WinForms hub/host for an explicit force-review decision before launching a compile-failed candidate
  - the same named-pipe hub is used with a separate `hostRequest` first-message shape, so normal Monitor/Roslyn MCP proxy traffic is not confused
  - smoke verified: Host cancel returns `overlay-errors-review-cancelled`; Host yes returns `winmerge-launched` with `validationGateDecision: force_review`
  - Host-unavailable returns `validationGateDecision: host_unavailable` so tests do not fabricate Operator actions
  - session queue enforcement: a cancelled/blocked overlay review marks the record `blocked-overlay-validation`, and later diff launches for other records in the same session return `review-chain-blocked`
- True new-file staging through `submit_file`:
  - nonexistent watched paths can be staged with a `<new-file>` original hash
  - accepted decisions require the watched file to exist with the staged hash
  - rejected decisions are clean when the watched file remains absent
  - smoke verified through `--fixture-new-file-smoke`
- Debug-only smoke-test catalog exposed through `get_smoke_test_catalog`.

## Partially Folded Into MCP Server

- New generated class template behavior:
  - smoke exists through `--fixture-template-class-smoke`
  - currently implemented as whole-file `submit_file` with a placeholder/empty existing file
  - not yet a first-class generated-class MCP tool
- Member editor surface:
  - narrow typed insertion tools exist for fields, properties, methods, constructors, and nested types
  - generic `add_symbol`, `submit_symbol`, and `remove_symbol` remain the main editing primitives
  - constructor/nested/private-helper placement policies still need deeper implementation and smoke coverage
- Using insertion:
  - `add_using` exists and sorts using directives
  - grouping preservation is not done; System/non-System blank-line grouping can still be flattened
- Removal cleanup:
  - clean field removal and dependency-failure smoke tests exist
  - automatic dependent-reference reporting/planning is not done
  - compile-failed staged candidates are gated before review, but the server does not yet propose coupled cleanup candidates

## Implemented Smoke Regressions

- `--fixture-property-placement-smoke`
- `--fixture-method-replacement-smoke`
- `--fixture-field-insertion-smoke`
- `--fixture-field-removal-smoke`
- `--fixture-field-removal-dependency-smoke`
- `--fixture-overlay-gate-smoke`
- `--fixture-overlay-queue-block-smoke`
- `--fixture-template-class-smoke`
- `--fixture-new-file-smoke`
- `--fixture-codex-surgery-drill`
- `--fixture-roslyn-semantic-smoke`

## Implemented External Roslyn Tool Smokes

- `--fixture-roslyn-semantic-smoke` verifies the CodeLens semantic ladder against the configured `CodeLensSolutionPath`:
  - `tools/list`
  - `list_solutions`
  - `get_diagnostics`
  - `search_symbols(query)`
  - `get_type_overview(typeName)`
  - `find_references(symbol)`
  - `find_callers(symbol)`
  - `find_implementations(symbol)`
  - `analyze_change_impact(symbol)`

## Needs MCP Server Tooling Or Hardening

- Manual live WinMerge review of true new-file staging. The deterministic new-file smoke covers accepted/rejected hash classification; the GUI launch path for a missing source file still needs an operator pass.
- First-class generated class template tool:
  - parameters: `namespace`, `visibility`, `className`, `isPartial`
  - optional regions: Fields, Constructors, Attributes, Properties, Public Methods, Protected/Internal Methods, Private Methods, Converters, Nested Types
  - enums belong in Nested Types unless local style says otherwise
- Region-aware insertion:
  - insert inside existing region boundaries
  - preserve existing regions
  - never add regions to existing files during functional edits unless requested
- Constructor overload insertion:
  - `add_constructor` exists
  - anchor by constructor parameter types
  - preserve constructor group ordering
- Private helper insertion:
  - generic `add_method` / `add_symbol` exists
  - anchor within private helper group
  - preserve public/private grouping
- Nested type insertion:
  - `add_nested_type` exists
  - place after helper methods near bottom of containing type
  - include enum/class nested type cases
- Using insertion with grouping:
  - preserve System/non-System grouping
  - do not collapse blank lines between using groups
  - current simple alphabetical sorting may be too crude
- Member editor surface:
  - typed add tools are implemented; add replacement/removal planners or narrow tools where useful
  - first-class editors should guide Claude/Codex into bounded operations for generated class templates and common cleanup plans
  - generic symbol tools remain the escape hatch, but common edits should have narrow tool names, narrow arguments, and predictable formatting behavior
- Removal cleanup:
  - Claude should not knowingly generate a field-only removal while leaving dependent assignments behind
  - clean/removal-dependency tests exist; add planner/tooling so dependent assignments/references are reported before or after staging
  - compile-failed staged candidates are useful feedback for Claude/Codex and may be returned for correction
  - the important gate is that compile-failed candidates should not be treated as final or sent to Operator review as done
  - likely next implementation: removal planning/editor helper that reports dependents and helps stage cleanup candidates after diagnostics

## Needs External Roslyn Semantic Smoke Hardening

- Add a disposable semantic fixture for CodeLens so reference/caller/implementation counts are deterministic instead of depending on the live DBV2 solution shape.
- Add overload-specific reference smoke to prove `find_references(symbol)` distinguishes overloaded members with the same name.
- Add async/API impact smoke that proves callers, implementations, and change impact are inspected before staging coupled multi-file candidates.
- Add generated-code/source-generator smoke covering `get_generated_code` and `get_source_generators`.
- Add diagnostic delta smoke: capture `get_diagnostics`, stage a System Monitor candidate, then query diagnostics again before review.
- Add explicit negative argument-shape smoke for common model mistakes such as `symbolName` passed to `find_references`.

## Needs Agent/Planning Harness

- Fixture setup mode that creates a disposable project and prints only the agent prompt plus expected checklist, then stops before editing.
- Dashboard checklist capture:
  - expected tools
  - actual tools
  - expected coupled staged files
  - expected insertion anchor
  - pass/fail notes

## Needs Documentation/Skill Packaging

- Convert `Docs/Skills/*.md` into actual Claude/Codex skill packaging when the runtime format is chosen.
- Keep `SkillRouter.md` as the default entry point.
- Keep long docs as references, not active prompt payload.
