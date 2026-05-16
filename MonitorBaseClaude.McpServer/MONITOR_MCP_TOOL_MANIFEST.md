# Monitor MCP Tool Manifest

This server is the MCP workflow API for the monitor source implementation at:

```text
C:\VSCodeProjects\ClaudeMonitor\Monitor
```

The WinForms operator UI lives at:

```text
C:\VSCodeProjects\MonitorBaseClaude
```

The new MCP server lives beside it:

```text
C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.McpServer
```

The current watched solution is:

```text
C:\Schema Studio - DBV2\Schema Studio.sln
```

## Tool Surface Rule

The command-line monitor behavior is represented here as typed MCP tools and typed tool arguments.

The WinForms UI should call this MCP server for workflow state, file operations, sessions, staged changes, and review actions.

Claude/Codex-style clients should treat this server as the workflow and safety surface, not as a raw filesystem editor. The small core should be easy for a Model to discover:

- status and tool manifest
- find/read/source-map/outline/symbol reads
- staged whole-file replacement
- explicit review outcome classification

The expected edit loop is:

```text
find related files
read source maps for the target file/folder
read only the needed symbols or full files
stage a complete candidate
Host/sidecar opens the sanity diff
Operator either saves the full candidate in WinMerge or leaves source unchanged
record_diff_decision classifies by vote-plus-hash agreement
```

Models are allowed and expected to read related files under the watched root when the change requires context. The preferred first read for C# is `get_source_map`, because it gives the real current structure without forcing a whole-file read.

Source maps and context anchors must not depend on decorative glyphs, emoji, or process comments. If source already contains such text, treat it as legacy content, not as an edit marker. Routine workflow notes belong in Monitor-owned records, not watched source.

Larger editing capabilities should remain documented in this manifest until implemented, then can be exposed as normal MCP tools or through client-side deferred tool loading where available.

## Canonical Agent Tool Sequences

These sequences are part of the tool contract. They are intentionally small so an MCP client can learn the loop before using broader edit tools.

### C# Preservation Edit

```text
find_file, if the target path is uncertain
get_source_map(path, scope: "file", mode: "selector")
get_symbol(path, symbolSelectorJson)
submit_file or submit_symbol
Host/sidecar WinMerge review/save
record_diff_decision(stagedRecordId, accepted|rejected)
```

Expected behavior:

- Use `get_source_map` before full `get_file` for C# edits.
- Use `get_symbol` for the smallest needed body.
- Use a structured selector or `stableSymbolKey` when available.
- For symbol staging or removal, prefer `stableSymbolKey` or structured `symbolSelectorJson`; name-only `symbolName` is a fallback of last resort for read compatibility, not mutation authority.
- Stage a complete candidate; do not directly mutate watched source.
- Treat `record_diff_decision` as vote-plus-hash agreement.
- Do not perform DRY cleanup or helper extraction as a side effect of a narrow change.

### Related-File Context

```text
find_file for likely interfaces/models/options/tests
get_source_map on related C# files or folders
get_symbol for only the bodies needed to understand the change
get_file only when the source-map/symbol lane is insufficient
```

Related files may be read when they materially affect correctness. The goal is not to forbid context; the goal is to make context narrow, current, and anchored to real source structure.

### Unsafe Requests

```text
Direct watched-source write -> refuse and use staging.
Manual WinMerge repair or partial hunk merge -> refuse and regenerate a smaller candidate.
Name-only mutation -> read source map and build a structured selector.
dirty-unexpected -> stop editing that file until Host/Operator refreshes or inspects state.
```

### No-Op Candidate

```text
stagedCandidateHash == originalBaselineHash
-> return no-change/no-op-staged
-> do not enqueue a normal diff by default
```

This prevents Accept and Reject from collapsing into the same raw hash state.

## Tool Map

| Source behavior | MCP tool target | Status |
| --- | --- | --- |
| `--help` | `get_tool_manifest` | scaffolded |
| `--self-check` | `get_self_check` | scaffolded |
| `WorkflowSettings:ObservedRoot` | `get_monitor_status.watchedSolutionPath` | scaffolded |
| source file path argument | `refresh_file.sourceFilePath` | scaffolded |
| `--refresh-only` | `refresh_file` | scaffolded |
| default refresh then compare | `refresh_and_compare_file` | planned |
| `--compare-only` | `compare_file` | scaffolded |
| `--ledger-summary` | `ledgerSummary` argument on compare tools | scaffolded |
| `--observed-root` | `watchedSolutionPath` / project context argument | planned |
| `--telemetry-window` | WinForms telemetry surface | planned |
| `_runs.json` | `list_monitor_runs`, `get_monitor_run` | scaffolded |
| `_telemetry.json` | `list_telemetry_runs`, `get_telemetry_run` | planned |
| Ledgers under `Working\History\Ledgers` | `list_ledgers`, `get_ledger` | scaffolded |
| model/context token usage | Host telemetry / future `record_model_usage` | planned |
| source file read | `get_file` | scaffolded |
| source file discovery | `find_file` | scaffolded |
| explicit durable state handle | `start_monitor_session`, `get_monitor_session`, `record_monitor_session_event`, `list_monitor_sessions` | scaffolded |
| session file hash tracking | `check_file_hash`, `get_file.sessionId` | scaffolded |
| token-saving outline reads | `get_file_outline`, `get_symbol` | scaffolded |
| staged whole-file replacement | `submit_file` | scaffolded |
| staged symbol replacement | `submit_symbol` | scaffolded |
| Roslyn symbol insertion | `add_symbol`, `add_using` | scaffolded |
| Roslyn symbol removal | `remove_symbol`, `remove_using` | scaffolded |
| Roslyn class insertion/removal | `add_class`, `remove_class` | planned |
| diff outcome classification | `record_diff_decision` | scaffolded |
| Roslyn source map | `get_source_map` | implemented |
| old executable command flags | future command-line bridge | breadcrumb only |

## Current Tools

### `get_monitor_status`

Returns path/config status for:

- WinForms UI root
- new MCP server root
- source implementation root
- watched solution
- watched project folder

### `list_watched_projects`

Temporary discovery helper. This may go away once watched solution configuration is final.

### `get_workflow_status`

Returns the watched solution, watched project folder, monitor Working folder, and WinMerge resolution.

### `get_self_check`

Returns configured roots, monitor-owned Working and History paths, watched solution existence, diff tool resolution, and guardrail decisions.

### Session State Tools

MCP clients should not assume implicit per-connection state. The monitor server exposes explicit durable session handles that can outlive one client connection.

- `start_monitor_session(purpose?)`: creates a server-side session handle under `Working\Sessions`.
- `list_monitor_sessions()`: lists known session handles.
- `get_monitor_session(sessionId)`: reads one durable session.
- `record_monitor_session_event(sessionId, eventType, summary, payloadJson?)`: appends an event.
- `check_file_hash(sessionId, sourceFilePath)`: checks whether a watched file has changed since it was last fetched in the session.

For local Ollama, WinForms will use these tools to simulate the host session loop:

1. Start or resume a session.
2. Record user message.
3. Ask Ollama for a strict action JSON.
4. Execute selected MCP tool.
5. Record tool call and tool result.
6. Ask Ollama for final answer.
7. Record final answer.

Storage note: durable sessions and staged edit records remain inspectable text/JSON files for now. SQLite may be added later as a query/index layer for telemetry, sessions, staged records, and run history after the file-based workflow is stable.

### Token And Context Telemetry

Token monitoring is a Host concern first, not a mutation gate.

Current low-risk rule:

- Record provider-reported usage when the Host has it.
- Estimate tool-result payload size for Monitor tools.
- Prefer `get_source_map`, `get_file_outline`, and `get_symbol` before full `get_file`.
- Do not block edits on token estimates.

Provider notes:

- Local Ollama `/api/chat` responses can include `prompt_eval_count` and `eval_count`; the Host can log these as prompt/response token counts.
- OpenAI API responses expose token usage in response metadata.
- Claude Code exposes context/cost/usage through its own UI commands such as `/context`, `/cost`, and `/usage`; Monitor should not depend on scraping those, but a Host integration may let the Operator record the displayed values.

Planned Monitor fields:

- `estimatedInputCharacters`
- `estimatedOutputCharacters`
- `estimatedInputTokens`
- `estimatedOutputTokens`
- `providerPromptTokens`
- `providerCompletionTokens`
- `providerContextTokens`
- `providerContextLimit`
- `contextBudgetWarning`

This is useful telemetry for choosing narrower tools; it is lower priority than the staged edit and decision gate.

### `refresh_file`

Copies a watched source file into the monitor-owned `Working` folder and records refresh state.

Arguments:

- `sourceFilePath`: absolute path or path relative to the watched solution folder.

### `get_file`

Reads a watched source file through the Monitor MCP server.

Arguments:

- `sourceFilePath`: absolute path or path relative to the watched solution folder.
- `sessionId`: optional durable session handle. When supplied, the server records the fetched file hash in the session.

`get_file` returns the full file. Token-efficient partial reads should use outline/symbol tools rather than arbitrary character clipping.

### `check_file_hash`

Checks a watched source file against the hash last recorded in a durable monitor session.

Arguments:

- `sessionId`: durable session handle returned by `start_monitor_session`.
- `sourceFilePath`: absolute path or path relative to the watched solution folder.

Returns:

- whether the file is known in the session
- whether it changed since the last session fetch
- current hash, length, and timestamp
- previously recorded session hash, length, timestamp, and fetch count

### `find_file`

Finds files under the watched project folder by filename or wildcard pattern.

Arguments:

- `fileNameOrPattern`: filename or wildcard pattern, such as `Program.cs` or `*.razor`.
- `maxResults`: maximum matches returned.

Use this to locate target files and related context files before staging. For example, a repository edit may require finding the interface, DTO, options/config file, and tests before the Model proposes a candidate.

### Source Marker And Glyph Rule

Do not add process markers to watched source files:

- no `AI edit here`
- no temporary monitor anchors
- no `... existing code ...`
- no emoji/glyph anchors

Source comments are allowed when they explain real code behavior. Domain metadata such as `AIFileContext` and `FileVersion` is allowed when it is part of the watched project's convention. All routine workflow state belongs in staged records, sessions, ledgers, history, or docs.

### `compare_file`

Creates a proposed snapshot from the monitor Working copy and returns paths for Host-launched WinMerge review against the watched source file.

Compatibility note: older monitor behavior launched WinMerge directly during compare. That is deprecated for the MCP Tool Server path. Host-like clients should launch GUI review tools using the returned paths.

Arguments:

- `sourceFilePath`: absolute path or path relative to the watched solution folder.
- `ledgerSummary`: optional compact summary appended to the monitor-owned ledger.
- `refreshIfMissing`: refreshes from source if the Working copy does not exist.

### `get_file_outline`

Returns a token-saving outline for a watched C# source file.

Arguments:

- `path`: watched source file path, absolute or relative to the watched solution folder.

### `get_source_map`

Returns a read-only Roslyn-derived source map for a C# file, folder, or the watched project.

Arguments:

- `path`: optional watched source file or folder path, absolute or relative to the watched solution folder. Omit for project scope.
- `scope`: `auto`, `file`, `folder`, or `project`.
- `mode`: `auto`, `navigation`, `selector`, or `full`.

Mode defaults:

- `scope=file` -> `mode=selector`
- `scope=folder` -> `mode=navigation`
- `scope=project` -> `mode=navigation`

Mode meanings:

- `navigation`: broad orientation. It returns current file/type/member shape, parse status, diagnostic counts, line spans, and lightweight type context. Use it to choose the next file/member without reading bodies.
- `selector`: target selection. It returns stable lexical symbol keys, normalized symbol text hashes, file hashes, structured parameters, attributes, flags, and syntax kinds. Use it to build `get_symbol`, `submit_symbol`, or `remove_symbol` selectors.
- `full`: audit/debug fidelity. It keeps absolute source paths, diagnostics summaries, usings, empty arrays, and the full source-map schema. Do not use it as broad model context by default.

The response includes `modePurpose`, `estimatedTokenProxy`, `budgetLimit`, `wasTruncated`, optional `suggestedNarrowing`, and ranked `suggestedNextCalls`. `suggestedNextCalls` makes the narrowing hierarchy explicit: navigation responses suggest file-level selector calls; selector responses suggest `get_symbol` calls by structured selector/stable key. Treat them as ranked affordances, not mandatory commands; choose the next call that matches the user's intent.

`budgetLimit` and `suggestedNarrowing` are advisory in the current implementation. If a response is over budget, the Tool Server identifies narrowing candidates, but it does not yet hard-truncate the result. When hard truncation is added, `wasTruncated` will become true and the response will include enough narrowing data to continue.

Event declarations and event fields are surfaced as `event` symbols. Signatures are stripped of leading trivia so comments and glyphs do not become accidental source-map anchors. It is a discovery tool; it does not stage or edit files.

This is a published Tier 1 tool, not a background artifact. Use it before C# edits to:

- see the current source shape without reading every body
- find the stable selector for `get_symbol`, `submit_symbol`, or `remove_symbol`
- inspect nearby/related files by folder or project scope
- detect whether a requested change is local, cross-file, or structural
- keep the Model anchored to the prior converged pattern before generating code

The source map is not an accept/reject verifier. The final gate remains `record_diff_decision` with vote-plus-hash agreement.

### `get_symbol`

Returns one C# symbol body from a watched source file.

Arguments:

- `path`: watched source file path, absolute or relative to the watched solution folder.
- `symbolName`: optional compatibility method/type/member name to locate. It must be unambiguous.
- `symbolSelectorJson`: optional structured selector JSON from `get_source_map`.

Selector behavior:

- Accept selectors from `get_source_map`, especially `stableSymbolKey`, `memberKind`, `containingNamespace`, `containingType`, `parameterTypes`, and `arity`.
- Reject ambiguous matches instead of falling back to name-only behavior.
- Keep `symbolName` only as a compatibility shortcut for unambiguous files.

### `submit_file`

Stages a complete replacement file under monitor-owned `Working\Staged`, writes a timestamped `StagedEditRecord`, derives Roslyn metadata, runs syntax validation, runs overlay compile validation, and returns staged/source paths.

Arguments:

- `path`: watched source file path, absolute or relative to the watched solution folder.
- `content`: complete replacement file content.
- `sessionId`: optional durable session handle.
- `manifestJson`: optional Model-intent manifest. It is recorded but not trusted for verification.
- `launchDiff`: deprecated compatibility flag. Prefer `false`; GUI diff launch belongs to the Host or sidecar runner.

### `submit_symbol`

Stages replacement of one C# symbol selected by structured selector JSON. The tool generates a full staged candidate file and never overwrites watched source directly.

Arguments:

- `path`: watched source file path, absolute or relative to the watched solution folder.
- `symbolSelectorJson`: JSON selector with fields such as `containingNamespace`, `containingType`, `memberKind`, `name`, `parameterTypes`, `arity`, and `stableSymbolKey`.
- `code`: complete replacement C# member declaration.
- `sessionId`: optional durable session handle.
- `manifestJson`: optional Model-intent manifest.

### `add_using` / `remove_using`

Stages adding or removing a using directive in a C# source file.

Arguments:

- `path`: watched source file path, absolute or relative to the watched solution folder.
- `namespace`: namespace to add or remove.
- `sessionId`: optional durable session handle.
- `manifestJson`: optional Model-intent manifest.

### `add_symbol` / `remove_symbol`

Stages adding or removing one C# member. `add_symbol` takes a containing type and complete member declaration. `remove_symbol` takes structured selector JSON.

Arguments:

- `path`: watched source file path, absolute or relative to the watched solution folder.
- `containingType`: containing type name for `add_symbol`.
- `symbolType`: expected symbol kind for `add_symbol`.
- `code`: complete C# member declaration for `add_symbol`.
- `afterSymbol`: optional placement hint for `add_symbol`.
- `symbolSelectorJson`: structured selector JSON for `remove_symbol`.
- `sessionId`: optional durable session handle.
- `manifestJson`: optional Model-intent manifest.

### `record_diff_decision`

Classifies the completed WinMerge review for a staged edit and enforces the strict v1 all-or-none vote-plus-hash gate.

Arguments:

- `stagedRecordId`: staged edit record id returned by `submit_file`.
- `decision`: Operator-reported outcome, `accepted` or `rejected`.
- `note`: optional Operator note.
- `sessionId`: optional durable session handle. Defaults to the staged record session when present.

The `decision` argument is not the authority. It is the Operator's report of what they believe happened:

- `accepted`: WinMerge saved the full staged candidate into the watched file.
- `rejected`: WinMerge was not saved and the watched file should remain at the original baseline.

Decision behavior is vote-plus-hash agreement:

- reported `accepted` and watched hash equals staged proposal hash -> `accepted`
- reported `accepted` and watched hash equals original baseline hash -> `dirty-unexpected`
- reported `accepted` and watched hash matches neither original nor staged -> `dirty-unexpected`
- reported `rejected` and watched hash equals original baseline hash -> `rejected`
- reported `rejected` and watched hash equals staged proposal hash -> `dirty-unexpected`
- reported `rejected` and watched hash matches neither original nor staged -> `dirty-unexpected`

The Operator report is not authority by itself. The hash is not enough by itself. Final classification is the agreement between the reported decision and the watched file hash.

The response includes hashes, queue status, decision record path, and whether the reported outcome matched the computed classification. It returns no file content.

### Run History Tools

- `list_monitor_runs(maxEntries?)`: reads monitor-owned run history entries from `Working\History\_runs.json`.
- `get_monitor_run(runId)`: returns entries for one recorded run id.

### Ledger Tools

- `list_ledgers(maxEntries?)`: lists monitor-owned per-file ledgers under `Working\History\Ledgers`.
- `get_ledger(sourceFilePath?, ledgerPath?)`: reads one ledger by watched source path or explicit ledger path under the ledger root.

### `prune_monitor_history`

Archives old monitor-owned history snapshots and prunes old ledgers. This does not touch watched source files.

### Command-Line Breadcrumb

The old monitor executable accepted flags such as `--refresh-only`, `--compare-only`, `--no-prune`, and `--ledger-summary`.

Those flags are not exposed as the primary MCP API. MCP clients should call typed tools directly:

- `--refresh-only` -> `refresh_file`
- `--compare-only` -> `compare_file`
- `--ledger-summary` -> `compare_file.ledgerSummary`
- `--no-prune` -> do not call `prune_monitor_history`

A future command-line bridge can translate legacy flags into these typed tool calls if needed.

### `get_tool_manifest`

Returns this manifest so MCP clients can understand the current and planned tool surface.

## Planned First Server Hardening

Harden the status/config path first because it is low risk and validates configuration.

Target tool:

```text
get_monitor_status
```

Next hardening pass:

```text
refresh_and_compare_file(sourceFilePath, ledgerSummary?)
Roslyn validation before compare
ordered multi-file compare queue
```

## Planned Roslyn Editing Tools

These tools are token-saving and safety tools for later phases. They should stage changes into monitor-owned temp/Working paths and return review paths before any watched source file is touched.

### Read/inspect tools

- `get_file_outline(path)`: return signatures, symbol names, attributes, and line spans without method bodies.
- `get_source_map(path?, scope?, mode?)`: return a read-only Roslyn source map for a C# file, folder, or project. `navigation` is broad orientation, `selector` is stable target selection, and `full` is audit/debug.
- `get_symbol(path, symbolName | symbolSelectorJson)`: return one symbol body on demand. Structured selector support lets source-map stable keys fetch exact bodies.

### Replace tools

- `submit_symbol(path, symbolSelector, code)`: replace an existing method/property/field/class body or declaration. This is a replace operation, not add/remove.
- `submit_file(path, content, sessionId?, manifestJson?)`: full file replacement staged to temp, with a timestamped `StagedEditRecord`, then reviewed by an explicit Host-owned diff workflow.

### Add tools

- `add_symbol(path, symbolType, code, afterSymbol?)`: insert a new method/property/field/class. Roslyn chooses the correct insertion point; `afterSymbol` is an optional ordering hint such as `BuildTrimmedCteSql`.
- `add_using(path, namespace)`: add a using directive if it is not already present.
- `add_class(path, code)`: add a new class to an existing file.

### Remove tools

- `remove_symbol(path, symbolSelector)`: delete a symbol cleanly, including attributes and XML/doc comments immediately attached to it. Leave no orphaned whitespace or comments.
- `remove_using(path, namespace)`: remove a using directive cleanly.
- `remove_class(path, className)`: remove a class and all of its members.

### Safety rules

- All add/replace/remove operations go through temp staging and diff before touching real files.
- Roslyn validates syntactic completeness before diff launch.
- The Tool Server must reject half-open syntax, unmatched braces, and incomplete symbol submissions.
- Model clients should prefer outline/symbol tools before requesting full file content when possible.

### Diff workflow tools

- `compare_file(path, sessionId)`: returns one staged/source pair for Host-launched diff review and moves workflow state to `awaiting-operator-decision`.
- `record_diff_decision(stagedRecordId, decision, note?, sessionId?)`: records the reported review outcome, classifies by strict vote-plus-hash gate, updates session hash when a session is present, and returns a small envelope only.

`record_diff_decision` arguments:

- `stagedRecordId`: staged edit record id returned by `submit_file`.
- `decision`: `accepted` or `rejected`.
- `note`: optional Operator note.
- `sessionId`: optional durable session handle.

`record_diff_decision` response:

- `sessionId`
- `filePath`
- `classification`
- `newHash`
- `manifestResults`
- `queueStatus`

No file content or symbol text should be returned.

## Planned Diff Pause Workflow

Diff launch should be an explicit MCP tool call, not an implicit side effect of every staged edit.

The monitor workflow needs an active diff state so multi-file work cannot race ahead while an Operator review window is open.

Planned states:

- `idle`
- `staged`
- `diff-open`
- `awaiting-operator-decision`
- `accepted`
- `rejected`

Rules:

- Only one diff may be active at a time.
- Multi-file changes become an ordered compare queue.
- The Tool Server returns staged/source paths and records the active file/session state.
- The Host launches the first GUI diff and logs launch details in telemetry.
- The Host/Model must pause until the Operator finishes review and reports the outcome.
- The Operator reports `accepted` only after saving the full candidate in WinMerge, or `rejected` when leaving source unchanged.
- The expected v1 Operator pattern is accept all or reject all. The Operator should not hand-edit in the middle of WinMerge review.
- The diff is a final sanity check that the staged proposal respected the current source file. If the diff shows drastic rewrites, moved code, or boundary damage, reject and regenerate.
- `record_diff_decision(stagedRecordId, decision, note?, sessionId?)` records the reported outcome, classifies the watched file by vote-plus-hash agreement, and releases the next queued diff only when clean.
- After `record_diff_decision`, the Tool Server returns the new hash in the response envelope. The Model does not need to call `check_file_hash` manually after a decision.
- Closing the diff window is not enough to prove a decision happened.
- On Accept, WinMerge/Operator save is the mutation path. The Tool Server verifies the watched hash equals the staged candidate hash.
- On Reject, the Tool Server verifies the watched file is still at the original baseline hash.
- Verification should classify the outcome as `accepted`, `rejected`, or `dirty-unexpected`.
- Verification follows the strict v1 priority order documented below.

Known behavior: WinMerge launched directly from the stdio Tool Server is unreliable for operator review. The stable split is Tool Server stages and validates, Host launches the GUI diff, Operator saves or does not save in WinMerge, Tool Server verifies.

`record_diff_decision` is an explicit Host action after the Operator reports the outcome. It is not triggered by WinMerge close detection.

### Sidecar Operator Test Runner

Repeatable operator workflow tests may live in:

```text
C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.ToolSmokeTests
```

Current staged-edit smoke command:

```powershell
dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --stage-comment-diff
```

The sidecar runner calls real MCP tools, receives staged/source paths, and launches WinMerge directly. This is allowed because it behaves as a Host. The Monitor Tool Server still does not own GUI lifetime.

For sidecar tests, `record_diff_decision` is triggered only after the Operator reports what happened after sanity review. The sidecar may prompt for `accepted` or `rejected`, but vote-plus-hash agreement is authoritative. WinMerge close detection is telemetry only.

Sequencing note: keep staged edit plus overlay compile validation stable first, then add the sidecar decision prompt, then build the post-decision verification pipeline.

## Confirmed Staged Edit Verification Architecture

The Model-submitted manifest is the request contract only. The Monitor Tool Server does not trust it as verification authority.

For every staged edit, the Monitor Tool Server creates a `StagedEditRecord` containing:

- `sessionId`
- `filePath`
- `operation`
- `originalHash`
- `stagedHash`
- `manifest`
- `serverDerivedMetadata`
- `queueStatus`

`serverDerivedMetadata` is produced by Roslyn and includes:

- actual symbols added
- actual symbols removed
- actual usings added
- actual usings removed
- original/staged symbol spans
- symbol text hashes
- staged file path

Strict v1 verification priority:

1. Reported Accept plus watched hash matching staged proposal -> `accepted`
2. Reported Reject plus watched hash matching original baseline -> `rejected`
3. Any mismatch between reported outcome and watched hash -> `dirty-unexpected`
4. Any watched hash outside original/staged all-or-none paths -> `dirty-unexpected`

No-op staged candidate rule:

- If `stagedHash == originalHash`, the staged candidate is a no-op.
- Return a `no-change` / `no-op-staged` status and do not enqueue a normal diff by default.
- This prevents Accept and Reject from becoming indistinguishable by raw file hash.

The expected v1 Operator pattern is all-or-nothing accept/reject. Tolerant symbol-level partial acceptance is not part of the workflow.

Conformance is not immutability. Structural refactors are allowed when they are explicit, bounded staged candidates. Accept applies the whole structural candidate and makes it the next converged pattern; Reject applies none of it.

Duplication is not automatically debt. Unnecessary abstraction is also debt. Preserve small repeated inline code when it keeps workflow state, source-map mode behavior, vote-plus-hash logic, or operator behavior easier to audit. Extract helpers only when the abstraction is explicitly requested, represents a real named concept, is reused across meaningful call sites, or reduces meaningful risk.

After `record_diff_decision`, the Tool Server silently:

- re-hashes the watched file
- verifies accepted staged candidates only after WinMerge/Operator has saved the real watched source
- verifies by vote-plus-hash agreement
- updates the session file hash to actual watched file state
- records decision, classification, and timestamp
- releases the next queued diff if available
- returns a small envelope only

The envelope should include session/file/classification/hash/manifest results/queue status. It should not include file content or symbol text.

## Edit Format References

The Monitor workflow treats text patches as useful but not authoritative. Context-anchored patches can drift when a file changes between read and write, so the planned edit path uses Roslyn symbol staging, overlay validation, Operator diff review, and post-decision verification.

Fixture accept-path testing is handled by the sidecar runner:

```powershell
dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --fixture-accept-smoke
```

The sidecar writes a fixture-specific config file and starts the Tool Server with that config. This keeps destructive accept-path testing away from the real watched DBV2 project.

Decision-gate testing is handled by:

```powershell
dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --fixture-decision-gate-smoke
```

This validates clean Accept, clean Reject, reported Accept without saved candidate, reported Reject after candidate landed, and unrelated dirty source edits.

Source-map artifact testing is handled by:

```powershell
dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --source-map-smoke "Data\BaseTableRepository.cs" --scope file --mode selector

dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --source-map-smoke Data --scope folder --mode navigation

dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --source-map-corpus-smoke
```

The focused commands call the real `get_source_map` tool against the configured watched solution and write `source-map-raw.json` plus `source-map-summary.md` under `Working\History\ToolSmokeTests\<timestamp>`. Use selector mode to review stable symbol target selection and navigation mode to review broad target/related-file discovery before reading bodies.

The corpus command walks configured DBV2 C# files and writes per-file full-mode maps plus `source-map-compact-index.json`, `source-map-navigation-index.json`, `source-map-corpus-analysis.json`, and `source-map-corpus-summary.md` under `Working\History\ToolSmokeTests\<timestamp>\source-map-corpus`. Use the raw/full maps for schema/debug review, the selector index for stable selector handoff, and the navigation index for broad orientation without dumping every file body.

Roslyn surgery testing is handled by:

```powershell
dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --fixture-roslyn-surgery-smoke
```

This stages and accepts `add_using`, `submit_symbol`, `add_symbol`, `remove_symbol`, and `remove_using` against the disposable DBV2-shaped fixture.

Razor current-lane testing is handled by:

```powershell
dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --fixture-razor-smoke
```

This uses a separate Razor-shaped fixture to verify `find_file`, `get_file`, empty C# outline behavior, full-file `.razor` staging, explicit `razor-validation-pending` overlay status, and strict vote-plus-hash Accept. Razor-aware syntax/build validation is still planned.

Official / OpenAI references:

- GPT-4.1 prompting guide: https://developers.openai.com/cookbook/examples/gpt4-1_prompting_guide
- OpenAI `apply_patch` tool guide: https://developers.openai.com/api/docs/guides/tools-apply-patch
- Agents JS `applyDiff` SDK reference: https://openai.github.io/openai-agents-js/openai/agents-core/functions/applydiff/

Third-party / commentary references:

- V4A diff format and context anchoring: https://codex.danielvaughan.com/2026/03/31/codex-cli-apply-patch-v4a-diff-format/
- Codex issue discussing shell-oriented defaults such as `rg`, `sed`, and `cat`: https://github.com/openai/codex/issues/14113
- AI patch drift discussion: https://www.morphllm.com/ai-apply-patch
- File-editing approach comparison: https://fabianhertwig.com/blog/coding-assistants-file-edits/
