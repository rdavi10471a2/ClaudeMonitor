# MonitorBaseClaude Architecture And Data Flow

## Names

- **Host**: the WinForms operator app in `C:\VSCodeProjects\MonitorBaseClaude`.
- **Monitor Tool Server**: the MCP server in `C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.McpServer`.
- **Source implementation**: the previous monitor implementation in `C:\VSCodeProjects\ClaudeMonitor\Monitor`.
- **Watched project**: the project under test, currently `C:\Schema Studio - DBV2`.
- **CodeLens Tool Server**: the external Roslyn code-intelligence MCP server.
- **Model**: Ollama now, Claude later.
- **Operator**: the human reviewing staged diffs.

## Tool Server Boundary

The Monitor Tool Server owns workflow, safety, file staging, sessions, hashes, ledgers, run history, and review state.

The CodeLens Tool Server owns code intelligence: diagnostics, dependencies, references, callers, type hierarchy, and similar Roslyn questions.

The Monitor Tool Server may use Roslyn internally for syntax, symbol metadata, and overlay validation. That is an implementation detail, not a merge of Tool Server responsibilities.

## Main Data Flow

```text
Operator request
  -> Host
  -> Model chooses a tool
  -> Host calls Monitor Tool Server or CodeLens Tool Server
  -> Tool Server returns typed result
  -> Host logs request/response
  -> Model answers or chooses next tool
```

## Safe Edit Data Flow

```text
Model proposes content or symbol change
  -> Monitor Tool Server stages proposed file under Working\Staged
  -> Tool Server derives server metadata with Roslyn
  -> Tool Server validates watched project with staged files as virtual overlay
  -> Operator opens/reviews one diff
  -> Operator records decision
  -> Tool Server verifies what actually landed
  -> Tool Server updates session hash and returns small envelope
```

The watched project is not overwritten directly by a Model tool call.

## Storage

The current source of truth for monitor-owned state is file based:

- `Working\Sessions`
- `Working\Staged`
- `Working\History`
- `Working\History\Ledgers`

SQLite is a possible future indexing layer for querying sessions, telemetry, staged records, and run history. It should not replace the file-based source of truth until the workflow is stable.

## Command-Line Breadcrumb

The old monitor executable used command-line flags. The MCP API should not expose a stringly command-line clone as its main shape.

Preferred MCP mapping:

- `--refresh-only` becomes `refresh_file`
- `--compare-only` becomes `compare_file`
- `--ledger-summary` becomes `compare_file.ledgerSummary`
- `--no-prune` means do not call `prune_monitor_history`

A later command-line bridge can translate old flags into typed MCP calls if a CLI entry point is needed.
