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

The Monitor Working candidate for a file is only created when the first candidate composition tool is called (`replace_span_in_file`, `submit_file`, `add_method`, etc.). For files that have never been worked on in any prior session, the Working copy does not exist before the first edit. This means the model cannot `Read` from `candidateFilePath` before its first edit — the current fallback is `get_file`, which for files above ~40KB causes a Claude API 500 (oversized tool result).

The `refresh_file` MCP tool already solves this. It copies a watched source file into the Working folder and records refresh state. The original CLI had a `--refresh-only` argument that mapped to exactly this tool. The issue is that `refresh_file` is not being used as a pre-step in the read-and-narrow workflow, and the instructions do not tell Claude to call it for large files before reading.

`compare_file` also has a `refreshIfMissing` flag that implicitly initializes the Working copy when absent.

## Repro

1. Watch a solution containing a large Razor file (> 40KB) with no prior Working candidate.
2. In a new session, call `get_file` on that file to read it before editing.
3. Monitor MCP server returns ~98K characters.
4. Claude API returns a 500 (oversized tool result). `status.claude.com` error shown in the host.

## Expected

Before calling `get_file` on any large file, the model should call `refresh_file(sourceFilePath)` to initialize the Working copy. The model then uses `Read(candidateFilePath, offset=N, limit=250)` in chunks — line numbers in cat-n format, directly usable in `replace_span_in_file`. `get_file` is not needed for large files at all.

## Fix Required

**Instruction gap, not a missing tool.** `refresh_file` already exists.

Add to CLAUDE.md Read-And-Narrow workflow: for Razor or text files where `find_file` reports `length > ~40000`, call `refresh_file` first to ensure the Working copy exists, then `Read(candidateFilePath)` in chunks rather than `get_file`. The `candidateFilePath` (Working path) is returned by `refresh_file` and is deterministic: `{uiRoot}\Working\{observedRootKey}\{relativeSourcePath}`.

The `--refresh-only` CLI origin confirms this was the intended pre-read initialization path. It should be restored as an explicit instruction step.

## Evidence

- Live test: `get_file` on `BaseViewCreator.razor` (85,976 bytes) → Claude API 500, status.claude.com error
- `get_file` on `ManageViews.razor` (36,310 bytes) → succeeded inline
- Tool manifest: `refresh_file` — "Copies a watched source file into the monitor-owned Working folder and records refresh state"
- Tool manifest: `--refresh-only` CLI arg → `refresh_file` (scaffolded)
- Tool manifest: `compare_file` `refreshIfMissing` flag — implicit Working copy init
- Related session: `monitor-20260522035417-6f84fd5593b740f3a`

## Notes

Once `refresh_file` has been called once for a file, the Working copy persists across sessions (hash-match wins). Subsequent sessions can Read from `candidateFilePath` immediately without re-calling `refresh_file` unless the watched source has changed since the last refresh.
