# MonitorBaseClaude Mini Skills

These cards are short, model-facing reminders. They should stay small enough to paste into a prompt or load as focused context.

Use the long docs for rationale and fixture evidence:

- `Docs/AgentToolCallPlaybook.md`
- `Docs/OracleFormattingFixtureCorpus.md`
- `Docs/CodexMcpSurgeryDrill.md`

## Cards

- `MonitorBaseClaudeSkillPack.md`: trimmed master prompt and card index.
- `SkillRouter.md`: choose the smallest relevant card instead of loading the whole doctrine.
- `RoslynFirstNavigation.md`: semantic discovery before grep/text search.
- `SystemMonitorStaging.md`: watched-source staging and decision flow.
- `SessionOverlayValidation.md`: multi-file session staging and overlay compile behavior.
- `ReviewQueueAndGates.md`: WinMerge, overlay gate, queue stop/unblock.
- `FormattingOracle.md`: insertion/replacement/removal layout rules.
- `AsyncPropagation.md`: async/signature caller propagation.
- `PartialClassRefactor.md`: human-guided companion partial extraction.
- `TroubleshootingDashboard.md`: reading live dashboard traffic.

## Layering

- Tool descriptions answer: how do I call this tool right now?
- Mini skills answer: what operating mode am I in?
- Long docs and fixture corpus answer: why does this rule exist, and what proved it?

Start with `SkillRouter.md`, then load only the cards required by the active task.
