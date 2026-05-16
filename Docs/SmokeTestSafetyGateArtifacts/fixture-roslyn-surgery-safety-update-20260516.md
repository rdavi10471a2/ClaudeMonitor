# Scripted Tool Smoke Test Summary

Generated: 2026-05-16T08:43:03.4112519-05:00

## Fixture Status

Tool: `get_monitor_status`
Question: Verify the Tool Server is pointed at the disposable fixture solution.
Error: `False`

```text
{
  "uiRoot": "C:\\VSCodeProjects\\MonitorBaseClaude",
  "mcpServerRoot": "C:\\VSCodeProjects\\MonitorBaseClaude\\MonitorBaseClaude.McpServer",
  "legacyMonitorRoot": "C:\\VSCodeProjects\\ClaudeMonitor\\Monitor",
  "watchedSolutionPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_084301578\\Schema Studio.sln",
  "watchedProjectFolder": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_084301578",
  "mcpServerRootExists": true,
  "legacyMonitorRootExists": true,
  "watchedSolutionExists": true
}
```

## Initial Fixture Source Map

Tool: `get_source_map`
Question: Read initial fixture structure.
Error: `False`

```text
{
  "scope": "file",
  "mode": "selector",
  "modePurpose": "stable-symbol-selection",
  "requestedPath": "Data\\BaseTableRepository.cs",
  "watchedProjectAlias": "20260516_084301578",
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

## Read NormalizeName By Stable Key

Tool: `get_symbol`
Question: Verify get_symbol can read the exact method body by get_source_map stableSymbolKey before editing.
Error: `False`

```text
        public string NormalizeName(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            return name.Trim();
        }

```

## Add Using

Tool: `add_using`
Question: Stage adding a using directive.
Error: `False`

```text
{
  "status": "staged",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_084301578\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_084301578_5f0627e90a27\\Data\\20260516_084302126_add_using_BaseTableRepository_8296add8.cs",
  "stagedRecordId": "20260516_084302126_add_using_BaseTableRepository_8296add8",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_084302126_add_using_BaseTableRepository_8296add8.json",
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "735b751aacdbfb4282db71ab2bc92c4c32e266d01a657e8fc003e971c6822184",
  "serverDerivedMetadata": {
    "symbolsAdded": [],
    "symbolsRemoved": [],
    "usingsAdded": [
      "System.Globalization"
    ],
    "usingsRemoved": [],
    "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_084301578_5f0627e90a27\\Data\\20260516_084302126_add_using_BaseTableRepository_8296add8.cs"
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

## Accept Add Using

Tool: `record_diff_decision`
Question: Accept staged result from add_using.
Error: `False`

```text
{
  "stagedRecordId": "20260516_084302126_add_using_BaseTableRepository_8296add8",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_084301578\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_084302126_add_using_BaseTableRepository_8296add8.json",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_084301578_5f0627e90a27\\Data\\20260516_084302126_add_using_BaseTableRepository_8296add8.cs",
  "operatorDecision": "accepted",
  "classification": "accepted",
  "decisionMatchesClassification": true,
  "blocksFurtherEdits": false,
  "originalHash": "6686295d65f760d371d3a6b556f4aa6131cebe94c6aa600cea5e820e2fb4f496",
  "stagedHash": "735b751aacdbfb4282db71ab2bc92c4c32e266d01a657e8fc003e971c6822184",
  "currentHash": "735b751aacdbfb4282db71ab2bc92c4c32e266d01a657e8fc003e971c6822184",
  "queueStatus": "accepted",
  "note": "Fixture Roslyn surgery smoke accepted add_using.",
  "decidedAt": "2026-05-16T13:43:02.7011082\u002B00:00",
  "decisionRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Decisions\\20260516\\20260516_084302126_add_using_BaseTableRepository_8296add8_084302701_accepted.json"
}
```

## Replace NormalizeName

Tool: `submit_symbol`
Question: Stage replacing one method by structured selector.
Error: `False`

```text
{
  "status": "staged",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_084301578\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_084301578_5f0627e90a27\\Data\\20260516_084302718_submit_symbol_BaseTableRepository_d6d8757e.cs",
  "stagedRecordId": "20260516_084302718_submit_symbol_BaseTableRepository_d6d8757e",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_084302718_submit_symbol_BaseTableRepository_d6d8757e.json",
  "originalHash": "735b751aacdbfb4282db71ab2bc92c4c32e266d01a657e8fc003e971c6822184",
  "stagedHash": "21e5d91b1d0677222d3b811ecc318705d82a1404cec06080782c56d1fec83cb5",
  "serverDerivedMetadata": {
    "symbolsAdded": [],
    "symbolsRemoved": [],
    "usingsAdded": [],
    "usingsRemoved": [],
    "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_084301578_5f0627e90a27\\Data\\20260516_084302718_submit_symbol_BaseTableRepository_d6d8757e.cs"
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

## Accept Replace NormalizeName

Tool: `record_diff_decision`
Question: Accept staged result from submit_symbol.
Error: `False`

```text
{
  "stagedRecordId": "20260516_084302718_submit_symbol_BaseTableRepository_d6d8757e",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_084301578\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_084302718_submit_symbol_BaseTableRepository_d6d8757e.json",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_084301578_5f0627e90a27\\Data\\20260516_084302718_submit_symbol_BaseTableRepository_d6d8757e.cs",
  "operatorDecision": "accepted",
  "classification": "accepted",
  "decisionMatchesClassification": true,
  "blocksFurtherEdits": false,
  "originalHash": "735b751aacdbfb4282db71ab2bc92c4c32e266d01a657e8fc003e971c6822184",
  "stagedHash": "21e5d91b1d0677222d3b811ecc318705d82a1404cec06080782c56d1fec83cb5",
  "currentHash": "21e5d91b1d0677222d3b811ecc318705d82a1404cec06080782c56d1fec83cb5",
  "queueStatus": "accepted",
  "note": "Fixture Roslyn surgery smoke accepted submit_symbol.",
  "decidedAt": "2026-05-16T13:43:02.8662124\u002B00:00",
  "decisionRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Decisions\\20260516\\20260516_084302718_submit_symbol_BaseTableRepository_d6d8757e_084302866_accepted.json"
}
```

## Add HasName Symbol

Tool: `add_symbol`
Question: Stage adding one method to the fixture repository.
Error: `False`

```text
{
  "status": "staged",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_084301578\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_084301578_5f0627e90a27\\Data\\20260516_084302887_add_symbol_BaseTableRepository_b53ff604.cs",
  "stagedRecordId": "20260516_084302887_add_symbol_BaseTableRepository_b53ff604",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_084302887_add_symbol_BaseTableRepository_b53ff604.json",
  "originalHash": "21e5d91b1d0677222d3b811ecc318705d82a1404cec06080782c56d1fec83cb5",
  "stagedHash": "4b379cded8a49b76c3378604eb2f0fcb513a4b911dcf3eeb96aa9aac630eb829",
  "serverDerivedMetadata": {
    "symbolsAdded": [
      {
        "name": "HasName",
        "kind": "method",
        "startLine": 15,
        "endLine": 18,
        "textHash": "ecfbe38fdadead8ff13b59a136b7e9d0b2e8b41cf5ec49075a7ab0c26dc73d53"
      }
    ],
    "symbolsRemoved": [],
    "usingsAdded": [],
    "usingsRemoved": [],
    "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_084301578_5f0627e90a27\\Data\\20260516_084302887_add_symbol_BaseTableRepository_b53ff604.cs"
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

## Accept Add HasName Symbol

Tool: `record_diff_decision`
Question: Accept staged result from add_symbol.
Error: `False`

```text
{
  "stagedRecordId": "20260516_084302887_add_symbol_BaseTableRepository_b53ff604",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_084301578\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_084302887_add_symbol_BaseTableRepository_b53ff604.json",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_084301578_5f0627e90a27\\Data\\20260516_084302887_add_symbol_BaseTableRepository_b53ff604.cs",
  "operatorDecision": "accepted",
  "classification": "accepted",
  "decisionMatchesClassification": true,
  "blocksFurtherEdits": false,
  "originalHash": "21e5d91b1d0677222d3b811ecc318705d82a1404cec06080782c56d1fec83cb5",
  "stagedHash": "4b379cded8a49b76c3378604eb2f0fcb513a4b911dcf3eeb96aa9aac630eb829",
  "currentHash": "4b379cded8a49b76c3378604eb2f0fcb513a4b911dcf3eeb96aa9aac630eb829",
  "queueStatus": "accepted",
  "note": "Fixture Roslyn surgery smoke accepted add_symbol.",
  "decidedAt": "2026-05-16T13:43:03.0279357\u002B00:00",
  "decisionRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Decisions\\20260516\\20260516_084302887_add_symbol_BaseTableRepository_b53ff604_084303027_accepted.json"
}
```

## Remove HasName Symbol

Tool: `remove_symbol`
Question: Stage removing the method that was just added.
Error: `False`

```text
{
  "status": "staged",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_084301578\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_084301578_5f0627e90a27\\Data\\20260516_084303040_remove_symbol_BaseTableRepository_850a9cf6.cs",
  "stagedRecordId": "20260516_084303040_remove_symbol_BaseTableRepository_850a9cf6",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_084303040_remove_symbol_BaseTableRepository_850a9cf6.json",
  "originalHash": "4b379cded8a49b76c3378604eb2f0fcb513a4b911dcf3eeb96aa9aac630eb829",
  "stagedHash": "c2ea27a91ceca83d30664b967d9dbdaec725d9786b395a4ea59917b546948fe4",
  "serverDerivedMetadata": {
    "symbolsAdded": [],
    "symbolsRemoved": [
      {
        "name": "HasName",
        "kind": "method",
        "startLine": 15,
        "endLine": 18,
        "textHash": "ecfbe38fdadead8ff13b59a136b7e9d0b2e8b41cf5ec49075a7ab0c26dc73d53"
      }
    ],
    "usingsAdded": [],
    "usingsRemoved": [],
    "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_084301578_5f0627e90a27\\Data\\20260516_084303040_remove_symbol_BaseTableRepository_850a9cf6.cs"
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

## Accept Remove HasName Symbol

Tool: `record_diff_decision`
Question: Accept staged result from remove_symbol.
Error: `False`

```text
{
  "stagedRecordId": "20260516_084303040_remove_symbol_BaseTableRepository_850a9cf6",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_084301578\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_084303040_remove_symbol_BaseTableRepository_850a9cf6.json",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_084301578_5f0627e90a27\\Data\\20260516_084303040_remove_symbol_BaseTableRepository_850a9cf6.cs",
  "operatorDecision": "accepted",
  "classification": "accepted",
  "decisionMatchesClassification": true,
  "blocksFurtherEdits": false,
  "originalHash": "4b379cded8a49b76c3378604eb2f0fcb513a4b911dcf3eeb96aa9aac630eb829",
  "stagedHash": "c2ea27a91ceca83d30664b967d9dbdaec725d9786b395a4ea59917b546948fe4",
  "currentHash": "c2ea27a91ceca83d30664b967d9dbdaec725d9786b395a4ea59917b546948fe4",
  "queueStatus": "accepted",
  "note": "Fixture Roslyn surgery smoke accepted remove_symbol.",
  "decidedAt": "2026-05-16T13:43:03.1961835\u002B00:00",
  "decisionRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Decisions\\20260516\\20260516_084303040_remove_symbol_BaseTableRepository_850a9cf6_084303196_accepted.json"
}
```

## Remove Using

Tool: `remove_using`
Question: Stage removing the using directive that was just added.
Error: `False`

```text
{
  "status": "staged",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_084301578\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_084301578_5f0627e90a27\\Data\\20260516_084303207_remove_using_BaseTableRepository_21f10cea.cs",
  "stagedRecordId": "20260516_084303207_remove_using_BaseTableRepository_21f10cea",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_084303207_remove_using_BaseTableRepository_21f10cea.json",
  "originalHash": "c2ea27a91ceca83d30664b967d9dbdaec725d9786b395a4ea59917b546948fe4",
  "stagedHash": "2891bf6ca6c51bf83fff854c34daa9ef264d783765f47dcdb973241db03e731e",
  "serverDerivedMetadata": {
    "symbolsAdded": [],
    "symbolsRemoved": [],
    "usingsAdded": [],
    "usingsRemoved": [
      "System.Globalization"
    ],
    "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_084301578_5f0627e90a27\\Data\\20260516_084303207_remove_using_BaseTableRepository_21f10cea.cs"
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

## Accept Remove Using

Tool: `record_diff_decision`
Question: Accept staged result from remove_using.
Error: `False`

```text
{
  "stagedRecordId": "20260516_084303207_remove_using_BaseTableRepository_21f10cea",
  "sourceFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Fixtures\\Dbv2ShapeAcceptSmoke\\20260516_084301578\\Data\\BaseTableRepository.cs",
  "relativeSourcePath": "Data\\BaseTableRepository.cs",
  "stagedRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Records\\20260516\\20260516_084303207_remove_using_BaseTableRepository_21f10cea.json",
  "stagedFilePath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\20260516_084301578_5f0627e90a27\\Data\\20260516_084303207_remove_using_BaseTableRepository_21f10cea.cs",
  "operatorDecision": "accepted",
  "classification": "accepted",
  "decisionMatchesClassification": true,
  "blocksFurtherEdits": false,
  "originalHash": "c2ea27a91ceca83d30664b967d9dbdaec725d9786b395a4ea59917b546948fe4",
  "stagedHash": "2891bf6ca6c51bf83fff854c34daa9ef264d783765f47dcdb973241db03e731e",
  "currentHash": "2891bf6ca6c51bf83fff854c34daa9ef264d783765f47dcdb973241db03e731e",
  "queueStatus": "accepted",
  "note": "Fixture Roslyn surgery smoke accepted remove_using.",
  "decidedAt": "2026-05-16T13:43:03.3949262\u002B00:00",
  "decisionRecordPath": "C:\\VSCodeProjects\\MonitorBaseClaude\\Working\\Staged\\Decisions\\20260516\\20260516_084303207_remove_using_BaseTableRepository_21f10cea_084303394_accepted.json"
}
```

## Final Fixture Source Map

Tool: `get_source_map`
Question: Read final fixture structure after Roslyn surgery smoke.
Error: `False`

```text
{
  "scope": "file",
  "mode": "selector",
  "modePurpose": "stable-symbol-selection",
  "requestedPath": "Data\\BaseTableRepository.cs",
  "watchedProjectAlias": "20260516_084301578",
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
      "sha256": "2891bf6ca6c51bf83fff854c34daa9ef264d783765f47dcdb973241db03e731e",
      "length": 444,
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
          "textHash": "c851302b1b9fd73ba6ec2d00f4282c4d831e7252a2f4001ef5ced1b693f86d2a",
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
          "textHash": "f338461401fbff1b9a126fe800978afef4bb21d1827f1fd96dbcd83ce1da99f8",
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
