# Claude Live Test Reports

This folder is Claude's source-controlled report lane.

Claude may write findings, bug reports, and doc suggestions here when live MCP testing exposes a problem or ambiguity. These files are input for Codex/operator review. They are not product workflow authority by themselves.

## Current Assignment

Pull latest `main`, rebuild MonitorBaseClaude, restart the VS Code MCP binding if needed, and confirm the live Monitor MCP tool list no longer contains any `*_old` tools.

Configure Claude Code statusline so MonitorBaseClaude can display Claude session usage in `View -> Claude Info`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File C:\VSCodeProjects\MonitorBaseClaude\Tools\ClaudeStatusLine\Write-ClaudeStatusSnapshot.ps1
```

After configuration, run any small Claude Code prompt and confirm the Monitor app can refresh `View -> Claude Info` from:

```text
Working\History\ClaudeCode\statusline-latest.json
```

Run a compact workflow-surface check:

```text
get_tool_manifest
get_staging_guide
get_workflow_status
start_monitor_session
get_source_map on a known C# file
get_source_map(scope: "namespace", namespaceName: "<known watched namespace>")
```

Confirm the docs and tool descriptions teach the current workflow and current tool surface:

```text
compose Working candidate with submit_file / submit_symbol / add_* / remove_* tools
stage_candidate_for_review
launch_staged_diff
record_diff_decision
```

Specific regression checks requested after Codex fixes:

```text
1. Removed overloaded constructors should appear in stage_candidate_for_review.serverDerivedMetadata.symbolsRemoved.
2. Overlay-error launch should show the Host force-review gate. "Force WinMerge Review" should produce validationGateDecision: force_review and open WinMerge. "Cancel Review" should produce cancel_for_fix and not open WinMerge.
3. Source-map selector results should include parameter names, stable-key-only suggested get_symbol calls, and namespace-neighborhood suggested get_source_map calls derived from using directives.
```

Real-form partial-class test requested:

```text
Using DBV2 or another watched WinForms project with real partial forms, pick one form and discover its partial layout from tools rather than from operator hints.

Task shape:
- Add a trivial button or similar UI entry point.
- Add or reuse a string/state field.
- Add a click handler that opens a MessageBox using that field.
- Prefer the safest composition path after inspecting the form shape. If Designer.cs is generated/designer-shaped, consider whether runtime setup in the main partial is safer than editing designer layout code.
- Keep all touched files in one monitor session so overlay validation sees sibling partial candidates together.
- Launch/review serially and record the decisions.

Report:
- Which discovery calls identified the partial files and insertion points.
- Whether any candidate was temporarily invalid before the sibling partial candidate existed.
- Whether the final staged overlay compiled cleanly.
- Whether grep/text search was needed; if yes, explain the diagnostic or missed-callsite reason.
```

Do not edit product code for this assignment. Test with disposable or already-designated smoke-test files only. If anything is confusing or broken, file a compact dated note or bug report in this folder.

## Folder Rules

- Use this folder for reports that should be reviewed from GitHub.
- Use local `.claude-local/` for private restart memory, scratch notes, and local VS Code/MCP binding workarounds.
- Do not put product source changes here.
- Do not edit official docs from this lane unless the operator explicitly asks.
- Do not treat old reports here as current workflow instructions.
- Do not append new findings to legacy rollup files such as `STATUS.md`, `FINDINGS.md`, `SCRATCH.md`, or `SESSION_RESUME.md`. Treat them as historical.
- Move any current local restart notes or scratch findings that need Codex/operator review into new dated files in this folder, then clear or archive the local copies yourself.

## Required Cleanup Pass

At the start of a live-test reporting session, inspect local scratch/restart notes and publish only the useful current items into dated report files here.

Run from the repository root:

```powershell
git status --short --branch
Get-ChildItem .\CLAUDE_Live_Tests
Get-ChildItem .\.claude-local -Recurse -File -ErrorAction SilentlyContinue
```

For each local note that still matters, create one dated report:

```powershell
New-Item -ItemType File .\CLAUDE_Live_Tests\YYYYMMDD-short-topic.md
notepad .\CLAUDE_Live_Tests\YYYYMMDD-short-topic.md
```

After the useful content has been moved, remove or locally archive the stale scratch copy:

```powershell
Remove-Item .\.claude-local\scratch-file-name.md
git status --short
```

Only commit reports that should be reviewed. Do not preserve old historical noise just because it exists.

## Naming

Every report file name must start with a local date stamp:

```text
YYYYMMDD-short-topic.md
```

Examples:

```text
20260518-finding-21-find-references-type-position.md
20260518-doc-suggestion-source-map-first.md
20260518-bridge-restart-binding-notes.md
```

Use ASCII lowercase words separated by hyphens after the date. Keep one finding or suggestion per file when possible.

## Required Header

Each report starts with this YAML header:

```yaml
---
status: new
type: finding
created: YYYY-MM-DD
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---
```

Allowed `type` values:

```text
finding
bug
doc-suggestion
test-result
restart-note
```

Allowed `status` values:

```text
new
needs-details
accepted
rejected
fixed
archived
```

`processed: false` means Codex/operator has not triaged it yet.

When Codex/operator triages a report, update the header instead of renaming the file:

```yaml
status: fixed
processed: true
processedBy: Codex
processedAt: 2026-05-18
resolution: Implemented list_session_staged_records and source-map initializer signatures.
resolutionCommit: c95c313
```

## Report Body Template

Use this shape for bugs/findings:

```markdown
## Summary

One short paragraph.

## Repro

1. Exact setup step.
2. Exact tool call.
3. Exact observed result.

## Expected

What should have happened.

## Actual

What happened instead.

## Evidence

- Tool name:
- Arguments:
- Result JSON path or pasted compact excerpt:
- Source file path:
- Related session id:

## Notes

Anything uncertain or operator-facing.
```

For Finding 21 specifically, include the exact Roslyn tool call, symbol/type name, file path, expected reference location, actual JSON, and whether `search_symbols` or `get_source_map` found what `find_references` missed.

## Legacy In-Folder Files (Pre-2026-05-18)

`STATUS.md`, `FINDINGS.md`, `SCRATCH.md`, `SESSION_RESUME.md`, and the `Pass*` files predate the date-stamped per-report layout. They are kept as-is for history. New findings and bug reports should use the `YYYYMMDD-short-topic.md` naming with the YAML header above.
