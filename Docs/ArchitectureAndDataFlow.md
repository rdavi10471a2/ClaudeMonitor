# MonitorBaseClaude Architecture And Data Flow

## Names

- **Host**: the WinForms operator app in `C:\VSCodeProjects\MonitorBaseClaude`.
- **Monitor Tool Server**: the MCP server in `C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.McpServer`.
- **Sidecar Test Runner**: the console harness in `C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.ToolSmokeTests`.
- **Source implementation**: the previous monitor implementation in `C:\VSCodeProjects\ClaudeMonitor\Monitor`.
- **Watched project**: the project under test, currently `C:\Schema Studio - DBV2`.
- **CodeLens Tool Server**: the external Roslyn code-intelligence MCP server.
- **Model**: Ollama now, Claude later.
- **Operator**: the human reviewing staged diffs.

## Tool Server Boundary

The Monitor Tool Server owns workflow, safety, file staging, sessions, hashes, ledgers, run history, and review state.

The CodeLens Tool Server owns code intelligence: diagnostics, dependencies, references, callers, type hierarchy, and similar Roslyn questions.

The Monitor Tool Server may use Roslyn internally for syntax, symbol metadata, and overlay validation. That is an implementation detail, not a merge of Tool Server responsibilities.

The Monitor Tool Server also owns durable state needed to resume and verify long-running work:

- `Working\Sessions` durable session handles.
- One `StagedEditRecord` per staged change.
- A diff queue with one active diff at a time.
- Post-decision verification state and results.

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

## Claude Integration Shape

When Claude Code or Claude Desktop becomes the Host, the same split remains:

```text
Claude Host
  -> discovers tools through MCP tools/list
  -> optionally reads resources/prompts exposed by the Monitor Tool Server
  -> calls typed tools
  -> receives structured results
  -> asks the Operator before unsafe actions
```

The Monitor Tool Server should keep a small always-visible core tool surface and expose larger editing surfaces through the manifest, prompts, or deferred tool loading where the client supports it.

Recommended always-visible tools:

- status and manifest tools
- file discovery/read tools
- outline/symbol tools
- staged file replacement
- operator decision recording

Recommended advanced tools:

- symbol add/replace/remove
- using add/remove
- class add/remove
- context patch fallback
- project/package staging
- history pruning

Project-scoped Claude configuration should live in `.mcp.json` when Claude Code is available. Machine-local executable paths should stay in appsettings or local settings, not hard-coded in docs.

Claude-facing local rules live in `CLAUDE.md`. Setup notes for Claude Desktop, Claude Code, and Roslyn CodeLens pairing live in `Docs\ClaudeDesktopAndCodeSetup.md`.

## Safe Edit Data Flow

```text
Model proposes content or symbol change
  -> Monitor Tool Server stages proposed file under Working\Staged
  -> Tool Server derives server metadata with Roslyn
  -> Tool Server validates watched project with staged files as virtual overlay
  -> Host opens one GUI diff using the staged/source paths returned by the Tool Server
  -> Operator reviews the diff
  -> Operator reports whether WinMerge saved the full candidate
  -> Tool Server verifies what actually landed
  -> Tool Server updates session hash and returns small envelope
```

The watched project is not overwritten directly by a Model tool call.

## GUI Diff Launch Rule

Interactive GUI diff windows are Host-owned.

Known behavior from testing: launching WinMerge from the stdio Monitor Tool Server can flash, vanish, or become hard to correlate with telemetry. The stable path is:

1. Tool Server stages and validates the proposed edit.
2. Tool Server returns source/staged paths and staged record metadata.
3. Host launches WinMerge directly with `UseShellExecute = true`.
4. Host logs the launch details in telemetry.
5. Operator reviews the sanity diff and either saves the full candidate in WinMerge or leaves source unchanged.
6. Operator explicitly records the outcome in the Host.
7. Host calls the Tool Server decision/verification tool.

Do not move interactive GUI diff lifetime back into the Tool Server. Future GUI review tools should follow the same split unless they are truly non-interactive CLI-only tools.

Window-close detection is not the synchronization point. The Host may display whether a WinMerge process is still visible, but `record_diff_decision` is triggered only after the Operator reports what happened. Vote-plus-hash agreement determines the classification.

The expected v1 Operator pattern is accept all or reject all. The Operator must not edit either side of the WinMerge review and must not partially merge hunks. Accept means the Operator saves the whole staged candidate from WinMerge into the watched file, then the Tool Server verifies the watched file hash equals the staged candidate hash. Reject means the Operator does not save the candidate and the watched file remains at the original baseline hash. Any other state is `dirty-unexpected` and blocks more AI edits on that file until the Host refreshes state.

Overlay compile validation is also a Host-owned gate before review. If a staged candidate has overlay errors, the Tool Server asks the WinForms Host for an Operator decision over the hub before WinMerge opens. `Cancel Review` keeps the staged candidate available for diagnostics, does not open WinMerge, and stops any ordered multi-file review queue at that file. `Force WinMerge Review` is the only path that opens WinMerge for a compile-failed candidate. If the Host is unavailable, that is recorded as a machine block, not an Operator cancel. A not-launched result is never permission to continue with the next file in a queue. For session-based queues, the Tool Server records `blocked-overlay-validation` and rejects later diff launches in that session with `review-chain-blocked` until the blocked item is fixed or force-reviewed.

The diff is a final sanity check that the Model respected the current source file. It is used to catch drastic rewrites, moved code, or boundary damage. If the diff is wrong, reject and regenerate.

Conformance is not immutability. The prior accepted source is the current converged pattern, but a staged candidate may intentionally evolve that pattern. Structural changes are allowed when they are explicit, bounded, staged, reviewed, and accepted all-or-none as the next converged pattern. The system prevents accidental structural drift; it does not prevent deliberate refactoring.

Current compatibility note: `submit_file.launchDiff` still exists, but callers should pass `false`. The primary path is Tool Server returns staged/source paths and the Host or sidecar launches WinMerge.

## Sidecar Test Runner

The sidecar test runner is the preferred place to automate operator workflow tests that should not clutter the WinForms UI.

Current staged-edit smoke command:

```powershell
dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --stage-comment-diff
```

Current disposable accept-path smoke command:

```powershell
dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --fixture-accept-smoke
```

The fixture accept smoke creates a DBV2-shaped watched project under `Working\Fixtures`, writes a fixture-specific config file, starts the real Monitor Tool Server against that config, stages a candidate, records `accepted`, and verifies the fixture source hash equals the staged candidate hash. It does not touch the real DBV2 project.

Current disposable Roslyn surgery smoke command:

```powershell
dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --fixture-roslyn-surgery-smoke
```

The Roslyn surgery smoke uses the same generated fixture/config path to stage and accept `add_using`, `submit_symbol`, `add_symbol`, `remove_symbol`, and `remove_using` through the real Monitor Tool Server. It is the first place to stress-test C# add/remove/replace behavior without touching the real watched DBV2 project.

Current disposable Razor smoke command:

```powershell
dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --fixture-razor-smoke
```

The Razor smoke uses a separate Razor-shaped fixture because raw `.razor` files are not raw C# syntax trees. It verifies `find_file`, `get_file`, empty C# outline behavior, full-file Razor staging, explicit `razor-validation-pending` overlay status, and strict vote-plus-hash Accept.

This command:

1. Calls the real Monitor MCP Tool Server `submit_file` tool.
2. Lets the Tool Server stage, record, and validate the proposed edit.
3. Receives staged/source paths.
4. Launches WinMerge directly from the sidecar runner.
5. Leaves the Operator to review the diff and save the full candidate or leave source unchanged.

The sidecar runner is a Host-like test harness. It may launch GUI tools because it is not the stdio Tool Server. This keeps repeatable tests available without adding one-off buttons to the WinForms dashboard.

For sidecar tests, `record_diff_decision` is triggered after sanity review when the Operator reports whether the full candidate was saved. The sidecar may prompt the Operator to press a key and choose `accepted` or `rejected`, but vote-plus-hash agreement is authoritative. WinMerge closing is telemetry only and must not be treated as proof of a clean outcome.

Current sequencing:

1. Keep staged edit plus overlay compile validation stable. Done.
2. Add sidecar prompt/selection for `record_diff_decision`. Done.
3. Build the strict post-decision verification pipeline. Done for v1 vote-plus-hash gate.
4. Add `get_source_map` as the next read-only Roslyn discovery tool. Done.
5. Add disposable Roslyn surgery fixture smoke for using/member add/remove/replace. Done.
6. Add separate Razor fixture smoke for current read/stage/accept behavior. Done.

Next fixture expansion should cover class add/remove and then Razor-aware outline/build validation.

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
