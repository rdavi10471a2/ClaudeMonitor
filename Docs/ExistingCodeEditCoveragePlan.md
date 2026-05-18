# Existing Code Edit Coverage Plan

This plan defines the edit surface for existing files. The goal is to let a Model such as Claude work over a large practical area while preserving the Monitor workflow:

```text
Model proposes
Tool Server stages
Tool Server validates
Host or sidecar opens diff
Operator saves full candidate or leaves source unchanged
Tool Server verifies
```

The watched source files are never overwritten directly by a Model tool call.

## Current State Snapshot

Workspace roles:

```text
MonitorBaseClaude
  C:\VSCodeProjects\MonitorBaseClaude
  The monitor system currently being built.

Host / WinForms operator app
  C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.csproj
  Human dashboard, manual test bench, telemetry/log surface, local Ollama loop.

Monitor Tool Server
  C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.McpServer\MonitorBaseClaude.McpServer.csproj
  MCP server that owns workflow, file staging, sessions, hashes, ledgers, history, and future safe edit tools.

Sidecar Test Runner
  C:\VSCodeProjects\MonitorBaseClaude\LocalSmokeTests\LegacyToolSmokeTests\LegacyToolSmokeTests.csproj
  Console harness that calls real MCP tools and can launch WinMerge as a Host-like process.

Source implementation being ported from
  C:\VSCodeProjects\ClaudeMonitor\Monitor

Watched solution under test
  C:\Schema Studio - DBV2\Schema Studio.sln

Watched project folder under test
  C:\Schema Studio - DBV2

Roslyn CodeLens Tool Server
  External MCP server installed as roslyn-codelens-mcp.
```

Current solution:

```text
C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.slnx
```

Current implemented Monitor Tool Server surface:

- `get_monitor_status`
- `get_workflow_status`
- `get_self_check`
- `start_monitor_session`
- `list_monitor_sessions`
- `get_monitor_session`
- `record_monitor_session_event`
- `refresh_file`
- `find_file`
- `get_file`
- `check_file_hash`
- `get_file_outline`
- `get_source_map`
- `get_symbol`
- `submit_file`
- `submit_symbol`
- `add_using`
- `remove_using`
- `add_symbol`
- `remove_symbol`
- `record_diff_decision`
- `compare_file`
- `list_monitor_runs`
- `get_monitor_run`
- `list_ledgers`
- `get_ledger`
- `prune_monitor_history`
- `get_tool_manifest`
- `list_watched_projects`

Current implemented editing capability:

```text
read/discover file
read file outline
read Roslyn source map
read one symbol
stage whole-file replacement
stage symbol replacement
stage using add/remove
stage member add/remove
create timestamped staged edit record
derive basic staged metadata
syntax validate
overlay compile validate
return source/staged paths
sidecar launches WinMerge directly
record all-or-nothing reported outcome
verify Operator-saved accepted candidate
classify accepted/rejected/dirty-unexpected by vote-plus-hash agreement
```

Not implemented yet:

- encoding/EOL/BOM/final-newline metadata on staged records
- structured selector hardening for remaining compatibility surfaces such as `get_symbol(symbolName)` and `add_symbol(containingType)`
- `add_class`
- `remove_class`
- `stage_search_replace`
- `stage_context_replace`
- `stage_v4a_patch`
- file lifecycle staging: add/delete/rename
- project/package staging
- MCP resources/prompts for sessions/staged records

Important v1 safety decision:

```text
record_diff_decision v1 should be strict vote-plus-hash gated.
ACCEPTED means the Operator saved the whole staged candidate from WinMerge into the watched file, then the Tool Server verifies the watched hash equals the staged hash.
REJECTED means the Operator did not save the candidate and the watched file still equals the original baseline.
Anything else is DIRTY_UNEXPECTED and blocks more AI edits on that file
until the Host/Operator refreshes state.
```

There is no middle-state classification in v1. The decision pipeline is deliberately strict, testable, and safe.

Expected Operator acceptance pattern:

```text
The Operator generally accepts the whole staged proposal or rejects the whole
proposal. The Operator does not edit either side of the WinMerge review and
does not partially merge hunks.
```

This is the documented happy path for future users and agents. If the Operator wants a different change, they should reject the staged proposal and ask the Model/Host to create a new staged proposal.

The diff review is a final sanity check, not the primary editing surface. The source file is a voting member in the generation: if the Model used the current source correctly, the staged proposal should be close enough to accept as a whole. If the diff shows drastic movement, accidental rewrites, or boundary damage, the right action is reject and regenerate.

Pattern conformance does not prohibit structural evolution. The system may split files, extract classes, move symbols, introduce partial classes, create new files, or reorganize boundaries. Those changes must be explicit structural candidates, performed in small manageable units, and accepted all-or-none into the next converged pattern. The goal is to prevent accidental structural drift, not deliberate architectural evolution.

Duplication is not automatically debt. Unnecessary abstraction is also debt. Do not extract helper methods or centralize small repeated inline code merely to satisfy DRY. Inline repetition is acceptable when it improves reviewability, preserves local workflow clarity, keeps state transitions visible, or avoids unnecessary structural churn. DRY cleanup must not be mixed into narrow feature/edit requests unless it is explicitly requested as a bounded structural candidate.

Structural staged records should eventually carry:

- `structuralIntent`
- `priorPatternVersion`
- `proposedPatternVersion`
- `structuralScope`
- `affectedFiles`
- `expectedMovedSymbols`
- `expectedCreatedFiles`
- `expectedRemovedFiles`
- `sourceMapBeforeHash`
- `sourceMapAfterHash`

Suggested `operationKind` values include `PreservePatternEdit`, `StructuralRefactor`, `ExtractClass`, `ExtractPartial`, `MoveSymbol`, `AddFile`, `RemoveFile`, `RenameSymbol`, `RenameFile`, and `ReorganizeNamespace`.

Important tested behavior:

```text
Tool Server should not own GUI diff lifetime.
Host or sidecar launches WinMerge.
Launching WinMerge from stdio Tool Server was unreliable.
Sidecar-launched WinMerge stayed open long enough for Operator review and survived standby.
```

Current build command:

```powershell
dotnet build C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.slnx
```

Current scripted smoke command:

```powershell
dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\LocalSmokeTests\LegacyToolSmokeTests\LegacyToolSmokeTests.csproj -- --scripted
```

Current disposable fixture accept smoke command:

```powershell
dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\LocalSmokeTests\LegacyToolSmokeTests\LegacyToolSmokeTests.csproj -- --fixture-accept-smoke
```

This creates a DBV2-shaped watched fixture under `Working\Fixtures`, writes a fixture-specific config file, starts the real Monitor Tool Server against that config, stages a candidate, records `accepted`, and verifies the fixture source hash equals the staged candidate hash.

Current disposable decision-gate smoke command:

```powershell
dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\LocalSmokeTests\LegacyToolSmokeTests\LegacyToolSmokeTests.csproj -- --fixture-decision-gate-smoke
```

This uses the DBV2-shaped fixture to test the strict decision classifications directly:

- clean Accept after simulated WinMerge save -> `accepted`
- clean Reject without saving -> `rejected`
- reported Accept without saving -> `dirty-unexpected`
- reported Reject after the candidate landed -> `dirty-unexpected`
- unrelated dirty source edit -> `dirty-unexpected`

Current real-watched source-map artifact commands:

```powershell
dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\LocalSmokeTests\LegacyToolSmokeTests\LegacyToolSmokeTests.csproj -- --source-map-smoke "Data\BaseTableRepository.cs" --scope file --mode selector

dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\LocalSmokeTests\LegacyToolSmokeTests\LegacyToolSmokeTests.csproj -- --source-map-smoke Data --scope folder --mode navigation

dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\LocalSmokeTests\LegacyToolSmokeTests\LegacyToolSmokeTests.csproj -- --source-map-corpus-smoke
```

The focused commands call the real `get_source_map` tool against the configured DBV2 watched solution and emit:

- `source-map-raw.json`
- `source-map-summary.md`

Use file-scope selector mode to test precise symbol selection. Use folder-scope navigation mode to test related-file discovery without reading full bodies. Use full mode explicitly for audit/debug source-map fidelity.

The corpus command walks configured DBV2 C# source files and writes a DBV2-wide source-map analysis under `Working\History\ToolSmokeTests\<timestamp>\source-map-corpus`:

- `maps\...*.source-map.json`: full-fidelity per-file source maps for audit/debug.
- `source-map-compact-index.json`: selector-mode index with stable symbol keys and hashes. The historical filename still says compact; treat it as the selector index.
- `source-map-navigation-index.json`: minimal navigation index for broad routing and next-read selection.
- `source-map-corpus-analysis.json`: machine-readable size ratios, token proxy estimates, symbol totals, and diagnostics.
- `source-map-corpus-summary.md`: human-readable corpus report.

Use raw per-file maps for smoke review and schema debugging. Use the navigation index for broad context selection. Use the selector index after the model has selected a target file or symbol. Corpus indexes are aggregate smoke artifacts, not exact live `get_source_map` envelopes; live responses also include `modePurpose`, budget metadata, and ranked `suggestedNextCalls`.

Current disposable Roslyn surgery smoke command:

```powershell
dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\LocalSmokeTests\LegacyToolSmokeTests\LegacyToolSmokeTests.csproj -- --fixture-roslyn-surgery-smoke
```

This uses the same DBV2-shaped fixture and real Tool Server path to stage and accept `add_using`, `submit_symbol`, `add_symbol`, `remove_symbol`, and `remove_using`.

Current disposable Razor smoke command:

```powershell
dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\LocalSmokeTests\LegacyToolSmokeTests\LegacyToolSmokeTests.csproj -- --fixture-razor-smoke
```

This uses a separate Razor-shaped fixture to verify current safe Razor behavior: discover/read `.razor`, return an empty C# outline, stage a full-file Razor candidate, report `razor-validation-pending`, and accept all-or-none by hash.

Current operator diff smoke command:

```powershell
dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\LocalSmokeTests\LegacyToolSmokeTests\LegacyToolSmokeTests.csproj -- --stage-comment-diff
```

Current next coding step:

```text
Add encoding/EOL/BOM/final-newline diagnostics to StagedEditRecord.
```

Reason:

```text
The system can stage, show a diff, verify Operator-saved accepted candidates, record
strict accept/reject outcomes, and return a read-only Roslyn source map today.
The source-map loop is now closed: a Model can take a stable symbol key or
structured selector from get_source_map and call get_symbol without falling back
to name-only lookup. Source-map structured metadata now includes parse status,
diagnostics, attributes, return and parameter fields, symbol flags, syntax kind,
and field/property distinction. The next useful improvement is better staged
record diagnostic metadata for dirty-unexpected states.
```

## Research Notes

The Codex edit model strongly favors context-anchored edits instead of line-number edits. OpenAI's apply_patch guidance describes V4A-style file operations for create/update/delete, with failures returned as explicit tool results so the model can recover. The GPT-4.1 prompting guide documents the same family of patch formats: hunk context, old code, new code, and no reliance on line numbers. The Codex prompting guide recommends the OpenAI apply_patch implementation because Codex models are trained for that format, but also notes custom tools and MCPs need tuning.

Claude's MCP model adds a discoverability requirement: tools need names and descriptions that tell the Model which tool to call next. Claude Code documentation also exposes MCP resources through explicit `@server:protocol://resource/path` references, while tools remain callable actions. That reinforces the split used here: `get_source_map`, `find_file`, `get_file_outline`, and `get_symbol` are published read/discovery actions, while future resources can expose stable session/staged/file handles.

Relevant source material:

- OpenAI Apply Patch docs: https://developers.openai.com/api/docs/guides/tools-apply-patch
- OpenAI GPT-4.1 prompting guide, apply_patch/V4A section: https://developers.openai.com/cookbook/examples/gpt4-1_prompting_guide
- OpenAI Codex prompting guide: https://developers.openai.com/cookbook/examples/gpt-5/codex_prompting_guide
- OpenAI Agents SDK `applyDiff`: https://openai.github.io/openai-agents-js/openai/agents-core/functions/applydiff/
- Claude Code MCP docs: https://code.claude.com/docs/en/mcp
- Claude Agent SDK MCP docs: https://code.claude.com/docs/en/agent-sdk/mcp
- Claude Code settings and project `.mcp.json` docs: https://code.claude.com/docs/en/settings
- MCP tool specification: https://modelcontextprotocol.io/specification/2025-06-18/server/tools
- MCP resource specification: https://modelcontextprotocol.io/specification/2025-06-18/server/resources
- MCP prompt specification: https://modelcontextprotocol.io/specification/2025-06-18/server/prompts
- V4A explainer used during design discussion: https://codex.danielvaughan.com/2026/03/31/codex-cli-apply-patch-v4a-diff-format/
- AI patching / line-number drift discussion: https://www.morphllm.com/ai-apply-patch
- Comparative edit-format discussion: https://fabianhertwig.com/blog/coding-assistants-file-edits/

Design consequence:

```text
Do not make line numbers authoritative.
Prefer Roslyn symbol identity for C#.
Use context anchors as a fallback for non-symbol or non-C# edits.
Always stage and diff.
```

## Marker And Anchor Research

There are four different things people casually call "markers." They should not be treated as one design.

### 1. Source Code Process Markers

Examples:

```csharp
// AI edit here
// TODO monitor replacement anchor
// ... existing code ...
```

Decision: do not use these as Monitor workflow anchors.

Reasons:

- They pollute watched source files with process state.
- They can accidentally ship.
- They violate the existing monitor rule that routine AI/process comments belong in monitor-owned history, not source.
- They make future models overfit to stale comments.

Exception: normal human-useful source comments are fine when they explain code behavior. Domain metadata such as `AIFileContext` and `FileVersion` is also allowed because it is part of the watched project's established conventions, not a transient edit anchor.

### 2. External Staged Record Markers

These are the preferred Monitor markers.

Store them outside the watched project under monitor-owned state:

```text
Working\Staged\<timestamp>_<safe-file>_<operation>.json
Working\Sessions\<sessionId>.json
Working\History\Ledgers\...
```

Each staged record should carry enough anchor data to verify later without adding comments to source:

- `stagedRecordId`
- `batchId`
- watched relative path
- prior converged file hash
- original baseline raw hash
- staged candidate raw hash
- normalized original/staged text hashes for diagnostics only
- original/staged encoding and BOM flags
- original/staged newline kind and final-newline flags
- operation kind
- Model intent manifest
- Roslyn-derived symbol metadata
- original and staged symbol text hashes
- using directive changes
- source/staged paths
- validation status
- queue state

This is the right home for "markers" in our workflow.

### 3. Context Anchors

Codex/V4A and Aider-style edits use nearby existing text as an anchor. This is useful for non-C# files and for edits that do not map cleanly to a Roslyn symbol.

Relevant observations from the research:

- OpenAI's apply_patch flow expects a patch harness to apply structured create/update/delete operations, report success/failure, and give the model actionable error output when a patch cannot apply.
- The Agents SDK `applyDiff` helper applies headerless V4A diffs and throws when the diff cannot apply cleanly.
- Aider uses model-specific edit formats, including whole-file and search/replace blocks; whole-file is simple but expensive, while search/replace is cheaper but needs exact source text.
- Cursor's Apply separates generation from integration: the chat model proposes, a specialized apply path integrates.

Monitor consequence:

```text
Context anchors are allowed as staging input.
They are not allowed as final verification authority.
```

If a context patch applies, the Tool Server still stages the result, derives metadata, validates, launches review through the Host/sidecar, and verifies after Operator decision.

### 4. Roslyn Semantic Anchors

For C#, Roslyn symbol identity is the strongest anchor:

- containing namespace/type
- symbol kind
- symbol name
- parameter list or property/event/field identity
- syntax span
- leading attributes/docs
- normalized symbol text hash

Decision: use Roslyn semantic anchors before text anchors for C# edits.

This gives us better coverage than Codex context matching for common C# work:

- replace a method body
- add a method after another method
- remove a property plus attached attributes/docs
- add or remove a using
- add a class to a namespace
- detect whether a staged proposal respected the intended symbol boundaries before the Operator saves it

### Marker Priority

Use this priority order when staging and verifying existing-file edits:

1. Roslyn semantic anchors for C# symbols/usings/classes.
2. Exact search/replace blocks for small non-symbol edits with unique old text.
3. V4A/context patches for broader non-symbol edits.
4. Whole-file replacement only when the file is small enough or the change is inherently whole-file.

Line numbers may be returned as hints for humans, but never as the authoritative edit location.

### Marker Failure Rules

When an anchor fails, return a recovery-shaped error:

- file not found
- original hash changed
- symbol not found
- symbol ambiguous
- context anchor not found
- context anchor matched multiple locations
- syntax validation failed
- overlay compile failed

The response should include enough information for the Model to re-read the outline/symbol/file and try again, but it should not dump unnecessary file content.

### Glyph And Comment Anchor Rule

Do not use emoji, decorative glyphs, or transient source comments as Monitor anchors. This is stronger than a style preference:

- glyphs can be altered by copy/paste, encodings, fonts, normalization, and tool output
- context patches can fail when a comment/glyph line changes even though the real symbol is stable
- process comments can accidentally ship
- models can overfit to stale marker text instead of reading the current source structure

If existing source contains glyphs or expressive comments, treat them as human-owned legacy content. Preserve or remove them only when the requested code change calls for it. Never use them as edit markers.

Preferred anchors remain:

1. Roslyn source map and structured symbol selectors for C#.
2. Exact unique text blocks for small non-C# edits.
3. V4A/context patches with real surrounding code, not process comments.
4. Whole-file staging when the change is inherently whole-file.

## Razor File Sensitivity

Razor files need a separate safety lane. They are not plain C# files even when they contain C#.

Microsoft's Razor documentation describes Razor as a mix of markup, C#, and HTML. Razor uses `@` transitions between HTML and C#, `.razor` component files can contain `@code` blocks, and generated C# is produced by the Razor toolchain. That means the current C#-only Roslyn parse path is not authoritative for `.razor` or `.cshtml` files.

Current Monitor behavior:

- `find_file` can discover `*.razor`.
- `get_file` can read `.razor`.
- `submit_file` can stage `.razor` as text and return diff paths.
- C# syntax validation is not applicable to raw `.razor` text.
- C# overlay compile validation is not authoritative for raw `.razor` text.
- `.razor` staging now reports overlay status `razor-validation-pending`.
- `get_file_outline` returns an empty outline for non-`.cs`.
- `get_symbol` rejects non-`.cs`.
- history pruning already treats `.razor` as a tracked source-like file type.
- the current watched DBV2 solution has no `.razor` or `.cshtml` files, so Razor behavior is exercised through the separate disposable Razor fixture smoke.

Risk:

```text
A .razor file can be staged today, but the Tool Server cannot yet prove
Razor syntax, generated C# validity, component parameter validity, route
directive validity, or markup/C# transition correctness.
```

Safe current rule:

```text
For .razor and .cshtml, allow read/find/full-file staging/diff only.
Do not offer symbol surgery yet.
Do not claim semantic validation passed.
Return validation status as razor-validation-pending until Razor-aware
project-build validation exists.
record_diff_decision can still classify exact ACCEPTED or exact REJECTED
by hash, but any hash mismatch is DIRTY_UNEXPECTED.
```

Razor-specific marker/anchor guidance:

- Prefer whole-file staging plus Operator diff for now.
- Context anchors can be used for small markup replacements, but only as staging input.
- Do not use C# Roslyn symbol anchors directly against `.razor` source text.
- Do not insert monitor process comments into Razor markup.
- If a comment is genuinely needed in Razor source, prefer the project's normal convention and avoid transient workflow markers.
- Treat `@code` blocks, directives, markup, attributes, and child content as different edit regions.

Future Razor-aware tools:

- `get_razor_outline(path)`
  - Return directives, route templates, component name, `@code` members, parameters, injected services, and top-level markup sections.

- `get_razor_region(path, regionKind, nameOrAnchor)`
  - Read one directive, `@code` member, markup block, or component section.

- `stage_razor_markup_replace(path, beforeContext, oldMarkup, newMarkup, afterContext?)`
  - Context replacement for markup-only edits.

- `stage_razor_code_block_replace(path, memberName, code)`
  - Replace a member inside `@code` only after Razor-aware parsing is available.

- `stage_razor_directive(path, directiveName, value, operation)`
  - Add/remove/replace directives such as `@page`, `@using`, `@inject`, `@attribute`.

- `validate_razor_overlay(path, stagedPath, sessionId?)`
  - Use the Razor project build/toolchain or generated Razor C# output, not raw `CSharpSyntaxTree.ParseText` over `.razor` text.

Implementation priority for Razor:

1. Add explicit `.razor` validation status values so pending validation is visible and not confused with success.
2. Add `get_razor_outline` as a read-only tool.
3. Add markup-only context staging.
4. Add Razor build validation through the project toolchain.
5. Only then add `@code` member operations.

## Claude/MCP Comparison Notes

Codex-style editing is mostly about applying local file changes reliably. Claude-style MCP integration adds a separate concern: the edit tools must be discoverable, permissioned, and cheap enough to keep available in a long session.

MCP tools are model-controlled, so Claude can discover and invoke them from the user request. The Tool Server still needs small, unambiguous tool names, strongly typed arguments, and structured results. For our workflow, every edit tool should return structured JSON plus a short text summary for compatibility. The structured result is the contract; the text summary is for humans and older clients.

MCP resources are application-driven context. They are a good fit for stable handles that are not themselves actions:

- `monitor://status/current`
- `monitor://sessions/{sessionId}`
- `monitor://staged/{stagedRecordId}`
- `monitor://files/{relativePath}/outline`

MCP prompts are user-controlled templates. They are a good fit for safe operator workflows that should be explicitly selected rather than inferred:

- `monitor-edit-existing-file`
- `monitor-stage-symbol-change`
- `monitor-review-staged-diff`
- `monitor-record-operator-decision`

Claude Code also supports project `.mcp.json` and settings-level allow/deny controls. That means the watched solution should eventually carry a project-scoped `.mcp.json` for the Monitor Tool Server and Roslyn CodeLens Tool Server, while machine-local paths stay configurable.

Claude's tool-loading model also argues for a small always-visible core and deferred or manifest-discovered advanced tools:

- Always visible: status, manifest, find/read/outline/symbol, stage file, record decision.
- Advanced: symbol surgery, patch fallback, project/package edits, history pruning.

This keeps the Model from drowning in a huge tool list while still giving it a route to the larger surface when needed.

## Token And Context Monitoring

Token monitoring is useful but lower priority than the staging and decision gate.

Current conclusion:

```text
Monitor should record token/context telemetry when a Host can provide it,
but token estimates must not become an edit correctness gate.
```

Practical sources:

- Local Ollama can return `prompt_eval_count` and `eval_count` from `/api/chat`; the Host can log these as provider-reported prompt/response token counts.
- OpenAI API responses expose usage fields such as prompt and completion tokens.
- Claude Code exposes context/cost/usage through UI commands such as `/context`, `/cost`, and `/usage`; Monitor should not depend on scraping these, but a Host bridge can let the Operator record them or display them beside Monitor telemetry.

Planned telemetry fields:

- `estimatedInputCharacters`
- `estimatedOutputCharacters`
- `estimatedInputTokens`
- `estimatedOutputTokens`
- `providerPromptTokens`
- `providerCompletionTokens`
- `providerContextTokens`
- `providerContextLimit`
- `contextBudgetWarning`

Design use:

```text
Use token/context telemetry to encourage narrower reads:
find_file -> get_source_map -> get_symbol -> get_file only when needed.
```

Do not spend engineering time trying to make token counts exact across all providers before the core edit loop is dogfooded.

## Current Implemented Editing Surface

Implemented now:

- `get_file`
- `find_file`
- `get_file_outline`
- `get_source_map`
- `get_symbol`
- `submit_file`
- `submit_symbol`
- `add_using`
- `remove_using`
- `add_symbol`
- `remove_symbol`
- session/hash tools
- staged edit records
- syntax validation
- overlay compile validation
- strict vote-plus-hash `record_diff_decision`
- sidecar WinMerge smoke test

Not implemented yet:

- encoding/EOL/BOM/final-newline metadata on staged records
- structured selector hardening for remaining compatibility surfaces
- class add/remove
- Codex-style context patch staging
- generic sidecar staging command

## Published Agent Loop

For C# edits, the surfaced tool descriptions should lead a Model through this loop:

```text
start_monitor_session, when useful
find_file to locate target and related files
get_source_map on the target file, folder, or project slice, using navigation mode for broad orientation and selector mode for chosen files
get_symbol or get_file for only the context needed
check_file_hash if the session has gone stale
submit_symbol/add_symbol/remove_symbol/add_using/remove_using or submit_file
Host/sidecar opens the returned source/staged paths in WinMerge
Operator saves the full candidate or leaves source unchanged
record_diff_decision classifies by vote-plus-hash agreement
```

The Model has permission to read related files under the watched root when the change requires context. It should prefer narrow reads:

1. `find_file` for candidate files.
2. `get_source_map(mode: navigation)` for broad structure and related symbol discovery.
3. `get_source_map(mode: selector)` for stable symbol keys and hashes after a file is chosen.
4. `get_symbol` for member bodies.
5. `get_file` only when the full file is needed.

`get_source_map` is a published Tier 1 tool now. It replaces the old background source-map artifact that Codex could not directly consume. It should be called on demand before C# edits, especially when related files, moved symbols, partial classes, or structural refactors are possible.

Source-map mode hierarchy:

- `navigation`: broad orientation for project/folder scope. It is cheap, body-free, and returns current file/type/member shape plus ranked `suggestedNextCalls` to file-level selector maps.
- `selector`: target selection for file scope. It returns stable symbol keys, text hashes, structured selector fields, attributes, flags, and ranked `suggestedNextCalls` to `get_symbol`.
- `full`: audit/debug mode. It returns full source-map fidelity and absolute paths when needed; it is not the default model context.

Current hardening notes:

- Source-map signatures strip leading trivia so comments and glyphs do not become accidental anchors.
- Type symbols include syntax-level base/interface text in `baseTypes`.
- Event declarations and event fields are surfaced as `event` symbols.
- Responses include `modePurpose`, `estimatedTokenProxy`, `budgetLimit`, `wasTruncated`, optional `suggestedNarrowing`, and ranked `suggestedNextCalls`. Over-budget responses now truncate source-map file payload details and tell the client to retry with less detail.
- Source maps are discovery artifacts only. They do not replace the strict vote-plus-hash accept/reject gate.

## Edit Strategy Tiers

### Tier 1: Read And Narrow

These tools reduce token usage and reduce accidental rewrites.

Existing:

- `find_file(fileNameOrPattern, maxResults)`
- `get_file(path)`
- `get_file_outline(path)`
- `get_source_map(path?, scope?, mode?)`
- `get_symbol(path, symbolName | symbolSelectorJson)` with structured-selector support.
- `check_file_hash(sessionId, path)`
- `record_diff_decision(stagedRecordId, accepted|rejected)` for completed staged proposals

Tool-description requirement:

```text
get_source_map must be described as an expected pre-edit discovery step.
navigation mode should help the Model choose the next file/type/member.
selector mode should help the Model choose stable symbol selectors.
find_file/get_file/get_symbol must make clear that related-file context reads
are allowed inside the watched root when needed for a bounded candidate.
```

Source-map response fields should be mode-specific:

- `navigation`: relative path, parse status, diagnostic count, symbol kind/name/signature, namespace/containing type when useful, base type text, line spans, and next selector-map calls.
- `selector`: navigation fields plus file hash, stable lexical symbol key, normalized symbol text hash, structured attributes, modifiers, return/parameter fields, symbol flags, syntax kind, and next `get_symbol` calls.
- `full`: selector fields plus full diagnostic summaries, usings, absolute source path, and schema/debug detail.
- events, fields, methods, constructors, properties, delegates, and types

Add:

- `search_text(pattern, path?, maxResults?)`
  - Search watched project text.
  - Return file, line snippet, and match count.
  - This is discovery only; line numbers are hints, not edit authority.

- `get_file_slice(path, startAnchor, endAnchor?, maxCharacters?)`
  - Read a bounded region anchored by text or symbol name.
  - Intended for non-C# and large mixed files.
  - Returns current file hash.

### Tier 2: Preferred C# Symbol Operations

Use Roslyn for C# whenever the edit maps to a symbol, member, class, or using directive. These operations should generate a full staged file internally but expose a small, typed tool contract to the Model.

Add:

- `submit_symbol(path, symbolSelector, code, manifestJson?)`
  - Replace an existing method, property, field, event, constructor, delegate, enum member, or nested type.
  - Preserve leading attributes and XML docs unless replacement explicitly includes them.
  - Server derives original and staged symbol metadata.

- `add_symbol(path, containingType, symbolType, code, afterSymbol?, manifestJson?)`
  - Insert method/property/field/event/constructor/nested type into an existing type.
  - `afterSymbol` is an ordering hint, not a required anchor.
  - Server chooses insertion point via Roslyn.

- `remove_symbol(path, symbolSelector, manifestJson?)`
  - Remove symbol plus attached attributes/XML docs.
  - Clean up whitespace.

- `add_using(path, namespace)`
  - Add using if missing.
  - Respect file-scoped namespace and existing ordering as much as practical.

- `remove_using(path, namespace)`
  - Remove using if present.
  - Do not remove if required by remaining code unless `force` is later added.

- `add_class(path, code, namespaceName?, afterType?)`
  - Add a class/record/struct/interface to an existing file.
  - Validate syntactic completeness before staging.

- `remove_class(path, className)`
  - Remove type and all attached attributes/XML docs.

Additional symbol coverage to add beyond the current planned list:

- `rename_symbol(path, oldName, newName, scope = "file")`
  - File-local rename first.
  - Cross-project rename should wait for CodeLens/Roslyn workspace integration.

- `replace_attribute(path, targetSymbol, attributeName, code)`
  - Useful for file-level metadata such as `FileVersion`.

- `add_attribute(path, targetSymbol, code)`
- `remove_attribute(path, targetSymbol, attributeName)`

- `replace_constructor_initializer(path, typeName, constructorSignature, initializerCode)`
  - Niche but common in C# refactors.

- `replace_property_accessor(path, propertyName, accessorKind, code)`
  - Covers getter/setter edits without replacing the whole property.

### Tier 3: Codex-Style Context Patch Fallback

Claude/Codex-like models sometimes need to perform edits that do not fit cleanly into one symbol operation. To give them a larger work area, add a staged patch tool that accepts context-anchored edits but still uses the Monitor safety model.

Add:

- `stage_v4a_patch(patchText, sessionId?, manifestJson?)`
  - Accept a V4A-like patch envelope.
  - Supported operations:
    - add file
    - update file
    - delete file
    - move/rename file, later
  - Paths must be relative to the watched project root.
  - Reject absolute paths and `..`.
  - Apply patch to a virtual overlay/staged workspace only.
  - Do not write watched source.
  - Return per-file staged records and per-file apply status.

- `stage_search_replace(path, oldText, newText, expectedOccurrences = 1, sessionId?, manifestJson?)`
  - Exact string replacement for cases where the Model can provide a unique old string.
  - Reject if occurrences do not match expectation.
  - Stage only.

- `stage_context_replace(path, beforeContext, oldText, newText, afterContext?, sessionId?, manifestJson?)`
  - A simpler non-V4A context replacement tool.
  - Useful for Models that are not fluent in V4A.
  - Reject ambiguous matches.

Context patch rules:

- No line-number-only edits.
- Every update must include old text or context anchors.
- Ambiguous anchors fail with an actionable error.
- Failed patch results must tell the Model which anchor failed.
- Multi-file patch support should be all-or-none by default until queue semantics are finished.
- Successful patch application still creates one `StagedEditRecord` per file.

### Tier 4: File-Level Operations

Add staged file lifecycle operations for existing-project work.

- `stage_new_file(path, content, manifestJson?)`
- `stage_delete_file(path, manifestJson?)`
- `stage_rename_file(oldPath, newPath, manifestJson?)`

Rules:

- For C# new files, validate syntax and overlay compile.
- For delete/rename, detect project references and compile impact.
- Deleting a C# file should require overlay compile to pass or return `staged-with-errors`.

### Tier 5: Project And Package Edits

These should come after symbol/file staging is stable.

- `stage_csproj_package_reference(csprojPath, packageName, version?)`
- `stage_csproj_project_reference(csprojPath, referencedProjectPath)`
- `stage_csproj_compile_item(csprojPath, itemPath, action)`

Most SDK-style C# projects include files by glob, so explicit compile item edits should be rare.

## Record Diff Decision Contract

This handoff is implemented and is the required gate before adding more edit tools.

Tool:

```text
record_diff_decision(stagedRecordId, decision, note?, sessionId?)
```

Arguments:

- `sessionId`: optional but preferred.
- `stagedRecordId`: exact staged edit record returned by staging.
- `decision`: Operator-reported outcome, `accepted` or `rejected` for v1.
- `note`: optional Operator note.

The decision argument is not the authority. It is the Operator's report:

- `accepted`: WinMerge saved the full staged candidate into the watched file.
- `rejected`: WinMerge was not saved and the watched file should still equal the original baseline.

The Operator report is not authority by itself. The raw file hash is not enough by itself. Final classification is the agreement between the reported decision and the watched file hash.

Behavior:

1. Read the staged record.
2. Re-hash watched source.
3. If reported `accepted` and watched hash equals staged candidate hash, classify `accepted`.
4. If reported `rejected` and watched hash equals original baseline hash, classify `rejected`.
5. If the report and watched hash disagree, classify `dirty-unexpected`.
6. If watched hash matches neither original baseline nor staged candidate, classify `dirty-unexpected`.
7. If staged hash equals original baseline hash, treat the candidate as `no-change` / `no-op-staged` and do not enqueue a normal diff by default.
8. Mark dirty paths blocked for further AI edits until refresh/re-read.
9. Return compact verification envelope.

Closing WinMerge is not a decision. The sidecar or Host asks the Operator for the decision and then calls this tool.

## Verification Classes

Use the strict v1 priority order:

1. `accepted`
   - Operator reported `accepted`, WinMerge/Operator saved the whole staged candidate into the watched file, and the current watched file hash equals the staged proposal hash.

2. `rejected`
   - Operator reported `rejected`, WinMerge/Operator did not save the candidate, and the current watched file hash still equals the original baseline hash.

3. `dirty-unexpected`
   - Operator report and watched hash disagree.
   - current watched file hash matches neither staged proposal nor original baseline.
   - the Operator may have reported Accept without saving, reported Reject after saving, made extra edits, accepted only part of the diff, or another process may have changed the file.
   - this is not accepted in v1; require refresh/re-read before more AI edits on that file.

Tolerant partial-merge classification is intentionally out of scope. If a staged candidate is close but wrong, reject it and regenerate from the prior accepted source state.

Expected user behavior for v1:

```text
Accept all or reject all.
Do not edit in the middle of the diff review.
Reject and request a new proposal if the staged change is close but not right.
Use the diff to catch drastic rewrites, moved code, or boundary mistakes.
```

The response envelope should not include file content. Return hashes, classification, manifest results, and queue status only.

## Sidecar Test Expansion

Current:

```powershell
dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\LocalSmokeTests\LegacyToolSmokeTests\LegacyToolSmokeTests.csproj -- --stage-comment-diff

dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\LocalSmokeTests\LegacyToolSmokeTests\LegacyToolSmokeTests.csproj -- --record-decision <stagedRecordId> <accepted|rejected>

dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\LocalSmokeTests\LegacyToolSmokeTests\LegacyToolSmokeTests.csproj -- --fixture-decision-gate-smoke
```

Add:

```powershell
--stage-file-replacement <path> <proposalFile>
--stage-search-replace <path> <oldFile> <newFile>
--stage-v4a-patch <patchFile>
```

Codex can then dogfood the workflow:

1. Codex prepares a proposal file or patch under monitor-owned temp.
2. Codex runs the sidecar command.
3. Sidecar calls real MCP tools.
4. Operator reviews WinMerge.
5. Operator tells Codex the outcome.
6. Codex runs the sidecar `--record-decision` command.

## Implementation Order

Completed:

1. Freeze the current `submit_file(... launchDiff:false)` sidecar path.
2. Implement strict v1 `record_diff_decision`.
3. Add sidecar `--record-decision`.
4. Implement `get_source_map`.
5. Implement and fixture-smoke `submit_symbol`, `add_using`, `remove_using`, `add_symbol`, and `remove_symbol`.
6. Add separate Razor fixture smoke for current read/full-file-stage/accept behavior.
7. Add decision-gate fixture smoke for strict accepted/rejected/dirty-unexpected outcomes.
8. Add `get_symbol` structured selector support and fixture-smoke stable source-map key -> exact body read.
9. Add pre-enqueue no-op detection with `no-op-staged` status.
10. Add source-map structured method/property/attribute fields, parse status, diagnostics, and field/property signature cleanup.

Next:

1. Add encoding/EOL/BOM/final-newline metadata to staged records.
2. Harden structured selector contracts across remaining compatibility surfaces such as `add_symbol(containingType)`.
3. Implement `add_class` / `remove_class`.
4. Add generic sidecar `--stage-file-replacement`.
5. Implement `stage_search_replace`.
6. Implement `stage_v4a_patch`.
7. Add file lifecycle staging: add/delete/rename.
8. Add project/package staging.
9. Add Razor-aware outline and project-build validation.

## Design Rule For Claude-Like Models

Give the Model the largest safe workspace by offering both:

```text
Typed semantic tools for high-confidence C# edits.
Context-anchored patch tools for everything else.
```

But every lane must end at the same safety gate:

```text
staged file(s)
server-derived metadata
syntax/overlay validation
Host-owned diff review
explicit reported outcome
post-decision verification
```
