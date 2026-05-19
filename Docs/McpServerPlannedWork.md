# MCP Server Planned Work

This document holds planned MCP server hardening, future tools, diff workflow design notes, and test command references that are useful to keep, but should not be mistaken for the live callable MCP tool contract.

The live tool contract remains in MonitorBaseClaude.McpServer/MONITOR_MCP_TOOL_MANIFEST.md.

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
- `add_standard_regions(path, regionProfile?, scope?)`: explicit cleanup/refactor operation that groups an existing C# type into the standard region layout. This must not be used as part of normal functional edits; it is a separate staged candidate with its own review.

### Remove tools

- `remove_symbol(path, symbolSelector)`: delete a symbol cleanly, including attributes and XML/doc comments immediately attached to it. Leave no orphaned whitespace or comments.
- `remove_using(path, namespace)`: remove a using directive cleanly.
- `remove_class(path, className)`: remove a class and all of its members.

### Safety rules

- All add/replace/remove operations go through temp staging and diff before touching real files.
- Roslyn validates syntactic completeness before diff launch.
- The Tool Server must reject half-open syntax, unmatched braces, and incomplete symbol submissions.
- C# parse/syntax errors block staging. Overlay compile diagnostics are reported as validation metadata and do not automatically block staging.
- `dirty-unexpected` recovery must be explicit. Internal compare refreshes may recreate missing Working copies, but they must not clear blocked staged records.
- Model clients should prefer outline/symbol tools before requesting full file content when possible.
- Standard-region retrofits are cleanup/refactor work only. Do not combine them with behavior changes or routine symbol edits.

### Diff workflow tools

- `compare_file(path, sessionId)`: returns one staged/source pair for Host-launched diff review and moves workflow state to `awaiting-operator-decision`. If `refreshIfMissing` recreates a missing Working copy, that is not a dirty-state recovery operation.
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
C:\VSCodeProjects\MonitorBaseClaude\LocalSmokeTests\LegacyToolSmokeTests
```

Current staged-edit smoke command:

```powershell
dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\LocalSmokeTests\LegacyToolSmokeTests\LegacyToolSmokeTests.csproj -- --stage-comment-diff
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
dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\LocalSmokeTests\LegacyToolSmokeTests\LegacyToolSmokeTests.csproj -- --fixture-accept-smoke
```

The sidecar writes a fixture-specific config file and starts the Tool Server with that config. This keeps destructive accept-path testing away from the real watched DBV2 project.

Decision-gate testing is handled by:

```powershell
dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\LocalSmokeTests\LegacyToolSmokeTests\LegacyToolSmokeTests.csproj -- --fixture-decision-gate-smoke
```

This validates clean Accept, clean Reject, reported Accept without saved candidate, reported Reject after candidate landed, and unrelated dirty source edits.

Source-map artifact testing is handled by:

```powershell
dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\LocalSmokeTests\LegacyToolSmokeTests\LegacyToolSmokeTests.csproj -- --source-map-smoke "Data\BaseTableRepository.cs" --scope file --mode selector

dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\LocalSmokeTests\LegacyToolSmokeTests\LegacyToolSmokeTests.csproj -- --source-map-smoke Data --scope folder --mode navigation

dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\LocalSmokeTests\LegacyToolSmokeTests\LegacyToolSmokeTests.csproj -- --source-map-corpus-smoke
```

The focused commands call the real `get_source_map` tool against the configured watched solution and write `source-map-raw.json` plus `source-map-summary.md` under `Working\History\ToolSmokeTests\<timestamp>`. Use selector mode to review stable symbol target selection and navigation mode to review broad target/related-file discovery before reading bodies.

The corpus command walks configured DBV2 C# files and writes per-file full-mode maps plus `source-map-compact-index.json`, `source-map-navigation-index.json`, `source-map-corpus-analysis.json`, and `source-map-corpus-summary.md` under `Working\History\ToolSmokeTests\<timestamp>\source-map-corpus`. Use the raw/full maps for schema/debug review, the selector index for stable selector handoff, and the navigation index for broad orientation without dumping every file body.

Roslyn surgery testing is handled by:

```powershell
dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\LocalSmokeTests\LegacyToolSmokeTests\LegacyToolSmokeTests.csproj -- --fixture-roslyn-surgery-smoke
```

This stages and accepts `add_using`, `submit_symbol`, `add_symbol`, `remove_symbol`, and `remove_using` against the disposable DBV2-shaped fixture.

Razor current-lane testing is handled by:

```powershell
dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\LocalSmokeTests\LegacyToolSmokeTests\LegacyToolSmokeTests.csproj -- --fixture-razor-smoke
```

This uses a separate Razor-shaped fixture to verify `find_file`, `get_file`, empty C# outline behavior, full-file `.razor` staging, explicit `razor-validation-pending` overlay status, and strict vote-plus-hash Accept.

Resolved V1 position: Razor follows the same practical lane as the original monitor. Raw `.razor` and `.cshtml` files are staged and reviewed as text, then validated by the watched project's real build after merge. They are not part of the C# Roslyn overlay compile gate. Razor-aware generated-C# validation is optional future research, not an active V1 backlog item.

Unreproduced backlog noise: Claude reported one partial-class overlay false positive during the live async repository propagation test, but local follow-up smokes did not reproduce it. Covered checks now include a synthetic partial-class overlay, a real DBV2 WinForms designer partial pair, and a real DBV2 same-type multi-candidate partial session. Reopen only with exact candidate files, staged record, diagnostics, and session state from a failing run.

Official / OpenAI references:

- GPT-4.1 prompting guide: https://developers.openai.com/cookbook/examples/gpt4-1_prompting_guide
- OpenAI `apply_patch` tool guide: https://developers.openai.com/api/docs/guides/tools-apply-patch
- Agents JS `applyDiff` SDK reference: https://openai.github.io/openai-agents-js/openai/agents-core/functions/applydiff/

Third-party / commentary references:

- V4A diff format and context anchoring: https://codex.danielvaughan.com/2026/03/31/codex-cli-apply-patch-v4a-diff-format/
- Codex issue discussing shell-oriented defaults such as `rg`, `sed`, and `cat`: https://github.com/openai/codex/issues/14113
- AI patch drift discussion: https://www.morphllm.com/ai-apply-patch
- File-editing approach comparison: https://fabianhertwig.com/blog/coding-assistants-file-edits/
