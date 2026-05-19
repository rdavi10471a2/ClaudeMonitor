---
status: fixed
type: test-result
created: 2026-05-19
processed: true
processedBy: Codex
processedAt: 2026-05-19
resolution: Codex commit 0102077 ("Fix force review and constructor metadata") verified end-to-end in Pass 19 against fresh 11:44 binaries. Findings 35, 36, 37 all pass.
resolutionCommit: 0102077
---

## Summary

Pass 19 (post-recycle) verified Codex's `0102077` fix against fresh binaries built at 11:44:43–11:44:44 (all three: `MonitorBaseClaude.exe`, `MonitorBaseClaude.McpServer.exe`, `Tools\McpHubBridge\bin\Debug\net10.0\McpHubBridge.exe`). Both `validationGateDecision` branches (force_review / cancel_for_fix) and both constructor metadata pipelines (`symbolsAdded` / `symbolsRemoved`) behave as designed.

A separate observation worth flagging: testing ergonomics. On the first Finding 37 attempt the `McpAddApiFixture.cs` Working candidate carried a leftover `McpAddApiFixture(int seed)` ctor from a prior pass, producing `operationCount: 2` and a CS0111 duplicate-member error. This is the **intended** baseline behaviour — CLAUDE.md states "later ops on the same Working candidate refuse with `candidate-baseline-stale` if watched source changed underneath," which implies the contrapositive: if watched source hash still matches, prior in-progress work is preserved. The Pass 18 leftover edit was correctly retained because watched source was unchanged between Pass 18 and Pass 19. What broke was my **test-pass assumption** that a new session means a fresh candidate; that contract doesn't exist. Logged as ergonomics suggestion below, not a bug.

## Pre-flight

| Check | Result |
| --- | --- |
| Branch / HEAD | `claude/live-test-notes-20260517`, HEAD `10c3853` (merge containing Codex `0102077`) |
| Binary build times | All three at 2026-05-19 11:44 (post-`0102077` 10:51) |
| WinForms host PID | 33460 (StartTime 11:49) |
| `get_monitor_status` / `get_workflow_status` / `get_tool_manifest` / `get_staging_guide` | All OK |
| `list_solutions` (Roslyn) | OK — `Schema Studio.sln`, status `ready` |
| `start_monitor_session` | OK — sessions `monitor-20260519171129-…` (gate tests) and `monitor-20260519171504-…` (ctor metadata tests) |
| Source map of `McpAddApiFixture.cs` | OK — 7 symbols, 1 parameterless ctor, sha256 `e9322d86…` |
| No `*_old` edit tools | Confirmed via deferred-tool listing |

## Trial matrix

| Trial | Finding | Staged record | Operator action | `validationGateDecision` | Server status | Vote+hash classification |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 35 Force | `…McpOverlayGateFixture4_9fe37ac9` | Force WinMerge Review → close-no-save | `force_review` | `winmerge-launched` (PID 17932) | `rejected` |
| 2 | 35 Cancel | `…McpOverlayGateFixture5_de3ccb83` | Cancel Review | `cancel_for_fix` | `overlay-errors-review-cancelled` | (n/a — review not launched, queue blocked-overlay-validation as designed) |
| 3 | 37 stage | `…McpAddApiFixture_e35b9111` | Force → Save | `force_review` | `winmerge-launched` (PID 34980) | `accepted` |
| 4 | 36 stage | `…McpAddApiFixture_674c48c9` | Force → Save | `force_review` | `winmerge-launched` (PID 33016) | `accepted` |

## Finding 35 — Force branch (was: button silently inert) — VERIFIED

Pre-fix: `Force WinMerge Review` produced `cancel_for_fix` regardless of operator click (per `20260519-test-result-finding-35-reproduced-force-button.md`).

Post-fix:

- Staged record `20260519_121216287_stage_candidate_for_review_McpOverlayGateFixture4_9fe37ac9` (deliberate CS0246 reference to `NonExistentTypeShouldFailOverlay`, plus residual leftover errors in unrelated Fixture/Fixture3 mirrors).
- `launch_staged_diff` response: `status: winmerge-launched`, `validationGateStatus: completed`, `validationGateDecision: force_review`, `validationGateMessage: "Overlay compile errors were force-reviewed before WinMerge launch."`, WinMerge PID 17932.
- Operator closed WinMerge without saving. `record_diff_decision(rejected)` → `classification: rejected`, `decisionMatchesClassification: true`, `currentHash == originalHash` (new-file baseline preserved).

The dialog button → DialogResult → `forceReview` boolean chain in `McpProxyHubService.cs:224` (now `result == DialogResult.OK`, per restart-pointer source inspection) drives the server correctly. Cancel branch still works (trial 2 below).

## Finding 35 — Cancel branch — VERIFIED

- Staged record `20260519_121423867_stage_candidate_for_review_McpOverlayGateFixture5_de3ccb83` (deliberate CS0246 on `AnotherUndefinedTypeForCancelBranch`).
- `launch_staged_diff` response: `status: overlay-errors-review-cancelled`, `validationGateDecision: cancel_for_fix`, `validationGateMessage: "Operator cancelled review so the agent can fix overlay compile errors first."`, `nextStep` directs fix-and-retry. WinMerge correctly did NOT launch.

The cancel branch was already known-working from the original Finding 35 trial 4; reconfirmed against the fresh binary.

## Finding 37 — `add_constructor` `symbolsAdded` populated — VERIFIED

Pre-fix: `add_constructor` produced an empty `serverDerivedMetadata.symbolsAdded` even though the candidate clearly added the ctor.

Post-fix:

- `add_constructor` on `McpAddApiFixture.cs` with `containingType: "McpAddApiFixture"`, `declaration: "public McpAddApiFixture(int seed) { _field = seed; }"`, `afterSymbol: "Property"`.
- `stage_candidate_for_review` response (`stagedRecordId: 20260519_121647366_…_McpAddApiFixture_e35b9111`):

  ```
  baselineHash: e9322d86…  (pre-add)
  stagedHash:   50a9b0fc…  (post-add)
  serverDerivedMetadata.symbolsAdded: [
    { name: "McpAddApiFixture", kind: "constructor",
      startLine: 8, endLine: 8,
      textHash: "fa226f35242166adeed2679a4d8cdcb6fc3de90361a1c4253d297e239f777463" }
  ]
  symbolsRemoved: []
  ```

- Force-review + WinMerge save accepted the candidate. `record_diff_decision(accepted)` → `classification: accepted`, `currentHash == stagedHash`. Watched source now has the new ctor at line 8.

## Finding 36 — `remove_symbol` `symbolsRemoved` populated — VERIFIED

Pre-fix: `remove_symbol` on a single constructor produced an empty `symbolsRemoved` despite the candidate clearly removing it.

Post-fix:

- Fresh `get_source_map` confirmed watched source updated: sha256 `50a9b0fc…`, 8 symbols, new ctor at line 8 with `parameterTypes: ["int"]` and disambiguating `stableSymbolKey: …::constructor::McpAddApiFixture(int)`.
- `remove_symbol` with structured selector keyed on that `stableSymbolKey` (+ `parameterTypes: ["int"]` for safety).
- `stage_candidate_for_review` response (`stagedRecordId: 20260519_121908356_…_McpAddApiFixture_674c48c9`):

  ```
  baselineHash: 50a9b0fc…  (post-add)
  stagedHash:   e9322d86…  (pre-add — exact round trip)
  serverDerivedMetadata.symbolsRemoved: [
    { name: "McpAddApiFixture", kind: "constructor",
      startLine: 8, endLine: 8,
      textHash: "fa226f35242166adeed2679a4d8cdcb6fc3de90361a1c4253d297e239f777463" }
  ]
  symbolsAdded: []
  ```

  `textHash` matches the textHash of the ctor that was added — clean round trip identity.

- Force-review + save → `classification: accepted`, watched source restored to pre-Pass-19 sha `e9322d86…`.

## Testing ergonomics observation — opt-in fresh candidate

Severity: **suggestion**. Not a regression — pre-existing, by-design behaviour. But it's a sharp edge for test passes that *want* a clean slate.

When I first called `add_constructor` against `McpAddApiFixture.cs` in session `monitor-20260519171504-…`, the response returned `operationCount: 2` and overlay reported `CS0111` ("Type 'McpAddApiFixture' already defines a member called 'McpAddApiFixture' with the same parameter types") at line 12. Reading the Working candidate revealed a duplicate `McpAddApiFixture(int seed)` ctor — one inserted at line 8 by my call, and another at line 12 left over from a prior pass's add.

This is exactly what the baseline rule says should happen: the watched-source hash (`e9322d86…`) was unchanged between Pass 18 and Pass 19, so the prior Working candidate edits were preserved across sessions. "Hash match wins" — `candidate-baseline-stale` only refuses when watched source has actually moved underneath. The candidate state JSON's `SessionId` was rewritten to my new session id, but operationCount and content correctly carry forward. So this is the same invariant that lets normal editors leave a file mid-edit, come back later, and pick up where they left off.

The mismatch is in **test-pass workflow**, not the product. A test pass deliberately verifying "fresh `add_constructor` on a clean baseline" has no first-class way to declare that intent. The agent has to either (a) inspect `operationCount` on the first response and recover, or (b) file-delete the Working mirror + state JSON before staging. Workaround used here: option (b), file-delete then redo — second attempt returned `operationCount: 1` and clean metadata.

Suggested ergonomics fixes (ranked low → high cost):

1. Mention in `get_staging_guide`: "a Working candidate persists across sessions whenever the watched-source baseline hash is unchanged. If you need a fresh candidate, delete the Working mirror file + state JSON, or call submit_file to overwrite."
2. Return a `candidateInheritedFromPriorSession: true` flag (or equivalent) on the first op of a session that finds an existing candidate, so test agents can detect inheritance without computing it from `operationCount`.
3. Add a `reset_working_candidate(path)` tool so test passes can opt into a clean slate without filesystem surgery.

(1) alone probably closes the gap for agents; (2) is cheap and removes ambiguity; (3) is only worth it if test-pass churn motivates it.

## Watched source — final state

```
C:\Schema Studio - DBV2\SchemaStudio.SematicModel\Model\McpAddApiFixture.cs
sha256: e9322d866059b2013728b294b0004d0163eab347b0eb369c08861003cfb29e4f
length: 351 bytes  (matches pre-Pass-19)
```

No new files were left in watched source. `McpOverlayGateFixture4.cs` and `McpOverlayGateFixture5.cs` were rejected (force-and-close) and force-cancelled respectively, so they remain absent from watched source. Working/Staged scratch records persist as expected.

## Evidence (key record ids)

- Session 1 (gate tests): `monitor-20260519171129-4e4c565dee11444e8`
  - `20260519_121216287_stage_candidate_for_review_McpOverlayGateFixture4_9fe37ac9` — Force branch, rejected
  - `20260519_121423867_stage_candidate_for_review_McpOverlayGateFixture5_de3ccb83` — Cancel branch, queue blocked
- Session 2 (ctor metadata tests): `monitor-20260519171504-39fc6253906f432e9`
  - `20260519_121647366_stage_candidate_for_review_McpAddApiFixture_e35b9111` — add, accepted
  - `20260519_121908356_stage_candidate_for_review_McpAddApiFixture_674c48c9` — remove, accepted (restored watched source)
