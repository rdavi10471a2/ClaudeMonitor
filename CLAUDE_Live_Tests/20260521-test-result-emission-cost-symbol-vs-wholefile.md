---
status: new
type: test-result
created: 2026-05-21
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---

## Summary

Two back-to-back live edits against the same C# repository file in the watched WebViewer Copy solution, timed with phase-by-phase breakdown, to localize where workflow wall-clock goes. Result: ~75% of wall-clock is model output emission, and symbol-level composition (`add_method`) is ~5x faster end-to-end than whole-file submission (`submit_file`) on the same gates because emission scales with output tokens, not file size. Companion evidence for [`20260521-finding-53-small-edit-after-accept-no-fast-path.md`](20260521-finding-53-small-edit-after-accept-no-fast-path.md) — the C# tests show the same workflow gates, isolating that the Razor pain is specifically the missing patch-style surface.

## Test 1: New file via `submit_file` (whole 7 KB C# file)

Created `SchemaStudio.Data\Repositories\DatabaseDomainAsyncRepository.cs` as a structural copy of `DatabaseDomainRepository.cs`, class renamed, no callers wired up.

| Phase | Wall-clock | Dominant cost |
|---|---|---|
| `find_file` (discovery) | ~1s | tool roundtrip |
| `get_file` (read 7 KB template) | ~5-7s | server I/O + transport |
| **`submit_file` (emit ~2K tokens out)** | **~45-50s** | **model emission** |
| `stage_candidate_for_review` | ~3-5s | server: metadata + overlay compile |
| `launch_staged_diff` (force-review) | ~2s | gate + WinMerge spawn |
| **Total to WinMerge open** | **~60-65s** | |

Staged record: `20260521_171443344_stage_candidate_for_review_DatabaseDomainAsyncRepository_1ccb84bb`
Accepted hash: `a599c2a259822b2a68f1ba8bc6f949880c96a49036974686431f383e192bb54a` (currentHash == stagedHash)

## Test 2: Add one method to existing file via `add_method` (symbol-level)

Added `GetByIdsAsync(IEnumerable<int> databaseDomainIds)` to the same `DatabaseDomainAsyncRepository` class. Same workflow, same file, same overlay-error force-review gate.

| Phase | Wall-clock | Dominant cost |
|---|---|---|
| `add_method` (emit ~150 tokens — method body only) | **~5-7s** | model emission (much smaller) |
| `stage_candidate_for_review` | ~3-5s | server: metadata + overlay compile |
| `launch_staged_diff` (force-review) | ~2s | gate + WinMerge spawn |
| **Total to WinMerge open** | **~10-15s** | |

Staged record: `20260521_171858062_stage_candidate_for_review_DatabaseDomainAsyncRepository_00bf5cdc`
Accepted hash: `eade9863806f3f3a57a71b4e2aa7b445230953dc0e5bb598d3ebb032d5cb2921` (currentHash == stagedHash)
serverDerivedMetadata.symbolsAdded reported `GetByIdsAsync` correctly with hashed body.

## Head-to-head

| | Test 1 (`submit_file`) | Test 2 (`add_method`) | Ratio |
|---|---|---|---|
| Output tokens emitted | ~2,000 | ~150 | 13× more in Test 1 |
| Total to WinMerge | ~60-65s | ~10-15s | ~5× slower in Test 1 |
| Server overhead (stage + launch) | ~5-7s | ~5-7s | same (fixed cost) |
| Composition cost (emission) | ~45-50s | ~5-7s | ~7× slower in Test 1 |

## Test 3: New file via `submit_file` (whole 2.9 KB C# file — scaling validation)

Created `SchemaStudio.Data\Repositories\SourceViewAsyncRepository.cs` as a structural copy of `SourceViewRepository.cs` (smaller template at 2913 bytes), class renamed, no callers wired up. Goal: confirm emission cost scales linearly with file size.

| Phase | Wall-clock | Dominant cost |
|---|---|---|
| `find_file` (discovery) | ~1s | tool roundtrip |
| `get_file` (read 2.9 KB template) | ~3-5s | server I/O + transport |
| **`submit_file` (emit ~750 tokens out)** | **~15-18s** | **model emission** |
| `stage_candidate_for_review` | ~3-5s | server: metadata + overlay compile |
| `launch_staged_diff` (force-review) | ~2s | gate + WinMerge spawn |
| **Total to WinMerge open** | **~25-30s** | |

Staged record: `20260521_173809783_stage_candidate_for_review_SourceViewAsyncRepository_dfd8a583`
Accepted hash: `481328613e42a0977b65a45340b21e597f41834cd9e126d9e6f38655b36a3d69` (currentHash == stagedHash)

## Three-test scaling

| Test | File size | Emit tokens | Emit wall-clock | Total to WinMerge | Composition tool |
|---|---|---|---|---|---|
| 1 | 7.2 KB | ~2,000 | ~45-50s | ~60-65s | `submit_file` (whole file) |
| 2 | +~600 B (one method into existing file) | ~150 | ~5-7s | ~10-15s | `add_method` (symbol) |
| 3 | 2.9 KB | ~750 | ~15-18s | ~25-30s | `submit_file` (whole file) |

Test 1 → Test 3: file is 2.5× smaller, emission is ~3× faster, total wall-clock is ~2× faster (fixed-overhead phases don't compress). Linear-in-tokens model holds.

## Test 4: New file via `submit_file` (whole 14.4 KB C# file — Sonnet 4.6, no /fast)

Created `SchemaStudio.Data\Repositories\DatabaseAsyncRepository.cs` as a structural copy of `DatabaseRepository.cs` (two classes: `DatabaseRepository` + `DatabaseLookupRelationshipRepository`, both renamed to async variants). Model: Sonnet 4.6 with /fast off. Goal: compare whole-file emission speed vs Opus 4.7+fast (Tests 1 and 3).

| Phase | Wall-clock | Dominant cost |
|---|---|---|
| `find_file` (discovery) | ~1s | tool roundtrip |
| `get_file` (read 14.4 KB template) | ~5-8s | server I/O + transport |
| **`submit_file` (emit ~4,000 tokens out)** | **not instrumented** | **model emission** |
| `stage_candidate_for_review` | ~3-5s | server: metadata + overlay compile |
| `launch_staged_diff` (force-review) | ~2s | gate + WinMerge spawn |
| **Total to WinMerge open** | **operator-observed** | |

Staged record: `20260521_174753122_stage_candidate_for_review_DatabaseAsyncRepository_c20d4236`
Accepted hash: `e9e6478304523d782133863cddcc9f6392a95da10605c042e75dbdf10a0800fc` (currentHash == stagedHash)

Note: submit_file phase was not instrumented; operator did not capture stopwatch. Extrapolating from linear-in-tokens model: 14.4 KB at ~4,000 tokens should be ~2× Test 1 emission (~90-100s on Opus+fast). Whether Sonnet 4.6 is faster or slower on whole-file emission is not resolved by this test.

## Test 5: Add one method to existing file via `add_method` (Sonnet 4.6, no /fast — instrumented)

Added `GetByIdsAsync(IEnumerable<int> databaseIds)` to `DatabaseAsyncRepository`. Phases measured with `date` shell timestamps.

| Phase | Start | End | Measured |
|---|---|---|---|
| `add_method` (~150 tokens out) | 17:54:01.312 | 17:54:12.042 | **~10.7s** |
| `stage_candidate_for_review` | 17:54:23.998 | 17:54:30.994 | **~7.0s** |
| `launch_staged_diff` | 17:54:30.994 | 17:54:36.389 | **~5.4s** |
| **Total tool-chain** | | | **~23s** |

Note: ~12s gap between add_method end and stage start is Claude processing overhead, not tool cost. Pure tool-chain: ~23s. Compare to Opus+fast Test 2 estimate of ~10-15s (uninstrumented). Sonnet 4.6 symbol-level is roughly comparable; fixed overhead (stage compile ~7s, launch ~5s) dominates — model speed barely moves the needle at this payload size.

Staged record: `20260521_175428271_stage_candidate_for_review_DatabaseAsyncRepository_5402d42e`
Accepted hash: `e9ac19b998cd7f111101720e9fc4ae9826ffb6396e29d20409fb1c1658fde2dc` (currentHash == stagedHash)

## Full scaling table

| Test | File size | Emit tokens | Emit wall-clock | Total tool-chain | Model | Composition tool |
|---|---|---|---|---|---|---|
| 1 | 7.2 KB | ~2,000 | ~45-50s | ~60-65s | Opus 4.7+fast | `submit_file` |
| 2 | +~600 B (one method) | ~150 | ~5-7s | ~10-15s | Opus 4.7+fast | `add_method` |
| 3 | 2.9 KB | ~750 | ~15-18s | ~25-30s | Opus 4.7+fast | `submit_file` |
| 4 | 14.4 KB | ~4,000 | not instrumented | not instrumented | Sonnet 4.6 | `submit_file` |
| 5 | +~600 B (one method) | ~150 | part of ~10.7s | ~23s (instrumented) | Sonnet 4.6 | `add_method` |

Tests 1→3 confirm linear-in-tokens emission on Opus+fast. Test 5 shows symbol-level on Sonnet 4.6 is in the same order of magnitude as Test 2 — fixed overhead (stage compile + launch) is ~12s regardless of model, leaving little room for emission speed to matter at small payload sizes.

## Conclusion

Emission cost dominates the workflow wall-clock on edits of any file size beyond trivial. The five workflow gates (`get_file` → `submit_*` → `stage` → `launch` → `record_decision`) each cost 1-5s; only the composition phase scales with the size of the change. Symbol-level tools (`add_method`, `submit_symbol`, `set_type_partial`, etc.) keep the composition payload proportional to the change rather than the file. Razor today has only `submit_file`, so every edit pays whole-file emission cost — see Finding 53 for design options.

**Model switching (Opus→Sonnet) does not fix the problem.** The bottleneck is the road, not the engine: whole-file emission on a 14 KB file will cost ~4,000 output tokens regardless of model. The workflow overhead (stage compile ~7s, launch ~5s, tool roundtrips) sets a floor of ~15s even for zero-token edits. The only lever that actually moves the wall-clock is reducing output tokens — which requires patch/replace-in-file tooling (Finding 53) or symbol-level composition surfaces.

## Side observations

1. **Overlay-compile redundancy.** `submit_file` / `add_method` and `stage_candidate_for_review` both ran overlay compile against the candidate (returned identical CS0433 diagnostics in back-to-back responses in both tests). On this 82-tree project the overlay compile cost ~3-7s per call. If the candidate hash is unchanged between submit_* and stage, the second compile is redundant — caching the prior result keyed by `candidateHash` would shave ~3-7s off every staging round.

2. **Pre-existing CS0433 SqlConnection ambiguity** in the WebViewer Copy solution (`Microsoft.Data.SqlClient` v6 and v7 both resolved) is not introduced by these edits. All tests required `forceReviewOnOverlayErrors: true`. The force-review gate worked as documented.

3. **`add_method`'s `afterSymbol` parameter behaved correctly** in both Test 2 and Test 5 — method inserted directly after the named symbol at the expected line position.

4. **New-file creation cannot be patched.** Whatever patch-style tool Codex builds for Razor edits will not help the new-file case — the full content must be emitted at least once. Finding 53's design options are scoped accordingly.

## Notes

Session id used for all tests: `monitor-20260521213704-b5584f2472ac4e648`.
Watched solution: `C:\SchemaStudioWebViewer - Copy\SchemaStudioWebViewer.sln`.
Tests 1-4 wall-clock figures are estimates from request/response observation, not instrumented. Test 5 phases are instrumented with shell timestamps; sub-second precision is real but includes tool roundtrip overhead.
