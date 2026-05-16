# Scripted Tool Smoke Test Summary

Generated: 2026-05-16T07:19:35.2773553-05:00

## Razor Fixture Status

Tool: `get_monitor_status`
Question: Verify the Tool Server is pointed at the disposable Razor fixture solution.
Error: `False`

```text
{
  "uiRoot": "C:\\VSCodeProjects\\MonitorBaseClaude",
  "mcpServerRoot": "C:\\VSCodeProjects\\MonitorBaseClaude\\MonitorBaseClaude.McpServer",
  "legacyMonitorRoot": "C:\\VSCodeProjects\\ClaudeMonitor\\Monitor",
  "watchedSolutionPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\RazorShapeSmoke\\20260516_071934084\\Razor Fixture.sln",
  "watchedProjectFolder": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\RazorShapeSmoke\\20260516_071934084",
  "mcpServerRootExists": true,
  "legacyMonitorRootExists": true,
  "watchedSolutionExists": true
}
```

## Find Razor Files

Tool: `find_file`
Question: Verify the Tool Server can discover Razor files in the fixture.
Error: `False`

```text
[
  {
    "fullPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\RazorShapeSmoke\\20260516_071934084\\Components\\Pages\\Counter.razor",
    "relativePath": "Components\\Pages\\Counter.razor",
    "length": 337
  }
]
```

## Read Razor File

Tool: `get_file`
Question: Read the full Razor fixture file.
Error: `False`

```text
@page "/counter"

<PageTitle>Counter</PageTitle>

<h1>Counter</h1>

<button class="btn btn-primary" @onclick="IncrementCount">Click me</button>

@if (currentCount > 0)
{
    <p role="status">Current count: @currentCount</p>
}

@code {
    private int currentCount;

    private void IncrementCount()
    {
        currentCount++;
    }
}
```

## Outline Razor File

Tool: `get_file_outline`
Question: Verify Razor does not go through the C# symbol outline path yet.
Error: `False`

```text
{
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\RazorShapeSmoke\\20260516_071934084\\Components\\Pages\\Counter.razor",
  "relativeSourcePath": "Components\\Pages\\Counter.razor",
  "symbols": []
}
```

## Stage Razor Candidate

Tool: `submit_file`
Question: Stage a Razor full-file candidate and require Razor validation to be visibly pending.
Error: `False`

```text
{
  "status": "staged",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\RazorShapeSmoke\\20260516_071934084\\Components\\Pages\\Counter.razor",
  "relativeSourcePath": "Components\\Pages\\Counter.razor",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_071934084_f26b11eb0efe\\Components\\Pages\\20260516_071935237_submit_file_Counter_66656d87.razor",
  "stagedRecordId": "20260516_071935237_submit_file_Counter_66656d87",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_071935237_submit_file_Counter_66656d87.json",
  "originalHash": "cc110d1a1adfc18625483b96cb9a61e07ef2e84b84ad0296514a14147d4f1262",
  "stagedHash": "15e7ef5e01c92acf438cd50e1f088c5be3e6cdef197c9bae0b3c02cc874c6907",
  "serverDerivedMetadata": {
    "symbolsAdded": [],
    "symbolsRemoved": [],
    "usingsAdded": [],
    "usingsRemoved": [],
    "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_071934084_f26b11eb0efe\\Components\\Pages\\20260516_071935237_submit_file_Counter_66656d87.razor"
  },
  "syntaxValidation": {
    "hasErrors": false,
    "diagnostics": []
  },
  "overlayValidation": {
    "status": "razor-validation-pending",
    "hasErrors": false,
    "syntaxTreeCount": 0,
    "overlayFileCount": 0,
    "diagnostics": []
  },
  "diffRequested": false
}
```

## Accept Razor Candidate

Tool: `record_diff_decision`
Question: Verify Accept recognizes the Operator-saved Razor staged candidate and returns accepted by hash.
Error: `False`

```text
{
  "stagedRecordId": "20260516_071935237_submit_file_Counter_66656d87",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\RazorShapeSmoke\\20260516_071934084\\Components\\Pages\\Counter.razor",
  "relativeSourcePath": "Components\\Pages\\Counter.razor",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_071935237_submit_file_Counter_66656d87.json",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_071934084_f26b11eb0efe\\Components\\Pages\\20260516_071935237_submit_file_Counter_66656d87.razor",
  "operatorDecision": "accepted",
  "classification": "accepted",
  "decisionMatchesClassification": true,
  "blocksFurtherEdits": false,
  "originalHash": "cc110d1a1adfc18625483b96cb9a61e07ef2e84b84ad0296514a14147d4f1262",
  "stagedHash": "15e7ef5e01c92acf438cd50e1f088c5be3e6cdef197c9bae0b3c02cc874c6907",
  "currentHash": "15e7ef5e01c92acf438cd50e1f088c5be3e6cdef197c9bae0b3c02cc874c6907",
  "queueStatus": "accepted",
  "note": "Razor fixture smoke verifies Operator-saved staged candidate all-or-none.",
  "decidedAt": "2026-05-16T12:19:35.2730274\u002B00:00",
  "decisionRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Decisions\\20260516\\20260516_071935237_submit_file_Counter_66656d87_071935273_accepted.json"
}
```
