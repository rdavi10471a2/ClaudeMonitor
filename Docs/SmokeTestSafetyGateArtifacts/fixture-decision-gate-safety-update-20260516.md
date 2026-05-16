# Scripted Tool Smoke Test Summary

Generated: 2026-05-16T07:50:36.6579240-05:00

## Fixture Status

Tool: `get_monitor_status`
Question: Verify the Tool Server is pointed at the disposable fixture solution.
Error: `False`

```text
{
  "uiRoot": "C:\\VSCodeProjects\\MonitorBaseClaude",
  "mcpServerRoot": "C:\\VSCodeProjects\\MonitorBaseClaude\\MonitorBaseClaude.McpServer",
  "legacyMonitorRoot": "C:\\VSCodeProjects\\ClaudeMonitor\\Monitor",
  "watchedSolutionPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_075034667\\Schema Studio.sln",
  "watchedProjectFolder": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_075034667",
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
  "watchedProjectAlias": "20260516_075034667",
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
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_075034667\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_075034667_4db38c5cd269\\Data\\20260516_075035205_submit_file_BaseTableRepository_b8047619.cs",
  "stagedRecordId": "20260516_075035205_submit_file_BaseTableRepository_b8047619",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_075035205_submit_file_BaseTableRepository_b8047619.json",
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "serverDerivedMetadata": {
    "symbolsAdded": [],
    "symbolsRemoved": [],
    "usingsAdded": [],
    "usingsRemoved": [],
    "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_075034667_4db38c5cd269\\Data\\20260516_075035205_submit_file_BaseTableRepository_b8047619.cs"
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

## Reject Syntax Error Candidate

Tool: `submit_file`
Question: Verify malformed C# is rejected before any staged record is created.
Error: `True`

```text
An error occurred invoking 'submit_file'.
```

## Stage Clean Accept

Tool: `submit_file`
Question: Stage decision-gate candidate for Clean Accept.
Error: `False`

```text
{
  "status": "staged",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_075034667\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_075034667_4db38c5cd269\\Data\\20260516_075035800_submit_file_BaseTableRepository_23124876.cs",
  "stagedRecordId": "20260516_075035800_submit_file_BaseTableRepository_23124876",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_075035800_submit_file_BaseTableRepository_23124876.json",
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "b6630747222891e135e61f85b33142df3dcaddaae7f549c767dd58ae52847b77",
  "serverDerivedMetadata": {
    "symbolsAdded": [],
    "symbolsRemoved": [],
    "usingsAdded": [],
    "usingsRemoved": [],
    "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_075034667_4db38c5cd269\\Data\\20260516_075035800_submit_file_BaseTableRepository_23124876.cs"
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
Question: Record accepted for 20260516_075035800_submit_file_BaseTableRepository_23124876 and classify by vote-plus-hash agreement.
Error: `False`

```text
{
  "stagedRecordId": "20260516_075035800_submit_file_BaseTableRepository_23124876",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_075034667\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_075035800_submit_file_BaseTableRepository_23124876.json",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_075034667_4db38c5cd269\\Data\\20260516_075035800_submit_file_BaseTableRepository_23124876.cs",
  "operatorDecision": "accepted",
  "classification": "accepted",
  "decisionMatchesClassification": true,
  "blocksFurtherEdits": false,
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "b6630747222891e135e61f85b33142df3dcaddaae7f549c767dd58ae52847b77",
  "currentHash": "b6630747222891e135e61f85b33142df3dcaddaae7f549c767dd58ae52847b77",
  "queueStatus": "accepted",
  "note": "Decision gate fixture smoke.",
  "decidedAt": "2026-05-16T12:50:35.9760758\u002B00:00",
  "decisionRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Decisions\\20260516\\20260516_075035800_submit_file_BaseTableRepository_23124876_075035976_accepted.json"
}
```

## Stage Clean Reject

Tool: `submit_file`
Question: Stage decision-gate candidate for Clean Reject.
Error: `False`

```text
{
  "status": "staged",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_075034667\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_075034667_4db38c5cd269\\Data\\20260516_075036000_submit_file_BaseTableRepository_b4dfb423.cs",
  "stagedRecordId": "20260516_075036000_submit_file_BaseTableRepository_b4dfb423",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_075036000_submit_file_BaseTableRepository_b4dfb423.json",
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "4b9664d00b7c86c093f84b4a22d40f2e1905ccc9fa2f641eeb10bf0bf0052a87",
  "serverDerivedMetadata": {
    "symbolsAdded": [],
    "symbolsRemoved": [],
    "usingsAdded": [],
    "usingsRemoved": [],
    "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_075034667_4db38c5cd269\\Data\\20260516_075036000_submit_file_BaseTableRepository_b4dfb423.cs"
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
Question: Record rejected for 20260516_075036000_submit_file_BaseTableRepository_b4dfb423 and classify by vote-plus-hash agreement.
Error: `False`

```text
{
  "stagedRecordId": "20260516_075036000_submit_file_BaseTableRepository_b4dfb423",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_075034667\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_075036000_submit_file_BaseTableRepository_b4dfb423.json",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_075034667_4db38c5cd269\\Data\\20260516_075036000_submit_file_BaseTableRepository_b4dfb423.cs",
  "operatorDecision": "rejected",
  "classification": "rejected",
  "decisionMatchesClassification": true,
  "blocksFurtherEdits": false,
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "4b9664d00b7c86c093f84b4a22d40f2e1905ccc9fa2f641eeb10bf0bf0052a87",
  "currentHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "queueStatus": "rejected",
  "note": "Decision gate fixture smoke.",
  "decidedAt": "2026-05-16T12:50:36.1258514\u002B00:00",
  "decisionRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Decisions\\20260516\\20260516_075036000_submit_file_BaseTableRepository_b4dfb423_075036125_rejected.json"
}
```

## Stage Accept Not Applied

Tool: `submit_file`
Question: Stage decision-gate candidate for Accept Not Applied.
Error: `False`

```text
{
  "status": "staged",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_075034667\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_075034667_4db38c5cd269\\Data\\20260516_075036134_submit_file_BaseTableRepository_7b2962af.cs",
  "stagedRecordId": "20260516_075036134_submit_file_BaseTableRepository_7b2962af",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_075036134_submit_file_BaseTableRepository_7b2962af.json",
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "b2224c8952175d902c521d81ebbeceeaaeb16d5c57075ab3d83b1312e3330012",
  "serverDerivedMetadata": {
    "symbolsAdded": [],
    "symbolsRemoved": [],
    "usingsAdded": [],
    "usingsRemoved": [],
    "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_075034667_4db38c5cd269\\Data\\20260516_075036134_submit_file_BaseTableRepository_7b2962af.cs"
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
Question: Record accepted for 20260516_075036134_submit_file_BaseTableRepository_7b2962af and classify by vote-plus-hash agreement.
Error: `False`

```text
{
  "stagedRecordId": "20260516_075036134_submit_file_BaseTableRepository_7b2962af",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_075034667\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_075036134_submit_file_BaseTableRepository_7b2962af.json",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_075034667_4db38c5cd269\\Data\\20260516_075036134_submit_file_BaseTableRepository_7b2962af.cs",
  "operatorDecision": "accepted",
  "classification": "dirty-unexpected",
  "decisionMatchesClassification": false,
  "blocksFurtherEdits": true,
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "b2224c8952175d902c521d81ebbeceeaaeb16d5c57075ab3d83b1312e3330012",
  "currentHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "queueStatus": "blocked-dirty-unexpected",
  "note": "Decision gate fixture smoke.",
  "decidedAt": "2026-05-16T12:50:36.2757689\u002B00:00",
  "decisionRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Decisions\\20260516\\20260516_075036134_submit_file_BaseTableRepository_7b2962af_075036275_dirty-unexpected.json"
}
```

## Stage Blocked After Accept Not Applied

Tool: `submit_file`
Question: Verify dirty-unexpected blocks additional staging until explicit recovery.
Error: `True`

```text
An error occurred invoking 'submit_file'.
```

## Recover After Accept Not Applied

Tool: `refresh_file`
Question: Refresh fixture source to recover from dirty-unexpected after Host/Operator inspection.
Error: `False`

```text
{
  "status": "refreshed",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_075034667\\Data\\BaseTableRepository.cs",
  "watchedProjectFolder": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_075034667",
  "observedRootKey": "20260516_075034667_4db38c5cd269",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "workingFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\20260516_075034667_4db38c5cd269\\Data\\BaseTableRepository.cs",
  "refreshStatePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\.state\\20260516_075034667_4db38c5cd269\\Data\\BaseTableRepository.cs.refresh.state"
}
```

## Stage Reject After Save

Tool: `submit_file`
Question: Stage decision-gate candidate for Reject After Save.
Error: `False`

```text
{
  "status": "staged",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_075034667\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_075034667_4db38c5cd269\\Data\\20260516_075036318_submit_file_BaseTableRepository_9b300d8c.cs",
  "stagedRecordId": "20260516_075036318_submit_file_BaseTableRepository_9b300d8c",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_075036318_submit_file_BaseTableRepository_9b300d8c.json",
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "620bd8eb75b86e3f19f4a3ddd24cba69418e7327b93cb29b4b5e669b39b474aa",
  "serverDerivedMetadata": {
    "symbolsAdded": [],
    "symbolsRemoved": [],
    "usingsAdded": [],
    "usingsRemoved": [],
    "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_075034667_4db38c5cd269\\Data\\20260516_075036318_submit_file_BaseTableRepository_9b300d8c.cs"
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
Question: Record rejected for 20260516_075036318_submit_file_BaseTableRepository_9b300d8c and classify by vote-plus-hash agreement.
Error: `False`

```text
{
  "stagedRecordId": "20260516_075036318_submit_file_BaseTableRepository_9b300d8c",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_075034667\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_075036318_submit_file_BaseTableRepository_9b300d8c.json",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_075034667_4db38c5cd269\\Data\\20260516_075036318_submit_file_BaseTableRepository_9b300d8c.cs",
  "operatorDecision": "rejected",
  "classification": "dirty-unexpected",
  "decisionMatchesClassification": false,
  "blocksFurtherEdits": true,
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "620bd8eb75b86e3f19f4a3ddd24cba69418e7327b93cb29b4b5e669b39b474aa",
  "currentHash": "620bd8eb75b86e3f19f4a3ddd24cba69418e7327b93cb29b4b5e669b39b474aa",
  "queueStatus": "blocked-dirty-unexpected",
  "note": "Decision gate fixture smoke.",
  "decidedAt": "2026-05-16T12:50:36.4545062\u002B00:00",
  "decisionRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Decisions\\20260516\\20260516_075036318_submit_file_BaseTableRepository_9b300d8c_075036454_dirty-unexpected.json"
}
```

## Recover After Reject After Save

Tool: `refresh_file`
Question: Refresh fixture source to recover from dirty-unexpected after Host/Operator inspection.
Error: `False`

```text
{
  "status": "refreshed",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_075034667\\Data\\BaseTableRepository.cs",
  "watchedProjectFolder": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_075034667",
  "observedRootKey": "20260516_075034667_4db38c5cd269",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "workingFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\20260516_075034667_4db38c5cd269\\Data\\BaseTableRepository.cs",
  "refreshStatePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\.state\\20260516_075034667_4db38c5cd269\\Data\\BaseTableRepository.cs.refresh.state"
}
```

## Stage Dirty External Edit

Tool: `submit_file`
Question: Stage decision-gate candidate for Dirty External Edit.
Error: `False`

```text
{
  "status": "staged",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_075034667\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_075034667_4db38c5cd269\\Data\\20260516_075036472_submit_file_BaseTableRepository_8a854c95.cs",
  "stagedRecordId": "20260516_075036472_submit_file_BaseTableRepository_8a854c95",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_075036472_submit_file_BaseTableRepository_8a854c95.json",
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "b031f1602d1cf3e32cc7623fe6911a82af7c8feb2621ac6233cc0db3489188df",
  "serverDerivedMetadata": {
    "symbolsAdded": [],
    "symbolsRemoved": [],
    "usingsAdded": [],
    "usingsRemoved": [],
    "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_075034667_4db38c5cd269\\Data\\20260516_075036472_submit_file_BaseTableRepository_8a854c95.cs"
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
Question: Record rejected for 20260516_075036472_submit_file_BaseTableRepository_8a854c95 and classify by vote-plus-hash agreement.
Error: `False`

```text
{
  "stagedRecordId": "20260516_075036472_submit_file_BaseTableRepository_8a854c95",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_075034667\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_075036472_submit_file_BaseTableRepository_8a854c95.json",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_075034667_4db38c5cd269\\Data\\20260516_075036472_submit_file_BaseTableRepository_8a854c95.cs",
  "operatorDecision": "rejected",
  "classification": "dirty-unexpected",
  "decisionMatchesClassification": false,
  "blocksFurtherEdits": true,
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "b031f1602d1cf3e32cc7623fe6911a82af7c8feb2621ac6233cc0db3489188df",
  "currentHash": "73fc56d0027aa773abf8fad71a22b636e09e947b01370ff09692e4f52ec6719a",
  "queueStatus": "blocked-dirty-unexpected",
  "note": "Decision gate fixture smoke.",
  "decidedAt": "2026-05-16T12:50:36.6402755\u002B00:00",
  "decisionRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Decisions\\20260516\\20260516_075036472_submit_file_BaseTableRepository_8a854c95_075036640_dirty-unexpected.json"
}
```

## Stage Blocked After Dirty External Edit

Tool: `submit_file`
Question: Verify dirty-unexpected blocks additional staging until explicit recovery.
Error: `True`

```text
An error occurred invoking 'submit_file'.
```
