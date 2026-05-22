# MonitorBaseClaude Agent Rules

This project is a monitor and MCP workflow host. Treat watched source as protected source, not as a scratchpad.

Solution index scope and hard coverage boundaries are documented in `Docs/SolutionIndexScope.md`.

## Mini Skill Loading

@Docs/Skills/SkillRouter.md

Use `Docs/Skills/SkillRouter.md` as the entrypoint for task-specific MonitorBaseClaude skills. Load only the smallest relevant card for the current task; do not load the whole `Docs/Skills/` set by default. If the active task is a watched-source edit, load `Docs/Skills/SystemMonitorStaging.md` after the router.

## Design Principle

Reason in the cloud; edit locally. Use compact context for understanding, then let the local Monitor server perform bounded edits, validation, staging, and review. Optimize both directions:

- Inbound: use the Solution Index MCP surface (`get_solution_index_tree`, `query_solution_index`, `find_indexed_symbols`, `find_indexed_references`, `find_indexed_callers`, `find_indexed_relationships`, `get_indexed_symbol`) plus source maps and symbols before loading bodies.
- Outbound: use the smallest safe composition tool. Prefer symbol tools for C#, `replace_text_in_file` for exact text changes, `replace_span_in_file` when exact line/column bounds are already known, and `submit_file` only for new files or deliberate whole-file rewrites.

## Report And Memory Lanes

Use `CLAUDE_Live_Tests/` for source-controlled findings, bug reports, test results, and doc suggestions that Codex/operator should review. Follow `CLAUDE_Live_Tests/README.md`: every report is date-stamped, has a status header, and can be marked processed after triage.

Use local `.claude-local/` for private restart memory, scratch notes, and VS Code/MCP binding workarounds. `.claude-local/` is ignored by git and is not product documentation.

When this rule is first seen in an existing checkout, move current local restart/scratch notes into `.claude-local/`. Move findings, bug reports, test results, and doc suggestions that Codex/operator should review into `CLAUDE_Live_Tests/` using the naming and header rules in `CLAUDE_Live_Tests/README.md`.

Claude owns cleanup of Claude-created notes. If local notes are stale, move only the useful summary into `.claude-local/` or a dated `CLAUDE_Live_Tests/` report, then archive or remove the noisy source note. Keep whatever compact restart summary helps you continue, but do not leave duplicate scratch/report piles for Codex to sort later.

Do not treat `CLAUDE_Live_Tests/`, `.claude-local/`, `Working/`, `LocalSmokeTests/`, or `Docs/Archive/` as authority for workflow rules. When instructions conflict, prefer the current user message, then this file, then `get_tool_manifest`, then `get_staging_guide`.

Claude may push markdown-only branches for review, never directly to `main`. Use branch names like `claude-notes/YYYYMMDD-topic`.

Claude-owned markdown paths:

- `CLAUDE.md`
- `CLAUDE_Live_Tests/**/*.md`

Markdown paths allowed only when the Operator explicitly asks:

- `MCP_CLIENT_TESTING.md`
- `README.md`
- `Docs/Skills/**/*.md`

Before pushing a Claude notes branch, run `git diff --name-only main...HEAD`. Stop if any non-Markdown file appears, or if any Markdown file is outside the allowed paths for the task.

## Required Edit Loop

For watched project source edits, use the Monitor MCP workflow:

1. Find related files with `find_file`.
2. Use Solution Index tools for project and dependency surfaces before loading source: `get_solution_index_tree` for orientation, `query_solution_index` for namespace/folder/file slices, `find_indexed_symbols` for declarations, `find_indexed_references` / `find_indexed_callers` for impact checks, and `find_indexed_relationships` for partials, inheritance, overrides, and interface implementations.
3. Read structure with `get_source_map` for C# files, folders, or project slices when live selectors or source-map shapes are needed. Use `mode: navigation` for broad folder/project orientation and `mode: selector` for a chosen file before symbol mutation.
4. Read the smallest needed body with `get_symbol`.
5. Use `get_file` only when index/source-map/symbol context is not enough and the file is below 32KB. For files at or above 32KB, call `refresh_file` first and chunk-read the returned Working file path instead of asking MCP to return the whole file.
6. Compose a complete Working candidate with `replace_text_in_file`, `replace_span_in_file`, `submit_file`, `submit_symbol`, `add_symbol`, `add_field`, `add_property`, `add_method`, `add_constructor`, `add_nested_type`, `set_type_partial`, `add_using`, `remove_using`, or `remove_symbol`.
7. Call `stage_candidate_for_review` only after the Working candidate is complete enough for review.
8. Use `launch_staged_diff`, or let the Host or sidecar open WinMerge between the real watched file and the staged candidate.
9. The Operator either saves the whole candidate in WinMerge or leaves source unchanged.
10. Call `record_diff_decision`.
11. Trust vote-plus-hash classification, not the reported outcome text alone.

The Monitor Tool Server never directly overwrites watched source. WinMerge save/no-save is the physical mutation path in the current workflow.

After an accepted single-file decision, `record_diff_decision` refreshes the monitor-owned solution index. For a multi-file session, accepted decisions are deferred while other staged records remain pending, then the index is rebuilt once when the session chain is complete. Check the returned `IndexRefresh` status before calling manual index refresh tools.

All current candidate composition tools write to the monitor-owned `Working\<observedRootKey>\<relative source path>` mirror. They do not create staged records by themselves. There are no live `*_old` edit tools in the current surface; if `tools/list` shows any, report it as stale binary or stale MCP binding evidence.

If no separate Host or sidecar is available to open WinMerge, use `launch_staged_diff(stagedRecordId)` after staging. This only launches review; it does not accept, reject, classify, or replace `record_diff_decision`.

## Expected Tool Sequences

Use these sequences as the default learned workflow. Do not skip directly to a broad read or staged edit unless the user explicitly asks for a whole-file operation and the risk is clear.

### Read And Narrow Before Editing

```text
User asks for a C# edit
-> find_file, if the path is uncertain
-> get_solution_index_tree or query_solution_index for the project/folder/namespace surface
-> find_indexed_symbols for target declarations; find_indexed_references / find_indexed_callers / find_indexed_relationships for impact checks
-> get_source_map(path, scope: file, mode: selector)
-> choose the smallest likely symbol from the source map
-> get_symbol(path, symbolSelectorJson)
-> only use get_file if the source map/symbol body is not enough
```

Example:

```text
User: Add a null guard to LoadTable.
Expected:
1. Use `find_indexed_symbols` or `query_solution_index` to locate `LoadTable` and related callers/references/relationships.
2. Use `get_source_map` for the containing C# file with `mode: selector` before mutation.
3. Use `get_symbol` for LoadTable using a structured selector or stableSymbolKey.
4. Stage a complete candidate only after the body and local impact context are known.
```

### Stage And Review

```text
Complete candidate prepared
-> replace_text_in_file, replace_span_in_file, submit_file, or submit_symbol
   or add_symbol / add_field / add_property / add_method / add_constructor / add_nested_type
   or set_type_partial / add_using / remove_using / remove_symbol
-> stage_candidate_for_review
-> launch_staged_diff, or Host/sidecar opens WinMerge using returned source/staged paths
   -> if launch/review is blocked or cancelled, stop the queue and fix before continuing
-> Operator saves the whole candidate or leaves source unchanged
-> record_diff_decision(stagedRecordId, accepted|rejected)
-> obey the returned classification
-> check IndexRefresh; accepted single-file edits refresh immediately, completed multi-file sessions rebuild once
```

For coupled multi-file C# edits, use one monitor session and stage all affected files before the first `launch_staged_diff`. Overlay compilation must see the proposed files together; WinMerge review is still serial, one file at a time.

For small Razor, markup, CSS, JSON, config, or other text edits, prefer `replace_text_in_file` with exact `oldText`, `newText`, and `expectedMatches: 1`. Use `replace_span_in_file` when exact line/column bounds are already known. Supply `expectedFileHash` and old-text/hash guards when available. Use full-file `submit_file` only for new files, broad rewrites, or unsafe narrow edits.

For any file at or above 32KB in a cold session, regardless of extension, do not call `get_file`. Call `refresh_file(sourceFilePath)`, then read the returned `workingFilePath` in bounded chunks. If the file is already in context in the current session, do not re-read it; call the narrow edit tool directly with `expectedOldText` or a hash guard from that in-context text.

Do not narrate routine known-good Monitor workflows. Call the needed tool and report only changed file, staged record or next required decision, validation result, and blockers.

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

Before emitting a call site to a type reached through `using` directives, local namespace context, or a known dependency, load that target type's real callable surface first. Use Roslyn `search_symbols` plus `get_type_overview`, or Monitor `get_source_map` / `get_symbol` when the target is in the watched project. Write calls against actual method names, return types, parameter types, and overloads. Overlay compile validation is the safety floor for missed call-site context, not a substitute for loading the target surface.

When the referenced surface lives in the watched project and the namespace is known, prefer `get_source_map(scope: "namespace", namespaceName: "...", mode: "navigation")` to inspect the namespace surface before drilling into a file or symbol. File-scope source-map `suggestedNextCalls` may include low-rank namespace-surface calls derived from `using` directives.

If overlay compile validation or build diagnostics reveal a missing/broken call site that Roslyn discovery did not surface, text search is allowed as a diagnostic fallback. Use grep/text search only to locate the missed file or literal call site, then confirm with Roslyn/Monitor structure where possible, add the missed file to the same monitor session, stage a corrected candidate, and retry review. This exception does not make grep the first-pass C# discovery tool.

Do not use Roslyn CodeLens `apply_code_action` against watched source in this workflow. Treat CodeLens as read/analysis-only unless the Operator explicitly authorizes a separate non-monitor mutation path. Refactorings and fixes for watched source should be converted into a complete candidate and staged through Monitor MCP so WinMerge review and vote-plus-hash classification remain authoritative.

`get_source_map` is a Monitor-owned read/discovery tool. It is expected before C# edits because it gives compact current structure and stable lexical symbol keys without loading full bodies. Treat it like a code manifest: signatures identify the contract surface (return type, name, arguments, modifiers), while bodies come from `get_symbol`. Use `navigation` mode to choose a file/member, `selector` mode to get stable keys and hashes for a chosen file, `detail` mode when contract detail is needed without full audit payloads, and `full` mode only for audit/debug. Source maps show durable file-header metadata such as `AIFileContext` and `FileVersion`, but omit legacy workflow-history attributes such as `AIChange`, `AIHistory`, `AIInstructions`, and `UserHistory`; do not remove those attributes from source merely for token cleanup.

## Marker And Glyph Rules

Do not add process markers to source:

- No `AI edit here`.
- No `TODO monitor anchor`.
- No `... existing code ...`.
- No temporary workflow comments.

Routine workflow notes belong in monitor-owned staged records, sessions, ledgers, or docs.

Do not use emoji, glyphs, or decorative Unicode as anchors, markers, or edit instructions. They are fragile in diffs, tokenization, copy/paste, encodings, and context patches. If existing source contains glyphs, treat them as legacy human content and never as an edit anchor.

Source comments are allowed only when they explain real code behavior. Domain metadata such as `AIFileContext` and `FileVersion` is part of the watched project's convention.

When making a meaningful C# source change, preserve and update the existing `AIFileContext` file-name / purpose / notes header as required by the change, and bump that physical file's `FileVersion`. For new C# files, add both `AIFileContext` and `FileVersion("1.0")`. Do not add routine process history with `AIChange`, `AIHistory`, `AIInstructions`, or `UserHistory`; put that in monitor notes or ledgers.

## Razor Files

Razor files are not plain C# files. Do not apply C# Roslyn symbol surgery directly to `.razor` or `.cshtml` source. For Razor edits, prefer `replace_text_in_file` for exact small changes or `replace_span_in_file` when bounds are already known. Full-file submission is still acceptable for new Razor files, broad structural rewrites, or unsafe narrow edits.
