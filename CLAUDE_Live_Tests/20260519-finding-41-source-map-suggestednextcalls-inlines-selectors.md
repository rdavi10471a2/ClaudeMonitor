---
status: new
type: finding
created: 2026-05-19
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---

## Title

`get_source_map` `suggestedNextCalls` inlines full selector JSON per entry, duplicating data already present in `files[].symbols[]`

## Severity

suggestion

## File/tool

`get_source_map` response shape — specifically the `suggestedNextCalls` array.

## Observed

Pass 19 source-map of `McpAddApiFixture.cs` returned 5 ranked next-calls, each containing the full structured selector JSON (`stableSymbolKey`, `memberKind`, `containingNamespace`, `containingType`, `name`, `parameterTypes`, `arity`). The same selectors are already present in `files[].symbols[]`. So each symbol's selector appears twice in the response when it is in both arrays.

For a 7-symbol file the duplicated selector data adds ~1.2k tokens out of ~2.2k total response. Linear with symbol count — a 50-symbol file pays ~8k tokens of duplicated selector data just for the next-call rankings.

## Expected

`suggestedNextCalls` should reference symbols by stable key or symbol-array index, not re-inline the full selector JSON. Clients can compose the actual selector argument from the indexed symbol entry.

## Minimal fix

Change `suggestedNextCalls[].arguments` to a small reference such as `{ "symbolStableKey": "..." }` or `{ "symbolIndex": N }`. Keep the existing `tool` and `reason` fields. Document the resolution step in the response.

## Evidence

Pass 19 selector-mode response for `McpAddApiFixture.cs`: 5 `suggestedNextCalls` entries each ~250 tokens of inlined selector × 5 = ~1.2k tokens of repeated data. `estimatedTokenProxy: 2254` total for the response, meaning the duplication is ~55% of the response budget for this file.
