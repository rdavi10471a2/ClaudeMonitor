# Documentation Cleanup Plan

This plan keeps active agent guidance small while preserving old implementation history under `Docs/Archive`.

## Active Guidance Set

These files are the forward path:

- `MonitorBaseClaude.McpServer/MONITOR_MCP_TOOL_MANIFEST.md`
- `Docs/Skills/README.md`
- `Docs/Skills/SkillRouter.md`
- `Docs/Skills/MonitorBaseClaudeSkillPack.md`
- `Docs/Skills/SystemMonitorStaging.md`
- `Docs/Skills/SessionOverlayValidation.md`
- `Docs/Skills/RoslynFirstNavigation.md`
- `Docs/Skills/ReviewQueueAndGates.md`
- `Docs/Skills/FormattingOracle.md`
- `Docs/Skills/AsyncPropagation.md`
- `Docs/Skills/PartialClassRefactor.md`
- `Docs/Skills/TroubleshootingDashboard.md`

## Archive Rules

- Archive implementation logs once they no longer describe current behavior.
- Archive peer-review prompts and review packages after their findings have been accepted, rejected, or moved into the monitor-side working notes.
- Archive fixed bug reports after the fix and regression note are captured in current status, backlog, or smoke catalog.
- Keep debug-only smoke docs out of normal Claude workflow guidance.
- Do not archive the manifest or active skill cards without replacing their role first.
- Working-memory notes such as current status and todo belong under the monitor/tool doing the work, not in this product docs folder.

## First Archive Pass

- `Docs/Archive/Skills/WriteSetPlanning.md` preserves the retired ReadSet/WriteSet planning card.
- Review old package zips under `Docs` and move stale ones to `Docs/Archive/Packages`.
- Review long design/research docs and move historical-only files to `Docs/Archive/History`.
- Keep `Docs/McpServerImplementationBacklog.md` until its remaining tasks are migrated into the monitor-side working notes.

## Generated Export Rule

Generated review/export bundles are not canonical docs and should stay out of source control. The canonical project-facing guidance is:

- the live manifest
- active skill cards
- the shortest staging/session-overlay references

If an external/free-Claude bundle is needed later, generate it locally from those canonical files and do not commit the generated folder or zip.

## Claude Review Loop

- Claude should consume the canonical Markdown files in the project path and run workflow tests from GitHub.
- Claude should not rewrite active docs directly as part of review feedback.
- Claude should file compact bug reports or doc suggestions with:
  - the task attempted
  - the tool/docs page involved
  - expected behavior
  - actual confusing or unsafe behavior
  - minimal suggested wording or reproduction steps
- Codex owns merging accepted Claude feedback into active docs and skill cards.
- This keeps Claude read/review output separate from Codex doc-authoring churn.
