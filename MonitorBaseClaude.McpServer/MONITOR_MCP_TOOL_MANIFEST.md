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

## Tool Map

| Source behavior | MCP tool target | Status |
| --- | --- | --- |
| `--help` | `get_tool_manifest` | scaffolded |
| `--self-check` | `get_monitor_status` | scaffolded |
| `WorkflowSettings:ObservedRoot` | `get_monitor_status.watchedSolutionPath` | scaffolded |
| source file path argument | `refresh_file.sourceFilePath` | scaffolded |
| `--refresh-only` | `refresh_file` | scaffolded |
| default refresh then compare | `refresh_and_compare_file` | planned |
| `--compare-only` | `compare_file` | scaffolded |
| `--ledger-summary` | `ledgerSummary` argument on compare tools | scaffolded |
| `--observed-root` | `watchedSolutionPath` / project context argument | planned |
| `--telemetry-window` | WinForms telemetry surface | planned |
| `_runs.json`, `_telemetry.json` | `list_monitor_runs`, `get_monitor_run` | planned |
| Ledgers under `Working\History\Ledgers` | `list_ledgers`, `get_ledger` | planned |
| source file read | `get_file` | scaffolded |
| source file discovery | `find_file` | scaffolded |
| explicit durable state handle | `start_monitor_session`, `get_monitor_session`, `record_monitor_session_event`, `list_monitor_sessions` | scaffolded |
| session file hash tracking | `check_file_hash`, `get_file.sessionId` | scaffolded |
| token-saving outline reads | `get_file_outline`, `get_symbol` | planned |
| staged whole-file replacement | `submit_file` | planned |
| staged symbol replacement | `submit_symbol` | planned |
| Roslyn symbol insertion | `add_symbol`, `add_class`, `add_using` | planned |
| Roslyn symbol removal | `remove_symbol`, `remove_class`, `remove_using` | planned |
| operator diff decision | `record_diff_decision` | planned |

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

### `compare_file`

Creates a proposed snapshot from the monitor Working copy and launches WinMerge against the watched source file.

Arguments:

- `sourceFilePath`: absolute path or path relative to the watched solution folder.
- `ledgerSummary`: optional compact summary appended to the monitor-owned ledger.
- `refreshIfMissing`: refreshes from source if the Working copy does not exist.

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

These tools are token-saving and safety tools for later phases. They should stage changes into monitor-owned temp/Working paths and launch diff review before any watched source file is touched.

### Read/inspect tools

- `get_file_outline(path)`: return signatures, symbol names, attributes, and line spans without method bodies.
- `get_symbol(path, symbolName)`: return one symbol body on demand.

### Replace tools

- `submit_symbol(path, symbolName, code)`: replace an existing method/property/field/class body or declaration. This is a replace operation, not add/remove.
- `submit_file(path, content, sessionId?, manifestJson?)`: full file replacement staged to temp, with a timestamped `StagedEditRecord`, then diff launched by explicit review workflow.

### Add tools

- `add_symbol(path, symbolType, code, afterSymbol?)`: insert a new method/property/field/class. Roslyn chooses the correct insertion point; `afterSymbol` is an optional ordering hint such as `BuildTrimmedCteSql`.
- `add_using(path, namespace)`: add a using directive if it is not already present.
- `add_class(path, code)`: add a new class to an existing file.

### Remove tools

- `remove_symbol(path, symbolName)`: delete a symbol cleanly, including attributes and XML/doc comments immediately attached to it. Leave no orphaned whitespace or comments.
- `remove_using(path, namespace)`: remove a using directive cleanly.
- `remove_class(path, className)`: remove a class and all of its members.

### Safety rules

- All add/replace/remove operations go through temp staging and diff before touching real files.
- Roslyn validates syntactic completeness before diff launch.
- The Tool Server must reject half-open syntax, unmatched braces, and incomplete symbol submissions.
- Model clients should prefer outline/symbol tools before requesting full file content when possible.

### Diff workflow tools

- `compare_file(path, sessionId)`: launches the configured diff tool for one staged edit and moves workflow state to `awaiting-operator-decision`.
- `record_diff_decision(sessionId, filePath, decision)`: records the Operator decision, triggers silent Roslyn verification, updates session hash, releases the next queued diff if available, and returns a small envelope only.

`record_diff_decision` arguments:

- `sessionId`: durable session handle.
- `filePath`: watched file path, absolute or relative to watched solution folder.
- `decision`: `accepted`, `rejected`, or `partially-merged`.

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
- The Tool Server launches the first diff and records the active file/session state.
- The Host/Model must pause until the Operator records a decision.
- A planned tool such as `record_diff_decision(sessionId, filePath, decision)` releases the next queued diff.
- After `record_diff_decision`, the Tool Server returns the new hash in the response envelope. The Model does not need to call `check_file_hash` manually after a decision.
- Closing the diff window is not enough to prove the merge happened.
- The Tool Server must verify the watched file after review by comparing the current watched file hash/content against the staged proposal.
- Verification should classify the outcome as `accepted`, `rejected`, `partially-merged`, or `unknown`.
- Verification follows the confirmed priority order documented below.

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

Verification priority:

1. Full file hash matches staged proposal -> `accepted`
2. Full file hash matches original baseline -> `rejected`
3. Target symbols and usings confirmed by Roslyn but full hash differs -> `symbol-accepted-with-other-edits`
4. Some manifest items confirmed by Roslyn -> `partially-merged`
5. No manifest items confirmed -> `rejected`
6. Roslyn cannot parse watched file -> `unknown`

`symbol-accepted-with-other-edits` is successful. The Operator may adjust whitespace, formatting, small fixes, or nearby code during merge.

After `record_diff_decision`, the Tool Server silently:

- re-hashes the watched file
- verifies against Roslyn-derived staged metadata
- updates the session file hash to actual watched file state
- records decision, classification, and timestamp
- releases the next queued diff if available
- returns a small envelope only

The envelope should include session/file/classification/hash/manifest results/queue status. It should not include file content or symbol text.

## Edit Format References

The Monitor workflow treats text patches as useful but not authoritative. Context-anchored patches can drift when a file changes between read and write, so the planned edit path uses Roslyn symbol staging, overlay validation, Operator diff review, and post-decision verification.

Official / OpenAI references:

- GPT-4.1 prompting guide: https://developers.openai.com/cookbook/examples/gpt4-1_prompting_guide
- OpenAI `apply_patch` tool guide: https://developers.openai.com/api/docs/guides/tools-apply-patch
- Agents JS `applyDiff` SDK reference: https://openai.github.io/openai-agents-js/openai/agents-core/functions/applydiff/

Third-party / commentary references:

- V4A diff format and context anchoring: https://codex.danielvaughan.com/2026/03/31/codex-cli-apply-patch-v4a-diff-format/
- Codex issue discussing shell-oriented defaults such as `rg`, `sed`, and `cat`: https://github.com/openai/codex/issues/14113
- AI patch drift discussion: https://www.morphllm.com/ai-apply-patch
- File-editing approach comparison: https://fabianhertwig.com/blog/coding-assistants-file-edits/
