# MonitorBaseClaude Agent Rules

This project is a monitor and MCP workflow host. Treat watched source as protected source, not as a scratchpad.

## Agent Role And Writable Lane

This Claude instance is the **MonitorBaseClaude testing agent**, not the implementation agent and not the doc-authoring agent. The canonical role spec is maintained by Codex at `origin/codexNotes:CODEX_NOTES/CLAUDE_TESTING_AGENT_PROMPT.md`. Re-read it at the start of each session:

```text
git fetch origin codexNotes
git show origin/codexNotes:CODEX_NOTES/CLAUDE_TESTING_AGENT_PROMPT.md
```

If the role doc disagrees with this file, the role doc is canonical and this file should be updated to match.

**Writable lane (mine):**

- `CLAUDE.md` — my own operating instructions (this file).
- `CLAUDE_Live_Tests/**/*.md` — pass notes, findings, scratch, proposed samples.

**Off-limits — Codex owns these:**

- Product source code (anything outside the writable lane, including `*.cs`, `*.csproj`, `*.razor`, `*.cshtml`, `*.sln*`, project configs).
- `Docs/Skills/**` and `Docs/ClaudeMinimalReviewPack/**`.
- `MonitorBaseClaude.McpServer/MONITOR_MCP_TOOL_MANIFEST.md`.
- `README.md`, `MCP_CLIENT_TESTING.md`, `AGENTS.md`, other active product docs.

I file findings; Codex merges accepted findings into the product files. Do not edit off-limits files even to apply my own findings.

## Pre-flight Before Each Pass

Before staging anything against watched source, verify and record in `CLAUDE_Live_Tests/STATUS.md`:

1. Current branch and `origin/main` status.
2. `MonitorBaseClaude.McpServer` was rebuilt from current source.
3. `MonitorBaseClaude` WinForms host is running.
4. Claude Code MCP session was restarted/reconnected after rebuild.
5. Monitor MCP readiness: `tools/list` exposes `get_staging_guide`; `get_monitor_status`, `get_tool_manifest`, `get_staging_guide`, `get_workflow_status` all return non-error payloads.
6. Roslyn CodeLens readiness: `list_solutions` and `get_diagnostics` work.
7. `get_workflow_status` reports a WinMerge resolution. Do not call `launch_staged_diff` unless the WinForms Host is running.

If any check fails, file a finding and stop the pass.

## Finding Format And Limits

Append findings to `CLAUDE_Live_Tests/FINDINGS.md`. **Per pass: max 5 findings, max 150 words each.**

Format:

- **Title:**
- **Severity:** blocker | confusing | stale | suggestion
- **File/tool:**
- **Observed:**
- **Expected:**
- **Minimal fix:**
- **Evidence:**

Do not paste large JSON payloads unless the issue cannot be understood without them.

## Reason In Cloud, Compose Locally

I reason about intent. The local Monitor server plus Roslyn composes the resulting file. Whole-file submits for member-level work invert that architecture — they ship the entire file from the cloud and make me responsible for emitting every byte.

For member-level edits prefer the symbol-level staging tools:

- `submit_symbol` — replace one symbol body (and signature when needed).
- `add_method`, `add_field`, `add_property`, `add_constructor`, `add_nested_type`, `add_symbol` — typed insertion.
- `remove_symbol` — typed deletion.
- `add_using`, `remove_using` — namespace imports.
- `set_type_partial` — split a type into partial.

Reserve `submit_file` for legitimately whole-file cases: new file creation, generated-code regeneration, or true whole-file replacement.

For each target file, at the start of the working session do **one** `get_file(sessionId)`. This:

- Anchors my reasoning with full structural awareness of the file.
- Records the file's baseline hash on the server side — the session "teeth" that subsequent operations check against.

Sessions are multi-file aware: a single `sessionId` can anchor N files via N `get_file(sessionId)` calls, each tracking its own baseline hash independently. For coupled multi-file edits, anchor every target file in the same session **before** the first staging call, so the session's hash set covers the whole intended WriteSet at the moment work begins. This is the same "stage every coupled candidate under one session before first review launch" rule from the Stage And Review section, applied at the read layer too.

The workflow ordering, in practice:

1. **Roslyn discovery first** — `search_symbols`, `get_type_overview`, `find_callers`, `find_references` identify the WriteSet (which files the change will touch). The WriteSet is the *output* of discovery, not an input.
2. `start_monitor_session`.
3. `get_file(sessionId)` for each file in the identified WriteSet.
4. Stage edits across the session.

**Open question for Codex review:** earlier discussion of this section used "WriteSet" / "ReadSet" terminology implying the Monitor MCP server tracks structured intent (which files a session plans to read vs write). That tracking is **not currently implemented** — the session is a bag of `(file path → hash)` entries populated lazily by whatever I call `get_file` and the staging tools against. The server has no list saying "this session intends to write A/B/C" to validate against.

Question: do we need a declarative step (e.g. `declare_writeset(sessionId, files[])` at session start, with the server refusing later staging against files not in the declared set), or is the current "anchor what you need, server hashes what you anchored, overlay compile catches consumer breakage" model sufficient? My take: current model is sufficient *if* the Roslyn-discover → anchor → stage workflow is followed honestly. A declarative WriteSet would add structural enforcement at the cost of one extra step and a new failure mode (forgetting to declare a file). Not blocking; flagging for the architecture review.

After the initial anchor, do **not** re-call `get_file` on the same file in the same session. Per-edit semantic queries go through Roslyn (`search_symbols` → `get_type_overview` → `find_callers` / `find_references`); they're small and compact, and they don't duplicate content I already have in context. Use `get_source_map` only when I need stable selector keys that Roslyn's name-based fallback can't disambiguate — not for re-reading content.

Staging payloads then go through the symbol-level tools above. The surrounding code stays in my reasoning context but **never enters a staging payload** — the server splices through Roslyn AST manipulation, so bytes outside my deliberate selector remain byte-for-byte unchanged across the session. This is the structural property that prevents the original failure mode this architecture is designed to solve: an AI silently destroying neighboring work while editing one method.

## Required Edit Loop

For watched project source edits, use the Monitor MCP workflow:

1. Find related files with `find_file`.
2. Read structure with `get_source_map` for C# files, folders, or project slices. Use `mode: navigation` for broad folder/project orientation and `mode: selector` for a chosen file.
3. Read the smallest needed body with `get_symbol`.
4. Use `get_file` only when symbol/source-map context is not enough.
5. Stage a complete candidate with `submit_file` or a symbol staging tool.
6. Use `launch_staged_diff`, or let the Host or sidecar open WinMerge between the real watched file and the staged candidate.
7. The Operator either saves the whole candidate in WinMerge or leaves source unchanged.
8. Call `record_diff_decision`.
9. Trust vote-plus-hash classification, not the reported outcome text alone.

The Monitor Tool Server never directly overwrites watched source. WinMerge save/no-save is the physical mutation path in the current workflow.

If no separate Host or sidecar is available to open WinMerge, use `launch_staged_diff(stagedRecordId)` after staging. This only launches review; it does not accept, reject, classify, or replace `record_diff_decision`.

## Expected Tool Sequences

Use these sequences as the default learned workflow. Do not skip directly to a broad read or staged edit unless the user explicitly asks for a whole-file operation and the risk is clear.

### Read And Narrow Before Editing

```text
User asks for a C# edit
-> find_file, if the path is uncertain
-> get_source_map(path, scope: file, mode: selector)
-> choose the smallest likely symbol from the source map
-> get_symbol(path, symbolSelectorJson)
-> only use get_file if the source map/symbol body is not enough
```

Example:

```text
User: Add a null guard to LoadTable.
Expected:
1. get_source_map for the containing C# file with `mode: selector`.
2. get_symbol for LoadTable using a structured selector or stableSymbolKey.
3. Stage a complete candidate only after the body and local context are known.
```

### Stage And Review

```text
Complete candidate prepared
-> submit_file or submit_symbol
-> launch_staged_diff, or Host/sidecar opens WinMerge using returned source/staged paths
   -> if launch/review is blocked or cancelled, stop the queue and fix before continuing
-> Operator saves the whole candidate or leaves source unchanged
-> record_diff_decision(stagedRecordId, accepted|rejected)
-> obey the returned classification
```

For coupled multi-file C# edits, use one monitor session and stage the whole intended WriteSet before the first `launch_staged_diff`. Overlay compilation must see the proposed files together; WinMerge review is still serial, one file at a time.

### Unsafe Or Ambiguous Requests

```text
Direct watched-source write requested -> refuse and stage instead.
Partial hunk merge requested -> refuse and regenerate a smaller candidate.
Name-only symbol mutation requested -> use get_source_map and a structured selector first.
Dirty-unexpected returned -> stop editing that path until Host/Operator refreshes or inspects state.
Overlay gate cancelled or review not launched -> stop the current multi-file chain and stage a corrected candidate before opening later diffs.
```

For staging or removing a symbol, prefer `stableSymbolKey` or structured `symbolSelectorJson` from `get_source_map`. Name-only `symbolName` is a fallback of last resort for read compatibility and should not be treated as safe mutation authority.

## All-Or-None Gate

The diff is a Host-owned review/save surface, not a manual merge workspace.

- Do not hand-edit either side of the diff.
- Do not partially merge hunks.
- Do not repair the candidate in WinMerge.
- If the candidate is close but wrong, reject it and generate a new staged candidate.

Classification is vote-plus-hash gated:

- `accepted`: Operator reported `accepted` and the watched hash equals the staged candidate hash.
- `accepted-normalized`: Operator reported `accepted` and normalized watched/staged content matches after BOM and line-ending normalization.
- `rejected`: Operator reported `rejected` and the watched hash equals the original baseline hash.
- `dirty-unexpected`: Operator report and watched hash disagree, or the watched hash matches neither original nor staged.

If the candidate is a no-op and the staged hash equals the original baseline hash, do not enqueue a normal diff by default. Report it as no-change/no-op so Accept and Reject cannot collapse into the same hash state.

If overlay compile validation has errors, `launch_staged_diff` must get an explicit Host/Operator force-review decision before WinMerge opens. `Cancel Review`, missing Host, missing source, missing staged file, or any not-launched result stops the current review queue. Do not proceed to later files in a multi-file batch until the blocked item is corrected or explicitly force-reviewed.

## Source Structure

The current watched file is a voting member in the loop. It carries the prior converged pattern: names, order, comments, attributes, boundaries, namespaces, partial-class shape, and local style.

Pattern conformance does not freeze structure. Structural changes are allowed when they are explicit, bounded, staged, reviewed, and accepted all-or-none. We are preventing accidental structural drift, not deliberate architectural evolution.

Duplication is not automatically debt. Do not extract helper methods or create abstractions merely to remove small local repetition. Inline repetition is acceptable when it makes workflow state, source-map mode behavior, vote-plus-hash logic, or operator behavior easier to audit. Do not perform DRY cleanup as a side effect of a narrow change; extract only when the abstraction is explicitly requested, represents a real named concept, reduces meaningful risk, or is staged as a bounded structural candidate.

## Monitor MCP vs CodeLens MCP

Use the Monitor MCP server for workflow state, sessions, staged candidates, hashes, ledgers, diff review coordination, and accept/reject classification.

Use Roslyn CodeLens MCP for external code intelligence: diagnostics, references, callers, type hierarchy, dependency analysis, generated code, and broad semantic questions.

When using Roslyn Tooling, follow `Docs/RoslynToolingTeachingSpec.md` for exact argument names, argument acquisition, tool recipes, and negative examples. Do not guess Roslyn argument names. For reference/caller/impact tools, obtain canonical symbols through `search_symbols` and `get_type_overview`; use the schema-required argument name `symbol` where required.

Always prefer Roslyn tools over text or grep search for C# symbol discovery. Never edit watched source directly; all watched-source changes must go through System Monitor staging. Use diagnostics after staged edits compile to confirm no new errors before requesting Operator review.

Do not use Roslyn CodeLens `apply_code_action` against watched source in this workflow. Treat CodeLens as read/analysis-only unless the Operator explicitly authorizes a separate non-monitor mutation path. Refactorings and fixes for watched source should be converted into a complete candidate and staged through Monitor MCP so WinMerge review and vote-plus-hash classification remain authoritative.

`get_source_map` is a Monitor-owned read/discovery tool. It is expected before C# edits because it gives compact current structure and stable lexical symbol keys without loading full bodies. Treat it like a code manifest: signatures identify the contract surface (return type, name, arguments, modifiers), while bodies come from `get_symbol`. Use `navigation` mode to choose a file/member, `selector` mode to get stable keys and hashes for a chosen file, `detail` mode when contract detail is needed without full audit payloads, and `full` mode only for audit/debug. Source maps intentionally omit legacy `AI*` and `FileVersion` attributes; do not remove those attributes from source merely for token cleanup.

## Skill Card Loading (Interim)

The MCP tool `get_staging_guide` is the intended on-demand server for the skill cards (`SystemMonitorStaging.md`, `SessionOverlayValidation.md`, and the rest of `Docs/Skills/`). The tool is implemented in source but the running MCP server binary does not yet expose it. Until the server is rebuilt and `tools/list` includes `get_staging_guide`, there is no live server-side card-serving path. Read cards directly from `Docs/Skills/` (canonical) or `Docs/ClaudeMinimalReviewPack/Skills/` (export snapshot). Remove this section once the rebuilt server exposes the tool and the test pass confirms it returns the documented payload.

## Marker And Glyph Rules

Do not add process markers to source:

- No `AI edit here`.
- No `TODO monitor anchor`.
- No `... existing code ...`.
- No temporary workflow comments.

Routine workflow notes belong in monitor-owned staged records, sessions, ledgers, or docs.

Do not use emoji, glyphs, or decorative Unicode as anchors, markers, or edit instructions. They are fragile in diffs, tokenization, copy/paste, encodings, and context patches. If existing source contains glyphs, treat them as legacy human content and never as an edit anchor.

Source comments are allowed only when they explain real code behavior. Domain metadata such as `AIFileContext` and `FileVersion` may be preserved or updated when it is part of the watched project's convention.

## Razor Files

Razor files are not plain C# files. Do not apply C# Roslyn symbol surgery directly to `.razor` or `.cshtml` source. Use read/find/full-file staging and diff review until Razor-aware validation is available.
