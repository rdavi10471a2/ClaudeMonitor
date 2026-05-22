---
status: new
type: doc-suggestion
created: 2026-05-21
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---

## Summary

`replace_span_in_file` requires the model to supply 1-based line/column coordinates. The model currently derives these by reading the file via `get_file` and counting lines visually. A `find_text_span` tool (or a `locateByText` parameter on `replace_span_in_file`) would let the server locate the span internally, eliminating the line-counting step and reducing the round-trip to one tool call for small isolated text edits.

## Proposed Tool: `find_text_span`

```
find_text_span(path, findText, expectedOccurrenceIndex = 0)
→ { startLine, startColumn, endLine, endColumn, occurrenceCount }
```

- `findText`: exact span text to locate (same ordinal match as `expectedOldText`)
- `expectedOccurrenceIndex`: which occurrence (0-based) to return; server errors if the index is out of range
- Returns line/column bounds that can be passed directly to `replace_span_in_file`

Alternatively, add `locateByText` to `replace_span_in_file` so the find-and-replace is a single atomic call:

```
replace_span_in_file(path, findText: "<PageTitle>Manage Views</PageTitle>", newText: "...", expectedOccurrence: 1)
```

Server finds the match, validates uniqueness, and replaces — no line numbers in the model prompt at all.

## Implementation Notes

No external library is needed for exact matching. `MemoryExtensions.IndexOf(ReadOnlySpan<char>, ReadOnlySpan<char>, StringComparison.Ordinal)` is O(n) with no allocation — identical asymptotic cost to the existing `GetOffsetFromLineColumn` walk.

```csharp
// Pseudo-implementation for exact match
int charOffset = baseText.AsSpan().IndexOf(findText.AsSpan(), StringComparison.Ordinal);
if (charOffset < 0) throw new InvalidOperationException("find_text_span: text not found.");
// validate uniqueness if expectedOccurrence == 1
int second = baseText.AsSpan(charOffset + 1).IndexOf(findText.AsSpan(), StringComparison.Ordinal);
if (second >= 0 && expectedOccurrenceIndex == 0)
    throw new InvalidOperationException($"find_text_span: {occurrenceCount} occurrences found; use expectedOccurrenceIndex to select one.");
// convert charOffset → line/column using existing inverse of GetOffsetFromLineColumn
```

If normalized line-ending-tolerant matching is ever needed (e.g., `\r\n` source, `\n` findText), **DiffPlex** (`NuGet: DiffPlex`) provides a normalized line-level differ that handles BOM and line-ending variants without manual normalization. For the typical use case (exact span from a just-read file), plain `IndexOf` is sufficient.

## Session Temperature and When `find_text_span` Adds Value

Two distinct session states determine whether line-counting is a real cost:

**Cold session** (file not yet in context):
- Model has no file content and no span coordinates.
- Current path: `get_file` (reads the whole file) → model counts to target line → `replace_span_in_file`.
- With `find_text_span` / `locateByText`: model supplies the known old text; server locates and replaces. No read, no line-counting.
- **This is the primary use case for the proposed tool.**

**Warm session** (file already loaded into context from a prior `get_file` or `Read` call this session):
- Model already holds the full file text and can identify exact span content from that in-context knowledge.
- Correct path: call `replace_span_in_file` directly with `expectedOldText` from context. No re-read, no `find_text_span`.
- The `expectedOldText` guard validates in-context reasoning server-side — it IS the safety mechanism.
- **Re-reading a warm file is a workflow error, not a missing-tool problem.**

Practical implication: the current doc/CLAUDE.md instruction `refresh_file → Read(workingFilePath) in chunks → replace_span_in_file` is correct for cold-session entry only. Warm-session continuation should skip the Read entirely.

## Workflow Impact

Cold session, current:
1. `get_file` — reads 36KB, model counts to line 60
2. `replace_span_in_file` — sends new text

Cold session, with `find_text_span` or `locateByText`:
1. `replace_span_in_file(findText: ..., newText: ...)` — one call, no line counting, no `get_file`

Warm session (file already in context), current best practice:
1. `replace_span_in_file(expectedOldText: <from context>, ...)` — zero overhead; `expectedOldText` validates in-context knowledge against server state

For large block replacements the saving is proportional in cold sessions: the model never needs to count lines into a 500-line Razor file; it supplies the old block text as `findText` and the server anchors it.

## Guard Properties Preserved

`expectedOldText` (used today) and `findText` (proposed) are the same string — both are ordinal exact matches. The proposed approach doesn't weaken the safety guarantee; it moves the offset-derivation step from model-side to server-side.
