# MonitorBaseClaude Restart

Use this first after a break, machine move, or context reset.

Detailed recovery note:

```text
Working\History\SessionRecovery_20260516_SourceMapModes.md
```

## Current Stable Thread

- `get_source_map(path?, scope?, mode?)` is live.
- Modes are `navigation`, `selector`, `full`, and `auto`.
- Defaults are `file -> selector`, `folder/project -> navigation`.
- Source-map responses include `modePurpose`, token proxy metadata, ranked `suggestedNextCalls`, and advisory narrowing data.
- Source maps are discovery only, not edit verification.
- The edit gate remains WinMerge review/save plus `record_diff_decision` vote-plus-hash agreement.
- Anti-DRY guardrail is documented: duplication is not automatically debt, and helper extraction must not happen as incidental cleanup.
- External comparable implementation to remember: `sdsrss/code-graph-mcp` (`/plugin marketplace add sdsrss/code-graph-mcp`) is a Rust whole-repo knowledge-graph MCP. Useful ideas to review later: tiny public tool list with hidden callable management tools, compact/result compression tiers, repo-wide index status, file watcher/incremental indexing, and plugin hooks that nudge Claude away from read fanout before edits. Do not treat it as a replacement for MonitorBaseClaude's staged edit/vote-plus-hash workflow.
- Future source-map idea: add an ultra-small navigation sketch mode that feels like the VS Code Outline tree plus a tiny generated orientation block. Keep usings/imports, namespace/class/interface/record/struct declarations and braces/nesting shape, enum declarations and members, member headers, and line spans. Drop method/property bodies, most metadata, hashes, stable keys, long flags, and diagnostic details unless nonzero. For Razor, make a separate magic inventory (`@page`, `@using`, `@inject`, `@inherits`, parameters, callbacks, and `@code` outline) instead of pretending raw Razor is normal C#.

## First Commands

```powershell
dotnet build .\MonitorBaseClaude.slnx

dotnet run --project .\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --source-map-smoke EditorSurface --scope folder --mode navigation

dotnet run --project .\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --source-map-smoke AI\AIAttributes.cs --scope file --mode selector

dotnet run --project .\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --source-map-corpus-smoke
```

## Claude Work-Machine First Pass

Run `MCP_CLIENT_TESTING.md` before real DBV2 staging.

Expected first lane:

```text
get_monitor_status
get_tool_manifest
get_source_map(path, scope: file, mode: selector)
get_symbol(path, symbolSelectorJson)
stop before staging
```

Only proceed to fixture staging after Claude proves it follows the source-map-first workflow.
