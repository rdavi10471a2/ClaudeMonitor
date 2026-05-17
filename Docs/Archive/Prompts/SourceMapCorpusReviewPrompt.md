# Source Map Corpus Review Prompt

Use this prompt with ChatGPT/Claude to review the DBV2-wide source-map corpus smoke artifacts.

```text
You are reviewing MonitorBaseClaude source-map artifacts for an MCP-assisted C# edit workflow.

Context:

- MonitorBaseClaude is an MCP/WinForms monitor workflow.
- The intended agent loop is:
  find_file -> get_source_map -> get_symbol -> stage complete candidate -> WinMerge review/save -> record_diff_decision.
- get_source_map is read-only discovery. It is not the edit verifier and not the accept/reject gate.
- Final decisions use vote-plus-hash agreement:
  reported accepted + watched hash == staged hash -> accepted
  reported rejected + watched hash == original hash -> rejected
  any mismatch -> dirty-unexpected
- WinMerge is the Host-owned review/save surface, not a manual repair or partial hunk merge surface.

New DBV2 source-map corpus smoke:

Command:

dotnet run --project .\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --source-map-corpus-smoke

Latest run:

- C# files: 32
- Successful maps: 32
- Error maps: 0
- Diagnostics: 0
- Source bytes: 154,562
- Full source-map bytes: 506,400
- Selector index bytes: 295,794
- Navigation index bytes: 102,570
- Estimated source tokens: 38,613
- Estimated full source-map tokens: 126,600
- Estimated selector-index tokens: 73,949
- Estimated navigation-index tokens: 25,643

Artifacts in this package:

- SourceMapCorpus/source-map-corpus-summary.md
- SourceMapCorpus/source-map-corpus-analysis.json
- SourceMapCorpus/source-map-compact-index.json
- SourceMapCorpus/source-map-navigation-index.json
- SourceMapCorpus/maps/EditorSurface/ExplorerControl.source-map.json
- SourceMapCorpus/maps/Models/BaseTableColumnDefinition.source-map.json
- SourceMapCorpus/maps/AI/AIAttributes.source-map.json
- SourceMapCorpus/maps/EditorSurface/EditorSurfaceControl.source-map.json
- AGENTS.md
- CLAUDE.md
- MCP_CLIENT_TESTING.md
- Docs/ExistingCodeEditCoveragePlan.md
- MonitorBaseClaude.McpServer/MONITOR_MCP_TOOL_MANIFEST.md

Current implementation:

- get_source_map now accepts mode: auto, navigation, selector, or full.
- file scope defaults to selector.
- folder/project scope defaults to navigation.
- responses include modePurpose, estimatedTokenProxy, budgetLimit, wasTruncated, suggestedNarrowing when over budget, and ranked suggestedNextCalls.
- navigation suggestedNextCalls point to get_source_map(..., mode: selector).
- selector suggestedNextCalls point to get_symbol(..., symbolSelectorJson).
- budgetLimit is enforced in the current implementation; over-budget responses set wasTruncated, omit source-map file payload details, and return narrowing guidance for a smaller retry.
- corpus indexes are aggregate smoke artifacts, not exact live get_source_map envelopes.
- property signatures now preserve accessor shape, such as `public string Warning { get; }`, instead of rendering properties as field-like semicolon declarations.
- agent/operator docs now include the anti-DRY guardrail: duplication is not automatically debt, unnecessary abstraction is also debt, and helper extraction/DRY cleanup must not happen as a side effect of narrow edits.

Please review the artifacts and recommend whether the live source-map response shapes are now useful for Claude-like MCP clients.

Questions:

1. Are navigation, selector, and full the right final mode names?
2. Are the current defaults right: file -> selector, folder/project -> navigation?
3. What fields should be trimmed from selector or navigation mode to reduce token pressure?
4. Should navigation mode include attributes, diagnostics, baseTypes, events, partial grouping, usings, or only file/type/member shape?
5. Should selector mode include private members by default, or require includePrivate=true?
6. What size/token budget should the tool enforce before asking the model to narrow path or scope?
7. What related-file hints should be added before full Roslyn CodeLens semantic integration?
8. Does the selector index have enough data to support stable get_symbol and future submit_symbol selectors?
9. Is the navigation index enough for broad orientation without dumping full file bodies?
10. Are ranked suggestedNextCalls useful enough, or should they be renamed/restructured as nextActions, nextToolCalls, or narrowingAffordances?
11. Is the anti-DRY guardrail worded strongly enough to prevent AI-driven helper extraction and cleanup refactors during narrow edits?

Please separate recommendations into:

- implement now
- useful next
- defer until Roslyn CodeLens / compilation context
- avoid
```
