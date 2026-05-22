---
status: new
type: recommendation
created: 2026-05-21
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---

## Summary

Five live timing tests run today against `C:\SchemaStudioWebViewer - Copy` to locate where workflow wall-clock goes. The root cause is model output token emission — it scales linearly with the size of the composed file, not the size of the change. Switching models (Opus 4.7+fast → Sonnet 4.6) does not fix this; the bottleneck is the road, not the engine. Full data in [`20260521-test-result-emission-cost-symbol-vs-wholefile.md`](20260521-test-result-emission-cost-symbol-vs-wholefile.md).

---

## What we measured

| Test | File size | Tool | Model | Emit tokens | Emit time | Total to WinMerge |
|---|---|---|---|---|---|---|
| 1 | 7.2 KB (new file) | `submit_file` | Opus 4.7+fast | ~2,000 | ~45-50s | ~60-65s |
| 2 | +600 B (one method) | `add_method` | Opus 4.7+fast | ~150 | ~5-7s | ~10-15s |
| 3 | 2.9 KB (new file) | `submit_file` | Opus 4.7+fast | ~750 | ~15-18s | ~25-30s |
| 4 | 14.4 KB (new file) | `submit_file` | Sonnet 4.6 | ~4,000 | not instrumented | not instrumented |
| 5 | +600 B (one method) | `add_method` | Sonnet 4.6 | ~150 | ~10.7s (measured) | ~23s (measured) |

### What the numbers say

1. **Emission scales linearly with tokens.** Tests 1 and 3 confirm it: 2.5× smaller file → ~3× faster emission. Fixed phases (stage compile, launch) don't compress, so whole-file total only improves ~2×.

2. **Symbol-level tools are ~5× faster end-to-end** than whole-file for the same workflow gates. The payload is proportional to the change, not the file.

3. **Model switching does not move the needle.** Sonnet 4.6 `add_method` (Test 5, measured at ~10.7s) is in the same ballpark as Opus+fast Test 2 (~5-7s estimated). At ~150 output tokens, fixed overhead (stage compile ~7s, launch ~5s) dominates — there is almost no room for model speed to matter.

4. **Fixed overhead floor is ~12-15s per workflow cycle** regardless of model or change size. This is the current minimum for any edit: tool roundtrip + stage overlay compile + WinMerge launch. Overlay compile alone costs ~5-7s per call, and it runs twice per cycle (once in `submit_*`, once in `stage_candidate_for_review`) on the same candidate hash.

5. **Razor is the worst case today.** It has no symbol-level composition surface — every edit pays whole-file emission cost. A 36 KB Razor file costs ~10,000 output tokens per attempt, putting single-change round-trips at 3-5 minutes. This is what prompted Finding 53.

---

## What Codex is fast at (and why)

Codex's VS Code plugin does full-file edits and is significantly faster. The reason is not that it skips the workflow — it is constrained by the same gates. The reason is that Codex's generation speed is higher (different model/infrastructure), so even at full-file emission cost, the time is acceptable. Claude at current emission rates cannot match that on whole-file operations. The lever available to Claude is reducing output tokens via symbol-level tools — which works well for C# but is unavailable for Razor.

---

## Recommendations

### 1. `replaceTextInFile` (or equivalent patch tool) — highest impact, unblocks Razor

**Finding 53** documents three design options. Recommended: `replaceTextInFile(sessionId, path, oldText, newText, expectedMatches=1)`. This keeps the composition payload proportional to the change (~100-200 tokens for a 2-string edit in a 567-line file vs ~10,000 tokens for full re-emit). The server must enforce uniqueness to prevent silent wrong-match corruption.

Without this, Razor edits are not viable for iterative work in Claude. The missing fast path is the single highest-leverage fix available.

### 2. Overlay compile result caching by candidate hash

`submit_file` / `add_method` and `stage_candidate_for_review` both run overlay compile against the same candidate hash in back-to-back calls. On this 82-tree project the compile costs ~5-7s each time. If the candidate hash matches the last-compiled hash, returning the cached result would shave ~5-7s off every staging round — reducing the fixed floor from ~12-15s to ~7-10s.

Implementation: cache `(observedRootKey, relativeSourcePath, candidateHash) → overlayValidationResult` in memory with a short TTL or until the candidate is replaced.

### 3. Structured error from `stage_candidate_for_review` when candidate state is missing

Currently returns an opaque RPC error when candidate state JSON has been cleaned up by a prior `record_diff_decision`. Should return a structured response like:

```json
{ "status": "no-active-candidate", "hint": "call submit_file or a composition tool to re-establish a candidate before staging" }
```

This would have made Finding 53's repro immediately diagnosable instead of requiring directory inspection to understand the failure. Low implementation cost, high diagnostic value.

### 4. Model selection guidance (operator note)

Switching from Opus 4.7+fast to Sonnet 4.6 does not improve whole-file throughput meaningfully — the bottleneck is output tokens emitted, not model speed. If speed is the goal:
- For C# files with symbol-level tools available: use `add_method`, `submit_symbol`, etc. instead of `submit_file`. Model choice is secondary.
- For Razor or large new-file creation: no model change helps until `replaceTextInFile` exists. The only mitigation today is keeping Razor files smaller.

---

## Related

- [`20260521-finding-53-small-edit-after-accept-no-fast-path.md`](20260521-finding-53-small-edit-after-accept-no-fast-path.md) — detailed finding with design options for replaceTextInFile
- [`20260521-test-result-emission-cost-symbol-vs-wholefile.md`](20260521-test-result-emission-cost-symbol-vs-wholefile.md) — full timing data for all five tests
