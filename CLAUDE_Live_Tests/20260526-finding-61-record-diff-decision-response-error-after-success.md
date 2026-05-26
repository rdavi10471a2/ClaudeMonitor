---
status: fixed
type: finding
created: 2026-05-26
processed: true
processedBy: Claude
processedAt: 2026-05-26
resolution: |
  Root cause: split_razor_code_to_companion fabricated a "razor-split-<timestamp>" sessionId
  in MonitorWorkflowService.SplitRazorCodeToCompanion but never durably created the session
  file. record_diff_decision later called sessionService.RecordEvent(result.SessionId, ...)
  which called LoadRequired(sessionId) which threw FileNotFoundException. Track re-threw,
  the MCP SDK wrapped the throw in a JSON-RPC error envelope, and the host bridge logged
  isError=true. Server-side telemetry (errors.jsonl) was disabled in the running McpServer
  process so the throw appeared "silent" from the McpServer side.
  Fix: added MonitorSessionService.EnsureSession (idempotent create-if-missing). The
  Program.cs SplitRazorCodeToCompanion wrapper now calls EnsureSession with the result's
  SessionId after the workflow returns, so RecordEvent/RecordFileFetch can find the session
  in the subsequent record_diff_decision calls.
resolutionCommit:
---

## Summary

`record_diff_decision` performs all server-side work correctly (writes decision record, updates staged record `QueueStatus`, runs/queues the post-decision index refresh, writes the watched-source change via WinMerge save) but then returns a JSON-RPC error envelope to the client instead of the normal result. Reproduced consistently across both files of a `split_razor_code_to_companion` session.

## Repro

1. Run `split_razor_code_to_companion` on a `.razor` file with inline `@code`. Stages two records under one session.
2. `launch_staged_diff` + accept the companion in WinMerge.
3. Call `record_diff_decision(stagedRecordId, "accepted")` for the companion record.
4. Repeat for the markup record.

## Expected

Both calls return a `MonitorDiffDecisionResult` payload (~1.9 KB) with `Classification: accepted`, `DecisionMatchesClassification: true`, and the `IndexRefresh` block.

## Actual

Both calls return `An error occurred invoking 'record_diff_decision'` (148/149-byte JSON-RPC error envelope).

Server-side state is correct in every observable way:
- `Working/Staged/Decisions/20260526/..._accepted.json` is written and complete (hash agreement, IndexRefresh status `deferred` for the first and `rebuilt-session-complete` for the second).
- `Working/Staged/Records/.../*.json` shows `QueueStatus: accepted`.
- Watched files match the staged hashes on disk.
- `Working/History/McpTelemetry/MonitorBaseClaude/errors.jsonl` has **no entry** for either failing call — so `MonitorMcpTelemetryService.Track` never caught an exception. The exception is being thrown after `Track` returns, in the MCP framework's response serialization (or the host bridge), and is being converted into a JSON-RPC error by the framework.

First call elapsed 76 ms (deferred-index path), second elapsed 33,627 ms (full multi-file index rebuild) — both fail with the same 148-byte envelope shape, ruling out a timeout cause.

## Evidence

- Tool: `mcp__monitor-base-claude__record_diff_decision`
- Session: `razor-split-20260526T213935675Z`
- Staged record ids:
  - companion: `20260526_163938673_stage_candidate_for_review_ColumnReconciliationDialog.razor_bce77bc3`
  - razor: `20260526_163935981_stage_candidate_for_review_ColumnReconciliationDialog_f37b3923`
- Telemetry: `Working/History/McpTelemetry/MonitorBaseClaude/responses.jsonl` ids 7 and 10 (this session) — both `isError=true`, `messageBytes=148/149`.
- Server stderr/error log: `Working/History/McpTelemetry/MonitorBaseClaude/errors.jsonl` last entry 2026-05-17 — confirms Track's catch never fired.
- Decision JSON on disk: `Working/Staged/Decisions/20260526/20260526_163935981_..._164425038_accepted.json` and `..._164257035_accepted.json`.

## Notes

The unknown is "what throws after `Track` returns." Suggests inspecting the MCP framework's `[McpServerTool]` wrapper or the host bridge (`McpProxyHubService`) for a response post-processing step that fails specifically on multi-file split-session decisions. The on-disk Decision JSON shape is well-formed when re-read, so the `MonitorDiffDecisionResult` record itself is not statically wrong — the failure may be path-dependent (e.g., a follow-up step in `Program.cs.RecordDiffDecision`'s lambda after `workflowService.RecordDiffDecision` returns, such as `sessionService.RecordEvent` or `RecordFileFetch`, throwing in a way that bypasses Track's catch).
