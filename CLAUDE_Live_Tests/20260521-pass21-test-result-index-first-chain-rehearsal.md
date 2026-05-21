---
title: Pass 21 — index-first tool surface end-to-end rehearsal
date: 2026-05-21
branch: claude/live-test-notes-20260517
head: 6947076 (local, unpushed merge of origin/main d07fd37)
session: monitor-20260521130506-02a9a5e5b0aa41a1a
status: passed-with-cleanup-and-1-new-finding
---

# Pass 21 — Index-first tool surface end-to-end rehearsal

Per RESTART.md task assignment: exercise the new index-first call chain (per `Docs/IndexApiSafetyFindings.md`) against the watched DBV2 solution. Doctrine: index/selector data is **discovery only**; a same-session freshness hop is mandatory before mutation.

## Pre-flight

| # | Tool | Result |
|---|---|---|
| 1 | `get_tool_manifest` | 9 index tools present (`get_solution_index_*`, `query_solution_index`, `find_indexed_symbols`, `refresh_solution_index*`, `refresh_file_and_index`, `get_indexed_symbol`). No `*_old` tools. |
| 2 | `get_monitor_status` | Watched solution attached: `C:\Schema Studio - DBV2\Schema Studio.sln`. UI root + McpServer root resolved. |
| 3 | `get_workflow_status` | WinMerge resolved at `C:\Program Files\WinMerge\WinMergeU.exe`. |
| 4 | `get_solution_index_status` | **Initially `isMissing:true, fileCount:0, symbolCount:0`** — operator confirmed it is *assumed* the rebuild task repopulates the index; the rebuild script apparently doesn't. |
| 5 | `refresh_solution_index` | Built clean in **3.4 s**: 84 files, 1105 symbols, 0 diagnostics. |
| 6 | `list_solutions` (Roslyn) | Schema Studio.sln active, status ready. |

## Index-first call chain (executed in order)

```text
find_indexed_symbols("McpAddApiFixture")        → 3 hits in McpAddApiFixture.cs (class + 2 ctors)
                                                   Note: index name-search did NOT return Nested or Value
                                                   (their names don't match the search text).
start_monitor_session                            → monitor-20260521130506-02a9a5e5b0aa41a1a
check_file_hash(session, file)                  → currentSha=6592eb30…, knownInSession:false (first fetch),
                                                   changedSinceLastFetch:false. Hash matches index fileHash exactly.
get_source_map(file, mode:selector)              → 5 live symbols (class + Nested + Value + 2 ctors),
                                                   sha=6592eb30…. Agrees with check_file_hash and with the index.
get_symbol(ctor McpAddApiFixture(string))        → text: `    public McpAddApiFixture(string parm) { }\r\n`
submit_symbol (replace with `_ = parm;`)         → status:candidate-updated, candidateHash=ac6ebb93…,
                                                   syntax:0 errors, overlay:3 errors (pre-existing
                                                   McpOverlayGateFixture* pollution per RESTART).
                                                   operationCount:2 on this session's first user op (see Finding 45).
[Read Working candidate file]                    → confirmed body-only delta, no inherited stale edits.
stage_candidate_for_review                       → stagedRecordId 20260521_080706955_…_McpAddApiFixture_0c3fccfc.
                                                   serverDerivedMetadata: empty symbolsAdded/Removed/usingsAdded/Removed
                                                   (correct for body-only replacement).
launch_staged_diff                               → validationGateDecision: force_review (host dialog accepted
                                                   force-review past pre-existing overlay pollution).
                                                   WinMerge launched, PID 45580.
[Operator votes accepted]                        → WinMerge saved staged candidate into watched source.
record_diff_decision(accepted)                   → classification: accepted (NOT accepted-normalized).
                                                   decisionMatchesClassification:true.
                                                   currentHash=ac6ebb93… == stagedHash. queueStatus:accepted.
```

## What this proved

1. The index-first chain works end-to-end with an unambiguous selector. Index → freshness hop → live source map → get_symbol → submit_symbol → stage → diff → accept all chained with no manual repair.
2. **The freshness hop is meaningful, not ceremonial.** Index `fileHash`, `check_file_hash` `currentSha256`, and live `get_source_map` `sha256` all matched. Mutation went forward on the basis of *current* agreement, not cached agreement — the doctrine is honoured by the tool surface itself.
3. **`find_indexed_symbols` is name-scoped.** Searching for the containing-type name does not surface nested types or members whose own name differs. A future agent must not assume "I searched for the class, therefore I see all its members." For full file shape, follow up with `get_solution_index_tree` scoped to file, or `get_source_map(file, selector)` (the latter is also the freshness hop).
4. **Pre-existing overlay pollution doesn't block a clean single-file edit, but it does invoke the host force-review dialog** — operator must approve. This is the intended contract per CLAUDE.md "Overlay gate cancelled or review not launched -> stop the current multi-file chain." For a single-file pass, force-review is the right escape hatch.
5. **`operationCount:2` on the first user op of a fresh session is a misleading signal** — see Finding 45.

## Hash trail

| Stage | Hash | Source |
|---|---|---|
| Index baseline | `6592eb30f00323fa5b9d4231bccd64ca7920ac0828137921c5a6e0e25ee76198` | `find_indexed_symbols.fileHash` |
| Session check | `6592eb30…` | `check_file_hash.currentSha256` |
| Live source map | `6592eb30…` | `get_source_map.files[0].sha256` |
| Candidate baseline | `6592eb30…` | `submit_symbol.baselineHash` |
| Candidate proposal | `ac6ebb93839967e0e390abc5b47cf7dd27bdd5837571b4fb992de3584ef6a163` | `submit_symbol.candidateHash` |
| Staged proposal | `ac6ebb93…` | `stage_candidate_for_review.stagedHash` |
| Watched after accept | `ac6ebb93…` | `record_diff_decision.currentHash` |

Vote (`accepted`) + hash (`currentHash == stagedHash`) agreed → `classification: accepted`. All-or-none gate held.

## New finding from this pass

- [Finding 45 — `operationCount` is not a reliable inherited-work signal](20260521-finding-45-operationcount-misleading-on-fresh-session.md)

## Open items carried forward (not regressed by this pass)

- `McpOverlayGateFixture*.cs` stale pollution still in Working — three CS0103/CS0246 errors visible in every overlay this pass. Already known per RESTART; not new this pass.
- Rebuild task evidently does **not** repopulate the solution index. Operator flagged they would check on this. Recording here so it doesn't get lost: a fresh `get_solution_index_status` after `Tools/Rebuild-MonitorMcp.ps1` returned `isMissing:true`.
- RESTART memory `feedback_set_restart_pointers_after_every_build.md` says "Write `.claude-local/RESTART.md` before every Claude-extension recycle" — refreshed at end of this session.

## Cleanup phase (post-accept, same session)

The forward accept above left the watched fixture mutated. The operator pushed back on accumulated fixture drift across passes ("you should reset your own test fixtures when the test is over") and clarified the preferred pattern: keep fixture templates as `.txt` outside the compile and copy them in at test start. Pass 21 executed the interim revert path as a one-time backfill, then set up the template directory for future passes.

### Revert chain (one extra `submit_symbol → stage → diff → accept`)

```text
submit_symbol (ctor McpAddApiFixture(string), code = "    public McpAddApiFixture(string parm) { }")
    → status: candidate-updated, operationCount: 1 (RESET from 2 in the forward op — see Finding 45 addendum),
       baselineHash: ac6ebb93… (current watched, NOT the original 6592eb30 — baseline tracks watched state),
       candidateHash: 6592eb30… (matches pre-pass baseline byte-for-byte)
stage_candidate_for_review
    → stagedRecordId 20260521_081335464_…_c717acd6,
       originalHash: ac6ebb93…, stagedHash: 6592eb30…,
       serverDerivedMetadata: empty added/removed arrays (correct, body-only revert)
launch_staged_diff
    → validationGateDecision: force_review (same pre-existing overlay pollution),
       WinMerge launched, PID 9988
[Operator votes accepted]
record_diff_decision(accepted)
    → classification: accepted (exact, not normalized),
       decisionMatchesClassification: true,
       currentHash: 6592eb30… == stagedHash. queueStatus: accepted.
```

Watched fixture is back to `6592eb30…` (pre-pass baseline). Net source change from Pass 21 on watched: zero.

### Template captured for future passes

Operator instruction adopted: maintain canonical fixture sources as `.txt` files in `CLAUDE_Live_Tests/Fixtures/`. At the start of each future pass, `submit_file` the template content into the watched `.cs` and accept — this gives the pass a known-good starting state regardless of what previous passes left behind.

- New directory: `CLAUDE_Live_Tests/Fixtures/`
- New README: `CLAUDE_Live_Tests/Fixtures/README.md` (rules + canonical-hash registry)
- New template: `CLAUDE_Live_Tests/Fixtures/McpAddApiFixture.cs.txt` — copied byte-for-byte from the watched fixture immediately after the revert was accepted, verified `sha256: 6592eb30f00323fa5b9d4231bccd64ca7920ac0828137921c5a6e0e25ee76198` matches the watched canonical.

Memory entry `feedback_reset_test_fixtures_when_done.md` rewritten to record the template-then-copy pattern (replacing the earlier "revert at end of pass" approach).

### Cleanup-phase hash trail

| Stage | Hash | Source |
|---|---|---|
| Watched after forward accept | `ac6ebb93…` | live read |
| Revert candidate baseline | `ac6ebb93…` | `submit_symbol.baselineHash` (correctly tracks current watched, not original) |
| Revert candidate proposal | `6592eb30…` | `submit_symbol.candidateHash` |
| Revert staged proposal | `6592eb30…` | `stage_candidate_for_review.stagedHash` |
| Watched after revert accept | `6592eb30…` | `record_diff_decision.currentHash` |
| Template `.cs.txt` after capture | `6592eb30…` | `Get-FileHash` |

All five hashes agree on `6592eb30…` at end of pass.
