# WriteSet Planning

Use before staging when a task can affect more than one file, symbol, interface, caller, or generated companion.

Important: ReadSet/WriteSet is currently a procedural planning discipline, not a server-enforced contract. The MCP server records optional `manifestJson` intent and validates staged files by session overlay, but it does not yet parse, compare, or block on declared ReadSet/WriteSet contents.

## Rules

- WriteSet planning is required when the merge shape is two-file or larger, or when a coupling trigger applies.
- For a single-file, single-symbol edit with no coupling trigger, proceed directly to source-map/symbol read and staging; a formal WriteSet declaration is optional.
- Identify the **ReadSet**: files and symbols inspected before editing.
- Identify the **WriteSet**: files and symbols intended for mutation.
- The WriteSet is how the agent declares a coupled or multi-file merge before any review window opens.
- Do not stage coupled or multi-file edits until the affected edit set is understood.
- If a change is coupled across files, use one monitor session for the whole WriteSet.
- Do not hide a multi-file change as a sequence of unrelated single-file edits.

## Coupling Triggers

- Signature, interface, base class, override, or async changes.
- Moving SQL/query text into a companion partial or helper type.
- Adding/removing constructor parameters, fields, or injected services.
- Adding a method and updating callers.
- Splitting a class, extracting a partial, or moving a member group.

## Expected Output

Before staging, report:

```text
ReadSet:
- file/symbol observed

WriteSet:
- file/symbol to stage

Coupling:
- why these edits must validate together, or "none"

Merge shape:
- one-file | two-file | three-plus-file
```
