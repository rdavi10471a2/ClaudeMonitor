# Operator Workflow Rules

This file translates the relevant source monitor `AGENTS.md` rules into project documentation for MonitorBaseClaude.

## Current Roots

- Host root: `C:\VSCodeProjects\MonitorBaseClaude`
- Monitor Tool Server root: `C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.McpServer`
- Source implementation root: `C:\VSCodeProjects\ClaudeMonitor\Monitor`
- Watched solution: `C:\Schema Studio - DBV2\Schema Studio.sln`
- Watched project folder: `C:\Schema Studio - DBV2`

## State Ownership

Generated monitor state belongs under the Host root, not inside the watched project:

- `Working`
- `Working\History`
- `Working\Staged`
- `Working\Sessions`
- `Working\History\Ledgers`

## Source Edit Rules

When editing watched source files:

- Preserve existing `AIFileContext`, `FileVersion`, and meaningful `AIChange` attributes.
- Do not add routine process comments to source files.
- Put routine change summaries in monitor-owned history, ledger notes, or nearby component maps.
- For meaningful C# edits, bump the edited file's `FileVersion`.
- For new C# files, add `AIFileContext` and `FileVersion("1.0")`.
- Do not use C# top-level statements in generated source or samples.

## Diff Review Rules

- Never launch multiple GUI diff windows back-to-back.
- Multi-file changes must become an ordered review queue.
- Launch the first diff only, then wait for Operator decision.
- After a decision, the Tool Server verifies what landed before the next diff is released.
- For multi-file C# edits, staged files may be validated together as an overlay even while diffs are reviewed one at a time.

## Documentation Timing

Generate documentation after stability, not during active churn.

For large generated or frequently edited surfaces, maintain nearby markdown maps only during a documentation pass or direct map-update request.
