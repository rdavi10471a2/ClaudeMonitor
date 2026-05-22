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

There is no token-efficient path to make a small change to a Razor file after a prior `record_diff_decision(accepted)`. `submit_file` requires re-emitting the entire file (36 KB in the repro case, ~10K output tokens), and the harness-`Edit`-on-Working-mirror fallback fails because `record_diff_decision` cleans up the candidate state JSON. Affects any large file where the only composition tool is `submit_file` (Razor today; would also apply to any future file type without symbol-level surgery).

## Repro

1. Live-edit ManageViews.razor (567 lines / 36 KB) via the normal flow: `submit_file` → `stage_candidate_for_review` → `launch_staged_diff` → `record_diff_decision(accepted)`. Watched file now matches staged hash.
2. Without an intervening composition call, edit the Working mirror file directly with the harness `Edit` tool (the candidate path under `Working\<observedRootKey>\...\ManageViews.razor` still exists on disk and hashes equal to watched).
3. Call `stage_candidate_for_review(path, sessionId)`.

## Expected

Either (a) `stage_candidate_for_review` hashes the on-disk Working mirror and creates a fresh staged record from whatever is there, OR (b) the tool returns a clear diagnostic explaining the missing candidate state and what call is needed to re-establish one.

## Actual

`stage_candidate_for_review` returned an opaque error: `"An error occurred invoking 'stage_candidate_for_review'."` with no detail.

Inspection of `C:\VSCodeProjects\MonitorBaseClaude\Working\.state\Candidates\SchemaStudioWebViewer - Copy_3481fe2b33a8\Components\Pages\ManageViews\` showed the directory was empty — the candidate state JSON had been removed by the prior `record_diff_decision(accepted)`. The Working mirror file itself survived (correct hash, matching watched).

So the failure is: candidate state is required to stage, but the Edit-on-mirror path does not re-create that state, and there is no observable hint to the caller about why.

## Evidence

- Tool name: `mcp__monitor-base-claude__stage_candidate_for_review`
- Arguments: `{ sessionId: "monitor-20260521213704-b5584f2472ac4e648", path: "Components\\Pages\\ManageViews\\ManageViews.razor" }`
- Result: `An error occurred invoking 'stage_candidate_for_review'.` (no JSON body, no diagnostic)
- Source file: `C:\SchemaStudioWebViewer - Copy\Components\Pages\ManageViews\ManageViews.razor` (567 lines, 36310 bytes)
- Working mirror: `C:\VSCodeProjects\MonitorBaseClaude\Working\SchemaStudioWebViewer - Copy_3481fe2b33a8\Components\Pages\ManageViews\ManageViews.razor` (exists, hash matched watched before Edits)
- Candidate state dir at time of failure: `C:\VSCodeProjects\MonitorBaseClaude\Working\.state\Candidates\SchemaStudioWebViewer - Copy_3481fe2b33a8\Components\Pages\ManageViews\` (empty)
- Related session id: `monitor-20260521213704-b5584f2472ac4e648`
- Prior accepted staged record id: `20260521_164025486_stage_candidate_for_review_ManageViews_1fc739c3`

## Notes

The cost being optimized for is model output tokens. Today's `submit_file` for a 2-string change in 567 lines costs ~10K output tokens and 30-60 seconds of generation wall-clock per attempt. Codex's `apply_patch`-style flow handles equivalent edits in ~100 tokens of unified-diff output. The gap is felt acutely on Razor files where no symbol-level composition surface exists.

Design options for Codex consideration (all three solve the immediate problem; trade-offs differ):

1. **`replaceInFile(sessionId, path, startIndex, endIndex, newText, expectedFileHash)`** — offset-based. Smallest payload, unambiguous by construction. Server must validate `expectedFileHash` to catch drift, otherwise silent corruption. Trade-off: LLM must compute char offsets from text it read, fragile across BOM/EOL and across any prior edit in the same candidate.

2. **`replaceTextInFile(sessionId, path, oldText, newText, expectedMatches=1)`** — pattern-based with required uniqueness. Same shape as the harness `Edit` tool, natural for LLMs to author. Server errors loudly if `expectedMatches` count is wrong, caller widens `oldText` context until unique. Trade-off: `oldText` payload can grow if the natural snippet appears multiple times in the file.

3. **`replaceTextInFile(sessionId, path, oldText, newText, occurrence=N, expectedFileHash)`** — pattern-based with caller-selected occurrence and optional hash precondition. Handles duplicates without bloating `oldText`. Server errors if occurrence N does not exist or if `expectedFileHash` does not match current candidate. Trade-off: caller must track which occurrence number to target if the file changed under it.

A separate but related sub-issue: regardless of which design is chosen, `stage_candidate_for_review` should either (a) re-establish a candidate state automatically when the Working mirror file exists but no candidate state JSON does, or (b) return a structured error like `{ status: "no-active-candidate", hint: "call submit_file or replaceTextInFile to re-establish a candidate before staging" }` instead of the opaque RPC error currently returned.

Operator hint: the current CLAUDE.md note now restricts Edit-on-mirror to "mid-iteration only, before `record_diff_decision`" so this failure mode is not re-introduced by Claude improvising.
