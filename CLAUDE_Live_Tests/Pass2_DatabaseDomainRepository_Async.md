# Pass 2 — DatabaseDomainRepository Async + SQL Dictionary

## Operator Prompt (verbatim, fat-finger preserved)

> i think the new engine is infinitley smarer so i am going to a bit brave so remember you are keeping details notes on idividual tests.
> i want you to make new DatabaseDomainReposiitory that is ASYNC , that stores the SQL strings in the current file as named items in a static dictonary Creating partial classes is allowed you should use the create file skill if it is avaialbe
> one thing to not that the docs might not make clear the intened design path is to use C# regoions as additonal context for where to inserrt code later so delclarrtions and functions remain together.. Is this clear enough?
>
> documvent this prompt in your notes and go if you think it is sufficent context

## Interpretation

- Target: `C:\Schema Studio - DBV2\SchemaStudio.Data\DatabaseDomainRepository.cs`.
- Make methods async (return `Task` / `Task<T>`, use `await` for DB IO).
- Move embedded SQL strings into a named static dictionary so methods look up SQL by key.
- "in the current file" allows partial-class split per the next bullet.
- Partial classes are allowed. If split, both partials belong to the same class.
- Use a "create file" capability for any new partial companion file. In this server that maps to `submit_file` against a new path (new-path support was a known prior bug; recent fix per PR around commit `b0618de` should make it work — verify on the fly).
- Design rule the docs do not state strongly: C# regions are insertion-anchor context. Declarations and the methods that use them must remain together within the same region. The SQL dictionary and the methods that read from it should sit in related/adjacent regions.

## Out Of Scope For This Test

- Updating every caller of the repository for the new async signatures. Caller propagation is a separate follow-up scope.
- Any unrelated cleanup, DRY refactor, or formatting churn.

## Prior Context (memory + last session)

- `DatabaseDomainRepository.cs` was part of the 5-file rejected refactor in session `monitor-20260516223908-b9e300b9233844448` on 2026-05-17 04:23 UTC. Operator note: "killed WinMerge windows; sequential-review constraint not honored on first attempt; re-planning with Roslyn-first approach."
- Memory: batch in-scope changes into one diff cycle per file, not one-per-symbol.
- Active rule: coupled multi-file edits stage under one monitor session before the first `launch_staged_diff`. Serial WinMerge review, one at a time. Stop on any not-launched result.

## Workflow Plan

1. Document this prompt (this file).
2. `start_monitor_session` for the test.
3. Roslyn discovery (read-only):
   - `search_symbols("DatabaseDomainRepository")`
   - `get_type_overview` for the canonical symbol
   - `find_callers` on each public method (informational; do not stage caller fixes here)
4. Monitor read:
   - `get_source_map(path, scope: file, mode: selector)` for the current file
   - `get_file` to read the 2010-byte body once
5. Plan the rewrite:
   - Decide single-file vs partial split based on size and region layout.
   - Map every literal SQL string to a stable dictionary key.
   - Convert methods to async, including return types and DB call await sites.
   - Lay out regions so SQL keys and the methods that use them stay adjacent.
6. Report the plan to the operator, including chosen file layout, before staging anything.
7. After approval, stage with one `sessionId`:
   - `submit_file` for the modified `DatabaseDomainRepository.cs`.
   - If split: `submit_file` for the new partial companion.
8. `launch_staged_diff` for file 1 only. Wait. `record_diff_decision`. Then file 2. Never two WinMerge windows open at once.
9. Update `STATUS.md` Pass 2 entry, add any findings to `FINDINGS.md`.

## Stop Conditions

- Operator cancels overlay review or kills WinMerge: stop the queue, leave a corrected candidate, do not open later diffs.
- `submit_file` rejects a new-path companion: report bug and either keep single file or wait for fix.
- Async change would require caller updates that the operator has not authorized: report and pause.
- Dirty-unexpected: stop, do not re-vote.

## Notes Log

### 2026-05-17 22:35 — Session started

- Monitor session: `monitor-20260517223523-706ea5677979487a9`.
- Active solution confirmed: `C:\Schema Studio - DBV2\Schema Studio.sln`.

### Discovery

- `DatabaseDomainRepository` is `public sealed class`, 4 public methods, 3 SQL literals, no callers in the solution. 2010 bytes.
- A parallel `DatabaseDomainRepositoryAsync` class already exists in the same project, 104 lines, async with `CancellationToken`, also zero callers. Reported to Operator before staging.
- Neither file uses C# regions today.

### Staging Attempt 1 — partial split

- `submit_file` on existing path `SchemaStudio.Data\DatabaseDomainRepository.cs`: **staged** as record `20260517_174813445_submit_file_DatabaseDomainRepository_4bc6cba3`. Overlay validation reports `CS0103: The name 'Sql' does not exist` on lines 32, 50, 64. Errors are legitimate; the partial companion never staged.
- `submit_file` on new path `SchemaStudio.Data\DatabaseDomainRepository.Sql.cs`: **failed** twice with `An error occurred invoking 'submit_file'`. No structured error, no staged record. Filed as Finding 6.

### Halt — Pass 2 marked invalid by Operator

- Operator flagged that I started staging without rebuilding the MCP server first and without confirming the WinForms host was running. Any `launch_staged_diff` would have returned `host_unavailable`. Findings 6 and 7 still stand as observations, but Pass 2 itself is not a valid test of current server behavior.
- Did not proceed to single-file restage. Did not call `launch_staged_diff` or `record_diff_decision`. The staged record above remains pre-launch in the session; nothing has been pushed to WinMerge.

### What stays for Codex review

- `Pass2_Proposed/DatabaseDomainRepository.cs` and `Pass2_Proposed/DatabaseDomainRepository.Sql.cs`: the intended partial-split candidates, preserved as evidence of the intended shape.
- `FINDINGS.md` Findings 6, 7, 8.

### What is needed to re-run Pass 2 cleanly

1. Rebuild `MonitorBaseClaude.McpServer` and reconnect Claude Code's MCP session.
2. Start the MonitorBaseClaude WinForms host.
3. Confirm `tools/list` now exposes `get_staging_guide` and the typed insertion tools, and confirm new-path `submit_file` no longer errors opaquely.
4. Decide whether to reject the existing pre-launch staged record or let it expire / supersede on the next attempt.

### Pre-rerun ops (executed)

- Killed stale `MonitorBaseClaude.McpServer.exe` (PID 149372).
- Initial `dotnet build .\MonitorBaseClaude.slnx` failed in 5.1 s with six errors against `CLAUDE_Live_Tests/Pass2_Proposed/DatabaseDomainRepository.cs` (Finding 9). Renamed `Pass2_Proposed/*.cs` to `*.cs.txt` to dodge the compile glob locally.
- Rebuild after rename: clean, 4.1 s wall-clock, all five projects (`CodeLensTelemetryProxy`, `MonitorBaseClaude.McpServer`, `McpHubBridge`, `MonitorBaseClaude`, `MonitorBaseClaude.ToolSmokeTests`).
- Started WinForms host `MonitorBaseClaude.exe`, PID 41280, ready in 2.1 s.
- MCP server will respawn through `Tools\Start-MonitorBaseClaudeMcp.ps1` when Claude Code reconnects (`/mcp`).

### Timing And Token Tracking (added by Operator request, mid-pass)

Going forward each test pass records:

- Tool-call wall-clock per Monitor / Roslyn call where it's interesting (anything over 1 s, or any call returning truncation/budget metadata).
- `estimatedTokenProxy` from `get_source_map` responses.
- For discovery in this pass:
  - `get_source_map` (file/selector) on `DatabaseDomainRepository.cs`: `estimatedTokenProxy` 2251, `budgetLimit` 25000, not truncated.
  - `get_source_map` (file/selector) on `DatabaseDomainRepositoryAsync.cs`: `estimatedTokenProxy` 2505, `budgetLimit` 25000, not truncated.
  - `get_file` on each of those two files returned the full body (2007 and 2776 text bytes).
  - Two `find_references` calls returned empty in negligible time.
- Wall-clock for the discovery phase (search_symbols + get_type_overview + 2x get_source_map + 2x get_file + 2x find_references): single-digit seconds total; below the threshold worth itemizing individually until the Monitor adds first-class call timing.
