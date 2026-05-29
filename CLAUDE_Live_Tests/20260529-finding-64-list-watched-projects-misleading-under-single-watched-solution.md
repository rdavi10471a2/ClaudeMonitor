---
status: new
type: finding
created: 2026-05-29
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---

## Summary

After the "Unify Claude MCP bridge configuration" change, the live config now resolves a single authoritative watched solution. `get_workflow_status` and `get_monitor_status` both report one `watchedSolutionPath`. But `list_watched_projects` still returns a directory enumeration of the watched-projects *root* (`C:\VSCodeProjects`), and every entry carries an empty `solutionFiles` array. The result is easy to misread as "these are the watched solutions," when in fact none of them is the active watched solution (which lives outside that root). The tool name and its current payload no longer match the single-watched-solution model and need an update.

## Repro

1. Confirm bindings are live (full `mcp__monitor-base-claude__*` surface bound).
2. Call `get_monitor_status` (or `get_workflow_status`).
3. Call `list_watched_projects`.
4. Compare the `watchedSolutionPath` from step 2 against the entries from step 3.

## Expected

A consumer reading `list_watched_projects` should be able to tell what is actually being watched. Under a single-watched-solution config, the tool should either:
- reflect the one authoritative watched solution (the same path `get_workflow_status` reports), or
- make clear that it is enumerating candidate project folders under a root, distinct from the active watched solution, so the two concepts are not conflated.

## Actual

`list_watched_projects` returned five folders under `C:\VSCodeProjects`, all with `solutionFiles: []`, none of which is the active watched solution:

```json
[
  {"name":"AIMonitorSchemaStudioWeb","path":"C:\\VSCodeProjects\\AIMonitorSchemaStudioWeb","solutionFiles":[]},
  {"name":"AIWorkflowMonitor","path":"C:\\VSCodeProjects\\AIWorkflowMonitor","solutionFiles":[]},
  {"name":"CTEEditorSample","path":"C:\\VSCodeProjects\\CTEEditorSample","solutionFiles":[]},
  {"name":"MonitorBaseClaude","path":"C:\\VSCodeProjects\\MonitorBaseClaude","solutionFiles":[]},
  {"name":"_buildtmp","path":"C:\\VSCodeProjects\\_buildtmp","solutionFiles":[]}]
```

Meanwhile the actual watched solution is elsewhere entirely:

```json
{"watchedSolutionPath":"C:\\SchemaStudioWebViewer V 1.1\\SchemaStudioWebViewer.sln","watchedProjectFolder":"C:\\SchemaStudioWebViewer V 1.1","watchedSolutionExists":true,"legacyMonitorRootExists":false}
```

So the tool's output and the authoritative config disagree on what "watched" means, and the empty `solutionFiles` arrays add a second misread (looks like "no solutions found anywhere").

## Evidence

- Tool name: `mcp__monitor-base-claude__list_watched_projects`
- Arguments: none
- Result JSON: five-folder enumeration above, all `solutionFiles: []`
- Cross-check tool: `mcp__monitor-base-claude__get_monitor_status` / `get_workflow_status`
- Cross-check result: single `watchedSolutionPath` = `C:\SchemaStudioWebViewer V 1.1\SchemaStudioWebViewer.sln`
- Related session: live binding confirmation session 2026-05-29

## Notes

This is filed as an observation, not a fix design. The two candidate directions above (reflect the single watched solution vs. clearly label root-folder enumeration as distinct) are framing for triage, not a chosen implementation. The empty `solutionFiles` arrays may be a separate sub-issue (root enumeration not probing for `.sln` files) and may or may not be related to the naming/semantics mismatch.
