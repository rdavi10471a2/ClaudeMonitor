---
status: new
type: finding
created: 2026-05-21
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---

## Summary

The Monitor Working candidate for a file is only created when the first candidate composition tool is called (`replace_span_in_file`, `submit_file`, `add_method`, etc.). For files that have never been worked on in any prior session, the Working copy does not exist. This means the model cannot `Read` from `candidateFilePath` before making its first edit. The current fallback is `get_file`, which for files above ~40KB causes a Claude API 500 (oversized tool result). The result is a hard failure with no clean recovery path.

The operator notes the original workflow had an implicit pre-initialization mechanism — believed to be a `--refresh` command-line argument or equivalent — that pre-populated the Working directory before any MCP edit tools were called. That initialization path appears to have been lost in the current implementation.

## Repro

1. Watch a solution that includes a large Razor file (> 40KB) that has never been worked on.
2. In a new session, call `get_file` on that file.
3. The Monitor MCP server returns ~98K characters.
4. The Claude API returns a 500 (oversized tool result). `status.claude.com` error shown.

## Expected

The Working copy should exist and be readable via `Read(candidateFilePath)` before any candidate composition tool is called. Either:
- The original `--refresh` initialization path is restored, OR
- A new lightweight MCP tool initializes the Working copy on demand and returns `candidateFilePath` without content.

## Proposed Fix: `initialize_candidate` tool

```
initialize_candidate(path, sessionId?)
→ {
    candidateFilePath,
    observedRootKey,
    baselineHash,
    length,
    alreadyExisted   // true if Working copy was already current
  }
```

- Calls `EnsureCandidateInitialized` internally — copies source to Working if not already current
- Returns the Working path and metadata; returns **no file content**
- Idempotent: if a current Working candidate already exists, returns its path unchanged
- Model then uses `Read(candidateFilePath, offset=N, limit=250)` in chunks — line numbers come back in cat-n format, directly usable in `replace_span_in_file`

This replaces `get_file` as the entry point for large files and closes the API overflow failure mode entirely.

## Evidence

- Live test: `get_file` on `BaseViewCreator.razor` (85,976 bytes) → Claude API 500
- `get_file` on `ManageViews.razor` (36,310 bytes) → succeeded inline
- Failure threshold is approximately 40–50KB in practice
- `candidateFilePath` from `replace_span_in_file` response: `C:\VSCodeProjects\MonitorBaseClaude\Working\SchemaStudioWebViewer - Copy_3481fe2b33a8\Components\Pages\BaseViewCreator\BaseViewCreator.razor`
- Related session: `monitor-20260522035417-6f84fd5593b740f3a`

## Notes

The Working copy persists across sessions by design (hash-match wins). So once `initialize_candidate` has been called once for a file, subsequent sessions can Read from `candidateFilePath` immediately without re-initializing — the file is already there if the watched source has not changed.

Restoring the original `--refresh` path (if it existed) would also solve this for the cold-start case, but the explicit MCP tool is more composable and visible to the model.
