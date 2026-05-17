# CLAUDE Live Tests

Working notes from a Claude tester/config-helper pass on the MonitorBaseClaude MCP + skill workflow.

## Scope

- This folder is for Claude's own running notes and compact findings during live workflow testing.
- It is not normative for the project. Codex owns merging accepted feedback into `CLAUDE.md`, `MCP_CLIENT_TESTING.md`, the manifest, and the active skill cards.
- Active docs are off-limits from this folder unless an Operator explicitly authorizes a change.
- The watched testing environment (`C:\Schema Studio - DBV2\`) is MCP-only. Nothing here describes a direct edit of watched source.

## Files

- `STATUS.md`: rolling status log for the active test pass. What was done, what is next, what is blocked.
- `FINDINGS.md`: compact bug reports and doc suggestions, one section per pass. Codex merges accepted items into the active docs.
- `SCRATCH.md`: short-lived working notes. Safe to clear between passes.

## Compact Finding Format

```text
Title:
Severity: blocker | confusing | stale | suggestion
File/tool:
Observed:
Expected:
Minimal fix:
Evidence:
```

Max 5 findings per pass. Max 150 words per finding.

## Workflow Rules This Folder Operates Under

- Stay in tester/config-helper mode. File compact findings, do not rewrite broad docs.
- All watched-source changes go through Monitor MCP staging plus WinMerge review plus `record_diff_decision`. Never edit watched source directly.
- Prefer Roslyn Tooling over grep/text search for C# semantic discovery.
- Coupled multi-file edits stage under one monitor session before the first review launch.
- `get_smoke_test_catalog` is debug/maintainer-only and is not part of normal review or edit planning.
