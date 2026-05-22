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

`EnsureCandidateBaselineIsCurrent` is called twice per edit operation in the `replace_span_in_file` (and other candidate-edit) path. Each call hashes the watched source file. The double hash is wasted I/O on every edit.

## Repro

1. Call any candidate edit tool (`replace_span_in_file`, `submit_file`, `add_method`, etc.) on an existing candidate.
2. Execution path: `ResolveCandidateEditBasePath` → `EnsureCandidateInitialized` → `EnsureCandidateBaselineIsCurrent` (call 1), then `WriteCandidateFile` → `EnsureCandidateInitialized` (returns existing state) → `EnsureCandidateBaselineIsCurrent` (call 2).

## Expected

`EnsureCandidateBaselineIsCurrent` is called once per edit operation.

## Actual

Called twice: once inside the `EnsureCandidateInitialized` early-return path (line ~734) and once explicitly at line ~686 in `WriteCandidateFile`. Both calls hash the watched source file.

## Evidence

- Source file: `MonitorBaseClaude.McpServer/MonitorWorkflowService.cs`
- `EnsureCandidateInitialized` (~line 729): calls `EnsureCandidateBaselineIsCurrent` when returning an existing state.
- `WriteCandidateFile` (~line 686): calls `EnsureCandidateBaselineIsCurrent` unconditionally after `EnsureCandidateInitialized` returns.
- `EnsureCandidateBaselineIsCurrent` (~line 821): reads `FileInfo` + `ComputeSha256` on the watched source each call — two file-system reads per invocation.

## Notes

Simplest fix: remove the explicit `EnsureCandidateBaselineIsCurrent` call from `WriteCandidateFile` and rely on the one already inside `EnsureCandidateInitialized`. The staleness check runs on the first initialize/resume and that is the authoritative moment; the second call adds no new protection since nothing between the two calls can change the watched source file (Monitor is single-writer on Working, and watched source is read-only from Monitor's perspective).

Impact is minor on small files but scales with file size and frequency of chained multi-span edits in a session.
