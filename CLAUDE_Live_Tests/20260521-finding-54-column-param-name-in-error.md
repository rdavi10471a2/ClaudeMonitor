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

`GetOffsetFromLineColumn` throws `ArgumentOutOfRangeException(nameof(column), ...)` for an out-of-range column regardless of whether the bad argument was `startColumn` or `endColumn`. The line parameter correctly uses the caller-supplied `parameterName` (so errors say `"startLine"` vs `"endLine"`), but the column side always says `"column"`. Diagnosing a bad end-column from a multi-span chain edit requires guessing which side failed.

## Repro

1. Call `replace_span_in_file` with a valid `startLine`/`startColumn` but an `endColumn` value that is past end-of-line.
2. Server throws `ArgumentOutOfRangeException` with `ParamName = "column"`.

## Expected

`ParamName` should be `"endColumn"` (or `"startColumn"` as appropriate), matching the convention already used for `startLine` / `endLine`.

## Actual

`ParamName` is always `"column"` for both start and end column validation failures.

## Evidence

- Source file: `MonitorBaseClaude.McpServer/MonitorWorkflowService.cs`
- Line 2268: `throw new ArgumentOutOfRangeException(nameof(column), "Column numbers are 1-based.");`
- Compare line 2263 for the line parameter: `throw new ArgumentOutOfRangeException(parameterName, ...)` — correctly uses the passed-in name.
- The method signature is `GetOffsetFromLineColumn(string text, int line, int column, string parameterName)` — `parameterName` is passed for `line` but no equivalent is passed for `column`.

## Notes

Fix: add a `columnParameterName` parameter alongside `parameterName`, or pass it as a separate argument at the two call sites in `ReplaceSpanInFile`:

```csharp
int startOffset = GetOffsetFromLineColumn(baseText, startLine, startColumn, nameof(startLine), nameof(startColumn));
int endOffset   = GetOffsetFromLineColumn(baseText, endLine,   endColumn,   nameof(endLine),   nameof(endColumn));
```
