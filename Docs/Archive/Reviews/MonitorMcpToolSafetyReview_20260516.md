# Monitor MCP Tool Surface Review (Deterministic, Evidence-based)
**Date:** 2026-05-16
**Scope:** Allowed files only
- `Program.cs`
- `MonitorWorkflowService.cs`
- `MonitorSessionService.cs`
- `MONITOR_MCP_TOOL_MANIFEST.md`

## Findings (severity-ordered)

### [Info] 1) Manifest enumerates planned tools not implemented in code
- **File/function:** `MONITOR_MCP_TOOL_MANIFEST.md` (tool table and planned sections) vs `Program.cs` (`MonitorTools` methods)
- **Observed:** Manifest documents tools including `add_class`, `remove_class`, `list_telemetry_runs`, `get_telemetry_run`, and `refresh_and_compare_file` that do not exist as MCP methods in `MonitorTools`.
- **Risk:** A client auto-consuming manifest entries as hard capabilities could issue calls to unavailable tools and fail mid-workflow.
- **Minimal fix:** Keep manifest and runtime capability parity explicit with machine-readable status tags and stable client parsing (e.g., only expose implemented tools as current actions; move planned tools to a clearly separated “planned-only” block).
- **Confidence:** High

### [Info] 2) `compare_file` refresh behavior is distinct from dirty-unexpected recovery, but not always obvious to callers
- **File/function:** `MonitorWorkflowService.cs` (`CompareFile`, `RefreshFile`) and `MonitorSessionService.cs` (session handling)
- **Observed:** `RefreshFile` passes `recoverDirtyUnexpected: true` and calls `RecoverBlockedDirtyUnexpectedRecords`; `CompareFile` calls `RefreshFile(..., recoverDirtyUnexpected: false)` only when `refreshIfMissing=true`.
  `record_diff_decision` explicitly blocks reclassification for blocked records.
- **Risk:** Client confusion rather than direct safety risk: operators may assume compare refresh always performs recovery.
- **Minimal fix:** Return explicit response text/state indicating whether recovery happened, and keep this behavior documented in tool docs.
- **Confidence:** High

### [Info] 3) Syntax errors are rejected before staging (positive safety control, not a defect)
- **File/function:** `MonitorWorkflowService.cs` (`StageFileReplacement`)
- **Observed:** `ValidateSyntaxIfCSharp` runs first; if syntax errors exist, method throws before staged record creation (`if (validation.HasErrors) throw...`) and before `CreateStagedRecord`.
- **Risk:** None; this is correct defense-in-depth.
- **Minimal fix:** N/A (no change).
- **Confidence:** High

### [Warning] 4) `get_file` can bypass orientation-before-mutation preference if used first
- **File/function:** `Program.cs` (`GetFile`)
- **Observed:** `GetFile` reads full source directly and returns entire contents.
- **Risk:** In weak clients, this can allow broad file read before source-map/symbol orientation, increasing token load and the chance of broad, unnecessary edits.
- **Minimal fix:** No code change required in this cycle; harden client/tool orchestration policy to prefer `get_source_map`/`get_symbol` first for C# edits.
- **Confidence:** High

## Scope answers

2026-05-17 update: the live manifest now documents `set_type_partial`, the typed add tools (`add_field`, `add_property`, `add_method`, `add_constructor`, `add_nested_type`), overlay review gating, and `get_smoke_test_catalog`. The remaining planned-only entries are still marked as planned in the manifest.

1) **Tools exposed in code vs manifest:**
- Code-exposed tools are all in `MonitorTools` (`Program.cs`).
- Manifest adds planned entries that are not yet implemented in code.

2) **Read/discovery-only tools:**
- `get_file`, `get_file_outline`, `get_source_map`, `get_symbol`, `find_file`, `check_file_hash`, `get_workflow_status`, `get_monitor_status`, `list_*`, `get_ledger` etc.

3) **Tools staging edits:**
- `submit_file`, `submit_symbol`, `set_type_partial`, `add_using`, `remove_using`, `add_symbol`, `remove_symbol`, `add_field`, `add_property`, `add_method`, `add_constructor`, and `add_nested_type` (`MonitorWorkflowService.Submit*` / `Add*/Remove*`).

4) **Workflow state / recovery tools:**
- `start_monitor_session`, `record_monitor_session_event`, `list/ get_monitor_session`, `record_diff_decision`, `RefreshFile` (recover path), `recover` behavior via blocked-record status updates.

5) **Source-map before mutation:**
- `GetSourceMap` exists and tool descriptions state it for C# edits first; `get_symbol` and all staged symbol tools can follow from selector/keys.

6) **Direct watched-source writes outside WinMerge/staging path:**
- No allowed-file evidence of direct watched source mutation in `MonitorWorkflowService`/`Program`.
- Writes are to staged paths (`Working/Staged`) and Working copies (`RefreshFile`).

7) **`record_diff_decision` hash classification:**
- Enforced in `ClassifyStrictDiffDecision`: accepted = reported accepted + current hash == staged hash; rejected = reported rejected + current hash == original.
- Mismatch in either direction returns `dirty-unexpected`.

8) **Dirty-unexpected block + recovery:**
- Blocking check: `EnsureFileIsNotBlockedByDirtyUnexpected` prevents further staging.
- Recovery is explicit via `RefreshFile` calling `RecoverBlockedDirtyUnexpectedRecords`.

9) **`compare_file` refresh vs dirty recovery separation:**
- Separated: `CompareFile` can refresh missing working copy and does **not** recover blocked records (`recoverDirtyUnexpected: false`).
- `RefreshFile` is the explicit recovery trigger.

10) **Guardrail boundaries:**
- `record_diff_decision` is vote-plus-hash authoritative; it writes no source content and classifies on hashes.
- Staging methods all return staged paths and metadata; no direct overwrite path in MCP surface for watched source.

## Top 3 concrete risks
1. Manifest-client mismatch for planned/implemented tools can create runtime capability failures.
2. Weak/legacy clients could still use `get_file` as first action and bypass orientation hierarchy.
3. Recovery semantics depend on `refresh_file`; if client workflows do not call it intentionally, blocked files may stay intentionally blocked.

## 3 explicit “ready for next run” test gaps
1. Add a test that validates manifest/tool-parity: only runtime-implemented MCP tools are treated as callable capabilities by clients.
2. Add a deterministic test that verifies blocked `dirty-unexpected` state plus explicit `refresh_file` recovery transition and subsequent staging re-allowed behavior.
3. Add a deterministic test that exercises `get_symbol` → `submit_symbol` workflow and asserts `StageFileReplacement` is only reached after source-map/symbol-driven path.

## 1–2 “do not change” recommendations
- Do not change manifest status taxonomy unless you keep planned vs implemented clearly machine-parsable.
- Do not weaken `record_diff_decision` hash gate; it is the critical accept/reject safety boundary.

## Verdict
**Safe for read-only trial**
No blocker found for read-only workflow under the allowed-file evidence, with primary blocker/monitoring concern being **manifest vs runtime tool-capability mismatch for planned tools** (clarify before wide autonomous runtime use).
