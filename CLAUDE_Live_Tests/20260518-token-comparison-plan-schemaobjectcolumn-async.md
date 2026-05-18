---
status: new
type: test-result
created: 2026-05-18
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---

## Summary

A controlled head-to-head measurement of token cost between two construction paths producing the **same final C# file**. Both paths receive the identical task prompt; only the toolchain differs. The test is deliberately scoped to a **new-file scenario** (an async sibling of an existing repository), which is the workflow's worst case — no existing bytes can be skipped, so the symbol-level "compose locally" advantage is minimized. The crossover question this test answers: at what file size / method count does the workflow's per-call overhead beat (or lose to) a single whole-file emit?

This file is both the plan and the results record. The plan is filled in below; results sections at the bottom are placeholders to be appended after each variant runs.

## Target

- Source file: [SchemaStudio.Data\SchemaObjectColumnRepository.cs](../../Schema%20Studio%20-%20DBV2/SchemaStudio.Data/SchemaObjectColumnRepository.cs) (path is symbolic — watched tree is in `C:\Schema Studio - DBV2\`).
- Size: 205 lines, 5 public methods + ctor + 1 backing field.
- Methods: `GetByObject`, `DeleteMissingForObject`, `Insert`, `Update`, `SaveAll`.
- Style of original: file-scoped namespace (`namespace SchemaStudio.Data;`), C# 8+ `using var conn = ...;` declaration-pattern, Dapper sync API.
- Output: a new sibling file `SchemaStudio.Data\SchemaObjectColumnRepositoryAsync.cs` that is the async equivalent **with deliberately divergent style** (see prompt below). The original file is **not modified**.

## Shared Test Prompt

Both variants are given exactly this prompt before tool calls begin:

> Create `SchemaStudio.Data\SchemaObjectColumnRepositoryAsync.cs` as the async-equivalent twin of `SchemaObjectColumnRepository.cs`.
>
> Requirements:
> - Namespace `SchemaStudio.Data` using **block-namespace syntax** (`namespace SchemaStudio.Data { ... }`), NOT file-scoped.
> - Class: `public sealed class SchemaObjectColumnRepositoryAsync` with the same `_connectionString` field and constructor.
> - Async-ify every public method, suffix with `Async`:
>   - `GetByObject(int) → GetByObjectAsync(int)` returning `Task<List<SchemaObjectColumnDefinition>>`.
>   - `DeleteMissingForObject(int, IEnumerable<string>) → DeleteMissingForObjectAsync(...)` returning `Task`.
>   - `Insert(...) → InsertAsync(...)` returning `Task<int>`.
>   - `Update(...) → UpdateAsync(...)` returning `Task`.
>   - `SaveAll(IEnumerable<...>) → SaveAllAsync(...)` returning `Task`.
> - **Dapper async methods**: `conn.QueryAsync<T>`, `conn.ExecuteAsync`, `conn.ExecuteScalarAsync<int>` as appropriate for each method's current sync call.
> - **Old classic `using` block syntax** for disposables: `using (var conn = new SqlConnection(_connectionString)) { ... }`. NOT `using var conn = ...;`.
> - SQL strings preserved byte-for-byte from the original (including verbatim-string formatting and whitespace).
> - Top-of-file `using` directives: `Dapper`, `Microsoft.Data.SqlClient`, `System.Data`.
> - Method order and parameter names preserved.

The prompt deliberately forces a transformation (style switch + async re-mapping), so neither path can short-circuit via literal duplication.

## Variant A — Workflow Path (Monitor + Roslyn, symbol-level)

**Goal:** compose the new file from typed, symbol-level staging calls into a Working candidate, then snapshot once via `stage_candidate_for_review` and accept via the standard diff flow.

Tool sequence (expected):

1. `find_file` (path discovery, if needed).
2. `get_source_map(SchemaObjectColumnRepository.cs, mode: selector)` — read the original's structure for selector keys.
3. `get_symbol` on each public method of the original — read bodies for SQL string reuse (5 calls).
4. `start_monitor_session`.
5. **Compose the new file** by repeatedly calling typed staging tools against `SchemaObjectColumnRepositoryAsync.cs` with the session id. Expected breakdown:
   - `submit_file` (or equivalent skeleton-bootstrap) to create the file with the namespace block, class shell, field, ctor — minimum viable scaffolding. *Note: if the V1 candidate composer supports building a from-scratch file with only `add_*` calls, prefer that and document; otherwise use one minimal `submit_file` for the skeleton and layer methods.*
   - `add_using` × 3 (`Dapper`, `Microsoft.Data.SqlClient`, `System.Data`).
   - `add_method` × 5 (one per async method).
6. `stage_candidate_for_review(SchemaObjectColumnRepositoryAsync.cs, sessionId)` — snapshot the Working candidate.
7. `launch_staged_diff(stagedRecordId)`.
8. `record_diff_decision(stagedRecordId, accepted)`.

**Token capture for Variant A:**
- Outbound payload bytes per tool call → recorded inline. The interesting number is the sum across all calls.
- Session-total tokens (input+output) at start and end → user reports from Claude Code UI.

## Variant B — Baseline Path (raw `Write`, no workflow)

**Goal:** emit the entire final file in one tool call to a sandbox path, with no Monitor staging and no Roslyn involvement.

Tool sequence (expected):

1. `Read` on the original `SchemaObjectColumnRepository.cs` — one call, full file body, needed to copy SQL strings verbatim and understand method shapes.
2. `Write` to `.claude-local/token-test-sandbox/SchemaObjectColumnRepositoryAsync.cs` — one call, full final file body.

That's it. The output file lives outside the watched tree, never enters the Monitor staging path, and the watched solution is untouched.

**Token capture for Variant B:**
- Outbound payload bytes: essentially `len(Write.content)` plus a small `Read` cost.
- Session-total tokens at start and end → user reports from Claude Code UI.

## Measurement Definitions

- **Outbound payload bytes** = total character count of all tool-call argument values I emit during the variant. Excludes my reasoning, the tool result envelopes coming back to me, and any conversation text outside tool calls. This is the cleanest measure of "bytes the cloud had to produce."
- **Session-total tokens** = the input+output token count for the variant, as reported by the Claude Code UI. This is the cost-to-the-user number. Captured by the user; Claude does not have direct access.
- **Result-equivalence check**: after both variants run, compare the two produced files. Differences in whitespace are allowed; differences in semantics (method names, async ops, SQL strings) are not. If the files diverge semantically the variant is re-run.

## Pre-Flight (run before either variant)

1. Confirm branch + status: `claude/live-test-notes-20260517`, no uncommitted product-source changes.
2. Confirm `SchemaObjectColumnRepository.cs` is untouched and has no async (5 sync methods).
3. Confirm `SchemaObjectColumnRepositoryAsync.cs` does **not** exist anywhere in the watched tree.
4. Confirm Monitor MCP readiness (Variant A only): `get_monitor_status`, `get_tool_manifest`, `get_staging_guide`, `get_workflow_status` all return non-error payloads.
5. Confirm Roslyn CodeLens readiness (Variant A only): `list_solutions`, `get_diagnostics` work.
6. Confirm WinForms host is running (Variant A only): `get_workflow_status` reports a WinMerge resolution.
7. Sandbox directory exists for Variant B: `.claude-local/token-test-sandbox/`. Create if missing.
8. Record starting session-total token count (user reads from UI).

## Run Order

**Constraint:** this plan is being filed inside a multi-hour Claude Code session. A clean "fresh session per variant" is no longer available. Adapting:

- **Primary metric** = outbound payload bytes (order-invariant; not affected by prior context).
- **Session-total tokens** = demoted to noisy secondary signal. A mid-session readout includes hours of prior conversation cost, so deltas are dominated by background context, not by the variant's own work. Report it if convenient but don't treat it as the headline number.
- **Variant order**: run **Variant B first** (single `Write` is cheap and produces the reference file for the equivalence check), then **Variant A**. Variant A must **not skip Roslyn reads** just because the original file body sits in earlier context — the workflow path is being measured as if it had no prior knowledge of the file. Concretely: A still calls `get_source_map`, still calls `get_symbol` per method, even if I "already know" the bodies. Skipping those calls because of in-context memory would silently bias A's outbound bytes downward and invalidate the comparison.

Both variants share the same task prompt; neither is allowed to claim "I already have that in context" as an excuse to skip a tool call its path would normally require.

## Results — Variant B (raw Write baseline)

*To be appended after run.*

- Date/time:
- Starting session-total tokens:
- Ending session-total tokens:
- Delta session-total tokens:
- Outbound payload bytes:
  - `Read(SchemaObjectColumnRepository.cs)`: result size only, no outbound payload beyond args.
  - `Write(.claude-local/token-test-sandbox/SchemaObjectColumnRepositoryAsync.cs)`: bytes in `content` arg.
- Total outbound payload bytes:
- Produced-file size (bytes / lines):
- Notes:

## Results — Variant A (Monitor + Roslyn workflow)

*To be appended after run.*

- Date/time:
- Starting session-total tokens:
- Ending session-total tokens:
- Delta session-total tokens:
- Per-call outbound payload bytes (one row per tool call):

  | # | Tool | Outbound bytes | Notes |
  |---|---|---|---|
  | 1 |  |  |  |

- Total outbound payload bytes:
- Number of tool calls:
- Produced-file size (bytes / lines):
- Result-equivalence vs Variant B: pass / fail / details:
- Notes:

## Comparison

*To be filled after both variants run.*

|  | Variant B (raw) | Variant A (workflow) | Ratio A:B |
|---|---|---|---|
| Outbound payload bytes |  |  |  |
| Session-total tokens |  |  |  |
| Tool calls |  |  |  |
| Wall-clock time |  |  |  |

**Interpretation placeholder:**
- If A < B on payload bytes: workflow wins even on its worst-case scenario; the per-call overhead is smaller than the once-emitted scaffolding savings.
- If A ≈ B on payload bytes: the test is on the crossover line; payoff depends on whether session-total tokens (which include reasoning cost) tip in A's favor.
- If A > B on payload bytes: confirms the suspicion that new-file scenarios are uneconomic for the workflow; the workflow's payoff is in incremental-edit-on-existing-large-file scenarios that this test does not cover. Recommend a Variant C follow-up.

## Defaults Picked Without Operator Confirmation

Recording explicitly so they can be overridden:

1. **Baseline path** = raw `Write` to `.claude-local/token-test-sandbox/`. Alternative considered: `submit_file` whole-file through Monitor (gated but single payload). Raw `Write` chosen because it isolates transport from staging overhead.
2. **Primary token unit** = outbound payload bytes (directly measurable by Claude). Session-total tokens captured secondarily via UI readout (more honest to cost but requires user cooperation).
3. **Sandbox path** = `.claude-local/token-test-sandbox/`. Outside watched tree, gitignored, no risk to product source.

Override any of these and I will rerun the plan.

## Open Questions for Operator Before Run

1. Confirm or override the three defaults above.
2. Confirm `.claude-local/` is the right sandbox parent (vs `LocalSmokeTests/` or another off-tree location).
3. Acknowledged: separate sessions per variant are not available (we are mid-session). Plan adjusted — outbound payload bytes is the primary metric, session-total is demoted to noisy secondary.
4. Confirm Variant A is expected to do its full Roslyn read sequence even though the original file body is already in this session's context. Skipping reads would invalidate the comparison.
