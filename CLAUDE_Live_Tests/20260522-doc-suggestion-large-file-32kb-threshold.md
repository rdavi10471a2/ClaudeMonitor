---
status: new
type: doc-suggestion
created: 2026-05-22
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---

## Summary

CLAUDE.md previously said "do not call `get_file` for large files" without defining "large." This left the cold-session entry threshold to model judgment, which is fragile and easy to get wrong by one file. Recommend a concrete 32KB threshold, written into CLAUDE.md, backed by empirical pass/fail evidence from the 2026-05-21 BaseViewCreator.razor test session.

## Evidence

Same session, two real files:

| File | Size | get_file result |
|---|---|---|
| `ManageViews.razor` | 36,310 bytes (~36KB) | Succeeded inline |
| `BaseViewCreator.razor` | 85,976 bytes (~86KB) | Claude API 500, status.claude.com error |

Tool-result wrapping (JSON envelope, escaping) adds overhead on top of raw content, so the budget for the wrapped result is smaller than the raw byte count suggests.

## Threshold: 32KB

Sits comfortably below the known-good 36KB data point with margin for envelope overhead. Anything at or above 32KB → `refresh_file` + chunked Read from the returned `workingFilePath`. Anything below 32KB may use `get_file` directly when index/source-map/symbol context isn't enough.

## CLAUDE.md Change Applied On This Branch

Two lines updated to make the threshold concrete:

1. Required Edit Loop step 5 — added the 32KB threshold and the empirical rationale.
2. Expected Tool Sequences — updated "for any large file in a cold session" to "for any file at or above 32KB in a cold session."

## Notes

- This is a conservative number, not a binding API contract. If Anthropic publishes a different documented tool-result limit, replace 32KB with whatever number leaves a clean margin under that limit.
- The threshold is for cold-session entry only. Warm sessions (file already in context) skip the read entirely regardless of file size — that rule does not change.
- This does not change the underlying tools; `refresh_file` and chunked Read already exist. The change only sharpens the trigger condition.
