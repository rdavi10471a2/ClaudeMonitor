# ClaudeMonitor / MonitorBaseClaude

MonitorBaseClaude is a local WinForms and MCP workflow monitor for AI-assisted code edits. It is built around a simple safety idea: the model can propose and stage changes, but the watched source is changed only through an explicit human review path and then verified by hashes.

The current target runtime client is Claude using MCP tools. The current implementation/test client is this repository plus its smoke harnesses.

## What It Does

- Exposes a local MCP Tool Server for source discovery, source maps, symbol reads, and staged edit proposals.
- Keeps generated monitor state under `Working`, not inside the watched project.
- Stages complete candidate files under monitor-owned paths instead of directly overwriting watched source.
- Uses WinMerge or a Host-owned review surface for all-or-none human review.
- Classifies review outcomes with vote-plus-hash agreement:
  - Operator reports `accepted` and watched hash equals staged hash -> `accepted`
  - Operator reports `accepted` and normalized watched/staged content matches after BOM/EOL normalization -> `accepted-normalized`
  - Operator reports `rejected` and watched hash equals original hash -> `rejected`
  - anything else -> `dirty-unexpected`
- Blocks further staging on a file after `dirty-unexpected` until explicit refresh/recovery.

## Main Projects

- `MonitorBaseClaude.csproj`
  - WinForms operator surface and local monitor client code.

- `MonitorBaseClaude.McpServer`
  - MCP Tool Server that exposes the controlled read, source-map, staging, and decision tools.

- `MonitorBaseClaude.ToolSmokeTests`
  - Console smoke harness for deterministic workflow and fixture testing.

## Core Workflow

Expected agent edit path:

1. `find_file` if the target file is not known.
2. `get_source_map` in `navigation` mode for broad project/folder orientation.
3. `get_source_map` in `selector` mode for a chosen file.
4. `get_symbol` for the exact body that needs editing.
5. `submit_file`, `submit_symbol`, `add_symbol`, `remove_symbol`, `add_using`, or `remove_using` to stage a complete candidate.
6. Host/Operator reviews the staged candidate against watched source.
7. `record_diff_decision` verifies the Operator vote against watched file hashes.

The source-map hierarchy is intentional:

- `navigation`: broad outline/orientation, no mutation-grade identity.
- `selector`: stable symbol keys, hashes, compact contract signatures, and structured selector metadata.
- `detail`: selector identity plus extra contract detail without full audit payloads.
- `full`: audit/debug source-map detail.
- `get_symbol`: actual source body read after narrowing.

## Safety Rules

- The Tool Server stages candidates; it does not directly mutate watched source.
- The Operator accepts all or rejects all. Partial hunk merging is outside the v1 workflow.
- `dirty-unexpected` blocks further staged edits on that file.
- `refresh_file` is the current v1 recovery path after Host/Operator inspection.
- Re-voting a blocked dirty record is refused; recovery is explicit.
- `compare_file` may refresh a missing Working copy, but that implicit refresh does not recover dirty blocks.
- C# parse/syntax errors are rejected before a staged record is written.
- Overlay compile diagnostics are reported as validation metadata, not a hard staging blocker, because project/reference/generated-code state can create false positives.
- Razor is handled conservatively: full-file staging plus `razor-validation-pending`; no pretend C# symbol surgery for raw `.razor` files.

## Build

```powershell
dotnet build .\MonitorBaseClaude.slnx
```

## Claude Desktop On Windows

Build the MCP server first, then point Claude Desktop at the server executables directly. Do not use `dotnet run` or PowerShell wrapper scripts as the Desktop MCP command on the Windows MSIX build; the wrapper layer can break stdio forwarding and MSBuild output can pollute the JSON-RPC stream.

Use `Docs/ClaudeDesktopAndCodeSetup.md` for the workstation-local direct-exe config.

## High-Value Smokes

Decision gate, syntax rejection, dirty blocking, and recovery:

```powershell
dotnet run --project .\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --fixture-decision-gate-smoke
```

Roslyn source-map and symbol surgery path:

```powershell
dotnet run --project .\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --fixture-roslyn-surgery-smoke
```

Razor safe-mode path:

```powershell
dotnet run --project .\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --fixture-razor-smoke
```

Real watched-project source-map navigation:

```powershell
dotnet run --project .\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --source-map-smoke EditorSurface --scope folder --mode navigation
```

## Current Verification Snapshot

As of the 2026-05-16 safety-gate checkpoint:

- solution build passes
- vote-plus-hash decision scenarios pass
- malformed C# candidate rejection is covered
- `dirty-unexpected` blocking and `refresh_file` recovery are covered
- Roslyn surgery fixture passes
- Razor safe-mode fixture passes
- DBV2 source-map generation has been smoke tested read-only

See:

- `Docs/SafetyGatePatchREADME.md`
- `Docs/SmokeTestCoverageAddendum.md`
- `Docs/SmokeTestSafetyGateArtifacts/`
- `MonitorBaseClaude.McpServer/MONITOR_MCP_TOOL_MANIFEST.md`
- `MCP_CLIENT_TESTING.md`

## Claude Runtime Status

The deterministic monitor workflow is ready for a constrained Claude MCP learning pass:

1. `get_monitor_status`
2. `get_tool_manifest`
3. `get_source_map` navigation
4. `get_source_map` selector
5. `get_symbol`
6. stop before real watched-source staging

Real watched-source mutation should remain deliberate and operator-reviewed.

## Repository Notes

This repository contains implementation notes for two different roles:

- `AGENTS.md`: build-agent rules for Codex or another assistant changing this repository.
- `CLAUDE.md`: runtime MCP-client rules for Claude when it later consumes the tool server.

That split is intentional. Codex builds the monitor; Claude is expected to consume the monitor.
