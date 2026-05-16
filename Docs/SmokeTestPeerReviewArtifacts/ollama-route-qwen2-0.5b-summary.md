# Ollama Route-Only Smoke Test Summary

Generated: 2026-05-15T23:23:01.0073400-05:00

These tests ask Ollama to choose a tool from the real discovered MCP surface.
The expected server/tool is recorded in the test harness, not in the prompt sent to Ollama.

## Route: source structure

Prompt: Show me the structure of EditorSurface\ExplorerControl.cs.
Expected: `monitor-base-claude` / `get_source_map`
Decision: `error` `` ``
Resolved server: ``
Pass: `False`
Executable: `False`
Issue: `Model answered instead of choosing a tool.`

## Route: symbol body

Prompt: Show me the body of LoadTable in EditorSurface\EditorSurfaceControl.cs.
Expected: `monitor-base-claude` / `get_symbol`
Decision: `call_tool` `monitor-base-claude` `get_file_outline`
Resolved server: `monitor-base-claude`
Pass: `False`
Executable: `True`
Issue: `Expected monitor-base-claude/get_symbol.`

## Find Program.cs

Prompt: Find file Program.cs in the watched project.
Expected: `monitor-base-claude` / `find_file`
Decision: `answer` `` ``
Resolved server: ``
Pass: `False`
Executable: `False`
Issue: `Model answered instead of choosing a tool.`

## Read Program.cs

Prompt: Read Program.cs from the watched project and summarize what application starts.
Expected: `monitor-base-claude` / `get_file`
Decision: `call_tool` `monitor-base-claude` `find_file`
Resolved server: `monitor-base-claude`
Pass: `False`
Executable: `True`
Issue: `Expected monitor-base-claude/get_file.`

## Workflow status

Prompt: Inspect the current monitor workflow status and tell me whether WinMerge is available.
Expected: `monitor-base-claude` / `get_workflow_status`
Decision: `answer` `` ``
Resolved server: ``
Pass: `False`
Executable: `False`
Issue: `Model answered instead of choosing a tool.`

## Start session

Prompt: Start a Monitor Server session for investigating Program.cs.
Expected: `monitor-base-claude` / `start_monitor_session`
Decision: `answer` `` ``
Resolved server: ``
Pass: `False`
Executable: `False`
Issue: `Model answered instead of choosing a tool.`

## List sessions

Prompt: List Monitor Server sessions and identify the most recent session.
Expected: `monitor-base-claude` / `list_monitor_sessions`
Decision: `error` `` ``
Resolved server: ``
Pass: `False`
Executable: `False`
Issue: `Model answered instead of choosing a tool.`
