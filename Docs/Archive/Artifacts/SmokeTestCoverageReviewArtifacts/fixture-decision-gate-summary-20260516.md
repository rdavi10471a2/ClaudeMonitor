# Scripted Tool Smoke Test Summary

Generated: 2026-05-16T07:19:37.0065958-05:00

## Fixture Status

Tool: `get_monitor_status`
Question: Verify the Tool Server is pointed at the disposable fixture solution.
Error: `False`

```text
{
  "uiRoot": "C:\\VSCodeProjects\\MonitorBaseClaude",
  "mcpServerRoot": "C:\\VSCodeProjects\\MonitorBaseClaude\\MonitorBaseClaude.McpServer",
  "legacyMonitorRoot": "C:\\VSCodeProjects\\ClaudeMonitor\\Monitor",
  "watchedSolutionPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_071934084\\Schema Studio.sln",
  "watchedProjectFolder": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_071934084",
  "mcpServerRootExists": true,
  "legacyMonitorRootExists": true,
  "watchedSolutionExists": true
}
```

## Decision Gate Source Map

Tool: `get_source_map`
Question: Read fixture structure before decision-gate scenarios.
Error: `False`

```text
{
  "scope": "file",
  "mode": "selector",
  "modePurpose": "stable-symbol-selection",
  "requestedPath": "Data\\BaseTableRepository.cs",
  "watchedProjectAlias": "20260516_071934084",
  "fileCount": 1,
  "symbolCount": 2,
  "estimatedTokenProxy": 665,
  "budgetLimit": 25000,
  "wasTruncated": false,
  "suggestedNextCalls": [
    {
      "rank": 1,
      "tool": "get_symbol",
      "reason": "read-selected-symbol-body",
      "arguments": {
        "path": "Data\\BaseTableRepository.cs",
        "symbolSelectorJson": "{\u0022stableSymbolKey\u0022:\u0022Data/BaseTableRepository.cs::SchemaStudio.Data::BaseTableRepository::method::NormalizeName(string)\u0022,\u0022memberKind\u0022:\u0022method\u0022,\u0022containingNamespace\u0022:\u0022SchemaStudio.Data\u0022,\u0022containingType\u0022:\u0022BaseTableRepository\u0022,\u0022name\u0022:\u0022NormalizeName\u0022,\u0022parameterTypes\u0022:[\u0022string\u0022],\u0022arity\u0022:0}"
      }
    }
  ],
  "files": [
    {
      "relativeSourcePath": "Data\\BaseTableRepository.cs",
      "sha256": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
      "length": 413,
      "parseStatus": "ok",
      "diagnosticCount": 0,
      "usings": [
        "SchemaStudio.AIHelpers"
      ],
      "symbols": [
        {
          "kind": "class",
          "name": "BaseTableRepository",
          "stableSymbolKey": "Data/BaseTableRepository.cs::SchemaStudio.Data::::class::BaseTableRepository",
          "signature": "[AIFileContext(\u0022BaseTableRepository.cs\u0022, \u0022Fixture repository used by MonitorBaseClaude accept smoke tests.\u0022)] [FileVersion(\u00221.0\u0022)] internal sealed class BaseTableRepository { }",
          "namespace": "SchemaStudio.Data",
          "attributes": [
            {
              "name": "AIFileContext",
              "argumentsSummary": "\u0022BaseTableRepository.cs\u0022, \u0022Fixture repository used by MonitorBaseClaude accept smoke tests.\u0022"
            },
            {
              "name": "FileVersion",
              "argumentsSummary": "\u00221.0\u0022"
            }
          ],
          "startLine": 5,
          "endLine": 14,
          "hasDocumentation": false,
          "hasAttributes": true,
          "textHash": "1253b5e033bd9c3535985635095619e240347b84e6f81691694a0e546ded8fb9",
          "modifiers": [
            "internal",
            "sealed"
          ],
          "arity": 0,
          "isStatic": false,
          "isAsync": false,
          "isOverride": false,
          "isVirtual": false,
          "syntaxKind": "ClassDeclaration"
        },
        {
          "kind": "method",
          "name": "NormalizeName",
          "stableSymbolKey": "Data/BaseTableRepository.cs::SchemaStudio.Data::BaseTableRepository::method::NormalizeName(string)",
          "signature": "public string NormalizeName(string name);",
          "namespace": "SchemaStudio.Data",
          "containingType": "BaseTableRepository",
          "startLine": 9,
          "endLine": 13,
          "hasDocumentation": false,
          "hasAttributes": false,
          "textHash": "f7ac1e147ca2c944c35226b5a1cd0fef13e679a5ba82ec811db85ddcdb3777b0",
          "modifiers": [
            "public"
          ],
          "returnType": "string",
          "parameterTypes": [
            "string"
          ],
          "parameterNames": [
            "name"
          ],
          "arity": 0,
          "isStatic": false,
          "isAsync": false,
          "isOverride": false,
          "isVirtual": false,
          "syntaxKind": "MethodDeclaration"
        }
      ]
    }
  ]
}
```

## Stage No-Op Candidate

Tool: `submit_file`
Question: Verify an identical candidate is reported as no-op-staged and does not request a normal diff.
Error: `False`

```text
{
  "status": "no-op-staged",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_071934084\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_071934084_bd583d2ff4f2\\Data\\20260516_071935339_submit_file_BaseTableRepository_69a42ad2.cs",
  "stagedRecordId": "20260516_071935339_submit_file_BaseTableRepository_69a42ad2",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_071935339_submit_file_BaseTableRepository_69a42ad2.json",
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "serverDerivedMetadata": {
    "symbolsAdded": [],
    "symbolsRemoved": [],
    "usingsAdded": [],
    "usingsRemoved": [],
    "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_071934084_bd583d2ff4f2\\Data\\20260516_071935339_submit_file_BaseTableRepository_69a42ad2.cs"
  },
  "syntaxValidation": {
    "hasErrors": false,
    "diagnostics": []
  },
  "overlayValidation": {
    "status": "compiled",
    "hasErrors": false,
    "syntaxTreeCount": 7,
    "overlayFileCount": 1,
    "diagnostics": []
  },
  "diffRequested": false
}
```

## Stage Clean Accept

Tool: `submit_file`
Question: Stage decision-gate candidate for Clean Accept.
Error: `False`

```text
{
  "status": "staged",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_071934084\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_071934084_bd583d2ff4f2\\Data\\20260516_071936085_submit_file_BaseTableRepository_2b378781.cs",
  "stagedRecordId": "20260516_071936085_submit_file_BaseTableRepository_2b378781",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_071936085_submit_file_BaseTableRepository_2b378781.json",
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "b6630747222891e135e61f85b33142df3dcaddaae7f549c767dd58ae52847b77",
  "serverDerivedMetadata": {
    "symbolsAdded": [],
    "symbolsRemoved": [],
    "usingsAdded": [],
    "usingsRemoved": [],
    "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_071934084_bd583d2ff4f2\\Data\\20260516_071936085_submit_file_BaseTableRepository_2b378781.cs"
  },
  "syntaxValidation": {
    "hasErrors": false,
    "diagnostics": []
  },
  "overlayValidation": {
    "status": "compiled",
    "hasErrors": false,
    "syntaxTreeCount": 7,
    "overlayFileCount": 1,
    "diagnostics": []
  },
  "diffRequested": false
}
```

## Decision Clean Accept

Tool: `record_diff_decision`
Question: Record accepted for 20260516_071936085_submit_file_BaseTableRepository_2b378781 and classify by vote-plus-hash agreement.
Error: `False`

```text
{
  "stagedRecordId": "20260516_071936085_submit_file_BaseTableRepository_2b378781",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_071934084\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_071936085_submit_file_BaseTableRepository_2b378781.json",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_071934084_bd583d2ff4f2\\Data\\20260516_071936085_submit_file_BaseTableRepository_2b378781.cs",
  "operatorDecision": "accepted",
  "classification": "accepted",
  "decisionMatchesClassification": true,
  "blocksFurtherEdits": false,
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "b6630747222891e135e61f85b33142df3dcaddaae7f549c767dd58ae52847b77",
  "currentHash": "b6630747222891e135e61f85b33142df3dcaddaae7f549c767dd58ae52847b77",
  "queueStatus": "accepted",
  "note": "Decision gate fixture smoke.",
  "decidedAt": "2026-05-16T12:19:36.2635202\u002B00:00",
  "decisionRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Decisions\\20260516\\20260516_071936085_submit_file_BaseTableRepository_2b378781_071936263_accepted.json"
}
```

## Stage Clean Reject

Tool: `submit_file`
Question: Stage decision-gate candidate for Clean Reject.
Error: `False`

```text
{
  "status": "staged",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_071934084\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_071934084_bd583d2ff4f2\\Data\\20260516_071936270_submit_file_BaseTableRepository_9191ee61.cs",
  "stagedRecordId": "20260516_071936270_submit_file_BaseTableRepository_9191ee61",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_071936270_submit_file_BaseTableRepository_9191ee61.json",
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "4b9664d00b7c86c093f84b4a22d40f2e1905ccc9fa2f641eeb10bf0bf0052a87",
  "serverDerivedMetadata": {
    "symbolsAdded": [],
    "symbolsRemoved": [],
    "usingsAdded": [],
    "usingsRemoved": [],
    "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_071934084_bd583d2ff4f2\\Data\\20260516_071936270_submit_file_BaseTableRepository_9191ee61.cs"
  },
  "syntaxValidation": {
    "hasErrors": false,
    "diagnostics": []
  },
  "overlayValidation": {
    "status": "compiled",
    "hasErrors": false,
    "syntaxTreeCount": 7,
    "overlayFileCount": 1,
    "diagnostics": []
  },
  "diffRequested": false
}
```

## Decision Clean Reject

Tool: `record_diff_decision`
Question: Record rejected for 20260516_071936270_submit_file_BaseTableRepository_9191ee61 and classify by vote-plus-hash agreement.
Error: `False`

```text
{
  "stagedRecordId": "20260516_071936270_submit_file_BaseTableRepository_9191ee61",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_071934084\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_071936270_submit_file_BaseTableRepository_9191ee61.json",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_071934084_bd583d2ff4f2\\Data\\20260516_071936270_submit_file_BaseTableRepository_9191ee61.cs",
  "operatorDecision": "rejected",
  "classification": "rejected",
  "decisionMatchesClassification": true,
  "blocksFurtherEdits": false,
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "4b9664d00b7c86c093f84b4a22d40f2e1905ccc9fa2f641eeb10bf0bf0052a87",
  "currentHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "queueStatus": "rejected",
  "note": "Decision gate fixture smoke.",
  "decidedAt": "2026-05-16T12:19:36.4104018\u002B00:00",
  "decisionRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Decisions\\20260516\\20260516_071936270_submit_file_BaseTableRepository_9191ee61_071936410_rejected.json"
}
```

## Stage Accept Not Applied

Tool: `submit_file`
Question: Stage decision-gate candidate for Accept Not Applied.
Error: `False`

```text
{
  "status": "staged",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_071934084\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_071934084_bd583d2ff4f2\\Data\\20260516_071936413_submit_file_BaseTableRepository_7a804897.cs",
  "stagedRecordId": "20260516_071936413_submit_file_BaseTableRepository_7a804897",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_071936413_submit_file_BaseTableRepository_7a804897.json",
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "b2224c8952175d902c521d81ebbeceeaaeb16d5c57075ab3d83b1312e3330012",
  "serverDerivedMetadata": {
    "symbolsAdded": [],
    "symbolsRemoved": [],
    "usingsAdded": [],
    "usingsRemoved": [],
    "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_071934084_bd583d2ff4f2\\Data\\20260516_071936413_submit_file_BaseTableRepository_7a804897.cs"
  },
  "syntaxValidation": {
    "hasErrors": false,
    "diagnostics": []
  },
  "overlayValidation": {
    "status": "compiled",
    "hasErrors": false,
    "syntaxTreeCount": 7,
    "overlayFileCount": 1,
    "diagnostics": []
  },
  "diffRequested": false
}
```

## Decision Accept Not Applied

Tool: `record_diff_decision`
Question: Record accepted for 20260516_071936413_submit_file_BaseTableRepository_7a804897 and classify by vote-plus-hash agreement.
Error: `False`

```text
{
  "stagedRecordId": "20260516_071936413_submit_file_BaseTableRepository_7a804897",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_071934084\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_071936413_submit_file_BaseTableRepository_7a804897.json",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_071934084_bd583d2ff4f2\\Data\\20260516_071936413_submit_file_BaseTableRepository_7a804897.cs",
  "operatorDecision": "accepted",
  "classification": "dirty-unexpected",
  "decisionMatchesClassification": false,
  "blocksFurtherEdits": true,
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "b2224c8952175d902c521d81ebbeceeaaeb16d5c57075ab3d83b1312e3330012",
  "currentHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "queueStatus": "blocked-dirty-unexpected",
  "note": "Decision gate fixture smoke.",
  "decidedAt": "2026-05-16T12:19:36.6101836\u002B00:00",
  "decisionRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Decisions\\20260516\\20260516_071936413_submit_file_BaseTableRepository_7a804897_071936610_dirty-unexpected.json"
}
```

## Stage Reject After Save

Tool: `submit_file`
Question: Stage decision-gate candidate for Reject After Save.
Error: `False`

```text
{
  "status": "staged",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_071934084\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_071934084_bd583d2ff4f2\\Data\\20260516_071936616_submit_file_BaseTableRepository_4f1eec88.cs",
  "stagedRecordId": "20260516_071936616_submit_file_BaseTableRepository_4f1eec88",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_071936616_submit_file_BaseTableRepository_4f1eec88.json",
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "620bd8eb75b86e3f19f4a3ddd24cba69418e7327b93cb29b4b5e669b39b474aa",
  "serverDerivedMetadata": {
    "symbolsAdded": [],
    "symbolsRemoved": [],
    "usingsAdded": [],
    "usingsRemoved": [],
    "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_071934084_bd583d2ff4f2\\Data\\20260516_071936616_submit_file_BaseTableRepository_4f1eec88.cs"
  },
  "syntaxValidation": {
    "hasErrors": false,
    "diagnostics": []
  },
  "overlayValidation": {
    "status": "compiled",
    "hasErrors": false,
    "syntaxTreeCount": 7,
    "overlayFileCount": 1,
    "diagnostics": []
  },
  "diffRequested": false
}
```

## Decision Reject After Save

Tool: `record_diff_decision`
Question: Record rejected for 20260516_071936616_submit_file_BaseTableRepository_4f1eec88 and classify by vote-plus-hash agreement.
Error: `False`

```text
{
  "stagedRecordId": "20260516_071936616_submit_file_BaseTableRepository_4f1eec88",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_071934084\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_071936616_submit_file_BaseTableRepository_4f1eec88.json",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_071934084_bd583d2ff4f2\\Data\\20260516_071936616_submit_file_BaseTableRepository_4f1eec88.cs",
  "operatorDecision": "rejected",
  "classification": "dirty-unexpected",
  "decisionMatchesClassification": false,
  "blocksFurtherEdits": true,
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "620bd8eb75b86e3f19f4a3ddd24cba69418e7327b93cb29b4b5e669b39b474aa",
  "currentHash": "620bd8eb75b86e3f19f4a3ddd24cba69418e7327b93cb29b4b5e669b39b474aa",
  "queueStatus": "blocked-dirty-unexpected",
  "note": "Decision gate fixture smoke.",
  "decidedAt": "2026-05-16T12:19:36.7949601\u002B00:00",
  "decisionRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Decisions\\20260516\\20260516_071936616_submit_file_BaseTableRepository_4f1eec88_071936795_dirty-unexpected.json"
}
```

## Stage Dirty External Edit

Tool: `submit_file`
Question: Stage decision-gate candidate for Dirty External Edit.
Error: `False`

```text
{
  "status": "staged",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_071934084\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_071934084_bd583d2ff4f2\\Data\\20260516_071936800_submit_file_BaseTableRepository_99045b41.cs",
  "stagedRecordId": "20260516_071936800_submit_file_BaseTableRepository_99045b41",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_071936800_submit_file_BaseTableRepository_99045b41.json",
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "b031f1602d1cf3e32cc7623fe6911a82af7c8feb2621ac6233cc0db3489188df",
  "serverDerivedMetadata": {
    "symbolsAdded": [],
    "symbolsRemoved": [],
    "usingsAdded": [],
    "usingsRemoved": [],
    "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_071934084_bd583d2ff4f2\\Data\\20260516_071936800_submit_file_BaseTableRepository_99045b41.cs"
  },
  "syntaxValidation": {
    "hasErrors": false,
    "diagnostics": []
  },
  "overlayValidation": {
    "status": "compiled",
    "hasErrors": false,
    "syntaxTreeCount": 7,
    "overlayFileCount": 1,
    "diagnostics": []
  },
  "diffRequested": false
}
```

## Decision Dirty External Edit

Tool: `record_diff_decision`
Question: Record rejected for 20260516_071936800_submit_file_BaseTableRepository_99045b41 and classify by vote-plus-hash agreement.
Error: `False`

```text
{
  "stagedRecordId": "20260516_071936800_submit_file_BaseTableRepository_99045b41",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_071934084\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_071936800_submit_file_BaseTableRepository_99045b41.json",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_071934084_bd583d2ff4f2\\Data\\20260516_071936800_submit_file_BaseTableRepository_99045b41.cs",
  "operatorDecision": "rejected",
  "classification": "dirty-unexpected",
  "decisionMatchesClassification": false,
  "blocksFurtherEdits": true,
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "b031f1602d1cf3e32cc7623fe6911a82af7c8feb2621ac6233cc0db3489188df",
  "currentHash": "73fc56d0027aa773abf8fad71a22b636e09e947b01370ff09692e4f52ec6719a",
  "queueStatus": "blocked-dirty-unexpected",
  "note": "Decision gate fixture smoke.",
  "decidedAt": "2026-05-16T12:19:37.0034727\u002B00:00",
  "decisionRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Decisions\\20260516\\20260516_071936800_submit_file_BaseTableRepository_99045b41_071937003_dirty-unexpected.json"
}
```
