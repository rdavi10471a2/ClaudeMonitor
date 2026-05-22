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

`refresh_file` copies the watched source into the Working folder and writes `.refresh.state`, but does not clear or update the `.candidate.json` state file. When a prior session's candidate has a baseline hash that no longer matches the current watched source (e.g., after a WinMerge accept changes the source), subsequent `replace_span_in_file` calls on the same file throw `candidate-baseline-stale` and fail. The Working copy is current (correctly refreshed) but the stale `.candidate.json` blocks all new edits.

## Repro

1. Edit a file, stage, and accept in WinMerge. Source file now has new content; old `.candidate.json` has the pre-accept baseline hash.
2. In a new session, call `refresh_file` on the same file. Returns `status: refreshed` — Working copy is correct.
3. Call `replace_span_in_file`. Server finds old `.candidate.json`, calls `EnsureCandidateBaselineIsCurrent`, detects hash mismatch, throws `candidate-baseline-stale`. Edit fails.
4. Workaround: manually delete the `.candidate.json` file at `Working\.state\Candidates\{observedRootKey}\{relativeSourcePath}.candidate.json`.

## Expected

`refresh_file` should detect that the existing `.candidate.json` baseline hash no longer matches the current watched source and either:
- Delete the stale `.candidate.json`, allowing `EnsureCandidateInitialized` to create a fresh one on the next edit, OR
- Re-initialize the `.candidate.json` with the refreshed source hash as the new baseline.

## Actual

`refresh_file` writes `.refresh.state` only. The stale `.candidate.json` is left untouched. Any subsequent candidate composition tool call on the same file fails with `candidate-baseline-stale` until the file is manually deleted.

## Evidence

- Live test session: `monitor-20260522042014-b9b6a9d36aa74567a`
- `refresh_file` response: `status: refreshed` — no mention of candidate state
- `replace_span_in_file` response: `An error occurred invoking 'replace_span_in_file'` (generic MCP error wrapping the internal `InvalidOperationException`)
- Stale candidate path: `Working\.state\Candidates\SchemaStudioWebViewer - Copy_3481fe2b33a8\Components\Pages\BaseViewCreator\BaseViewCreator.razor.candidate.json`
- Fix: deleted the `.candidate.json` manually → `replace_span_in_file` succeeded immediately

## Notes

The generic `An error occurred` MCP error surface makes this hard to diagnose without knowing the internal exception message. A follow-on improvement would be to surface the actual exception message in the MCP error response so the model can self-diagnose and take corrective action (delete the stale file) rather than requiring operator intervention.

Related: Finding 56 — `refresh_file` should be called before reading large files. If `refresh_file` also clears stale candidates, it becomes the single correct entry point for both "pre-read initialization" and "post-accept re-initialization."
