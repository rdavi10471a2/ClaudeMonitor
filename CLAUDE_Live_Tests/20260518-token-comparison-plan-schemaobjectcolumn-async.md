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

**Status:** not run in this session. Operator deferred Variant B to a clean session to avoid the in-context-contamination problem flagged in the Run Order section. Expected outbound bytes for B once run: `Read` envelope (~80 bytes for path arg) + `Write` envelope (~80 bytes path arg + 7,178 bytes content) ≈ **~7,340 bytes**. Will be filled in post-run.

## Results — Variant A (Monitor + Roslyn workflow)

**Status:** completed 2026-05-18, ~23:13 UTC. Watched file `SchemaStudio.Data\SchemaObjectColumnRepositoryAsync.cs` created, 7,178 bytes / 212 lines, SchemaStudio.Data project compiles with 0 errors post-accept.

### Outbound payload bytes (approximate, per tool call)

Counts are JSON-encoded argument-value lengths, rounded. Pre-flight reads and the failed typed-add attempts are included in the actual-cost subtotal; an "ideal-path" subtotal excludes the failed attempts to model what a perfectly-routed agent would have spent.

| # | Phase | Tool | Outbound bytes (approx.) | Notes |
|---|---|---|---|---|
| 1 | Pre-flight | `get_monitor_status` | ~2 | empty args |
| 2 | Pre-flight | `get_workflow_status` | ~2 | empty args |
| 3 | Pre-flight | `list_solutions` (Roslyn) | ~2 | empty args |
| 4 | Pre-flight | `get_diagnostics` (Roslyn) | ~49 | severity + project filter |
| 5 | Pre-flight | `find_file` | ~56 | confirm new file path is free |
| 6 | Read | `get_source_map` (selector) | ~88 | path + mode |
| 7 | Read | `get_symbol` × 6 (ctor + 5 methods) | ~3,000 | path + ~450-byte selectorJson per call |
| 8 | Compose | `start_monitor_session` | ~110 | purpose string |
| 9 | Compose (skeleton) | `submit_file` | ~480 | path + sessionId + 383-byte skeleton body |
| F | Failed typed-add probes | `add_method` × 3 + `add_symbol` × 1 + `add_method` (new path) | ~2,670 | 4 distinct failures: 1100 + 1100 + 150 + 180 + 140 (some 5 calls, one with afterSymbol, three minimal probes) |
| 10 | Diagnostic | `get_staging_guide` | ~2 | empty args, called to re-check the V1 protocol after typed-add failures |
| 11 | Compose (full body) | `submit_file` | ~7,260 | path + sessionId + 7,178-byte file content |
| 12 | Stage | `stage_candidate_for_review` | ~130 | path + sessionId |
| 13 | Diff | `launch_staged_diff` | ~110 | stagedRecordId |
| 14 | Decision | `record_diff_decision` | ~350 | stagedRecordId + decision + sessionId + note |
| 15 | Post-verify | `get_diagnostics` (Roslyn) | ~49 | confirm 0 errors after accept |

**Subtotals:**

- Actual outbound bytes (everything I sent, including failed attempts and the diagnostic staging-guide re-read): **~14,360 bytes**
- "Ideal path" outbound bytes (no failed typed-add probes, no after-failure staging-guide re-read): **~11,690 bytes**
- Construction-only (rows 8-14, the actual workflow path that produced the file): **~8,440 bytes** — dominated by the row-11 `submit_file` payload (~7,260 bytes).

### Number of tool calls

- Actual: 18 tool calls (excluding Bash diagnostics).
- Ideal path: 13 tool calls.

### Produced-file size

7,178 bytes / 212 lines / SHA-256 e65b3032...12f54.

### Workflow path actually taken (vs plan)

The plan assumed I could compose the new file from `submit_file` (skeleton) + N typed `add_method` calls. **That assumption was wrong**: V1 `add_method` and `add_symbol` both error opaquely against any path not already present in Roslyn's loaded solution. The Working candidate created via `submit_file` exists on disk but is not registered with Roslyn's solution model, so subsequent typed adds have no syntax tree to splice into. This was reproduced with a brand-new probe path (`SchemaStudio.Data\TokenTestProbe.cs`) that had no prior `submit_file` at all — identical failure — confirming the issue is "typed adds require a watched, Roslyn-loaded file," not "typed adds require an existing candidate." The staging guide's mode table is consistent: it maps "Create a brand-new file" exclusively to `submit_file`, never to typed adds.

The realistic workflow path for a new file is therefore **one** `submit_file` carrying the entire file body, followed by stage/diff/decision. The "compose locally, save bytes by skipping unchanged regions" advantage that the symbol-level path is built for does not apply, because there is no class body to splice into yet.

### Notes / observations

- **Workflow-overhead bytes outside construction:** rows 1-7 + 15 (pre-flight + reads + post-verify) = ~3,200 bytes. Of these, ~3,000 are the six `get_symbol` calls, each carrying a ~450-byte structured selector JSON.
- **`get_symbol` selector cost is significant:** each call's outbound is dominated by the `stableSymbolKey` (~150 chars) + the full structured selector object (~300 chars). Six methods costs ~3 KB of selectors alone. If the file had 20 methods, that's ~9 KB of selector overhead just to read bodies.
- **`submit_file` is the headline cost:** ~7,260 bytes for the 7,178-byte file body + the path/sessionId envelope. There is no avoiding this for a new file under the current tool surface.
- **Failed-attempt overhead:** ~2,670 bytes spent on 5 typed-add probes that all returned `An error occurred invoking 'add_method'.` / `'add_symbol'.` with no diagnostic detail. Server-side stack trace not visible to the agent; session log (`Working\Sessions\monitor-...json`) did not record the failures either.

### Session-total tokens (secondary, mid-session readout)

Not captured — single-session run; mid-session deltas would be dominated by hours of prior conversation cost. Skipped per the run-order constraint above.
- Result-equivalence vs Variant B: pass / fail / details:
- Notes:

## Comparison (Variant A complete, Variant B projected)

Variant B has not yet been run; the row below uses the projected total from the Variant B section.

|  | Variant B (raw, projected) | Variant A (workflow, actual) | Ratio A:B |
|---|---|---|---|
| Outbound payload bytes (ideal path) | ~7,340 | ~11,690 | **1.59x** |
| Outbound payload bytes (actual, with failed probes) | ~7,340 | ~14,360 | **1.96x** |
| Tool calls (ideal path) | 2 | 13 | 6.5x |
| Tool calls (actual) | 2 | 18 | 9x |
| Wall-clock time | <1 min (projected) | ~7 min (incl. WinMerge accept) | — |

**Interpretation:**

- On its worst-case scenario (new file from scratch), the workflow path costs ~1.6× the outbound bytes of a raw `Write` even when everything goes smoothly — and ~2× when the agent's first attempt at typed adds fails and has to fall back to whole-file submit. This **confirms** the suspicion that new-file creation is uneconomic for the workflow's per-call overhead. The workflow's intended payoff (skipping unchanged bytes on incremental edits to large existing files) does not apply here.
- The ~3 KB of `get_symbol` selector overhead is the largest non-payload cost; on a 20-method file that overhead alone would scale to ~9 KB and could exceed the payload itself.
- **Recommend a Variant C follow-up**: take an existing large repository file (200+ lines) and use `add_method` to append N async-sibling methods to it. That is the workflow's actual claimed strong case — incremental adds where the existing file body is preserved byte-for-byte by Roslyn AST splicing. The crossover question for Variant C is: at what method count does the workflow start beating "raw `Write` of the entire updated file"?

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
