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

Variant C runs the token-comparison test in the workflow's **strong case** — incremental member-level adds against an existing watched, Roslyn-loaded file — and exercises the **iterate-build-and-fix amortization** that Variants A and B could not measure. Baseline comparison is a projected Variant D (raw `Read` + raw `Write × K` for K iterations, each re-emitting the full augmented file).

The headline result: in the workflow's home turf the per-iteration marginal cost is ~10× smaller than raw `Write`, and the workflow wins even at K=0 (single clean pass). This **complements** the Variant A/B finding — A/B showed the workflow loses on new-file scenarios; C shows it wins decisively on existing-file iteration.

See the prior test-result for the new-file scenario: [20260518-token-comparison-plan-schemaobjectcolumn-async.md](20260518-token-comparison-plan-schemaobjectcolumn-async.md).

## Target

- File: [SchemaStudio.Data\SchemaObjectColumnRepositoryAsync.cs](../../Schema%20Studio%20-%20DBV2/SchemaStudio.Data/SchemaObjectColumnRepositoryAsync.cs) — the same file Variant A created, now Roslyn-loaded in watched solution. Pre-C size 7,178 bytes / 8 symbols (5 async methods + ctor + class + field).
- Goal: append two new helper methods — `CountByObjectAsync(int schemaObjectId)` returning `Task<int>` and `ExistsByIdAsync(int schemaObjectColumnId)` returning `Task<bool>`.
- **Bug-and-fix iteration shape:** op1 deliberately introduces a `_connection` (vs `_connectionString`) typo so the workflow's overlay validation surfaces a real CS0103 pre-Operator-review; op3 fixes it via `submit_symbol`.

## Shared Test Prompt

> In `SchemaObjectColumnRepositoryAsync.cs`, append two new methods:
>
> - `public async Task<int> CountByObjectAsync(int schemaObjectId)` — runs `SELECT COUNT(*) FROM SchemaObjectColumn WHERE SchemaObjectId = @schemaObjectId` via Dapper async.
> - `public async Task<bool> ExistsByIdAsync(int schemaObjectColumnId)` — runs `SELECT CAST(1 AS bit) FROM SchemaObjectColumn WHERE SchemaObjectColumnId = @schemaObjectColumnId` via Dapper async; coalesce null to `false`.
>
> Both methods use classic `using (var conn = new SqlConnection(_connectionString)) { ... }` block syntax matching the existing file. Insert after `SaveAllAsync`. Preserve all other content byte-for-byte.

A bug+fix iteration is part of the C measurement: op1 introduces a `_connection` typo (CS0103); workflow's overlay validation surfaces it; op3 (`submit_symbol`) replaces just the broken method body.

## Pre-flight

- Branch `claude/live-test-notes-20260517`, no uncommitted product-source from C.
- Watched repo compiles clean: Roslyn `get_diagnostics(severity=error) = []`. Solution active, 6 projects, status `ready`.
- Monitor MCP surface present at session start; `get_workflow_status` returns WinMerge resolution.
- Target file present in Roslyn (hash `e65b3032...` matches Variant A's record).

## Discovery

Normal — not artificially trimmed. The task ("append two new methods that don't reference existing bodies") doesn't require `get_symbol × N` reads of the existing methods, so those are naturally skipped. One `get_source_map(selector mode)` is the entire discovery surface.

- `get_source_map(SchemaObjectColumnRepositoryAsync.cs, mode: selector)`: 8 symbols, 2840 estimatedTokenProxy, parseStatus ok, 0 diagnostics.

## Workflow Path Actually Taken

Session `monitor-20260519000019-138ecd02021d43d7b`.

| # | Phase | Tool | Outbound bytes (approx.) | Result |
|---|---|---|---|---|
| 1 | Pre-flight | `get_workflow_status` | ~2 | clean |
| 2 | Pre-flight | `list_solutions` (Roslyn) | ~2 | 1 solution, status ready |
| 3 | Pre-flight | `get_diagnostics` (Roslyn, error) | ~22 | `[]` |
| 4 | Discovery | `get_source_map(selector)` | ~120 | 8 symbols, parseStatus ok |
| 5 | Session | `start_monitor_session` | ~50 | session created |
| 6 | Op1 | `add_method(CountByObjectAsync, buggy)` | ~590 | candidate-updated, **overlay flagged CS0103 line 214** as expected — workflow caught the bug pre-review |
| 7 | Op2 | `add_method(ExistsByIdAsync, clean)` | ~650 | candidate-updated, composed (op count 2), overlay still flagged op1's bug |
| 8 | Stage S1 | `stage_candidate_for_review` | ~110 | staged `...fe4d74bb`, overlay still has CS0103 |
| 9 | Op3 fix | `submit_symbol(CountByObjectAsync, fixed)` | ~940 | candidate-updated, op count 3, **overlay clean** — submit_symbol composed against working candidate, replaced just the broken method body |
| 10 | Stage S2 | `stage_candidate_for_review` | ~110 | staged `...a17bad18`, **overlay clean**, both new methods present (CountByObjectAsync textHash changed `5caf00f9 → 0f959f58` confirming the fix landed) |
| 11 | Diff | `launch_staged_diff(S2)` | ~110 | WinMerge PID 36724 |
| 12 | Decision | `record_diff_decision(accepted)` | ~350 | `classification: accepted` (exact byte match, not normalized) |
| 13 | Post-verify | `get_diagnostics` (Roslyn, error) | ~22 | `[]` — clean compile |
| 14 | Post-verify | `PowerShell` (file size + sha + tail) | ~280 | 8,057 bytes, sha `e197ee19...` matches stagedHash |

**Subtotals:**

- Actual outbound bytes (everything): **~3,360 bytes**
- Construction-only (rows 5–12, session start through diff decision): **~2,910 bytes**
- Per-iteration marginal cost of the fix step (rows 9–10, `submit_symbol` + restage): **~1,050 bytes**

### Number of tool calls

- Actual: 14 tool calls (including post-verify).
- Construction-only: 8 tool calls.

### Produced-file result

- 8,057 bytes (CRLF + BOM, Windows-native via Monitor). sha256 `e197ee19a85f23c218aec27373854af561be894d525f58b5358b4598e9f6693c`.
- Compiles clean (`get_diagnostics(severity=error) = []`).
- 2 new methods added (`CountByObjectAsync`, `ExistsByIdAsync`), all 5 prior methods preserved byte-for-byte.

## Variant D — Baseline Projection (raw Read + Write × K)

Projected, not executed (would burn a clean session and a full file Write each iteration).

For an end-state of 8,057 bytes augmenting a 7,178-byte original:

**K=0 (one-shot clean Write, no iteration):**

- `Read` original: ~90 bytes
- `Write` augmented file: ~7,990 bytes (8,057-byte content + JSON escape overhead + path arg)
- **Total: ~8,080 bytes**

**K=1 (one fix iteration — initial-with-bug, then re-Write with fix):**

- `Read` original: ~90 bytes
- `Write` initial (with bug): ~7,990 bytes
- *Operator/build catches bug*
- `Write` fixed (whole-file re-emit, raw `Write` has no partial mode): ~7,990 bytes
- **Total: ~16,070 bytes**

**K=2 (two iterations — e.g., second method added in a later round):**

- 1 `Read` + 3 `Write` = **~24,060 bytes**

## Comparison

|  | Variant C (workflow, actual) | Variant D K=0 (projected) | Variant D K=1 (projected) | Variant D K=2 (projected) |
|---|---|---|---|---|
| Outbound payload bytes | ~3,360 | ~8,080 | ~16,070 | ~24,060 |
| Tool calls | 14 (8 construction) | 2 | 3 | 4 |
| Wall-clock | ~3 min (incl. WinMerge accept) | <1 min projected | <2 min projected | <3 min projected |
| Bytes per iteration step (marginal) | ~1,050 | ~7,990 | ~7,990 | ~7,990 |
| **Ratio C:D** | — | **0.42×** (C 2.4× cheaper) | **0.21×** (C 4.8× cheaper) | **0.14×** (C 7.2× cheaper) |

**Per-iteration marginal cost ratio: C ~1,050 bytes vs D ~7,990 bytes ≈ 7.6× advantage** for the workflow per fix cycle.

## Interpretation

- **The workflow wins even at K=0.** A clean one-shot Variant C beats raw Write 2.4× because each `add_method` sends only the new method body (~500 bytes) instead of the full file (~8 KB). The Roslyn AST splices the method into the unchanged file body locally; only the delta crosses the wire. This is the architectural payoff that's missing from new-file scenarios (Variant A) — and exactly what the discovery + pre-flight tax was paid for.
- **Iteration amortizes hard.** Each additional fix in D costs ~8 KB (re-emit whole file); each fix in C costs ~1 KB (one `submit_symbol` + restage). The K=2 ratio is 7.2× and grows linearly with K. For a maintenance workflow where any non-trivial change goes through 2–3 review cycles, the C-mode total cost is amortized to ~3.5–4 KB regardless of K, while D scales O(K × filesize).
- **Overlay validation catches bugs pre-Operator-review.** Op1's `_connection` typo was flagged by the workflow's own overlay compilation against the project graph as CS0103, BEFORE WinMerge launched. Raw `Write` mode has no equivalent — bugs are caught only at Operator-build-and-test time, requiring a full Write retry. This is a quality story on top of the byte-cost story.
- **submit_symbol composes within the V1 candidate flow.** The fix step worked seamlessly — `submit_symbol(CountByObjectAsync, fixed)` returned `status: candidate-updated`, `operationCount: 3`, composing against the same Working candidate as the prior `add_method` ops. No need to restage between op1 and op3; the candidate accumulates body-replacement edits alongside body-add edits.

## Crossover Analysis

C cost ≈ 2,510 (setup: pre-flight + discovery + session + finalize) + K × 1,050 (per fix cycle) + ~1,250 (the two `add_method` baseline ops).
D cost ≈ 90 (Read) + K × 7,990 (each iteration is a full file Write).

Crossover where C = D:
- 3,760 + K × 1,050 = 90 + K × 7,990
- 3,670 = K × 6,940
- K = **0.53**

C wins for any K ≥ 1 iteration and effectively breaks even at K = 0.5. In practice, K = 1 (one bug surfaced and fixed) is unavoidable on non-trivial edits — and even clean one-shot edits favor C because the discovery overhead is amortized across the two `add_method` deltas vs one full-file Write.

## Observations

- **CLAUDE.md is stale on `submit_symbol`.** The "Working Candidate Composition Flow" section lists `submit_symbol` as "not yet promoted" (still creating staged records directly). Observed behavior: `submit_symbol` returned `status: candidate-updated` with V1 candidate fields (`candidateFilePath`, `candidateStatePath`, `candidateHash`, `operationCount`) and NO staged-record id. It has been V1-promoted. CLAUDE.md should remove it from the not-yet-promoted list.
- **`add_method`'s overlay validation works exactly as advertised.** Op1's CS0103 surfaced inside the first `add_method` response (no need to wait for `stage_candidate_for_review`). Workflow detected the bug ~5 seconds after the typo was written — at the speed of one tool round-trip, not one full Operator-review cycle.
- **Symbol composition across tool kinds works smoothly.** Mixing `add_method` (new insertion) with `submit_symbol` (body replacement) in the same Working candidate composes correctly — the candidate accumulates both kinds of edits transparently.

## Defaults Picked Without Operator Confirmation

1. Method choice: two read-only helpers (`CountByObjectAsync`, `ExistsByIdAsync`) — chosen for being structurally simple, Dapper-async-shaped, and not requiring any cross-file changes. Override if a different real-life shape is preferred.
2. Bug shape: undefined-identifier typo (`_connection` instead of `_connectionString`). Chosen because it's a CS0103 — a class of error overlay validation reliably catches. Compile-fail bugs are the workflow's clearest selling point; runtime/logic bugs would not trigger overlay validation.
3. Variant D: projected only, not executed. The projection is straightforward arithmetic — raw `Write` payload size scales with file size, no behavior to discover. Re-run with an actual D if a measured number matters.

## Open Questions

1. Worth running a Variant E with K=3+ to see if the C marginal stays ~1 KB or whether session/state bookkeeping grows? Expected from this run: stays flat.
2. Should `submit_symbol`-as-V1 (per observation above) be filed as a finding, or is it expected and CLAUDE.md just needs updating?
3. Is there a pre-existing-file scenario worth measuring where the *existing* file is much larger (e.g., 500+ lines, 20+ methods) to test whether the C cost stays flat regardless of file size? Expected: yes, because Roslyn AST splicing means only deltas cross the wire.
