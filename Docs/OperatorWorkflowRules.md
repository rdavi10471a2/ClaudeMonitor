# Operator Workflow Rules

This file translates the relevant source monitor `AGENTS.md` rules into project documentation for MonitorBaseClaude.

## Current Roots

- Host root: `C:\VSCodeProjects\MonitorBaseClaude`
- Monitor Tool Server root: `C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.McpServer`
- Source implementation root: `C:\VSCodeProjects\ClaudeMonitor\Monitor`
- Watched solution: `C:\Schema Studio - DBV2\Schema Studio.sln`
- Watched project folder: `C:\Schema Studio - DBV2`

## State Ownership

Generated monitor state belongs under the Host root, not inside the watched project:

- `Working`
- `Working\History`
- `Working\Staged`
- `Working\Sessions`
- `Working\History\Ledgers`

## Source Edit Rules

When editing watched source files:

- Preserve existing `AIFileContext`, `FileVersion`, and meaningful `AIChange` attributes.
- Do not add routine process comments to source files.
- Put routine change summaries in monitor-owned history, ledger notes, or nearby component maps.
- For meaningful C# edits, bump the edited file's `FileVersion`.
- For new C# files, add `AIFileContext` and `FileVersion("1.0")`.
- Do not use C# top-level statements in generated source or samples.
- Do not add emoji, glyphs, or decorative Unicode as edit anchors, process markers, or workflow instructions.

Existing glyphs or expressive comments are human-owned source content. Preserve or change them only when the requested code change calls for it; never rely on them as anchors. Use `get_source_map`, structured symbol selectors, exact source text, or staged records instead.

## Read/Narrow Before Edit

For C# edits, prefer `get_source_map` before `get_file`. Use navigation mode for broad folder/project orientation, selector mode for a chosen file, and `get_symbol` for the selected symbol body before requesting a full file.

Use full-file reads only when source-map and symbol context are insufficient, or when the requested change is inherently whole-file.

Models are allowed to read related files under the watched root when the change requires context. Related context should still follow the narrow path:

```text
find_file -> get_source_map(mode: navigation) -> get_source_map(mode: selector) -> get_symbol -> get_file only when needed
```

## Structural Evolution Rule

Pattern conformance does not mean freezing source structure. Files may be split, classes extracted, symbols moved, partial classes introduced, namespaces reorganized, and new boundaries created.

Structural changes must be explicit staged candidates, not accidental side effects of unrelated edits. Keep each structural candidate small, named, bounded, explainable, all-or-none, and hash-verified.

For a multi-file structural candidate, the batch should be treated as all-or-none unless it was deliberately split into independent staged candidates.

Duplication is not automatically debt. Unnecessary abstraction is also debt. Do not extract helper methods, move code, or introduce abstractions merely to reduce small local repetition. Keep repeated inline code when it preserves local workflow clarity, keeps state transitions visible, or makes operator review easier.

Good structural candidates include extracting one class into a new file, moving one method group into a partial file, splitting one large file into one additional partial file, moving one responsibility into a helper class, or adding one new service with a narrow call site.

Avoid mixing feature work with structural cleanup, formatting while moving symbols, broad project rewrites, cleanup sweeps, DRY cleanup, or model-driven reorganization without a specific purpose.

Accept means the whole structural candidate becomes the next converged pattern. Reject means none of it is applied.

## Diff Review Rules

- Never launch multiple GUI diff windows back-to-back.
- Multi-file changes must become an ordered review queue.
- The Tool Server stages and validates edits, then returns source/staged paths.
- The Host launches the first GUI diff only, then waits for the Operator to finish review.
- The Operator reports `accepted` only after saving the full staged candidate in WinMerge, or `rejected` when leaving the watched source unchanged.
- The Operator must not edit either side of the diff and must not partially merge hunks.
- Accept means the Operator used the Host-owned diff tool to save the whole staged candidate into the watched file, then the Tool Server verifies the watched file exactly matches the staged proposal hash.
- Reject means the Operator did not save the candidate and the Tool Server verifies the watched file still matches the original baseline hash.
- Any other watched file state is `dirty-unexpected` and blocks more AI edits on that file until the Host refreshes state.
- Expected v1 behavior is accept all or reject all. If the proposal is close but not right, reject it and ask for a new staged proposal.
- The diff review is a final sanity check, not the main editing surface. Use it to catch drastic rewrites, moved code, or boundary mistakes. The source file is a voting member in generation, so a valid proposal should respect the current file shape.
- After a decision, the Tool Server verifies what landed before the next diff is released.
- For multi-file C# edits, staged files may be validated together as an overlay even while diffs are reviewed one at a time.

## Decision Classification Rule

`record_diff_decision` classifies by vote-plus-hash agreement.

The Operator report supplies the intended outcome. The watched file hash supplies the mechanical proof.

Classification is:

- reported `accepted` + watched hash equals staged proposal hash -> `accepted`
- reported `rejected` + watched hash equals original baseline hash -> `rejected`
- reported `accepted` + watched hash equals original baseline hash -> `dirty-unexpected`
- reported `rejected` + watched hash equals staged proposal hash -> `dirty-unexpected`
- watched hash matches neither original nor staged -> `dirty-unexpected`

`dirty-unexpected` blocks further AI edits on that file until Host/Operator refresh, inspection, reset, or explicit recovery. The model must not invent an automatic recovery path for `dirty-unexpected`.

## No-Op Candidate Rule

If `stagedCandidateHash` equals `originalBaselineHash`, the Tool Server should return `no-change` or `no-op-staged` and should not enqueue a normal diff by default.

A no-op candidate does not need Operator review because Accept and Reject collapse into the same raw file hash state.

## GUI Diff Ownership

WinMerge and other GUI review tools are Host-owned. The Monitor Tool Server must not own interactive GUI lifetime because stdio server-launched windows proved unreliable: WinMerge can appear briefly, disappear, or become hard to correlate with telemetry. The stable operator workflow is Tool Server stages, Host launches, Operator saves or does not save in WinMerge, Tool Server verifies.

Closing WinMerge is not a decision. The Host may show window/process status, but `record_diff_decision` is called only after the Operator reports the outcome. Vote-plus-hash agreement is authoritative.

## Sidecar Test Runner

Operator workflow tests may live in the sidecar console harness instead of the WinForms UI:

```text
C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.ToolSmokeTests
```

The sidecar runner can call real MCP tools and launch WinMerge directly. This is allowed because the sidecar is a Host-like process, not the stdio Monitor Tool Server. Use it for repeatable smoke tests such as staged edit, diff launch, and `record_diff_decision`.

The sidecar must not infer acceptance from WinMerge closing. After Operator review, the sidecar explicitly records the reported outcome through `record_diff_decision`. For Accept, WinMerge/Operator save is the mutation path and the Tool Server verifies the resulting hash.

Current sidecar decision command:

```powershell
dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --record-decision <stagedRecordId> <accepted|rejected>
```

Current disposable accept-path smoke command:

```powershell
dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --fixture-accept-smoke
```

This command uses a generated config file and DBV2-shaped fixture under `Working\Fixtures` so accepted-path mutation can be stress-tested without touching the real watched project.

Current disposable Roslyn surgery smoke command:

```powershell
dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --fixture-roslyn-surgery-smoke
```

This command uses the same generated fixture/config path to test `add_using`, `submit_symbol`, `add_symbol`, `remove_symbol`, and `remove_using`.

Current disposable Razor smoke command:

```powershell
dotnet run --project C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.ToolSmokeTests\MonitorBaseClaude.ToolSmokeTests.csproj -- --fixture-razor-smoke
```

This command uses a separate Razor-shaped fixture to test current safe Razor behavior: discover, read, stage a full `.razor` candidate, return `razor-validation-pending`, and accept by strict vote-plus-hash gate.

## Claude Client Acceptance Rule

Before trusting a Claude MCP client with real watched-source staging, run a learning pass against the documented sequence in `MCP_CLIENT_TESTING.md`:

```text
get_monitor_status
get_tool_manifest
get_source_map for the target C# file, using selector mode after any broad navigation pass
get_symbol for the selected symbol body
stop before staging
```

The pass is successful only if Claude narrows through source maps and symbols, avoids full-file reads unless justified, describes WinMerge as the Host-owned review/save surface, and treats `record_diff_decision` as vote-plus-hash agreement.

If Claude drifts, update `CLAUDE.md`, `MCP_CLIENT_TESTING.md`, or the manifest with a sharper sequence or negative example. Do not weaken server-side enforcement to accommodate a model mistake.

## Documentation Timing

Generate documentation after stability, not during active churn.

For large generated or frequently edited surfaces, maintain nearby markdown maps only during a documentation pass or direct map-update request.
