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
compose a complete Working candidate
stage the completed candidate for review
Host/sidecar opens the sanity diff
Operator either saves the full candidate in WinMerge or leaves source unchanged
record_diff_decision classifies by vote-plus-hash agreement
```

The V1 composition path is:

```text
get_source_map(path, scope: "file", mode: "selector")
submit_file / replace_text_in_file / replace_span_in_file / add_symbol / add_field / add_method
repeat candidate edits as needed
stage_candidate_for_review
launch_staged_diff or Host/sidecar WinMerge review/save
record_diff_decision(stagedRecordId, accepted|rejected)
```

`submit_file`, `replace_text_in_file`, `replace_span_in_file`, `add_symbol`, `add_field`, and `add_method` write to the normal monitor-owned `Working\<observedRootKey>\<relative source path>` mirror. They do not create staged records. The session id is metadata only; it is not part of the visible Working path.

Models are allowed and expected to read related files under the watched root when the change requires context. The preferred first read for C# is `get_source_map`, because it gives the real current structure without forcing a whole-file read.

Source maps and context anchors must not depend on decorative glyphs, emoji, or process comments. If source already contains such text, treat it as legacy content, not as an edit marker. Routine workflow notes belong in Monitor-owned records, not watched source.

Larger editing capabilities should remain documented in this manifest until implemented, then can be exposed as normal MCP tools or through client-side deferred tool loading where available.

## Canonical Agent Tool Sequences

These sequences are part of the tool contract. They are intentionally small so an MCP client can learn the loop before using broader edit tools.

### C# Preservation Edit

```text
find_file, unless the full path was returned by a Roslyn or Monitor tool in this session
get_source_map(path, scope: "file", mode: "selector")
get_symbol(path, symbolSelectorJson)
submit_file, replace_text_in_file, replace_span_in_file, or submit_symbol
launch_staged_diff or Host/sidecar WinMerge review/save
record_diff_decision(stagedRecordId, accepted|rejected)
```

Expected behavior:

- Use `get_source_map` before full `get_file` for C# edits.
- Use `get_symbol` for the smallest needed body.
- Use a structured selector or `stableSymbolKey` when available.
- For symbol staging or removal, prefer `stableSymbolKey` or structured `symbolSelectorJson`; name-only `symbolName` is a fallback of last resort for read compatibility, not mutation authority.
- Compose a complete Working candidate; do not directly mutate watched source.
- Call `stage_candidate_for_review` only after the candidate is complete enough for review.
- Use `launch_staged_diff` when the MCP client has no separate Host or sidecar available to open WinMerge.
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
blocked-dirty-unexpected record -> do not re-vote to accepted/rejected; recover explicitly.
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
| Claude staging guide | `get_staging_guide` | scaffolded |
| smoke-test catalog | `get_smoke_test_catalog` | debug-only |
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
| session staged edit visibility | `list_session_staged_records` | scaffolded |
| session file hash tracking | `check_file_hash`, `get_file.sessionId` | scaffolded |
| token-saving outline reads | `get_file_outline`, `get_symbol` | scaffolded |
| watched solution index rebuild | `refresh_solution_index` | implemented |
| watched solution index status | `get_solution_index_status` | implemented |
| watched solution index file refresh | `refresh_solution_index_file`, `refresh_file_and_index` | implemented |
| watched solution index queries | `get_solution_index`, `get_solution_index_tree`, `query_solution_index`, `find_indexed_symbols`, `get_indexed_symbol`, `find_indexed_references`, `find_indexed_callers`, `find_indexed_relationships` | implemented |
| Working mirror full-file candidate | `submit_file` | implemented |
| Working mirror verified text/span candidate | `replace_text_in_file`, `find_text_span`, `replace_span_in_file` | implemented |
| Working mirror member candidates | `add_symbol`, `add_field`, `add_method`, `add_property`, `add_constructor`, `add_nested_type` | implemented |
| Working candidate review snapshot | `stage_candidate_for_review` | implemented |
| Working mirror symbol replacement/removal | `submit_symbol`, `remove_symbol` | implemented |
| Working mirror type declaration modifier | `set_type_partial` | implemented |
| Working mirror using directive edits | `add_using`, `remove_using` | implemented |
| Roslyn class insertion/removal | `add_class`, `remove_class` | planned |
| staged candidate WinMerge launch | `launch_staged_diff` | scaffolded |
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

Temporary discovery helper.

### `get_workflow_status`

Returns the watched solution, watched project folder, monitor Working folder, and WinMerge resolution.

### `get_self_check`

Returns configured roots, monitor-owned Working and History paths, watched solution existence, diff tool resolution, and guardrail decisions.

### Session State Tools

MCP clients should not assume implicit per-connection state. The monitor server exposes explicit durable session handles that can outlive one client connection.

- `start_monitor_session(purpose?)`: creates a server-side session handle under `Working\Sessions`.
- `list_monitor_sessions()`: lists known session handles.
- `get_monitor_session(sessionId)`: reads one durable session.
- `list_session_staged_records(sessionId)`: lists staged edit records linked to one session, including queue status, staged paths, and validation status.
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

Copies a watched source file into the monitor-owned `Working` folder, records refresh state, and clears any existing candidate state for that file. Use this as the cold-session entry point before reading any large file in chunks from the returned `workingFilePath`. Do not use `get_file` for large files where returning the whole content could overflow the model/client tool-result budget.

Arguments:

- `sourceFilePath`: absolute path or path relative to the watched solution folder.

### `get_file`

Reads a watched source file through the Monitor MCP server.

Arguments:

- `sourceFilePath`: absolute path or path relative to the watched solution folder.
- `sessionId`: optional durable session handle. When supplied, the server records the fetched file hash in the session.

`get_file` returns the full file. Token-efficient partial reads should use outline/symbol tools rather than arbitrary character clipping.

For large files of any extension, prefer `refresh_file` and chunked reads from the returned Working path instead of `get_file`.

### `split_razor_code_to_companion`

Splits a Razor component that contains markup plus inline `@code` into a `.razor` markup file and a `.razor.cs` partial-class companion, staged as two normal monitor candidates under one session.

Supported input shapes:

- `Foo.razor`: existing markup file with inline `@code`; stages modified `Foo.razor` plus new `Foo.razor.cs`.
- `Foo.razor.cs`: legacy hybrid file containing Razor markup plus inline `@code`; stages new `Foo.razor` plus cleaned `Foo.razor.cs`.

Safety rules:

- Refuses normal `.razor` input when `Foo.razor.cs` already exists.
- Refuses legacy hybrid `.razor.cs` input when `Foo.razor` already exists.
- Refuses when either output already has a monitor Working candidate in progress.
- Does not write watched source directly. The returned staged records must still go through `launch_staged_diff` and `record_diff_decision` one file at a time.

Arguments:

- `sourceFilePath`: watched `.razor` or legacy hybrid `.razor.cs` path.
- `namespaceName`: optional namespace override for the generated companion.
- `leaveEmptyCodeBlock`: optional; when true leaves `@code { }` in the markup file.
- `sessionId`: optional durable monitor session id; one is created if omitted.
- `manifestJson`: optional intent/verification manifest.

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

### `list_session_staged_records`

Lists staged edit records linked to a durable monitor session.

Arguments:

- `sessionId`: durable session handle returned by `start_monitor_session`.

Returns compact record summaries:

- staged record id and queue status
- source and staged file paths
- operation and creation time
- original/staged hashes
- syntax and overlay validation status
- manifest JSON when the client supplied it

Use this when a client needs to confirm what is staged for the session before launching or recording diff decisions. It is read-only and does not change queue state.

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

### `refresh_solution_index`

Rebuilds the monitor-owned SQLite index for the watched solution folder. The first implementation indexes C# files, file hashes, syntax diagnostics, namespaces, declared symbols, signatures, line spans, and stable symbol keys. It is local-only and does not upload index data.

### `refresh_solution_index_file`

Refreshes one watched C# file in the monitor-owned SQLite index. Because reference/caller rows are solution-shaped, the current implementation rebuilds the semantic index and then returns the requested file slice.

### `refresh_file_and_index`

Refreshes the monitor-owned Working copy from the watched source file, clears stale candidate state for that file, then refreshes the same file in the SQLite solution index. This is the one-call path for "reload this file and its index slice."

### `get_solution_index_status`

Returns the local SQLite database path, watched solution path, observed root key, last indexed time, file count, symbol count, diagnostic count, reference count, call-site count, and stale file count.

### `get_solution_index`

Returns the indexed file and symbol JSON for the watched solution. Use `maxFiles` and `maxSymbols` to budget the payload. Symbol rows include `fileHash` and `symbolTextHash` so clients can verify freshness before using a cached stable key for edit targeting.

### `get_solution_index_tree`

Returns compact solution tree JSON: solution index status, namespaces, and files. Use this as the cheapest whole-project map before requesting symbol details.

### `query_solution_index`

Returns indexed files and symbols from the monitor-owned SQLite index without reparsing the watched solution. Supported scopes are `solution`, `namespace`, `folder`, and `file`. Folder scopes use descendant path matching; namespace scope accepts `(global)` as the UI display value for the empty/global namespace.

### `find_indexed_symbols`

Searches indexed declarations by symbol name text with optional kind and namespace filters.

### `get_indexed_symbol`

Returns one indexed declaration by stable symbol key.

### `find_indexed_references`

Returns persisted reference rows for one stable symbol key from the monitor-owned SQLite index. This is not the live Roslyn MCP `find_references` tool; references are computed during index rebuild with in-process Roslyn `CSharpCompilation`/`SemanticModel` and then served as SQL rows.

### `find_indexed_callers`

Returns persisted invocation call-site rows for one stable method or constructor key from the monitor-owned SQLite index. This is not the live Roslyn MCP caller tool; callers are computed during index rebuild and then served as SQL rows.

### `find_indexed_relationships`

Returns first-class relationship rows for one stable symbol key from the monitor-owned SQLite index. Relationship kinds include `partial_declaration`, `inherits_from`, `derived_type`, `overrides`, `overridden_by`, `implements_interface_member`, and `implemented_by`. Use `direction` as `outgoing`, `incoming`, or `both`, and optionally filter by exact relationship kind.

### `get_source_map`

Returns a read-only Roslyn-derived source map for a C# file, folder, namespace, or the watched project.

Arguments:

- `path`: optional watched source file/folder path, or namespace text when `scope=namespace`. Omit for project scope.
- `scope`: `auto`, `file`, `folder`, `namespace`, or `project`.
- `mode`: `auto`, `navigation`, `selector`, `detail`, or `full`.
- `namespaceName`: optional namespace text when `scope=namespace`. If omitted, `path` is treated as the namespace. Calls that provide `namespaceName` with any other scope are rejected.

Mode defaults:

- `scope=file` -> `mode=selector`
- `scope=folder` -> `mode=navigation`
- `scope=namespace` -> `mode=navigation`
- `scope=project` -> `mode=navigation`

Mode meanings:

- `navigation`: broad orientation. It returns current file/type/member shape, parse status, diagnostic counts, line spans, and lightweight type context. Use it to choose the next file/member without reading bodies.
- `selector`: target selection. It returns stable lexical symbol keys, normalized symbol text hashes, file hashes, compact contract signatures, parameter types/names, modifiers, flags, and syntax kinds. Use it to build `get_symbol`, `submit_symbol`, or `remove_symbol` selectors.
- `detail`: contract detail. It keeps selector identity plus usings, diagnostics when present, parameter names, and non-AI attribute argument summaries. Use it when interface/shape detail matters but full audit fidelity is unnecessary.
- `full`: audit/debug fidelity. It keeps absolute source paths, diagnostics summaries, usings, empty arrays, and the full source-map schema. Do not use it as broad model context by default.

The response includes `modePurpose`, `estimatedTokenProxy`, `budgetLimit`, `wasTruncated`, optional `suggestedNarrowing`, and ranked `suggestedNextCalls`. `suggestedNextCalls` makes the narrowing hierarchy explicit: navigation responses suggest file-level selector calls; selector responses suggest `get_symbol` calls by stable-key-only selector JSON and low-rank namespace-surface calls for file `using` directives. Treat them as ranked affordances, not mandatory commands; choose the next call that matches the user's intent.

`budgetLimit` is enforced by the Tool Server. If a shaped response would exceed budget, the Tool Server returns `wasTruncated: true`, omits source-map file payload details, and includes narrowing guidance so the client can retry with less detail.

Event declarations and event fields are surfaced as `event` symbols. Signatures are compact contract signatures, closer to a Visual Studio tree view than a source excerpt, so comments and generated process metadata do not become accidental source-map anchors. Property and field initializers are included because they are part of the local dependency/default-value shape. Durable file-header metadata such as `AIFileContext` and `FileVersion` remains visible in source-map output; legacy workflow-history attributes such as `AIChange`, `AIHistory`, `AIInstructions`, and `UserHistory` are omitted. All attributes remain untouched in source files and remain visible through `get_file` / `get_symbol` / `full` source text. It is a discovery tool; it does not stage or edit files.

This is a published Tier 1 tool, not a background artifact. Use it before C# edits to:

- see the current source shape without reading every body
- find the stable selector for `get_symbol`, `submit_symbol`, or `remove_symbol`
- inspect nearby/related files by folder or project scope
- inspect a referenced namespace surface with `scope=namespace`
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

Writes a complete replacement candidate into the monitor-owned Working mirror. It does not create a staged record and does not overwrite watched source. Use `stage_candidate_for_review` when the Working candidate is complete.

Arguments:

- `path`: watched source file path, absolute or relative to the watched solution folder.
- `content`: complete replacement file content.
- `sessionId`: optional durable session handle. The session id is metadata only and is not part of the Working path.
- `manifestJson`: optional Model-intent manifest. It is recorded but not trusted for verification.

Candidate path rule:

```text
Working\<observedRootKey>\<relative source path>
```

Baseline rule:

- The first candidate operation copies the watched source into the Working mirror and records source hash, length, and timestamp.
- Later candidate operations refuse with `candidate-baseline-stale` if the watched source changed after the candidate was initialized.
- For new-file candidates, the baseline is `<new-file>` and staging remains a review action; watched source is not directly overwritten by candidate composition.

### `replace_span_in_file`

Replaces an exact 1-based line/column span in the monitor-owned Working mirror candidate. Use it for small Razor, markup, CSS, JSON, or other text edits when a full-file `submit_file` would force unnecessary model output. It does not create a staged record and does not overwrite watched source. Use `stage_candidate_for_review` when the Working candidate is complete.

Arguments:

- `path`: watched source file path, absolute or relative to the watched solution folder.
- `startLine`, `startColumn`: 1-based start position.
- `endLine`, `endColumn`: 1-based exclusive end position.
- `newText`: replacement text for exactly that span.
- `expectedFileHash`: optional SHA-256 hash of the current edit base; use it to reject stale candidates.
- `expectedOldTextHash`: optional SHA-256 hash of the extracted old span text; use it to reject wrong spans.
- `expectedOldText`: optional exact old span text; use it when the old text is short enough to send cheaply.
- `sessionId`: optional durable session handle.
- `manifestJson`: optional Model-intent manifest.

Safety rule:

- Prefer `expectedFileHash` plus `expectedOldTextHash` or `expectedOldText`.
- If the hash or extracted old span does not match, the server rejects the edit before writing the candidate.
- Do not use this as a fuzzy search/replace; the span must come from the exact current file text.

### `find_text_span`

Finds exact text in the current edit base and returns 1-based line/column bounds suitable for `replace_span_in_file`.

Arguments:

- `path`: watched source file path, absolute or relative to the watched solution folder.
- `findText`: exact text to find using ordinal matching.
- `occurrenceIndex`: 0-based occurrence index when the text appears multiple times.
- `expectedFileHash`: optional SHA-256 hash of the current edit base.
- `sessionId`: optional durable session handle.

Use this as a dry run when line/column bounds are needed for review or diagnostics. For normal text replacement, prefer `replace_text_in_file`.

### `replace_text_in_file`

Replaces exact `oldText` in the monitor-owned Working mirror candidate. This is the preferred small-edit path for Razor, markup, CSS, JSON, config, and other text where emitting the full file would waste output tokens.

Arguments:

- `path`: watched source file path, absolute or relative to the watched solution folder.
- `oldText`: exact old text to replace using ordinal matching.
- `newText`: replacement text.
- `expectedMatches`: required number of matches in the current edit base. Defaults to `1`.
- `occurrenceIndex`: 0-based occurrence index to replace when `expectedMatches` is greater than `1`.
- `expectedFileHash`: optional SHA-256 hash of the current edit base.
- `expectedOldTextHash`: optional SHA-256 hash of `oldText`.
- `sessionId`: optional durable session handle.
- `manifestJson`: optional Model-intent manifest.

Safety rule:

- Prefer `expectedMatches: 1`.
- If the match count, file hash, or old-text hash does not match, the server returns a structured error result and does not write the candidate.
- Widen `oldText` until it is unique instead of relying on a fragile short token.

### `stage_candidate_for_review`

Snapshots the completed Working candidate into `Working\Staged`, writes one immutable `StagedEditRecord`, derives Roslyn metadata, runs syntax validation, and returns the staged record id for `launch_staged_diff` / `record_diff_decision`. Candidate state problems return structured statuses such as `no-active-candidate`, `candidate-working-missing`, and `candidate-baseline-stale` instead of an opaque tool failure.

Arguments:

- `path`: watched source file path, absolute or relative to the watched solution folder.
- `sessionId`: optional durable session handle.
- `manifestJson`: optional Model-intent manifest.

For new files, the staged record uses `<new-file>` as the original baseline. `launch_staged_diff` still creates a blank throwaway review baseline for WinMerge.

### `submit_symbol`

Writes replacement of one C# symbol selected by structured selector JSON into the Working mirror candidate. It does not create a staged record. Call `stage_candidate_for_review` when all edits to the file are complete.

Arguments:

- `path`: watched source file path, absolute or relative to the watched solution folder.
- `symbolSelectorJson`: JSON selector with fields such as `containingNamespace`, `containingType`, `memberKind`, `name`, `parameterTypes`, `arity`, and `stableSymbolKey`.
- `code`: complete replacement C# member declaration.
- `sessionId`: optional durable session handle.
- `manifestJson`: optional Model-intent manifest.

### `add_using` / `remove_using`

Adds or removes a using directive in the Working mirror candidate. These tools do not create staged records. Call `stage_candidate_for_review` when all edits to the file are complete.

Arguments:

- `path`: watched source file path, absolute or relative to the watched solution folder.
- `namespace`: namespace to add or remove.
- `sessionId`: optional durable session handle.
- `manifestJson`: optional Model-intent manifest.

### `set_type_partial`

Adds or removes the `partial` modifier on one C# type declaration in the Working mirror candidate. Use this before adding a companion partial file/member when the original type is not already partial. It does not create a staged record.

Arguments:

- `path`: watched source file path, absolute or relative to the watched solution folder.
- `containingType`: containing type name.
- `isPartial`: true to require `partial`; false to remove `partial`.
- `sessionId`: optional durable session handle.
- `manifestJson`: optional Model-intent manifest.

### `add_symbol` / `remove_symbol`

`add_symbol` adds one C# member to the Working mirror candidate. `remove_symbol` removes one selected C# symbol from the Working mirror candidate. Neither tool creates a staged record.

Arguments:

- `path`: watched source file path, absolute or relative to the watched solution folder.
- `containingType`: containing type name for `add_symbol`.
- `symbolType`: expected symbol kind for `add_symbol`.
- `code`: complete C# member declaration for `add_symbol`.
- `afterSymbol`: optional placement hint for `add_symbol`.
- `symbolSelectorJson`: structured selector JSON for `remove_symbol`.
- `sessionId`: optional durable session handle.
- `manifestJson`: optional Model-intent manifest.

### Typed member insertion tools

Prefer these narrow tools over generic `add_symbol` when the member kind is known:

- `add_field`
- `add_method`
- `add_property`
- `add_constructor`
- `add_nested_type`

These tools write to the Working mirror candidate and do not create staged records. Use `stage_candidate_for_review` after composing all same-file edits.

Common arguments:

- `path`: watched source file path, absolute or relative to the watched solution folder.
- `containingType`: containing type name.
- `declaration`: complete C# member/type declaration.
- `afterSymbol`: optional existing member name after which to insert the new member.
- `sessionId`: optional durable session handle.
- `manifestJson`: optional Model-intent manifest.

Use `add_nested_type` for nested classes, structs, interfaces, records, and enums. Use generic `add_symbol` only when the narrow tools do not fit.

### `record_diff_decision`

Classifies the completed WinMerge review for a staged edit and enforces the strict v1 all-or-none vote-plus-hash gate.

Arguments:

- `stagedRecordId`: staged edit record id returned by `stage_candidate_for_review`.
- `decision`: Operator-reported outcome, `accepted` or `rejected`.
- `note`: optional Operator note.
- `sessionId`: optional durable session handle. Defaults to the staged record session when present.

The `decision` argument is not the authority. It is the Operator's report of what they believe happened:

- `accepted`: WinMerge saved the full staged candidate into the watched file.
- `rejected`: WinMerge was not saved and the watched file should remain at the original baseline.

Decision behavior is vote-plus-hash agreement:

- reported `accepted` and watched hash equals staged proposal hash -> `accepted`
- reported `accepted` and watched normalized hash equals staged normalized hash -> `accepted-normalized`
- reported `accepted` and watched hash equals original baseline hash -> `dirty-unexpected`
- reported `accepted` and watched hash matches neither original nor staged -> `dirty-unexpected`
- reported `rejected` and watched hash equals original baseline hash -> `rejected`
- reported `rejected` and watched hash equals staged proposal hash -> `dirty-unexpected`
- reported `rejected` and watched hash matches neither original nor staged -> `dirty-unexpected`

The Operator report is not authority by itself. The hash is not enough by itself. Final classification is the agreement between the reported decision and the watched file hash. `accepted-normalized` is reserved for byte-shape-only drift such as BOM or line-ending changes from the diff tool; the decision response includes normalized hashes when that path is evaluated.

For accepted decisions, the tool also refreshes the monitor-owned solution index. A single accepted file refreshes immediately. A multi-file session returns `IndexRefresh.Status = deferred` while other staged records remain pending, then rebuilds the index once when the last pending staged record in that session is decided.

The response includes hashes, queue status, decision record path, index refresh status, and whether the reported outcome matched the computed classification. It returns no file content.

### `launch_staged_diff`

Launches WinMerge for an existing staged edit record and returns review paths plus launch details. This tool exists for MCP clients such as Claude Desktop that can call Monitor MCP tools but do not have a separate Host or sidecar process available to open the GUI diff.

Arguments:

- `stagedRecordId`: staged edit record id returned by `stage_candidate_for_review`.
- `forceReviewOnOverlayErrors`: optional boolean. Leave `false` unless the Operator explicitly asked to review a compile-failed staged candidate.

Behavior:

- reads the staged record
- verifies watched source and staged candidate files still exist
- if overlay compile validation has errors, asks the WinForms Host over the hub for an explicit `force_review` decision before launching WinMerge
- if the Host is unavailable, or the Operator cancels, returns a not-launched result so the agent can fix diagnostics first
- launches WinMerge against watched source and staged candidate
- returns `sourceFilePath`, `stagedFilePath`, `diffToolPath`, `processId`, launch arguments, validation gate status when applicable, and a next-step reminder

It does not classify, accept, reject, hash, or mutate source. After Operator review, call `record_diff_decision`.

Queue rule: any not-launched result from this tool stops the current review chain. For multi-file work, do not open the next file after `overlay-errors-review-cancelled`, `overlay-errors-host-unavailable`, `source-missing`, `staged-file-missing`, `winmerge-not-found`, or another blocked launch state. Return the diagnostics/status to the agent, stage a corrected candidate, then retry the queue from the blocked item.

Server-side queue block: when an overlay-error review is cancelled by the Operator or the Host is unavailable, the staged record queue status becomes `blocked-overlay-validation`. Later `launch_staged_diff` calls for other staged records in the same monitor session return `review-chain-blocked` until the blocked item is fixed or explicitly force-reviewed. Host-unavailable is recorded as `host_unavailable`, not as an Operator cancel.

Multi-file overlay expectation: for coupled C# changes, use one monitor session and pass the same `sessionId` to every staging call before the first review launch. Overlay validation reads the staged files in that session together, while WinMerge review remains serial.

Overlay validation is cached in-process by observed root plus candidate overlay file hashes. Re-staging the same candidate content can return the same validation status with `fromCache: true` instead of recompiling the same overlay.

### Run History Tools

- `list_monitor_runs(maxEntries?)`: reads monitor-owned run history entries from `Working\History\_runs.json`.
- `get_monitor_run(runId)`: returns entries for one recorded run id.

### Ledger Tools

- `list_ledgers(maxEntries?)`: lists monitor-owned per-file ledgers under `Working\History\Ledgers`.
- `get_ledger(sourceFilePath?, ledgerPath?)`: reads one ledger by watched source path or explicit ledger path under the ledger root.

### `prune_monitor_history`

Archives old monitor-owned history snapshots and prunes old ledgers. This does not touch watched source files.

### `shutdown_server`

Requests graceful shutdown of the current Monitor MCP server process.

Use this when a direct MCP server launch has become stale and is locking build outputs. The server also has an idle self-exit guard for forgotten direct launches; set `MONITORBASECLAUDE_IDLE_EXIT_MINUTES=0` or pass `--idle-exit-minutes 0` to disable that guard for a long-running diagnostic session.

Arguments:

- `reason`: optional operator/client reason recorded in the shutdown response.

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

### `get_staging_guide`

Returns the normal Claude review guidance from `Docs/Skills/SystemMonitorStaging.md` and `Docs/Skills/SessionOverlayValidation.md`.

Use this after `get_tool_manifest` when the client needs the practical edit loop, session-overlay staging rules, and multi-file review guidance. This is review-facing guidance. It does not include debug smoke coverage.

### `get_smoke_test_catalog`

Debug/maintainer tool. Returns `Docs/SmokeTestCatalog.md` so maintainers can discover runnable smoke modes, coverage areas, output artifacts, and remaining smoke gaps. This is not part of the normal Claude review or edit-planning path.
## Planned Work

Future MCP tools, hardening notes, diff workflow plans, and smoke-test command references live in Docs/McpServerPlannedWork.md.

The live manifest intentionally keeps planned/design material out of the current tool contract so MCP clients do not confuse roadmap notes with callable tools.
