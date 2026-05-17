# Claude Minimal Review Pack

This pack is the minimal review context for a Claude client with no prior project knowledge. It includes the callable tool contract and the active skill cards Claude should review before attempting edits.

## Standard MCP Calls

Use these first:

```text
tools/list
get_monitor_status
get_tool_manifest
get_staging_guide
get_workflow_status
```

Do not use `get_smoke_test_catalog` for normal review or edit planning. Smoke tests are debug/maintainer tools.

## Files In This Pack

- `MONITOR_MCP_TOOL_MANIFEST.md`: tool contract, normal workflow, current/planned tool surface.
- `Skills/README.md`: skill folder index.
- `Skills/MonitorBaseClaudeSkillPack.md`: overall skill plan / trimmed master model.
- `Skills/SkillRouter.md`: entry point for choosing the smallest relevant skill card.
- `Skills/SystemMonitorStaging.md`: shortest safe-edit workflow.
- `Skills/SessionOverlayValidation.md`: coupled-file staging and overlay validation rules.
- `Skills/*.md`: active individual skill definition cards.

## Review Feedback Rule

Claude should file compact bug reports or doc suggestions. Codex owns merging accepted feedback into active docs and skill cards.

## Current Caveat

ReadSet/WriteSet planning has been retired from active guidance. The live workflow relies on session overlay validation: coupled files are staged together under one `sessionId`, validated together, and then reviewed one diff at a time.
