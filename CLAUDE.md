# MonitorBaseClaude Agent Rules

This project is a monitor and MCP workflow host. Treat watched source as protected source, not as a scratchpad.

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
-> Operator saves the whole candidate or leaves source unchanged
-> record_diff_decision(stagedRecordId, accepted|rejected)
-> obey the returned classification
```

### Unsafe Or Ambiguous Requests

```text
Direct watched-source write requested -> refuse and stage instead.
Partial hunk merge requested -> refuse and regenerate a smaller candidate.
Name-only symbol mutation requested -> use get_source_map and a structured selector first.
Dirty-unexpected returned -> stop editing that path until Host/Operator refreshes or inspects state.
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
- `rejected`: Operator reported `rejected` and the watched hash equals the original baseline hash.
- `dirty-unexpected`: Operator report and watched hash disagree, or the watched hash matches neither original nor staged.

If the candidate is a no-op and the staged hash equals the original baseline hash, do not enqueue a normal diff by default. Report it as no-change/no-op so Accept and Reject cannot collapse into the same hash state.

## Source Structure

The current watched file is a voting member in the loop. It carries the prior converged pattern: names, order, comments, attributes, boundaries, namespaces, partial-class shape, and local style.

Pattern conformance does not freeze structure. Structural changes are allowed when they are explicit, bounded, staged, reviewed, and accepted all-or-none. We are preventing accidental structural drift, not deliberate architectural evolution.

Duplication is not automatically debt. Do not extract helper methods or create abstractions merely to remove small local repetition. Inline repetition is acceptable when it makes workflow state, source-map mode behavior, vote-plus-hash logic, or operator behavior easier to audit. Do not perform DRY cleanup as a side effect of a narrow change; extract only when the abstraction is explicitly requested, represents a real named concept, reduces meaningful risk, or is staged as a bounded structural candidate.

## Monitor MCP vs CodeLens MCP

Use the Monitor MCP server for workflow state, sessions, staged candidates, hashes, ledgers, diff review coordination, and accept/reject classification.

Use Roslyn CodeLens MCP for external code intelligence: diagnostics, references, callers, type hierarchy, dependency analysis, generated code, and broad semantic questions.

Do not use Roslyn CodeLens `apply_code_action` against watched source in this workflow. Treat CodeLens as read/analysis-only unless the Operator explicitly authorizes a separate non-monitor mutation path. Refactorings and fixes for watched source should be converted into a complete candidate and staged through Monitor MCP so WinMerge review and vote-plus-hash classification remain authoritative.

`get_source_map` is a Monitor-owned read/discovery tool. It is expected before C# edits because it gives compact current structure and stable lexical symbol keys without loading full bodies. Treat it like a code manifest: signatures identify the contract surface (return type, name, arguments, modifiers), while bodies come from `get_symbol`. Use `navigation` mode to choose a file/member, `selector` mode to get stable keys and hashes for a chosen file, `detail` mode when contract detail is needed without full audit payloads, and `full` mode only for audit/debug. Source maps intentionally omit legacy `AI*` and `FileVersion` attributes; do not remove those attributes from source merely for token cleanup.

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
