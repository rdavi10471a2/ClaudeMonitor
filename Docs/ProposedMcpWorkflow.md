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

This Tool Server owns the safe edit workflow. It should never overwrite watched source files directly during AI edits. It stages output under monitor-owned folders and returns paths for Host-owned diff/review.

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
5. The Host opens the GUI diff tool for review using Tool Server returned paths.
6. Test compares the staged output to expected output.
7. No watched source file is overwritten.

This lane proves the Tool Server is safe and deterministic.

### 1b. Sidecar Operator Workflow Tests

Purpose: prove operator workflow mechanics without cluttering the WinForms UI.

Project:

```text
C:\VSCodeProjects\MonitorBaseClaude\LocalSmokeTests\LegacyToolSmokeTests
```

Current staged-edit smoke command:

```powershell
dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\LocalSmokeTests\LegacyToolSmokeTests\LegacyToolSmokeTests.csproj -- --stage-comment-diff
```

Flow:

1. Sidecar runner calls the real Monitor MCP Tool Server `submit_file` tool.
2. Tool Server stages, records, and validates the edit.
3. Tool Server returns staged/source paths.
4. Sidecar runner launches WinMerge directly.
5. Operator reviews the sanity diff and either saves the full candidate in WinMerge or leaves source unchanged.

The sidecar runner acts like a Host for test purposes. It may own GUI diff lifetime. The stdio Tool Server must not own GUI diff lifetime.

The sidecar `record_diff_decision` trigger is explicit, not automatic. After WinMerge review, the sidecar should ask the Operator to report whether the full candidate was saved (`accepted`) or source was left unchanged (`rejected`). WinMerge close detection is telemetry only and is not sufficient to trigger verification.

The expected v1 Operator pattern is accept all or reject all. The Operator should not hand-edit in the middle of WinMerge review. If the staged proposal is close but not right, reject it and ask the Model/Host for a new staged proposal.

The diff is a final sanity check, not the primary editing surface. The source file is a voting member in generation: a valid staged proposal should preserve the current file shape and make a focused change. If the diff shows drastic rewrites, moved code, or boundary damage, reject and regenerate.

Sidecar implementation sequence:

1. Keep staged edit plus overlay compile validation stable. Done.
2. Add sidecar outcome command and call `record_diff_decision`. Done.
3. Build strict post-decision verification after the prompt contract is stable. Done for v1 vote-plus-hash gate.
4. Add `get_source_map` as the next read-only discovery tool before symbol surgery. Done.
5. Add disposable DBV2-shaped fixture accept smoke using a generated config file. Done.
6. Add disposable Roslyn surgery smoke for `add_using`, `submit_symbol`, `add_symbol`, `remove_symbol`, and `remove_using`. Done.
7. Add separate disposable Razor smoke for current read/full-file-stage/accept behavior with `razor-validation-pending`. Done.

Current implementation note: `submit_file.launchDiff` still exists as a compatibility parameter, but GUI launch from the Tool Server is deprecated. Sidecar and Host callers should pass `launchDiff:false`, then launch WinMerge themselves using the returned staged/source paths.

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
- `get_symbol(path, symbolSelector)` for the future structured selector surface.
- `check_file_hash(path, sessionId)`

Staged replacement tools:

- `submit_file(path, content, manifest)`
- `submit_symbol(path, symbolSelector, code, manifest)`

Roslyn symbol operations:

- `add_symbol(path, symbolType, code, afterSymbol?, manifest)`
- `remove_symbol(path, symbolSelector, manifest)`
- `add_using(path, namespace, manifest)`
- `remove_using(path, namespace, manifest)`
- `add_class(path, code, manifest)`
- `remove_class(path, className, manifest)`

All editing tools stage output for review/diff. They do not directly overwrite watched source.

The Model-submitted manifest is intent, not authority. The Tool Server should derive verification metadata from the staged file using Roslyn and use Server-derived metadata as the verification authority.

## Diff Pause Rule

Opening an interactive GUI diff is a Host action using paths returned by the Tool Server. A non-interactive CLI diff may later be exposed as an MCP tool, but GUI lifetime belongs to the Host.

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
- The Tool Server returns staged/source paths and records the active file/session state.
- The Host opens the first GUI diff and logs the launch in telemetry.
- The Operator reviews the sanity diff and either saves the full candidate in WinMerge or leaves source unchanged.
- The Operator explicitly records the outcome in the Host.
- The Host then calls an MCP tool such as `record_diff_decision(stagedRecordId, decision, note?, sessionId?)`.
- Only after that decision may the Tool Server release the next queued diff.
- Closing the diff window is not enough to prove a decision happened.
- The Tool Server must verify the watched file after review by comparing the current watched file hash/content against the staged proposal and staged edit metadata.
- The Tool Server updates the session file hash after the confirmed state is known.

This pause is required because GUI diff tools are outside the MCP protocol. Testing showed WinMerge should be launched by the Host, not the stdio Tool Server. The Operator's reported outcome plus watched hash is the synchronization point.

`record_diff_decision` is not triggered by automatic WinMerge close detection. The Host may show process/window status as telemetry, but classification is authoritative only after the Operator reports what happened and the Tool Server re-hashes the watched file.

Current v1 signature:

```text
record_diff_decision(stagedRecordId, decision, note?, sessionId?)
```

The decision argument is the Operator-reported outcome: `accepted` means the Operator believes WinMerge saved the full candidate; `rejected` means the Operator believes source was left unchanged. The Tool Server computes the actual classification from vote-plus-hash agreement.

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
batchId
priorConvergedHash
originalBaselineHash
stagedCandidateHash
rawOriginalHash
rawStagedHash
normalizedOriginalTextHash
normalizedStagedTextHash
originalEncoding
stagedEncoding
originalNewLineKind
stagedNewLineKind
originalHadBom
stagedHadBom
originalHadFinalNewLine
stagedHadFinalNewLine
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

Full-file staging is the v1 verification model:

```text
Reported Accept:
  Operator saves the full staged candidate through WinMerge
  Tool Server verifies watched hash equals staged hash -> accepted
  any other watched hash -> dirty-unexpected

Reported Reject:
  Tool Server verifies watched hash equals original baseline -> rejected
  any other watched hash -> dirty-unexpected

No-op candidate:
  staged hash equals original baseline hash -> no-change/no-op-staged
  do not enqueue a normal diff by default
```

The Operator's expected v1 behavior is all-or-nothing accept/reject. The Tool Server does not accept hand-edited middle states in v1. Accept means the Operator reported Accept, WinMerge/Operator saved the entire staged candidate, and the Tool Server verifies the watched hash equals the staged hash. Reject means the Operator reported Reject, the candidate was not saved, and the Tool Server verifies the watched hash still equals the original baseline. If the Operator report and watched hash disagree, or if the watched file hash matches neither staged nor original, the file is blocked for further AI edits until the Host refreshes state.

Roslyn symbol operations are harder and are postponed until the strict vote-plus-hash path is stable.

For `submit_symbol`, `add_symbol`, `remove_symbol`, `add_using`, and similar tools, the Tool Server may generate a full staged file from a small symbol-level operation. The Operator still reviews the whole staged candidate and either saves it all or leaves source unchanged. Partial hunk acceptance and hand repair inside the diff tool are not part of the workflow.

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

Strict v1 verification strategy:

1. For reported Accept, verify the watched file now matches the staged candidate.
2. For reported Reject, verify the watched file still matches the original baseline.
3. Any mismatch between reported outcome and watched hash is `dirty-unexpected`.
4. Any watched hash outside the original/staged hashes is `dirty-unexpected`.
5. The Tool Server never copies the staged candidate into watched source; WinMerge/Operator save is the mutation path.

Tolerant symbol-level verification is intentionally out of scope. The Operator either saves the whole staged candidate or leaves source unchanged.

The Tool Server should return a small envelope after `record_diff_decision`:

```json
{
  "sessionId": "abc123",
  "filePath": "CteFieldParser.cs",
  "classification": "accepted",
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

No file content should be returned in this envelope. For `dirty-unexpected`, the Host should refresh/re-read state before allowing more AI edits on that file.

### Implementation Order

1. Staged edit records

Add `StagedEditRecord` with the required fields above. The Tool Server populates `serverDerivedMetadata` via Roslyn on every staging operation.

2. Explicit diff workflow

`compare_file` / `open_diff` launches one diff tool instance. Workflow enters `awaiting-operator-decision`. No next file may proceed while a diff is open. Only one active diff at a time is a hard rule.

3. `record_diff_decision`

Records the reported outcome, re-hashes watched file, classifies by strict v1 vote-plus-hash gate, updates session file hash to actual watched file state when a session is present, records outcome/classification/timestamp, releases next queued diff if available, and returns a small envelope only.

For Accept, WinMerge/Operator save is the mutation path and the Tool Server verifies the watched file hash equals the staged candidate hash. For Reject, it verifies the watched file still matches the original baseline hash.

4. Symbol operations

Only after steps 1-3 are stable, add:

- `submit_symbol(path, symbolSelector, code, manifest)`
- `add_symbol(path, symbolType, code, afterSymbol?, manifest)`
- `remove_symbol(path, symbolSelector, manifest)`
- `add_using(path, namespace, manifest)`
- `remove_using(path, namespace, manifest)`
- `add_class(path, code, manifest)`
- `remove_class(path, className, manifest)`

All symbol operations populate `serverDerivedMetadata` via Roslyn before staging. All enter the diff queue. Symbol edit tools do not overwrite watched source files directly. In the WinMerge workflow, the Operator-saved diff result mutates the watched source, and `record_diff_decision` verifies that the watched hash exactly matches the staged candidate for Accept.

### Core Design

```text
Model proposes
Tool Server stages and derives metadata via Roslyn
Operator reviews via diff
Operator saves the full candidate or leaves source unchanged
Tool Server verifies Operator-saved accepted candidates all-or-none
Tool Server verifies by strict vote-plus-hash gate
Session state advances
Model receives small envelope only
```

Full file content should stay in Model context memory where possible. No file reload is needed after exact `accepted` or `rejected`. For `dirty-unexpected`, refresh/re-read state before allowing more AI edits on that file.

## Structural Evolution

Conformance does not mean freezing the source structure. The system may evolve structure by splitting files, extracting classes, moving symbols, adding partial classes, creating new files, or introducing new boundaries.

Those changes must be explicit structural candidates, not accidental side effects of unrelated edits. Each structural candidate should be small enough for the Operator to review as one intentional move. Accept applies the whole candidate and makes it the next converged pattern. Reject applies nothing.

`get_source_map` supports this by showing the real current structure before the Model proposes a refactor. It helps distinguish intentional movement from accidental movement, bounded extraction from broad derangement, and expected symbol relocation from unexpected symbol disappearance. The source map is not the accept/reject gate; the gate remains `record_diff_decision` vote-plus-hash agreement.

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
