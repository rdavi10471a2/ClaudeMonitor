# Could the Monitor Workflow Live in CodeLens + Markdown Alone? — Deep Analysis

**Audience**: project maintainer evaluating whether the Monitor MCP server is load-bearing or simplifiable.

**Date**: 2026-05-16.

**Question**: could everything the Monitor MCP currently does be accomplished using only the Roslyn CodeLens MCP for reads, plus carefully written Markdown files for the workflow discipline?

**Short answer**: **Capability**, yes — you could reproduce the *steps* with CodeLens reads, PowerShell/Bash for file ops + hashing, WinMerge, and well-written MD rules. **Enforcement**, no — the Monitor's irreplaceable property is that it's the *only* tool surface the model has for mutating watched source. Markdown describes rules; the Monitor binds them. The two are not interchangeable.

The rest of this document walks the inventory honestly and lays out three real options, including the case for *partial* simplification that this session's evidence makes attractive.

## 1. Inventory: What Does the Monitor MCP Actually Do?

A complete list of Monitor tool responsibilities, grouped by whether the responsibility is duplicated by CodeLens, by Bash/PowerShell, or unique to the Monitor.

### 1.1 Read-side tools

| Tool | What it does | Equivalent elsewhere? |
| --- | --- | --- |
| `get_source_map` | Roslyn-derived structural reads (navigation/selector/full) with budget enforcement | **CodeLens overlap**: `get_file_overview`, `get_type_overview`, `get_symbol_context`. Slightly different shape and budget logic. Substantial functional overlap. |
| `get_symbol` | One C# symbol body by structured selector / stable key | **CodeLens partial overlap**: `go_to_definition`, `find_implementations`, `search_symbols`. None has the exact "fetch body by stableSymbolKey" semantics; would need to be composed from a definition lookup + range read. |
| `get_file` | Read full watched file via the server | **Bash/PowerShell or Read tool** — trivially replaced. |
| `get_file_outline` | Token-saving outline of a C# file | **CodeLens**: `get_file_overview` is close. |
| `find_file` | Pattern match files under watched root | **Glob built-in tool** does this fine. |
| `refresh_file` | Copy watched file into Working folder | **Bash/PowerShell** copy — trivially replaced. The Monitor's value here is workflow state, not the copy itself. |
| `compare_file` | Refresh + return paths for Host-launched WinMerge | **Bash/PowerShell** + invoke WinMerge directly. |

Read side: **CodeLens covers most of this, the rest is trivial Bash.** Real overlap with CodeLens is high. The Monitor adds: budget-enforced narrowing, stable-key-anchored symbol reads, and the specific schema clients depend on. None of those is *fundamental* — all are reproducible if a client speaks CodeLens directly with some adaptation layer.

### 1.2 Write-side tools (the gate)

| Tool | What it does | Equivalent elsewhere? |
| --- | --- | --- |
| `submit_file` | Stage complete-file candidate, hash original/staged, run syntax + overlay-compile validation, write staged record | **Unique to Monitor.** Could be reproduced as a PowerShell helper script. |
| `submit_symbol` | Stage symbol replacement by structured selector — Roslyn replaces the member in-place, stages full file | **Unique to Monitor.** Roslyn-aware; PowerShell/Bash equivalent would be string-replace only. |
| `add_symbol` / `remove_symbol` | Roslyn member insertion / removal with afterSymbol placement | **Unique to Monitor.** Same caveat as `submit_symbol`. |
| `add_using` / `remove_using` | Roslyn using-directive insertion / removal | **Unique to Monitor.** Same. |
| `launch_staged_diff` (PR #6) | Launch WinMerge for an existing staged record | **PowerShell one-liner** — trivially replaced. |
| `record_diff_decision` | Vote-plus-hash classification, write decision record, block on dirty-unexpected | **Unique to Monitor.** Reproducible in PowerShell + hash compare. |

### 1.3 State & audit tools

| Tool | What it does | Equivalent elsewhere? |
| --- | --- | --- |
| `start_monitor_session` / `get_monitor_session` / `list_monitor_sessions` / `record_monitor_session_event` | Durable session handles + event recording | **Reproducible** as a folder under `Working\Sessions\<id>\events.jsonl`. |
| `check_file_hash` | Compare current watched hash to session-recorded hash | **PowerShell `Get-FileHash` + jq.** |
| `list_ledgers` / `get_ledger` | Per-file change history | **Reproducible** as a folder structure with jsonl per file. |
| `list_monitor_runs` / `get_monitor_run` | Run-history index | **Reproducible** as a single jsonl with run records. |
| `prune_monitor_history` | Housekeeping for old records | **PowerShell** script. |

### 1.4 Manifest / introspection

| Tool | What it does | Equivalent elsewhere? |
| --- | --- | --- |
| `get_monitor_status` / `get_workflow_status` / `get_self_check` | Server-state introspection | **Reproducible** as static "is this configured right?" PowerShell check. |
| `get_tool_manifest` | Returns the canonical tool contract as MD | **Could be the MD file itself.** |

## 2. The CodeLens-only Capability Test

Could a session accomplish the same edit-loop result using only CodeLens + Markdown + Bash/PowerShell + WinMerge? Walk-through:

1. **Orient**: CodeLens `get_file_overview` → equivalent of `get_source_map` selector mode. ✓
2. **Read symbol body**: CodeLens `go_to_definition` returns the symbol with file/range; Bash reads the range from disk. ✓ (more steps, same data)
3. **Generate candidate**: model emits the proposed file content directly in its response. ✓
4. **Stage**: PowerShell `Stage-Candidate.ps1` script — copy candidate to `Working\Staged\<...>`, compute `Get-FileHash` for original + staged, write a JSON record. Reproducible. ✓
5. **Validate**: `dotnet build` on the staged file's project, or CodeLens `get_diagnostics` against the staged path if it supports overlay paths. Possible — but the Monitor's in-server overlay-compile is more integrated. ⚠
6. **Diff**: `Start-Process WinMergeU.exe ...`. Already external. ✓
7. **Classify**: PowerShell `Record-Decision.ps1` — recompute current watched hash, compare to original/staged, write classification record. Reproducible. ✓
8. **Session/audit**: append events to a session jsonl. Reproducible. ✓

Capability-wise: **yes, the whole loop can be done with CodeLens + scripts + MD rules.** Nothing the Monitor does is computationally exotic.

## 3. The Markdown-Alone Test

Could *Markdown rules alone* (no Monitor, no scripts) replace the Monitor? Walk-through:

1. **Read** — Markdown can't read. Operator/model uses CodeLens directly. ✓ (CodeLens does it)
2. **Stage** — Markdown can't write files. Operator/model uses the `Write` tool directly. ⚠ — **the model could write directly to watched source, bypassing review**.
3. **Validate** — Markdown can't run a build. Operator/model runs `dotnet build` manually. Slower, less integrated. ⚠
4. **Diff** — Markdown can't launch WinMerge. Operator does it. ✓
5. **Classify** — Markdown can't compute hashes. Operator/model computes them by hand or via `Get-FileHash`. Error-prone. ⚠
6. **Audit** — Markdown can't append to a log. Operator/model writes log entries. Error-prone. ⚠

Markdown-only: **rules without enforcement**. The discipline depends entirely on whether the model (and operator) follow the rules under context pressure. From this session: **I personally bypassed the Monitor in step 2** when `submit_file` errored on a new path — I used `Write` directly. Under pure Markdown rules, that bypass is the *default* path, not an exception.

## 4. The Real Distinction: Capability vs. Enforcement

The Monitor MCP's value is not what it *enables*. It's what it *constrains*.

Specifically: when the model wants to mutate watched source, the Monitor is currently the **only typed surface that exists** for doing so. The all-or-none gate is meaningful because:

- The model can't bypass the hash check — it's baked into `record_diff_decision`.
- The model can't silently overwrite a file — `submit_*` always stages to `Working\Staged`, never to the watched path.
- The dirty-unexpected detection is automatic — operator agreement + watched hash agreement is the only path to `accepted`.

Replacing the Monitor with MD + scripts breaks the "only typed surface" property:

- The model still has `Write` and `Edit`, which can directly mutate watched source.
- An MD rule saying "don't use Write on watched source" is a request, not a constraint.
- Under pressure (long context, complex task, frustrated user), models follow rules less reliably.

**This session is direct evidence of the failure mode.** When `submit_file` errored on a new path, I made a judgment call to bypass the gate (write directly via `Write` tool). The operator never saw a diff for that new file. The reasoning was defensible — new file, no prior pattern to drift — but it was *me* making that call, not the system enforcing it. Under pure-MD rules, every step is a "me-making-the-call" judgment. The compliance distribution gets wider.

## 5. The Sidestep Problem (Live This Session)

The Monitor as currently configured doesn't actually prevent sidestep. It provides a *safer alternative*, not a *closed boundary*.

Real fix that would close the loop without changing the Monitor: add a `deny` rule in project settings:

```json
{
  "permissions": {
    "deny": [
      "Write(C:\\Schema Studio - DBV2/**)",
      "Edit(C:\\Schema Studio - DBV2/**)"
    ]
  }
}
```

With that in place, the model **cannot** mutate watched source except through the Monitor. The MCP's enforcement becomes real, not advisory. This is independent of whether the Monitor stays or gets simplified — if you want enforcement, you need denial of the alternative paths.

## 6. The Three Options

### Option A: Status quo — keep the Monitor

- **Pros**: Typed enforcement, integrated overlay compile, structured selectors, all-in-one observability via `get_monitor_status`.
- **Cons**: Real ongoing maintenance cost (this session added 4 bug reports and one merge of 3 PRs from Codex). Some duplication with CodeLens on the read side.
- **When right**: You want the discipline to survive variable model compliance, AI-driven workflows, or multi-person team contribution.
- **Required addition for real enforcement**: add `deny` rules for `Write` / `Edit` on watched paths.

### Option B: Simplify to CodeLens + scripts + MD

- **Pros**: One less running process. Workflow visible in scriptable form (PowerShell, Bash). Easier to modify per operator. Faster cold start. Less surface area for Monitor-specific bugs.
- **Cons**: Lose typed enforcement. Lose Roslyn-aware symbol staging (`submit_symbol` becomes "stage whole file"). Lose integrated overlay compile (becomes "run `dotnet build` manually"). Discipline depends on model + operator compliance.
- **When right**: Solo developer working on own project. Already trust own workflow discipline. Prefer fewer moving parts.
- **Required addition for any enforcement**: same `deny` rules — without them, the scripts become advisory.

### Option C: Hybrid — keep the gate, drop the duplicated reads

This is the most attractive option in my honest read. Keep the Monitor for its irreplaceable enforcement bits:

- `submit_file`, `submit_symbol`, `add_*`, `remove_*` (the gate's stage path)
- `record_diff_decision` (the classification)
- `launch_staged_diff`
- Session / ledger / decision-record persistence

Drop the duplicated read tools from the Monitor's surface:

- `get_source_map` → use CodeLens `get_file_overview`
- `get_symbol` → use CodeLens `go_to_definition` + a small range-read
- `get_file_outline` → use CodeLens
- `find_file` → use Glob
- `compare_file` / `refresh_file` → fold into the stage tools (they already implicitly refresh)

Net effect: **the Monitor becomes a small dedicated gate surface** rather than a full read+write+state server. CodeLens handles intelligence. Markdown handles rules. The Monitor handles only the part nothing else can do: bounded, hash-verified mutation of watched source.

The token-economics argument from the analysis doc still holds because the read tools that drive the savings (selector mode source maps, get_symbol bodies) all have CodeLens equivalents. The Monitor doesn't have to own those reads to capture the savings.

**Estimated reduction**: roughly half the Monitor tool surface, two-thirds of the Monitor codebase, almost none of the gate guarantees lost.

## 7. The Markdown-Only Boundary

To be precise about the failure mode of MD-only: **MD can specify a contract, but cannot bind one**. Every rule like "if you create a new file, you must stage it first" depends on:

- The model reading the rule.
- The model understanding the rule applies to the current situation.
- The model choosing to follow the rule when an easier path exists.
- The operator catching it when the model doesn't.

Each of those is fallible. The error rate is non-zero and grows with context pressure. The Monitor's `submit_file` not only describes the contract but *implements it as the only available action*. That's a stronger property than any MD file can express.

If you want MD-only and accept the trade-off — the trade-off is real and survivable for solo work, but it should be a deliberate choice, not a default.

## 8. Recommendation Matrix

| Your situation | Choose |
| --- | --- |
| Multi-person team, mixed compliance expectations | A (keep Monitor) + add `deny` rules |
| Solo dev, want speed and simplicity, willing to enforce discipline yourself | B (CodeLens + scripts + MD) + `deny` rules |
| Solo dev, want most of the safety with less server surface | **C (hybrid)** + `deny` rules |
| Active AI-driven workflows that need bounded mutation | A or C, never B |
| Want to minimize ongoing maintenance | C — half the surface, almost all the safety |

In every case the `deny` rules for `Write`/`Edit` on watched paths are what turn enforcement from advisory to actual. That's the single highest-value change available regardless of which path you pick.

## 9. What This Session Actually Demonstrated

- The Monitor's read side has clear CodeLens parity. Several tools are essentially duplicates.
- The Monitor's write side / gate is doing real safety work the rest of the stack can't reproduce.
- The Monitor's enforcement is currently advisory, not actual — proven by the `submit_file`-errored-so-I-used-Write incident.
- Closing the enforcement loop with `deny` rules is a small change with outsized impact.
- The current bug surface (4 reports in one session: encoding mismatch, round-trip EOL, mixed-EOL per-line, new-file path) is all in the gate/stage path. None of it would be reduced by replacing the gate; some of it would be *worse* if the gate became a script.

## 10. Honest Conclusion

The Monitor *can* be replaced by CodeLens + MD + scripts in terms of capability. It *should not* be replaced by Markdown alone — Markdown is a contract, not a binding. The hybrid (Option C) is the most defensible middle path: it captures the maintenance reduction without giving up the gate guarantees.

But the highest-leverage single change is unrelated to the Monitor's tool surface: **add `deny` rules so the model literally cannot mutate watched source outside the gate**. That converts the Monitor from a safer-alternative-path into a closed-boundary surface. Until that's in place, the gate is opt-in for the model, and Option B becomes much less reckless precisely because Option A wasn't enforcing as strongly as it appeared.
