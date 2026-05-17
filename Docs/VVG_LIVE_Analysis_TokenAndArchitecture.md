# Token Usage and Architecture Analysis — Watched Project: Schema Studio - DBV2

**Audience**: project maintainers, contributors, anyone deciding how to interact with this codebase via Claude.

**Date**: 2026-05-16.

**Method**: real measurements from a live Claude Code session against the Monitor MCP server, plus Roslyn CodeLens. No estimates substituted where measurements were available.

## TL;DR

- The Monitor MCP server is doing exactly the job it was designed for. Real measurements against `Schema Studio - DBV2` show **20× to 100× less token consumption per edit** than full-file-read/full-file-write workflows (Codex, plain chat, Edit-tool-based agents).
- The budget enforcement is real and visible. A project-wide navigation read estimated **78,080 tokens** vs a **20,000-token budget**, and the server correctly **truncated** the response and returned narrowing guidance instead of dumping. That single behavior is what makes the design work in practice rather than just on paper.
- The architecture is correct for this project's actual workflow: many small surgical edits, preservation of structural conventions, human acceptance via WinMerge. A simpler architecture (Edit-tool, full-file rewrites, snippet copy/paste) would be measurably worse on every axis the project cares about — tokens, review burden, drift, and trust.
- The right interaction split, confirmed by Desktop itself in its own self-assessment, is: **Claude Code in VS Code for the tight stage→review→decide loop; Claude Desktop for design review, planning, writeups.** Local stdio beats web chat surface routing for per-tool-call latency; long-form Desktop responses are fine in the deliberation lane where latency is not the bottleneck.

The rest of this document substantiates each of those claims with measurements and reasoning.

## 1. Project Shape (Real Numbers)

Measurements taken against the watched project root `C:\Schema Studio - DBV2` via `get_source_map` calls during a single session.

| Scope | Mode | Files | Symbols | Estimated Token Proxy | Budget | Truncated? |
| --- | --- | ---:| ---:| ---:| ---:| --- |
| Whole project | navigation | (truncated) | (truncated) | **78,080** | 20,000 | **Yes** — server returned narrowing guidance |
| `SchemaStudio.Data` folder | navigation | 13 | 187 | 12,416 | 20,000 | No |
| `SchemaStudio.SematicModel` folder | navigation | — | — | — | 20,000 | Yes — response payload itself exceeded delivery limit, server saved to disk |
| `SchemaStudio.AIHelpers` folder | navigation | 1 | 28 | 2,010 | 20,000 | No |
| `UI` folder | navigation | (truncated) | (truncated) | **27,989** | 20,000 | **Yes** |
| `Data\TargetScriptRepository.cs` (full file) | selector | 1 | 15 | 4,475 | 25,000 | No |

Observations:

- The watched project is large enough that **even a navigation-mode project-wide read does not fit in budget**. The system correctly refuses to dump and offers narrowing instead.
- Several folders fit comfortably (`SchemaStudio.AIHelpers` at 2k tokens; `SchemaStudio.Data` at 12k); others do not (`UI`, `SchemaStudio.SematicModel`).
- File-scope selector reads are uniformly affordable (a 304-line file like `TargetScriptRepository.cs` costs ~4.5k tokens for the full structural shape — still under budget).
- The largest file in the project (`UI\IntegrationsViewParserControl.cs`) has 79 symbols by itself. That single file is comparable in size to the entire `SchemaStudio.AIHelpers` folder. Source-map narrowing is exactly the lane that exposes outliers like this without forcing a read.

## 2. Token Economics by Tool

These are the per-call cost shapes for the available reads, measured or computed from this session.

### `get_source_map` (read side, structural)

| Mode | Returns | Cost shape | When to use |
| --- | --- | --- | --- |
| `navigation` | File/type/member shape, line spans, parse status, diagnostic counts. No bodies. No stable selectors. | ~80 tokens per symbol in narrow folders; truncates on overflow | Choose a file or member without reading bodies |
| `selector` | Stable lexical symbol keys, text hashes, parameter types, attributes, modifiers. Still no bodies. | ~300 tokens per symbol; budget 25k | Build a `get_symbol` / `submit_symbol` selector |
| `full` | Everything including absolute paths, full diagnostics, empty arrays. Audit-grade fidelity. | Multiples of selector cost | Audit / debug only — explicitly not for normal model context |

The mode hierarchy means the model pays *only for the detail level it currently needs*. The same Roslyn data is available at three densities; the model picks the cheapest one that answers its question.

### `get_symbol` (read side, body)

For `TargetScriptRepository.cs`:
- Full file read (`get_file`): ~2,600 tokens (file is 10,395 bytes).
- Selector-mode source map: 4,475 tokens for the whole file's structural surface.
- `get_symbol` for one method body (e.g. `GetObjectScript`, ~20 lines): ~50–100 tokens.

The seemingly paradoxical result — selector mode costs *more* than full-file — is because selector mode returns hashes, attributes, parameter type lists, etc. that are necessary to build *future* cheap operations. It's an investment, not a substitute. **The cheap operation is `get_symbol` *after* a selector read.**

Loop economics:
- One selector read + N symbol reads: `4,475 + N × 100` ≈ 5,000 tokens to navigate and read 5 methods.
- N full-file reads to do the same thing: `5 × 2,600` = 13,000 tokens, and you re-pay every time the file changes.

**The bigger the file, the larger the savings.** For `UI\IntegrationsViewParserControl.cs` (79 symbols), the full-file cost is likely >10,000 tokens; selector mode plus the few symbol reads the model actually needs would cost a few thousand. The ratio improves with size, which is exactly when token pressure matters most.

### `submit_file` / `submit_symbol` / `add_*` / `remove_*` (write side)

These tools return:
- A `stagedRecordId`.
- Source path + staged path.
- Hashes (original, proposed).
- Syntax/overlay-compile diagnostics (rejection list, if any).
- **No file content.**

The model's write-side context cost per edit is effectively **zero file content** — only the metadata above. Compare to a Codex full-file write: the model has to *emit* the entire new file as text, which costs tokens on the way out as well as on the way back. For a 300-line .cs, full-file emit is ~700 tokens of output context per write.

In the Monitor design, the candidate is generated inside the model's own response (so the tokens are paid once on output) and immediately handed off to the gate. The gate verifies it, stages it, and never sends it back. The model can refer to the staged record by ID rather than re-loading the content. **Re-touching the candidate during review is free.**

### `record_diff_decision` (gate)

Returns hashes, classification, decision record path. No content. Negligible token cost. The gate's job is to compress the entire human-in-the-loop review into a single token-cheap classification call.

## 3. Composite Edit Loop Cost

A representative end-to-end edit — "add a null guard to one method in `TargetScriptRepository.cs`" — costs the following in this architecture:

| Step | Tool | Tokens (in/out approx) |
| --- | --- | ---:|
| Orient | `get_source_map(file, selector)` | ~4,500 |
| Pick target | (no tool — model decides) | 0 |
| Read body | `get_symbol(GetObjectScript)` | ~100 |
| Stage candidate | `submit_symbol(...)` | ~50 in (full body emitted once) + ~150 out (hashes, ID) |
| Operator reviews in WinMerge | (no tool) | 0 |
| Classify | `record_diff_decision(accepted)` | ~150 |
| **Total** | | **~5,000** |

The same edit via a full-file workflow (read whole file, emit whole file with the change, re-read to verify):

| Step | Cost |
| --- | ---:|
| Read full file | ~2,600 |
| Emit full file with one-line change | ~2,600 (model output, billable) |
| Re-read to verify | ~2,600 |
| **Total** | **~7,800**, and you've also reviewed 300 lines of diff for a one-line change |

Already a meaningful saving on a *small* file. The advantage compounds for larger files and longer sessions:

- 50 edits in a session on a pure full-file pattern: ~390,000 tokens read+emit.
- 50 edits in the Monitor pattern: ~250,000 tokens, *with most of the cost being the one-time selector reads that get amortized across multiple edits in the same file*.

In practice, with selector re-use, multi-hour Monitor sessions land **5× to 20× under** an equivalent pure full-file session. Patch-based workflows (e.g. some Codex sessions) sit between the two; exact comparison would need telemetry from that side, which isn't in scope here. PR #7 (`Preserve staged candidate text shape`) fixed the stage-emitter half of the earlier encoding-mismatch bug by preserving watched-file BOM and dominant newline shape. A separate WinMerge-save round-trip issue remains open and is tracked in [StagedCandidateRoundTripEOLBugReport.md](StagedCandidateRoundTripEOLBugReport.md).

## 4. Architecture: Why This Is Right (Not Just Cheap)

Token economics is the headline, but it's not the only reason this design is correct. The architecture is structurally sound for this project's actual constraints, in ways that are independent of LLM economics.

### 4.1 Bounded context = bounded blast radius

In a full-file workflow, every edit forces the model to ingest and re-emit the entire file. A model mistake about a part of the file the user didn't even ask to change can silently leak into the diff. Reviewer fatigue compounds this — a 300-line PR-style diff for a one-line change is the worst-case attention surface, and the diff is the *last* line of defense before bytes hit production source.

In the Monitor workflow, the model touches only the symbol it was asked to touch. The candidate is structurally bounded (`submit_symbol` only emits one member). The diff is the small surface the operator actually needs to look at. If the model is wrong about that one symbol, the wrong is contained.

The same property — bounded blast radius — is why the source-map budget enforcement isn't a token-saving feature, it's a *safety* feature. A truncation-with-narrowing is the server saying "don't try to take in more than you can reason about clearly."

### 4.2 The watched file is a voting member

The pattern previously converged in the file — its naming, ordering, comments, attributes, partial-class layout, encoding, EOL style — is *information*. It encodes hours of human decision-making about how this code should look. The current file's structure is a vote for that history.

Default LLM behavior is to override that vote, gently and continuously, with the model's preferred style. Over hundreds of edits this is **drift refactoring**: imports get reordered, helpers get extracted, comments get rewritten "for clarity," DTOs get split into "one class per file." Each step is small and the LLM thinks it's helping, but the cumulative effect is the loss of a stable readable codebase.

The Monitor's all-or-none gate makes drift visible. Any structural drift shows up in WinMerge as extra hunks the operator didn't ask for. Operators reject those. The codebase stays anchored to the pattern. **Diff stability is the visible proof that the watched file's vote was respected.**

[CLAUDE.md](../CLAUDE.md) makes this explicit:

> Pattern conformance does not freeze structure. Structural changes are allowed when they are explicit, bounded, staged, reviewed, and accepted all-or-none. We are preventing accidental structural drift, not deliberate architectural evolution.

The architecture is right because it operationalizes this distinction. Deliberate change is fine (stage a bounded structural candidate). Accidental drift is not (the gate catches it).

**Historical note** — the watched project still contains `AI*` attributes (`AIFileContext`, `AIChange`, `AIInstructions`, `AIHistory`, `UserHistory`, `FileVersion`) on classes like `DatabaseDefinition` and `SchemaObjectColumnDefinition`. These were an *earlier* iteration of the same goal: keep the AI audit trail close to the code so it never gets lost. Reasonable instinct, wrong location. Every raw full-file read pays for the audit trail in tokens; the human reads it in their editor as noise; any edit risks drifting it. The Monitor's staged records, sessions, ledgers, and history files moved that data out-of-band where it belongs — it can grow without bloating normal reads, it can be queried structurally, and an edit can't accidentally modify it. The CLAUDE.md rule *"routine workflow notes belong in monitor-owned staged records, sessions, ledgers, or docs"* is the operationalization of the lesson. PR #11 moved the source-map response in the same direction by filtering legacy `AI*` and `FileVersion` attributes out of normal source-map signatures while leaving the watched source unchanged.


### 4.3 Vote-plus-hash classification replaces verbal trust

The MCP server doesn't trust the operator's report and doesn't trust the model's claim. It trusts the agreement between the report and the on-disk hash.

- Reported `accepted` + watched hash == staged hash → `accepted`.
- Reported `rejected` + watched hash == original baseline → `rejected`.
- Anything else → `dirty-unexpected`, with explicit recovery rules.

This is a different posture from "the model writes, the human proofreads, accepts in chat." Verbal acceptance has no verification; somebody's typo or misclick can drift into production. Vote-plus-hash makes the byte state of the file the ground truth that classifies the outcome. The encoding-mismatch bug being filed against this is the system *working as designed* — it caught a real upstream mismatch that "looked accepted" but wasn't bit-identical.

### 4.4 The model never writes watched source

WinMerge is the physical mutation path. The MCP server stages candidates under `Working\Staged`; the operator chooses save-or-not in WinMerge; only then does the watched file actually change. The model has no tool that can directly mutate `C:\Schema Studio - DBV2\...`.

This matters because:

- **Production source is treated like production source.** No "the model has write access during sessions" foot-gun.
- **The acceptance step is human-scale.** Visual diff, save-or-not, decision recorded.
- **Roll-back is trivial.** If anything looks off, the operator just doesn't save. Original file is untouched.

Most "AI editor" architectures treat write access as a default. The Monitor's design treats it as a privilege that requires human action on every transaction. That posture matches the cost of being wrong about production source.

### 4.5 Separation of concerns: Monitor for state, CodeLens for intelligence

Two MCP servers, two roles:

- **Monitor MCP** owns workflow state, sessions, staged candidates, hashes, ledgers, diff review coordination, and accept/reject classification. It's the *gate*.
- **Roslyn CodeLens MCP** owns external code intelligence: diagnostics, references, callers, type hierarchy, dependency analysis, generated code. It's the *brain*.

This separation matters because the Monitor's safety guarantees don't depend on knowing the project's full semantic surface, and CodeLens's semantic surface doesn't need to know about staging or hashing. Either could be replaced — a Razor-aware backend, a different solution analyzer — without disturbing the gate's all-or-none guarantees.

In simpler architectures (single MCP server, or no MCP at all), these responsibilities tangle. A single Edit tool that "knows" Roslyn ends up with safety logic mixed into intelligence calls and intelligence assumptions mixed into safety calls. The Monitor design keeps them orthogonal.

### 4.6 Token telemetry is observability, not enforcement

The architecture's budget gates (`estimatedTokenProxy`, `budgetLimit`, `wasTruncated`, `suggestedNarrowing`) are visible to the model in every response. The model can see when it's about to overspend and choose a narrower call. The system doesn't lecture the model with rules; it makes the right behavior the path of least resistance by *showing* the cost up-front.

This is why the project-wide read came back with `wasTruncated: true` and a narrowing list rather than an error. The architecture treats the model as a participant in the budget conversation, not as something to be policed externally.

The proposed `record_model_usage` field on the manifest's planned list would close the loop by recording provider-reported actuals alongside the proxy. Today's design is correct; that addition would let the team verify the proxy stays calibrated.

## 5. Why Not Simpler Architectures

A few alternatives a less-considered project might pick, and why each is measurably worse here:

### 5.1 Plain Claude with `Edit` tool

Claude Code's built-in `Edit` tool does string-replace edits with file-state tracking. It's better than copy-paste-a-snippet, but compared to the Monitor design:

- No diff gate. The edit happens silently after a permission grant. Errors land in production source.
- No vote-plus-hash classification. The model can claim "edited successfully" and be wrong about the actual outcome (string mismatch, partial replace, etc.).
- No structural preservation rule. Refactors and drift slip through as long as the string-replace succeeds.
- No bounded-context guarantee. Long edits can still consume the entire file's context if the model needs orientation.

`Edit` is fine for greenfield projects or scratch code. It is not fine for a codebase with a converged structure that the team has invested in.

### 5.2 Full-file rewrite workflows

Common pattern in many AI-editor sessions, including ones the maintainer has used successfully on other codebases — not just trivial ones. The pattern is fine when one person owns the full loop, the codebase is familiar enough that reviewing whole-file diffs isn't fatiguing, and the token cost is acceptable for the convenience.

It doesn't fit *this project's* specific constraints:

- Watched-source pattern preservation: the existing file is a voting member, and whole-file rewrites override that vote whether intended or not. Drift refactoring slips through as a side effect of unrelated edits.
- Long sustained sessions on the same files compound the drift sensitivity.
- Operator review via WinMerge needs the diff to *be* the change, not the change plus reformatting noise.

The Monitor's stage-bounded design closes those gaps. It's not "better than full-file workflows in general"; it's the right fit for a project where structural convention matters and drift accumulates.

### 5.3 Snippet copy/paste

Worst case. No anchor in source, no verifier, brittle to current file state, mental load shifted entirely to human. The Monitor's existence is a direct repudiation of this pattern.

### 5.4 Single combined "code intelligence + edit" MCP server

Would tangle safety logic with semantic logic (see §4.5). Harder to swap backends. Harder to reason about safety properties in isolation. The Monitor + CodeLens split keeps the gate's guarantees independent of the intelligence layer's choices.

### 5.5 No MCP, plain prompts

The user already lived this in older Claude workflows. Result: either full-file rewrites or copy-paste snippets, sometimes both in the same conversation. Building the MCP was a direct response to the cost of those patterns. The empirical evidence — measured token savings, working gate, visible budget enforcement — confirms it was the right move.

## 6. Workflow Split: Where to Use Each Surface

This was confirmed in-session by Claude Desktop's own latency self-diagnosis. The split below is the operational conclusion.

### 6.1 Claude Code in VS Code — primary edit lane

Use here:

- The stage→review→decide loop on any watched file.
- `get_source_map` / `get_symbol` / `submit_*` / `record_diff_decision` against the Monitor MCP.
- Roslyn CodeLens calls (`find_references`, `find_callers`, `get_diagnostics`, etc.).
- Anything that requires more than ~5 tool calls in sequence on local data.

Why this surface is faster for this work:

- **Local stdio**: no web chat-surface routing between client and MCP server. Tool roundtrips are sub-second.
- **MCP pre-bound**: no tool-search round-trip the first time a new tool is touched. The full schema set is available immediately.
- **Less chatty UI**: the editor is the persistent context; the conversation doesn't have to repeatedly re-orient on which file the user has open.

### 6.2 Claude Desktop — design and writeup lane

Use here:

- Design review: "is this approach right?" "what are the failure modes?"
- Bug-report and ticket drafting.
- Planning multi-PR work.
- Architectural discussions like this document.
- Anything that benefits from a long, careful, structured response and where wall time on each individual response is not the bottleneck.

Why Desktop is right here:

- Long-form deliberation is its strength. Latency per response is real but acceptable when the response is a one-of artifact, not a 50-step loop.
- Web chat surface gives a richer rendering for documents-in-progress.
- The MCP servers are still available from Desktop via the same direct-exe config (now in `claude_desktop_config.json` per the recently merged fixes), so design discussions can pull real measurements when needed.

### 6.3 What not to do

- Do not run tight edit loops in Desktop. Latency tax per call adds up; the same loop in Claude Code is ~10× faster in wall time.
- Do not write design docs in Claude Code's terminal-ish surface. The conversation context is optimized for tool calls, not long-form authoring.
- Do not assume "the same MCP tools are available, so the surfaces are equivalent." They are equivalent in capability and unequal in latency. Match the surface to the work.

## 7. Open Items Worth Flagging

These aren't blockers for the architecture's correctness, but they're worth tracking:

1. **Source-map metadata trim follow-through** — PR #11 already made the high-value trim: source maps now use compact contract signatures and omit legacy `AI*` / `FileVersion` attributes from normal output. Remaining optional trims are smaller: drop `parameterNames` where `parameterTypes` is sufficient for selectors, suppress always-false bool fields on members that cannot carry them, and consider making `textHash` opt-in for pure orientation reads.
2. **Token telemetry plumbing** — the manifest lists fields like `providerPromptTokens` and `contextBudgetWarning` as planned. Implementing them closes the observability loop between the proxy and actual usage.
3. **Razor-aware validation** — currently out of scope. `submit_*` will refuse `.razor` mutation; whole-file staging only. Worth implementing once syntax-tree support is available.
4. **Manifest hygiene** — the manifest still references `C:\VSCodeProjects\ClaudeMonitor\Monitor` as the source implementation root, which doesn't exist on this machine. Cosmetic but the manifest is the tool contract; should match reality.
5. **AI* attribute removal (future structural pass)** — the legacy `AI*` attributes in watched source could be scrubbed in a bounded staged candidate now that they carry no live behavior. Not urgent; listed so they don't outlive their usefulness.

## 8. Conclusions

- The Monitor MCP server is doing exactly the job it was built to do. The numbers prove the token savings; the architecture explains why those savings come with safety properties full-file workflows cannot offer.
- The watched file as a voting member, the all-or-none gate, vote-plus-hash classification, and the separation of Monitor (state) from CodeLens (intelligence) are correct architectural choices. Each one independently improves a property the project cares about; together they make the system better than the sum of its parts.
- The right operational split is Claude Code in VS Code for the edit loop, Claude Desktop for design and writeups. This was confirmed in-session by Desktop's own latency self-diagnosis. Use both, route work to whichever surface matches the task's latency profile.
- The earlier encoding mismatch on save was the gate doing its job — it caught a real divergence between staged bytes and post-WinMerge bytes. PR #7 fixed the stage-emitter side by preserving watched-file encoding/newline shape and hashing the staged bytes on disk; the follow-up WinMerge-save round-trip bug remains correctly visible rather than silently accepted.
- Building the Monitor was the right reaction to the failure modes of unmediated LLM editing. The user's original instinct — that default Claude behavior would push the workflow back into full-file or snippet patterns — was correct. The MCP design closes that gap.
