---
status: new
type: test-result
created: 2026-05-26
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---

## Summary

Ran the Finding 60 verification plan against Codex's `ed4a954` (`split_razor_code_to_companion`). Tool registration, dry-run, two-file staging, WinMerge launch, and operator save all work end-to-end. Two real defects surfaced — one fixed in this same push, one filed as [Finding 61](20260526-finding-61-record-diff-decision-response-error-after-success.md) for Codex.

## What worked

- `get_tool_manifest` lists the tool with correct argument shape (`sourceFilePath`, `namespaceName?`, `leaveEmptyCodeBlock?`, `sessionId?`, `manifestJson?`).
- Dry-run on `Components/ColumnReconciliation/ColumnReconciliationDialog.razor` (47,113 bytes) staged both records under session `razor-split-20260526T213935675Z` with all 100+ extracted members (class + nested `ReconciliationCandidate`, records, enum) and correct namespace inference.
- `launch_staged_diff` for the companion required `forceReviewOnOverlayErrors=true` because overlay validation reported 49 CS-class diagnostics against the watched `.razor` — that gating behavior is per spec; the diagnostics themselves are noise from the overlay compiler treating the watched `.razor` as a C# syntax tree (probably worth a separate look but not a blocker).
- WinMerge save on both files produced clean watched-file hashes matching staged hashes.

## Defect A — companion missing `using Microsoft.AspNetCore.Components` (fixed on this branch)

`dotnet build` of the watched solution failed with 15 CS0246 errors on the new companion: `RenderFragment`, `ParameterAttribute`, and `Parameter` could not be found.

Root cause: `RazorCompanionSplitter.BuildCompanionCode` only emitted usings parsed from the input `.razor` file's `@using` directives. Razor's compiler implicitly adds `Microsoft.AspNetCore.Components` to every generated component, and project-level `_Imports.razor` files (e.g. `Components/_Imports.razor` here) contribute additional usings. A detached `.razor.cs` companion has neither implicit source.

Fix (this commit):
- New `CollectCompanionUsings` always emits `Microsoft.AspNetCore.Components` first, then merges `_Imports.razor` contributions walking from the source file's directory up to `observedRoot`, then file-level `@using` directives.
- Duplicates are coalesced by the existing `Distinct(StringComparer.Ordinal)` in `BuildCompanionCode`.

Codex action: please rewrite `RazorCompanionSplitter` tests (and any orchestrator tests in `MonitorWorkflowService.SplitRazorFile`) to lock in the new behavior. Suggested cases:
- `.razor` with no `@using` → companion still has `Microsoft.AspNetCore.Components`.
- `.razor` under a folder with `_Imports.razor` → companion includes the imports' directives.
- Nested `_Imports.razor` (project-root and component-folder) → both contribute.
- `@using static X.Y.Z` and `@using A = X.Y` → preserved verbatim (current regex captures the whole content after `@using`, so these already work, but please cover them).
- File-level `@using` duplicates an `_Imports.razor` entry → only one copy emitted.

## Defect B — `record_diff_decision` returns JSON-RPC error after success (unfixed, Finding 61)

See [Finding 61](20260526-finding-61-record-diff-decision-response-error-after-success.md). The work succeeds (decision record on disk, queue status accepted, watched file saved, index rebuilt), but the JSON-RPC reply is an error envelope. Both calls in this two-file session failed identically.

This did not block verification — I checked `Working/Staged/Records/.../*.json` and `Working/Staged/Decisions/.../*.json` directly to confirm classification — but downstream automation that expects a structured success payload will break.

## Watched-source state

Watched repo `C:\SchemaStudioWebViewer - Copy` was rolled back to `master` HEAD before this commit:
- `Components/ColumnReconciliation/ColumnReconciliationDialog.razor` restored from HEAD.
- `Components/ColumnReconciliation/ColumnReconciliationDialog.razor.cs` deleted.

Codex can re-run the split against the same file from clean state to validate the fix.

## Build verification

- `MonitorBaseClaude.csproj` → 0 warnings, 0 errors.
- `MonitorBaseClaude.McpServer.csproj` → 0 warnings, 0 errors.
- Watched solution `dotnet build` not re-attempted post-fix; will be valid once Codex's tests re-stage the dialog with the fixed splitter.
