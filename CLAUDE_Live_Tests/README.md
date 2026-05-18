# Claude Live Test Reports

This folder is Claude's source-controlled report lane.

Claude may write findings, bug reports, and doc suggestions here when live MCP testing exposes a problem or ambiguity. These files are input for Codex/operator review. They are not product workflow authority by themselves.

## Folder Rules

- Use this folder for reports that should be reviewed from GitHub.
- Use local `.claude-local/` for private restart memory, scratch notes, and local VS Code/MCP binding workarounds.
- Do not put product source changes here.
- Do not edit official docs from this lane unless the operator explicitly asks.
- Do not treat old reports here as current workflow instructions.

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
