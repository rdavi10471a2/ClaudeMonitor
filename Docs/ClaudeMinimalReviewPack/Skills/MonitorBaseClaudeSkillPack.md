# MonitorBaseClaude Skill Pack

This is the compact master prompt for using MonitorBaseClaude with Claude/Codex against C# watched source.

## Core Rule

Use Roslyn Tooling for semantic discovery and System Monitor for protected staged edits. Do not directly edit watched source.

## Load Order

1. Start with `SkillRouter.md`.
2. Load only the cards needed for the active task.
3. Use live MCP tool descriptions for exact argument names.
4. Use long docs only for rationale or fixture evidence.

## Required First Calls

```text
System Monitor: get_monitor_status
System Monitor: get_workflow_status
Roslyn Tooling: list_solutions
Roslyn Tooling: tools/list
```

## Card Index

- `RoslynFirstNavigation.md`: find symbols/references/callers before grep.
- `SystemMonitorStaging.md`: stage watched-source changes safely.
- `SessionOverlayValidation.md`: validate coupled staged files together.
- `ReviewQueueAndGates.md`: WinMerge, overlay gate, queue stop/unblock.
- `FormattingOracle.md`: placement, trivia, generated-file layout.
- `AsyncPropagation.md`: async/signature propagation through callers/contracts.
- `PartialClassRefactor.md`: human-guided companion partial extraction.
- `TroubleshootingDashboard.md`: verify live traffic and diagnose drift.

## Golden Path

```text
Roslyn semantic discovery
start_monitor_session for coupled work
System Monitor source maps/selectors
stage all coupled candidates with the same sessionId
review overlay validation
launch one WinMerge diff at a time
record each decision
stop on any blocked/not-launched result
```

## Stop Conditions

- Direct watched-source write temptation.
- Ambiguous symbol selector.
- Unknown Roslyn argument name.
- Overlay validation errors not explicitly force-reviewed.
- Review launch not started.
- Dirty-unexpected.
- Review-chain-blocked.
