# Agent Tool-Call Playbook

This playbook is the model-facing contract for using MonitorBaseClaude as Claude's compiler-backed navigation and safe-edit host.

For the current short guidance, use `Docs/Skills/SkillRouter.md` and `Docs/Skills/SystemMonitorStaging.md`.

## Names

- **System Monitor**: the MonitorBaseClaude safety/workflow server. It owns watched-source safety, staging, sessions, WinMerge review paths, ledgers, hashes, and decision classification.
- **Roslyn Tooling**: the compiler-backed navigation surface. It owns semantic code intelligence for Claude: references, callers, diagnostics, type hierarchy, project dependencies, generated code, and analyzer-style inspection.

The intent is compiler-backed help across the whole edit loop:

- before the edit: Roslyn Tooling and source maps guide Claude to the right symbols and call sites
- during the edit: System Monitor uses Roslyn-backed source editing tools for structured symbol surgery and whole-file candidate validation
- after the edit: System Monitor runs syntax/overlay compile validation before the Operator review gate

The WinForms app is the live middleman. Clients should connect through the repo bridge scripts so traffic appears in the dashboard:

```text
Claude/Codex -> McpHubBridge -> WinForms hub -> real MCP server
```

Logs are history. The dashboard live stream is the first-class view of tool traffic.

## First Calls

Every new client session should prove both surfaces before doing real work.

System Monitor:

```text
get_monitor_status
get_tool_manifest
get_staging_guide, when the client needs staging and session-overlay rules
get_workflow_status
```

Roslyn Tooling:

```text
list_solutions
tools/list
```

If the task mentions symbols, references, callers, diagnostics, dependencies, source generators, or impact, use Roslyn Tooling early. If the task mentions source edits, staged changes, WinMerge, hashes, ledgers, sessions, or acceptance, use System Monitor.

`get_smoke_test_catalog` is a debug/maintainer tool. Use it when investigating smoke coverage, reproducing a tool failure, or adding regression probes; do not include it in the normal Claude review or edit-planning path.

For C# symbol discovery, prefer Roslyn Tooling over text or grep search. Literal text search is still valid when the user asks for literal text, comments, strings, generated artifacts, or non-C# files.

## Read Strategy

Use the narrowest current source shape that answers the question.

For C# editing context:

```text
System Monitor find_file, if path is uncertain
System Monitor get_source_map(scope: "folder" or "file", mode: "navigation") for orientation
System Monitor get_source_map(scope: "file", mode: "selector") for the chosen file
System Monitor get_symbol(symbolSelectorJson) for the smallest body
System Monitor get_file only when the map/body path is insufficient
```

For semantic impact:

```text
Roslyn Tooling search_symbols
Roslyn Tooling get_type_overview
Roslyn Tooling find_references / find_callers / find_implementations
Roslyn Tooling analyze_change_impact when blast radius matters
```

Do not replace one with the other. Together they are the compiler-backed navigation experience: source maps identify the current file contract and stable selectors for staged edits; Roslyn references identify cross-file semantic usage.

## Edit Strategy

All watched-source edits go through System Monitor.

```text
start_monitor_session, when doing multi-step work
read/narrow with source maps and symbols
optionally inspect semantic impact with Roslyn Tooling
submit_symbol, set_type_partial, typed add tools, add_symbol, remove_symbol, add_using, remove_using, or submit_file
Host/Operator reviews the staged candidate
record_diff_decision
obey the classification
```

`submit_symbol`, `set_type_partial`, `add_field`, `add_property`, `add_method`, `add_constructor`, `add_nested_type`, `add_symbol`, `remove_symbol`, `add_using`, and `remove_using` are Roslyn-backed source editing surfaces owned by System Monitor. They produce staged candidates instead of mutating watched source.

Prefer the typed add tools when the intended member kind is known. Use generic `add_symbol` as the escape hatch when the narrow tools do not fit.

Never directly write watched source as the normal path. Never use external Roslyn `apply_code_action` as the default mutation path for watched source. If Roslyn Tooling suggests a fix, translate it into a System Monitor staged candidate.

For async or signature changes, do not edit until references, callers, implementations, and change impact have been checked. Caller conversion may need to recurse upward through the call chain. After staged edits compile, run diagnostics again before requesting Operator review.

For async, signature, API, or multi-file edits, stage every coupled candidate into the same monitor session before launching the first review. Preserve encoding, newline mode, and surrounding trivia unless the Operator explicitly requests formatting cleanup. Selector resolution failures and ambiguous selectors must hard-fail staging; do not widen a one-symbol edit into a whole-file edit to get past selector trouble.

## Layout And Diff Stability

For existing C# files, preserve the current member order and local style. When adding a new member, prefer placing it after the most closely related existing member. If there is no obvious related member, append it near the bottom of the containing type, before nested types. Do not reorganize declarations, introduce new region blocks, or perform aesthetic regrouping during a functional edit.

For new C# files generated from scratch, prefer a stable type layout:

```text
constants/static readonly fields
instance fields
constructors
public properties
public methods
protected/internal members
private helpers
nested types
```

If the project already uses regions in comparable files, use ordinary region names such as `Fields`, `Constructors`, `Properties`, `Public Methods`, and `Private Methods`. Do not add regions to existing files that do not already use them unless the Operator explicitly asks for a layout pass.

For new generated classes where no local style exists, regions are acceptable as a front-loaded correctness aid. Prefer this order:

```text
file parameters: namespace, visibility, className, isPartial
Fields
Constructors
Attributes, when the class owns attribute helper types
Properties
Public Methods
Protected/Internal Methods, when needed
Private Methods
Converters, when the class owns converter/helper conversion logic
Nested Types, including enums
```

This new-file convention must not bleed into existing-file functional edits.

TODO: after the workflow stabilizes, decide whether member-order and region conventions should live only in documentation, in a project style profile, or in a monitor-side formatting/layout helper.

## Formatting Oracle Rules

These are product rules for the final System Monitor edit surface. Smoke fixtures should encode them as known-answer checks:

- Replaced symbols should stay at their original location.
- Added symbols should land at the requested insertion point, usually after the closest related member.
- Existing members should not move during functional edits.
- Formatting should be localized to the touched symbol or insertion boundary.
- The touched symbol should be pretty-printed in file context: braces, indentation, nested blocks, collection initializers, and member indentation should align with surrounding code.
- The local spacing rhythm should be preserved. If comparable properties or methods are separated by blank lines, a newly inserted peer should have the same spacing before and after.
- The top-to-bottom diff should remain readable: one changed member or one inserted member, not unrelated whitespace churn.
- Existing source attributes used for monitor/process history, such as `AIFileContext` and `FileVersion`, should not be surfaced as meaningful source-map attributes.

## Insertion Logic Findings

As smoke fixtures are added, capture the reusable logic discovered by each fixture. This is implementation knowledge for the final product, not just test trivia.

### Member Insertion

When adding a member to an existing type:

- Resolve the containing type structurally, not by text position.
- Resolve the insertion point from `afterSymbol` when supplied.
- Insert after the related member rather than regrouping the file.
- Copy the leading trivia pattern from the previous peer member when inserting after a member. This preserves indentation and the blank-line rhythm used by the local member group.
- Give the inserted member trailing end-of-line trivia so the following member stays visually separated.
- Run localized Roslyn formatting on the inserted member in the full file context.
- Verify the final file by ordering checks: previous related member < new member < next original member.

Property-placement fixture finding:

```text
IncludeArchivedCustomers
IncludeInactiveCustomers
CommandTimeoutSeconds
```

The correct result preserved a blank line before and after the inserted property. Plain Roslyn formatting fixed indentation but did not infer the local blank-line rhythm; the monitor had to carry insertion trivia from neighboring members.

Field-insertion fixture finding:

```text
_connectionString
_logger
_clock
constructor
```

Field anchors work with the field variable name, for example `afterSymbol: _logger`. A compact field group with no blank lines between fields should stay compact; the inserted field should receive a trailing end-of-line and the constructor should remain separated by the existing blank-line rhythm.

### Symbol Replacement

When replacing an existing member:

- Resolve the target by selector/stable key.
- Preserve the target member's leading and trailing trivia.
- Replace the whole member, not body text fragments.
- Run localized Roslyn formatting on the replacement member in the full file context.
- Verify unrelated members remain byte-stable or at least text-stable around the replacement.

SQL surgery fixture finding:

```text
GetDatabaseNames stays in its original location.
GetActiveDatabaseNames is a separate insertion.
SqlQueries is a separate insertion.
```

Replacing the method body was safe once the formatter was scoped to the touched node. Whole-file formatting is unnecessary and would damage diff readability.

Method replacement fixture finding:

```text
GetAllTables changed.
GetActiveTables remained unchanged.
Query helper remained below both methods.
```

The oracle must include any support symbols needed by the replacement. In one failed draft, the method replacement referenced `SqlQueries` without defining it; overlay validation caught the compile error. Known-answer fixtures should prove both formatting and compile viability, not just text shape.

### Declaration Modifier Edits

When a refactor requires a companion partial file:

- Treat changing the original type to `partial` as its own staged declaration-level operation.
- Do not smuggle declaration modifier changes into unrelated symbol edits.
- Keep this as an advanced/human-guided refactor shape, not the baseline expectation for simple named-query extraction.

Partial-class fixture finding:

```text
set_type_partial is useful, but not every SQL extraction should force a partial-file design.
```

### Future Fixture Notes

For every new oracle fixture, record:

- the inserted/replaced symbol kind
- the selected insertion anchor
- the local spacing/trivia pattern that had to be preserved
- whether Roslyn formatting alone was enough
- any monitor-side placement/trivia logic required beyond Roslyn formatting
- the final ordering assertion that proves the diff remains readable

## Decision Boundaries

System Monitor is authoritative for:

- what file is watched
- where Working/Staged/History live
- whether a candidate compiled enough to review
- what compiler diagnostics were produced by the staged candidate overlay
- whether the watched file hash matches accepted or rejected state
- whether `dirty-unexpected` blocks more edits

The Operator is authoritative for intent:

- accepted means the Operator saved the full staged candidate in WinMerge
- rejected means the Operator left the watched file unchanged

The final state is the agreement between intent and hash.

## Common Failure Modes

- Calling `get_file` first for C# edits: use `get_source_map` and `get_symbol` first unless full-file context is truly needed.
- Calling Roslyn `find_references` with stale argument names: inspect the schema or known examples. The expected current shape uses `symbol`, for example `DatabaseRepository.GetAll`.
- Treating `symbolName` as mutation authority: use `symbolSelectorJson` or stable keys from selector mode.
- Treating WinMerge as a partial merge workspace: reject close-but-wrong candidates and generate a smaller staged candidate.
- Continuing after `dirty-unexpected`: stop and ask the Host/Operator to inspect or refresh.
- Trying to re-vote a `dirty-unexpected` decision: recover explicitly by refresh/rebase/restage or Operator reconcile.
- Assuming logs are the live signal: the hub live stream is the signal; logs are durable receipts.

## Example: Reference-First Question

User asks:

```text
Where is DatabaseRepository.GetAll used?
```

Good sequence:

```text
Roslyn Tooling search_symbols(query: "DatabaseRepository")
Roslyn Tooling get_type_overview(typeName: "SchemaStudio.Data.DatabaseRepository")
Roslyn Tooling find_references(symbol: "DatabaseRepository.GetAll")
```

No Monitor staging is needed because this is not an edit.

## Example: Safe Edit Question

User asks:

```text
Make DatabaseRepository.GetAll async and update callers.
```

Good sequence:

```text
System Monitor find_file("*Database*Repository*")
System Monitor get_source_map("Data\\DataBaseRepository.cs", scope: "file", mode: "selector")
Roslyn Tooling find_references(symbol: "DatabaseRepository.GetAll")
System Monitor get_symbol for GetAll using selector JSON
System Monitor get_source_map or get_symbol for each affected caller
Roslyn Tooling analyze_change_impact if the cascade is unclear
Stage bounded candidates through System Monitor
```

The model should not stage until it understands the call sites and whether sync wrappers or async propagation are required.

## Example: Dashboard Acceptance Test

Ask a model to perform discovery only:

```text
Use both MCP servers through the project configuration.
Call System Monitor status and manifest.
Call Roslyn Tooling list_solutions.
Find references to DatabaseRepository.GetAll.
Do not stage or edit anything.
```

Expected dashboard evidence:

- System Monitor Traffic shows `get_monitor_status` and `get_tool_manifest`.
- Roslyn Tooling Traffic shows `list_solutions` and `find_references`.
- Rows are marked `live` while the model is working.
- No `submit_*`, `add_*`, `remove_*`, `launch_staged_diff`, or `record_diff_decision` calls occur.

## Setup Rule

Install Roslyn CodeLens locally for this repo even if a global tool exists:

```powershell
.\Tools\Install-RoslynCodeLensLocal.ps1
```

The hub prefers `Tools\RoslynCodeLens\roslyn-codelens-mcp.exe`. Global PATH resolution is a fallback only.
