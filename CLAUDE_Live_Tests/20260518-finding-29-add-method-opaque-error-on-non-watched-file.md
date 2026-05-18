---
status: new
type: finding
created: 2026-05-18
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---

## Summary

`add_method` and `add_symbol` return a generic `An error occurred invoking 'add_method'.` / `An error occurred invoking 'add_symbol'.` with no diagnostic detail when the target file path is not present in the active Roslyn solution. The real cause is a precondition violation — these typed adds need an existing watched, Roslyn-loaded file with a real class declaration to splice into. The current error surface looks like an internal server exception, which leads a fresh-to-the-protocol agent to probe blindly (try `afterSymbol`, try a different declaration, try `add_symbol` instead, try a different path) before eventually re-reading the staging guide and discovering that the mode table maps "Create a brand-new file" exclusively to `submit_file`. The workflow is not defective — new files are intended to come down as one `submit_file` blob — but the error message is hostile to discovery and burns agent tokens on dead-end probes.

## Repro

Session: `monitor-20260518230627-9028ef0152a445d1b`. Branch `claude/live-test-notes-20260517`.

Two repro shapes, both produce the identical opaque error:

**Shape A (path with a prior `submit_file` skeleton):**

1. `submit_file(path: "SchemaStudio.Data\\SchemaObjectColumnRepositoryAsync.cs", content: "<namespace block + class shell + ctor + field, no methods>")` → succeeds, `status: candidate-updated`, overlay validation clean, candidate file exists on disk at `Working\Schema Studio - DBV2_6c4e124c9922\...`.
2. `add_method(path: same, containingType: "SchemaObjectColumnRepositoryAsync", declaration: "<async method body>")` → fails with `An error occurred invoking 'add_method'.`. Retried without `afterSymbol`, same result. Retried with a trivial `public int Ping() { return 1; }`, same result.
3. `add_symbol(path: same, containingType: same, symbolType: "method", code: "public int Ping() { return 1; }")` → fails with `An error occurred invoking 'add_symbol'.`.

**Shape B (a fresh path with no prior `submit_file`):**

1. `add_method(path: "SchemaStudio.Data\\TokenTestProbe.cs", containingType: "TokenTestProbe", declaration: "public int Ping() { return 1; }")` → identical failure: `An error occurred invoking 'add_method'.`.

Identical failure mode in both shapes confirms the issue is not "the candidate needs to be created first" — it is "the target file must already exist in the watched, Roslyn-loaded solution."

## Expected

One of the following:

1. A specific, descriptive error message identifying the precondition violation. Example: `target file 'SchemaStudio.Data\\TokenTestProbe.cs' is not loaded in the active solution; use submit_file to create a brand-new file, or pick an existing watched file as the target`.
2. Or, if the error is genuinely internal, the response should include the server-side exception type and message rather than the wrapper.

Either would have stopped the agent on the first failure and pointed at the right tool (`submit_file`).

## Actual

`An error occurred invoking 'add_method'.` — string only, no diagnostic detail, no exception type, no hint at which precondition was violated. The session log (`Working\Sessions\monitor-...json`) does **not** record the failed calls either, so an Operator inspecting state cannot retrace what the agent attempted.

## Evidence

- Failed tool call 1: `mcp__monitor-base-claude__add_method` (after `submit_file` skeleton). Args: `path=SchemaStudio.Data\SchemaObjectColumnRepositoryAsync.cs, containingType=SchemaObjectColumnRepositoryAsync, declaration=<~1KB async method body>, afterSymbol=SchemaObjectColumnRepositoryAsync`. Response: `{ "error": "An error occurred invoking 'add_method'." }`.
- Failed tool call 2: same, without `afterSymbol`. Same response.
- Failed tool call 3: same, with `declaration: "public int Ping() { return 1; }"`. Same response.
- Failed tool call 4: `mcp__monitor-base-claude__add_symbol` with `symbolType: method, code: "public int Ping() { return 1; }"`. Response: `{ "error": "An error occurred invoking 'add_symbol'." }`.
- Failed tool call 5: `mcp__monitor-base-claude__add_method` with a brand-new probe path (`SchemaStudio.Data\TokenTestProbe.cs`, no prior `submit_file`). Same response — confirms the issue is path-not-in-solution, not candidate-state.
- Working candidate state JSON at the time of the failures: healthy (`operationCount: 1`, `BaselineHash: <new-file>`, `CandidateLength: 383`). Submit succeeded; subsequent typed adds did not.
- Session log: `Working\Sessions\monitor-20260518230627-9028ef0152a445d1b.json` shows only `session-started`. Neither the submit nor the failures were recorded as session events.
- Authoritative protocol: `get_staging_guide` mode table maps "Create a brand-new file" exclusively to `submit_file`. The staging guide is correct; the agent didn't read it before composing the test plan.

## Notes

- Severity is **suggestion**, not blocker. The workflow's design — submit_file for new files, typed adds for existing files — is sound. This is purely an error-message UX problem and an agent-discovery problem.
- Three small server-side fixes would close this:
  1. Detect the precondition (`document not found in active solution`) in `add_method` / `add_symbol` / `add_field` / `add_property` / etc., and return a specific error message naming the precondition and the right alternate tool.
  2. Log failed staging calls into the session event stream so an Operator can retrace agent behavior.
  3. Optionally: surface the underlying exception type and message in the MCP response when the failure is truly internal, instead of swallowing it into a generic wrapper.
- Related plan + variant-A results: [20260518-token-comparison-plan-schemaobjectcolumn-async.md](20260518-token-comparison-plan-schemaobjectcolumn-async.md). Variant A burned ~2.6 KB of outbound on five typed-add probes before the agent re-read `get_staging_guide` and switched to whole-file `submit_file`. That cost is what this finding asks the server to prevent for future agents.
- A simple agent-side mitigation also exists, recorded for completeness: when entering an unfamiliar scenario (new file, partial-class edit, multi-file rename), call `get_staging_guide` **before** the first composition tool, not after the first failure. That habit would have caught this in 0 probes. The agent-side fix is being recorded as feedback memory separately.
