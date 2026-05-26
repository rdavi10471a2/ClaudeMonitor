---
status: new
type: bug
created: 2026-05-26
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---

## Summary

`list_watched_projects` returns folders enumerated under `MonitorClient:WatchedProjectsRoot`, which is unrelated to the actual observed solution. With the current `appsettings.json`, the tool returns five folders under `C:\VSCodeProjects` — none of which is the project being watched (`C:\SchemaStudioWebViewer - Copy`). The tool name implies authoritative "what is being watched" but reports a sibling-folder listing of a parent path that is not load-bearing for the workflow.

## Repro

1. `appsettings.json` configured as:
   - `MonitorClient:WatchedProjectsRoot` = `C:\VSCodeProjects`
   - `MonitorClient:WatchedSolutionPath` = `C:\SchemaStudioWebViewer - Copy\SchemaStudioWebViewer.sln`
   - `MonitorClient:CodeLensSolutionPath` = `C:\SchemaStudioWebViewer - Copy\SchemaStudioWebViewer.sln`
   - `WorkflowSettings:ObservedRoot` = `C:\SchemaStudioWebViewer - Copy`
2. Call `mcp__monitor-base-claude__list_watched_projects` with no arguments.
3. Tool returns five entries: `AIMonitorSchemaStudioWeb`, `AIWorkflowMonitor`, `CTEEditorSample`, `MonitorBaseClaude`, `_buildtmp` — all under `C:\VSCodeProjects`.

## Expected

The tool should make the actual watched target unambiguous. Options (any one would resolve the ambiguity):

- Return the `ObservedRoot` / `WatchedSolutionPath` as the authoritative entry, optionally with sibling folders flagged as non-watched.
- Mark the entry that matches `ObservedRoot` as the active watched project and the rest as `watched: false`.
- Rename the tool to reflect what it does today (e.g., `list_watched_projects_root_folders`) and add a separate `get_observed_root` / `get_watched_solution` tool for the authoritative answer.

## Actual

Tool returns a flat folder listing under `WatchedProjectsRoot` with no indication that none of them is the active observed solution. An agent asked "report watched root" will confidently answer with the wrong path unless it cross-checks `appsettings.json` manually.

## Evidence

- Tool name: `mcp__monitor-base-claude__list_watched_projects`
- Arguments: `{}`
- Result (compact):
  ```json
  [
    {"name":"AIMonitorSchemaStudioWeb","path":"C:\\VSCodeProjects\\AIMonitorSchemaStudioWeb","solutionFiles":[]},
    {"name":"AIWorkflowMonitor","path":"C:\\VSCodeProjects\\AIWorkflowMonitor","solutionFiles":[]},
    {"name":"CTEEditorSample","path":"C:\\VSCodeProjects\\CTEEditorSample","solutionFiles":[]},
    {"name":"MonitorBaseClaude","path":"C:\\VSCodeProjects\\MonitorBaseClaude","solutionFiles":[]},
    {"name":"_buildtmp","path":"C:\\VSCodeProjects\\_buildtmp","solutionFiles":[]}
  ]
  ```
- Source file path: `c:\VSCodeProjects\MonitorBaseClaude\appsettings.json`
- Related session id: none (read-only discovery call)

Note that every `solutionFiles` array is empty even though the actual watched solution (`SchemaStudioWebViewer.sln`) lives outside `WatchedProjectsRoot` — confirming the tool is scanning a path that has no relationship to the active observed solution.

## Notes

- The tool description ("List watched project folders under the configured watched-projects root.") is technically accurate but the tool name overpromises. Either the description needs to be the visible truth (rename the tool) or the implementation needs to reflect the name (point at `ObservedRoot`/`WatchedSolutionPath`).
- This trips up any agent doing pre-flight orientation. The Solution Index tools, `get_workflow_status`, and staging surface all key off `ObservedRoot`, so `list_watched_projects` is the odd one out.
- Suggest also surfacing `ObservedRoot` and `WatchedSolutionPath` in `get_workflow_status` or `get_tool_manifest` so the agent has a single authoritative answer without parsing `appsettings.json`.
