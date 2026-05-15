# Proposed MCP Workflow

This project is building a local WinForms Host and a Monitor MCP Tool Server.

## Terms

- **Host**: the client application that discovers MCP Tool Servers, asks a Model what to do, executes MCP tool calls, and logs the trace. Today this is the WinForms app. Later this may be Claude Code or Claude Desktop.
- **Tool Server**: an MCP process exposing tools. Examples: Monitor MCP Tool Server and Roslyn CodeLens MCP Tool Server.
- **Model**: Claude, Ollama, or another language model that interprets intent and chooses tool calls. The Model may itself be backed by a remote or local model server, but in this document it is not called "Server."
- **Operator**: the human reviewing diffs and making accept/reject decisions.

This vocabulary is intentional. "Server" alone is ambiguous because an LLM may also be hosted by a server. Use **Tool Server** for MCP tool providers and **Model** for Claude/Ollama.

## Current Tool Servers

### Monitor MCP Tool Server

Use for workflow and file operations:

- watched project files
- durable monitor sessions
- file hashes
- full file reads
- file outline reads
- single-symbol reads
- staged file submissions
- refresh / compare / WinMerge review
- monitor-owned Working and History folders

This Tool Server owns the safe edit workflow. It should never overwrite watched source files directly during AI edits. It stages output under monitor-owned folders and launches diff/review.

### Roslyn CodeLens MCP Tool Server

Use for code intelligence:

- project and solution information
- NuGet dependencies
- project dependencies
- diagnostics
- symbol lookup
- references
- callers/callees
- type hierarchy
- public API surface
- code analysis

This Tool Server answers questions about the codebase. It is not the monitor workflow owner.

The two Tool Servers should stay separate:

- Monitor owns workflow, safety, staging, diff pause, and session state.
- Roslyn CodeLens owns code intelligence.
- Monitor may use Roslyn internally for validation and symbol-safe staging, but that does not merge the two Tool Server responsibilities.

## Three Test Lanes

### 1. Scripted Tool Server Tests

Purpose: prove Tool Server behavior is correct without trusting any Model.

The smoke test supplies exact tool calls and expected behavior.

Example:

```json
{
  "server": "monitor-base-claude",
  "tool": "submit_file",
  "arguments": {
    "path": "Program.cs",
    "content": "complete expected file content",
    "launchDiff": false
  }
}
```

Expected behavior:

1. Tool Server resolves the watched file.
2. Tool Server stages the submitted content under `Working\\Staged`.
3. Tool Server validates syntax for C# files.
4. Tool Server returns staged path, validation result, and optional diff information.
5. A separate MCP call opens the diff tool for review.
6. Test compares the staged output to expected output.
7. No watched source file is overwritten.

This lane proves the Tool Server is safe and deterministic.

### 2. Ollama Host Simulation

Purpose: prove the Host can simulate an MCP-capable AI client.

Ollama is the local "baby-Claude" Model. It is useful for testing tool-routing prompts, session state, logging, and basic decision loops.

Flow:

1. Operator enters a request.
2. Host discovers local MCP Tool Servers from `.mcp.json`.
3. Host gives the Model a compact routing map.
4. Model returns JSON:

```json
{
  "action": "call_tool",
  "server": "monitor-base-claude",
  "tool": "find_file",
  "arguments": {
    "fileNameOrPattern": "Program.cs",
    "maxResults": 10
  }
}
```

5. Host executes the tool against the chosen Tool Server.
6. Host sends the result back to the Model.
7. Model writes a final answer.
8. Host logs the full trace under `Working\\History`.

Ollama is not expected to be a reliable coding brain. It is good enough to test the Host loop and simple Tool Server selection.

### 3. Claude As The Real Coding Brain

Purpose: let a stronger MCP-capable Model drive the same Tool Servers.

Claude Code or Claude Desktop should eventually replace the Ollama Model simulation.

Flow:

1. Claude discovers MCP Tool Servers from workspace config.
2. Claude reads tool names, descriptions, and schemas.
3. Claude chooses between Monitor MCP Tool Server and Roslyn CodeLens MCP Tool Server.
4. Claude calls tools.
5. Monitor MCP Tool Server stores durable workflow/session state.
6. Claude uses compact session handles instead of carrying large histories in every prompt.
7. Edits are submitted through staged tools and reviewed with diff before source files are touched.

The Tool Server architecture should not change when swapping Ollama for Claude.

## Session State

The Monitor MCP Tool Server should own durable workflow state.

Example tools:

- `start_monitor_session`
- `list_monitor_sessions`
- `get_monitor_session`
- `record_monitor_session_event`
- `check_file_hash`

Pattern:

1. Model/Host starts a session.
2. Tool Server returns a small `sessionId`.
3. Model/Host passes `sessionId` in future calls.
4. Tool Server stores bulky history on disk.
5. Tool Server returns compact summaries and file-change status.

This is token-saving because the Model carries a short handle instead of a full workflow log.

The Tool Server should track fetched files and hashes. If a file has not changed since the session last fetched it, the Tool Server can report that instead of resending content.

### Storage Note

Durable session state and staged edit records should remain inspectable text/JSON files for now. SQLite is a possible later expansion as an index for querying many sessions, telemetry rows, staged records, and run history, but it should not become the first source of truth until the file-based workflow is stable.

## Editing Rule

The current file is a voting member in generation.

Rules:

- Do not rewrite from a different starting point.
- Preserve working code unless explicitly told otherwise.
- Make minimal targeted changes.
- Use outline and symbol tools before requesting full files when possible.
- Stage changes under monitor-owned folders.
- Validate syntax before launching diff.
- Never overwrite watched source files directly from an AI tool call.

## Planned Safe Editing Tools

Read/token-saving tools:

- `get_file_outline(path)`
- `get_symbol(path, symbolName)`
- `check_file_hash(path, sessionId)`

Staged replacement tools:

- `submit_file(path, content, manifest)`
- `submit_symbol(path, symbolName, code, manifest)`

Roslyn symbol operations:

- `add_symbol(path, symbolType, code, afterSymbol?, manifest)`
- `remove_symbol(path, symbolName, manifest)`
- `add_using(path, namespace, manifest)`
- `remove_using(path, namespace, manifest)`
- `add_class(path, code, manifest)`
- `remove_class(path, className, manifest)`

All editing tools stage output and launch review/diff. They do not directly overwrite watched source.

The Model-submitted manifest is intent, not authority. The Tool Server should derive verification metadata from the staged file using Roslyn and use Server-derived metadata as the verification authority.

## Diff Pause Rule

Opening a diff is an explicit MCP tool call.

The Tool Server should expose a workflow state similar to:

```text
idle
staged
diff-open
awaiting-operator-decision
accepted
rejected
```

When a diff is opened, the workflow enters `awaiting-operator-decision`.

Rules:

- Only one diff may be active at a time.
- The Host and Model must not advance to the next file while a diff is open.
- Multi-file work must become an ordered compare queue.
- The Tool Server opens the first diff and records the active file/session state.
- The Operator reviews, merges, rejects, or asks for changes.
- The Model or Host then calls an MCP tool such as `record_diff_decision(sessionId, filePath, decision)`.
- Only after that decision may the Tool Server release the next queued diff.
- Closing the diff window is not enough to prove the merge happened.
- The Tool Server must verify the watched file after review by comparing the current watched file hash/content against the staged proposal and staged edit metadata.
- The Tool Server updates the session file hash after the confirmed state is known.

This pause is required because GUI diff tools are outside the MCP protocol. The Tool Server can launch the review, but the Operator decision is the synchronization point.

## Post-Decision Verification

This section is confirmed architecture.

### Manifest Trust Model

The Model-submitted manifest is the request contract only.

The Tool Server never blindly trusts the manifest. The Tool Server uses Roslyn to derive actual staged metadata independently:

- actual symbols added
- actual symbols removed
- actual usings added
- actual usings removed
- original file hash
- staged file hash
- symbol spans
- symbol text hashes

The manifest expresses Model intent. Tool Server-derived Roslyn metadata is the verification authority.

Discrepancies between the manifest and Tool Server-derived metadata are flagged.

### Tool Server Responsibility Clarification

Monitor MCP Tool Server and Roslyn CodeLens MCP Tool Server must not merge as MCP Tool Servers. Their external responsibilities remain separate:

- Monitor owns workflow, safety, staging, verification, session state.
- CodeLens owns external code intelligence queries.

The Monitor Tool Server may use Roslyn internally for staging and verification. This is an implementation detail, not a responsibility merge.

### Staged Edit Record

Every staged edit should create a `StagedEditRecord` with:

```text
sessionId
filePath
operation
originalHash
stagedHash
manifest
serverDerivedMetadata:
  symbolsAdded (name, kind, span, textHash)
  symbolsRemoved (name, kind, originalSpan, originalTextHash)
  usingsAdded
  usingsRemoved
  stagedFilePath
queueStatus
```

Store staged edit records under monitor-owned `Working\\Staged` with session linkage.

Full-file staging is easy to verify:

```text
current watched file hash == staged proposal hash -> accepted
current watched file hash == original baseline hash -> rejected
otherwise -> partially-merged or unknown
```

Roslyn symbol operations are harder.

For `submit_symbol`, `add_symbol`, `remove_symbol`, `add_using`, and similar tools, the Tool Server may generate a full staged file from a small symbol-level operation. After Operator review, the watched file might not match the full staged proposal exactly because the Operator may accept only part of the change or adjust it during merge.

Symbol-level staged edits should record metadata:

```text
operation kind
source file path
original file hash
staged file hash
target symbol name
target symbol kind
original symbol span
replacement/generated symbol span
original symbol text hash
replacement/generated symbol text hash
using directive changes, if any
```

Verification strategy:

1. Check full-file hash first.
2. If full file matches staged proposal, classify `accepted`.
3. If full file matches original baseline, classify `rejected`.
4. If target symbols and usings are confirmed by Roslyn but full file hash differs, classify `symbol-accepted-with-other-edits`.
5. If some manifest items are confirmed by Roslyn, classify `partially-merged`.
6. If no manifest items are confirmed, classify `rejected`.
7. If Roslyn cannot parse the watched file, classify `unknown`.

`symbol-accepted-with-other-edits` is a successful outcome. The Operator is allowed to adjust whitespace, formatting, small fixes, or nearby code during merge. What matters is whether the intended change landed.

The Tool Server should return a small envelope after `record_diff_decision`:

```json
{
  "sessionId": "abc123",
  "filePath": "CteFieldParser.cs",
  "classification": "symbol-accepted-with-other-edits",
  "newHash": "a3f9...",
  "manifestResults": {
    "added": {
      "BuildVerificationMap": "matched"
    },
    "removed": {},
    "usingsAdded": {
      "System.Collections.Frozen": "present"
    },
    "usingsRemoved": {}
  },
  "queueStatus": "queue-empty"
}
```

No file content should be returned in this envelope. The Model should use `get_file_outline` first for partial/unknown outcomes. Full `get_file` is the last resort.

### Implementation Order

1. Staged edit records

Add `StagedEditRecord` with the required fields above. The Tool Server populates `serverDerivedMetadata` via Roslyn on every staging operation.

2. Explicit diff workflow

`compare_file` / `open_diff` launches one diff tool instance. Workflow enters `awaiting-operator-decision`. No next file may proceed while a diff is open. Only one active diff at a time is a hard rule.

3. `record_diff_decision`

Records Operator decision, re-hashes watched file, runs Roslyn verification against `serverDerivedMetadata`, classifies outcome, updates session file hash to actual watched file state, records decision/classification/timestamp, releases next queued diff if available, and returns a small envelope only.

4. Symbol operations

Only after steps 1-3 are stable, add:

- `submit_symbol(path, symbolName, code, manifest)`
- `add_symbol(path, symbolType, code, afterSymbol?, manifest)`
- `remove_symbol(path, symbolName, manifest)`
- `add_using(path, namespace, manifest)`
- `remove_using(path, namespace, manifest)`
- `add_class(path, code, manifest)`
- `remove_class(path, className, manifest)`

All symbol operations populate `serverDerivedMetadata` via Roslyn before staging. All enter the diff queue. None overwrite watched source files directly.

### Core Design

```text
Model proposes
Tool Server stages and derives metadata via Roslyn
Operator reviews via diff
Tool Server verifies against Roslyn-derived metadata
Session state advances
Model receives small envelope only
```

Full file content should stay in Model context memory where possible. No file reload unless the classification is `partially-merged` or `unknown`. For `partially-merged` or `unknown`, use `get_file_outline` first and `get_file` only as a last resort.

## Edit Format References

These references explain why the Monitor workflow uses Roslyn-backed staging and overlay validation rather than treating text patches as the primary edit mechanism.

Official / OpenAI references:

- GPT-4.1 prompting guide, including V4A-style patch examples: https://developers.openai.com/cookbook/examples/gpt4-1_prompting_guide
- OpenAI `apply_patch` tool guide: https://developers.openai.com/api/docs/guides/tools-apply-patch
- Agents JS `applyDiff` SDK reference: https://openai.github.io/openai-agents-js/openai/agents-core/functions/applydiff/

Third-party / commentary references:

- V4A diff format and context anchoring: https://codex.danielvaughan.com/2026/03/31/codex-cli-apply-patch-v4a-diff-format/
- Codex issue discussing shell-oriented defaults such as `rg`, `sed`, and `cat`: https://github.com/openai/codex/issues/14113
- Why line-number based edits drift in AI workflows: https://www.morphllm.com/ai-apply-patch
- Comparison of patch, search/replace, and whole-file editing approaches: https://fabianhertwig.com/blog/coding-assistants-file-edits/

Working conclusion:

- Text patches are useful, but context anchors can drift when the file changes between read and write.
- Roslyn symbol operations should anchor on syntax and symbols instead of brittle text boundaries.
- Overlay validation lets the Tool Server compile the watched project with proposed staged edits before the Operator reviews a diff.
- The diff remains the Operator review step, not the primary application mechanism.

## Core Question

Is this the right MCP workflow?

Specifically:

1. Should scripted Tool Server tests be used to prove safety before trusting Model-generated edits?
2. Should Ollama remain as a baby-Claude Host simulation even if it is too weak for real code surgery?
3. Should Claude eventually replace only the Model decision/code-generation layer while the same MCP Tool Servers stay unchanged?
4. Is the split between Monitor MCP Tool Server and Roslyn CodeLens MCP Tool Server correct?
5. Is durable Tool Server-side session state the right way to preserve long-running workflow context without wasting tokens?
