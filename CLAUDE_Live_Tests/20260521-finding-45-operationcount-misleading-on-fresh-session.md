---
status: new
type: finding
created: 2026-05-21
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---

## Summary

`submit_symbol` (and presumably the other Working-candidate composition tools) returns `operationCount: 2` on the **first user op of a fresh monitor session** when the Working candidate file already exists from a prior session. The candidate body itself can be byte-for-byte equal to the baseline plus the current edit — i.e. genuinely clean, no inherited stale work — yet the counter implies inherited operations.

This contradicts the CLAUDE memory heuristic `project_working_candidate_persists_across_sessions.md`: "operationCount > 1 on first op = inherited prior work." Pass 21 reproduces a clean `operationCount: 2` first op, so the heuristic is unreliable in at least one direction.

## Repro

Session `monitor-20260521130506-02a9a5e5b0aa41a1a`, branch `claude/live-test-notes-20260517`, local HEAD `6947076`.

1. `start_monitor_session` — new handle, no prior state in this session.
2. `submit_symbol` against `SchemaStudio.SematicModel\Model\McpAddApiFixture.cs` ctor `McpAddApiFixture(string)`, replacing `{ }` body with `{ _ = parm; }`.
3. Response: `status: candidate-updated`, `operationCount: 2`, `baselineHash: 6592eb30…` (matches watched), `candidateHash: ac6ebb93…`.
4. Read the candidate file (`Working\Schema Studio - DBV2_…\SchemaStudio.SematicModel\Model\McpAddApiFixture.cs`) and its `.candidate.json` state file.
5. Candidate content equals the baseline file byte-for-byte except for the one-line ctor body change. `CandidateLength: 310` vs `BaselineLength: 300` — delta is exactly `_ = parm; ` (10 chars). No inherited edits.

The candidate appears to have been initialized in a prior session (likely the same path used during Reg #2 earlier today, session `monitor-20260521053731-…`), but the prior content was either reset to baseline or overwritten by this op. The counter retained "2" rather than resetting to "1" on the first op of the new session.

## Expected

One of:

1. `operationCount` resets to 1 on the first op of a new session that doesn't inherit any visible candidate delta. The number then matches "ops performed in this session."
2. The response distinguishes "ops in this session" from "ops since baseline initialization" with two separate fields, so an agent can tell whether prior session ops affected the candidate's current state.
3. If `operationCount: 2` legitimately means "the baseline-copy bookkeeping op + my submit," then the CLAUDE-memory heuristic and any agent training that relies on `> 1 ⇒ inherited` should be retracted.

## Actual

`operationCount: 2` on the first user op of a fresh session, candidate content clean. The number is structurally accurate (it's the second write to the .candidate.json since this candidate's filesystem creation) but operationally misleading (it falsely implies inherited dirty work to an agent following the documented heuristic).

## Evidence

- Pass 21 evidence note: [20260521-pass21-test-result-index-first-chain-rehearsal.md](20260521-pass21-test-result-index-first-chain-rehearsal.md), "Index-first call chain" section, sub-bullet on `submit_symbol`.
- Memory entry that the heuristic comes from: `project_working_candidate_persists_across_sessions.md` (Claude-side auto-memory, not in repo).
- Candidate state file shown in pass evidence: `OperationCount: 2`, but `SessionId` is the new session and `UpdatedAt` postdates `start_monitor_session`.

## Minimal fix

Pick option 1, 2, or 3 above. Lowest-risk is option 1 (reset to 1 on first op of a new session). Highest-clarity is option 2 (two fields). If neither is implemented, at minimum update CLAUDE.md / agent-facing docs to retract the "operationCount > 1 = inherited prior work" heuristic.

## Severity

**confusing** — does not block the workflow (the chain succeeded with clean hashes in Pass 21), but creates a false-positive for inherited-work detection. A pessimistic agent will waste cycles auditing a clean candidate; an optimistic agent might miss a real inherited-work case if it learns to ignore the signal. Either failure mode degrades safety vs. the documented contract.

## Notes

- The candidate-persistence behavior itself is by design and was confirmed correct on prior passes; this finding is narrowly about the *counter semantics*, not the persistence.
- The fix may turn out to be docs-only if `operationCount` is intentionally a candidate-lifetime counter, not a session counter. In that case the heuristic in the memory entry simply needs to be retracted and a different inherited-work signal proposed (e.g. compare candidate content to baseline after the first op — if the delta only contains your one edit, there's no inheritance).

## Addendum — corroborating evidence from the same session's cleanup revert

The same Pass 21 session executed a second mutation after the first one was accepted: a revert `submit_symbol` returning the ctor body to `{ }`. The response showed:

- `operationCount: 1` — **not 2**, even though this is the second user-op in the same session against the same path.
- `baselineHash: ac6ebb93…` — correctly the *current* watched hash (post-forward-accept), not the original `6592eb30…`. Baseline tracks watched state across accept cycles.
- `candidateHash: 6592eb30…` — proposed revert content matches the original pre-pass baseline byte-for-byte.

So the counter reset after the previous accept *despite* the same session id, same path, and a still-existing Working candidate file. This tightens the semantics: `operationCount` is per-candidate-lifetime, where the lifetime ends at `record_diff_decision: accepted` (and presumably also at `rejected`). The CLAUDE-memory heuristic "operationCount > 1 on first op = inherited prior work" is wrong in both directions:

- Forward op (no inheritance, clean candidate from prior session): `operationCount: 2`. Heuristic *falsely flags* inheritance.
- Revert op (same session, same path, prior accept landed): `operationCount: 1`. Heuristic *misses* the fact that the candidate file was reinitialized — which an agent might want to know.

The right inherited-work signal is content-based, not counter-based: read the Working candidate file content, compare to the watched baseline, and any delta that wasn't your op is inherited. This is what the Pass 21 evidence file did in practice and it caught the absence of inherited dirty work cleanly.
