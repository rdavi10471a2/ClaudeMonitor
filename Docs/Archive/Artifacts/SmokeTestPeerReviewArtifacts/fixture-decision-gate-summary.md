# Scripted Tool Smoke Test Summary

Generated: 2026-05-15T23:22:11.4044615-05:00

## Fixture Status

Tool: `get_monitor_status`
Question: Verify the Tool Server is pointed at the disposable fixture solution.
Error: `False`

```text
{
  "uiRoot": "C:\\VSCodeProjects\\MonitorBaseClaude",
  "mcpServerRoot": "C:\\VSCodeProjects\\MonitorBaseClaude\\MonitorBaseClaude.McpServer",
  "legacyMonitorRoot": "C:\\VSCodeProjects\\ClaudeMonitor\\Monitor",
  "watchedSolutionPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260515_232208870\\Schema Studio.sln",
  "watchedProjectFolder": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260515_232208870",
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
  "requestedPath": "Data\\BaseTableRepository.cs",
  "watchedProjectAlias": "20260515_232208870",
  "watchedProjectFolder": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260515_232208870",
  "fileCount": 1,
  "symbolCount": 2,
  "files": [
    {
      "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260515_232208870\\Data\\BaseTableRepository.cs",
      "relativeSourcePath": "Data\\BaseTableRepository.cs",
      "sha256": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
      "length": 413,
      "parseStatus": "ok",
      "diagnosticCount": 0,
      "diagnosticsSummary": [],
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
          "baseTypes": [],
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
          "parameterTypes": [],
          "parameterNames": [],
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
          "baseTypes": [],
          "attributes": [],
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
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260515_232208870\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260515_232208870_e91a3c9d5602\\Data\\20260515_232209564_submit_file_BaseTableRepository_2e1fe2db.cs",
  "stagedRecordId": "20260515_232209564_submit_file_BaseTableRepository_2e1fe2db",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260515\\20260515_232209564_submit_file_BaseTableRepository_2e1fe2db.json",
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "serverDerivedMetadata": {
    "symbolsAdded": [],
    "symbolsRemoved": [],
    "usingsAdded": [],
    "usingsRemoved": [],
    "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260515_232208870_e91a3c9d5602\\Data\\20260515_232209564_submit_file_BaseTableRepository_2e1fe2db.cs"
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
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260515_232208870\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260515_232208870_e91a3c9d5602\\Data\\20260515_232210292_submit_file_BaseTableRepository_102478e7.cs",
  "stagedRecordId": "20260515_232210292_submit_file_BaseTableRepository_102478e7",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260515\\20260515_232210292_submit_file_BaseTableRepository_102478e7.json",
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "b6630747222891e135e61f85b33142df3dcaddaae7f549c767dd58ae52847b77",
  "serverDerivedMetadata": {
    "symbolsAdded": [],
    "symbolsRemoved": [],
    "usingsAdded": [],
    "usingsRemoved": [],
    "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260515_232208870_e91a3c9d5602\\Data\\20260515_232210292_submit_file_BaseTableRepository_102478e7.cs"
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
Question: Record accepted for 20260515_232210292_submit_file_BaseTableRepository_102478e7 and classify by vote-plus-hash agreement.
Error: `False`

```text
{
  "stagedRecordId": "20260515_232210292_submit_file_BaseTableRepository_102478e7",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260515_232208870\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260515\\20260515_232210292_submit_file_BaseTableRepository_102478e7.json",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260515_232208870_e91a3c9d5602\\Data\\20260515_232210292_submit_file_BaseTableRepository_102478e7.cs",
  "operatorDecision": "accepted",
  "classification": "accepted",
  "decisionMatchesClassification": true,
  "blocksFurtherEdits": false,
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "b6630747222891e135e61f85b33142df3dcaddaae7f549c767dd58ae52847b77",
  "currentHash": "b6630747222891e135e61f85b33142df3dcaddaae7f549c767dd58ae52847b77",
  "queueStatus": "accepted",
  "note": "Decision gate fixture smoke.",
  "decidedAt": "2026-05-16T04:22:10.5239162\u002B00:00",
  "decisionRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Decisions\\20260515\\20260515_232210292_submit_file_BaseTableRepository_102478e7_232210524_accepted.json"
}
```

## Stage Clean Reject

Tool: `submit_file`
Question: Stage decision-gate candidate for Clean Reject.
Error: `False`

```text
{
  "status": "staged",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260515_232208870\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260515_232208870_e91a3c9d5602\\Data\\20260515_232210534_submit_file_BaseTableRepository_cbd51347.cs",
  "stagedRecordId": "20260515_232210534_submit_file_BaseTableRepository_cbd51347",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260515\\20260515_232210534_submit_file_BaseTableRepository_cbd51347.json",
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "4b9664d00b7c86c093f84b4a22d40f2e1905ccc9fa2f641eeb10bf0bf0052a87",
  "serverDerivedMetadata": {
    "symbolsAdded": [],
    "symbolsRemoved": [],
    "usingsAdded": [],
    "usingsRemoved": [],
    "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260515_232208870_e91a3c9d5602\\Data\\20260515_232210534_submit_file_BaseTableRepository_cbd51347.cs"
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
Question: Record rejected for 20260515_232210534_submit_file_BaseTableRepository_cbd51347 and classify by vote-plus-hash agreement.
Error: `False`

```text
{
  "stagedRecordId": "20260515_232210534_submit_file_BaseTableRepository_cbd51347",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260515_232208870\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260515\\20260515_232210534_submit_file_BaseTableRepository_cbd51347.json",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260515_232208870_e91a3c9d5602\\Data\\20260515_232210534_submit_file_BaseTableRepository_cbd51347.cs",
  "operatorDecision": "rejected",
  "classification": "rejected",
  "decisionMatchesClassification": true,
  "blocksFurtherEdits": false,
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "4b9664d00b7c86c093f84b4a22d40f2e1905ccc9fa2f641eeb10bf0bf0052a87",
  "currentHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "queueStatus": "rejected",
  "note": "Decision gate fixture smoke.",
  "decidedAt": "2026-05-16T04:22:10.8100492\u002B00:00",
  "decisionRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Decisions\\20260515\\20260515_232210534_submit_file_BaseTableRepository_cbd51347_232210810_rejected.json"
}
```

## Stage Accept Not Applied

Tool: `submit_file`
Question: Stage decision-gate candidate for Accept Not Applied.
Error: `False`

```text
{
  "status": "staged",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260515_232208870\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260515_232208870_e91a3c9d5602\\Data\\20260515_232210813_submit_file_BaseTableRepository_7e8c8271.cs",
  "stagedRecordId": "20260515_232210813_submit_file_BaseTableRepository_7e8c8271",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260515\\20260515_232210813_submit_file_BaseTableRepository_7e8c8271.json",
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "b2224c8952175d902c521d81ebbeceeaaeb16d5c57075ab3d83b1312e3330012",
  "serverDerivedMetadata": {
    "symbolsAdded": [],
    "symbolsRemoved": [],
    "usingsAdded": [],
    "usingsRemoved": [],
    "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260515_232208870_e91a3c9d5602\\Data\\20260515_232210813_submit_file_BaseTableRepository_7e8c8271.cs"
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
Question: Record accepted for 20260515_232210813_submit_file_BaseTableRepository_7e8c8271 and classify by vote-plus-hash agreement.
Error: `False`

```text
{
  "stagedRecordId": "20260515_232210813_submit_file_BaseTableRepository_7e8c8271",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260515_232208870\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260515\\20260515_232210813_submit_file_BaseTableRepository_7e8c8271.json",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260515_232208870_e91a3c9d5602\\Data\\20260515_232210813_submit_file_BaseTableRepository_7e8c8271.cs",
  "operatorDecision": "accepted",
  "classification": "dirty-unexpected",
  "decisionMatchesClassification": false,
  "blocksFurtherEdits": true,
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "b2224c8952175d902c521d81ebbeceeaaeb16d5c57075ab3d83b1312e3330012",
  "currentHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "queueStatus": "blocked-dirty-unexpected",
  "note": "Decision gate fixture smoke.",
  "decidedAt": "2026-05-16T04:22:11.0171394\u002B00:00",
  "decisionRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Decisions\\20260515\\20260515_232210813_submit_file_BaseTableRepository_7e8c8271_232211017_dirty-unexpected.json"
}
```

## Stage Reject After Save

Tool: `submit_file`
Question: Stage decision-gate candidate for Reject After Save.
Error: `False`

```text
{
  "status": "staged",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260515_232208870\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260515_232208870_e91a3c9d5602\\Data\\20260515_232211020_submit_file_BaseTableRepository_7f30927e.cs",
  "stagedRecordId": "20260515_232211020_submit_file_BaseTableRepository_7f30927e",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260515\\20260515_232211020_submit_file_BaseTableRepository_7f30927e.json",
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "620bd8eb75b86e3f19f4a3ddd24cba69418e7327b93cb29b4b5e669b39b474aa",
  "serverDerivedMetadata": {
    "symbolsAdded": [],
    "symbolsRemoved": [],
    "usingsAdded": [],
    "usingsRemoved": [],
    "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260515_232208870_e91a3c9d5602\\Data\\20260515_232211020_submit_file_BaseTableRepository_7f30927e.cs"
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
Question: Record rejected for 20260515_232211020_submit_file_BaseTableRepository_7f30927e and classify by vote-plus-hash agreement.
Error: `False`

```text
{
  "stagedRecordId": "20260515_232211020_submit_file_BaseTableRepository_7f30927e",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260515_232208870\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260515\\20260515_232211020_submit_file_BaseTableRepository_7f30927e.json",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260515_232208870_e91a3c9d5602\\Data\\20260515_232211020_submit_file_BaseTableRepository_7f30927e.cs",
  "operatorDecision": "rejected",
  "classification": "dirty-unexpected",
  "decisionMatchesClassification": false,
  "blocksFurtherEdits": true,
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "620bd8eb75b86e3f19f4a3ddd24cba69418e7327b93cb29b4b5e669b39b474aa",
  "currentHash": "620bd8eb75b86e3f19f4a3ddd24cba69418e7327b93cb29b4b5e669b39b474aa",
  "queueStatus": "blocked-dirty-unexpected",
  "note": "Decision gate fixture smoke.",
  "decidedAt": "2026-05-16T04:22:11.1956606\u002B00:00",
  "decisionRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Decisions\\20260515\\20260515_232211020_submit_file_BaseTableRepository_7f30927e_232211195_dirty-unexpected.json"
}
```

## Stage Dirty External Edit

Tool: `submit_file`
Question: Stage decision-gate candidate for Dirty External Edit.
Error: `False`

```text
{
  "status": "staged",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260515_232208870\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260515_232208870_e91a3c9d5602\\Data\\20260515_232211201_submit_file_BaseTableRepository_ba3160d0.cs",
  "stagedRecordId": "20260515_232211201_submit_file_BaseTableRepository_ba3160d0",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260515\\20260515_232211201_submit_file_BaseTableRepository_ba3160d0.json",
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "b031f1602d1cf3e32cc7623fe6911a82af7c8feb2621ac6233cc0db3489188df",
  "serverDerivedMetadata": {
    "symbolsAdded": [],
    "symbolsRemoved": [],
    "usingsAdded": [],
    "usingsRemoved": [],
    "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260515_232208870_e91a3c9d5602\\Data\\20260515_232211201_submit_file_BaseTableRepository_ba3160d0.cs"
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
Question: Record rejected for 20260515_232211201_submit_file_BaseTableRepository_ba3160d0 and classify by vote-plus-hash agreement.
Error: `False`

```text
{
  "stagedRecordId": "20260515_232211201_submit_file_BaseTableRepository_ba3160d0",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260515_232208870\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260515\\20260515_232211201_submit_file_BaseTableRepository_ba3160d0.json",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260515_232208870_e91a3c9d5602\\Data\\20260515_232211201_submit_file_BaseTableRepository_ba3160d0.cs",
  "operatorDecision": "rejected",
  "classification": "dirty-unexpected",
  "decisionMatchesClassification": false,
  "blocksFurtherEdits": true,
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "b031f1602d1cf3e32cc7623fe6911a82af7c8feb2621ac6233cc0db3489188df",
  "currentHash": "73fc56d0027aa773abf8fad71a22b636e09e947b01370ff09692e4f52ec6719a",
  "queueStatus": "blocked-dirty-unexpected",
  "note": "Decision gate fixture smoke.",
  "decidedAt": "2026-05-16T04:22:11.3769557\u002B00:00",
  "decisionRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Decisions\\20260515\\20260515_232211201_submit_file_BaseTableRepository_ba3160d0_232211377_dirty-unexpected.json"
}
```
