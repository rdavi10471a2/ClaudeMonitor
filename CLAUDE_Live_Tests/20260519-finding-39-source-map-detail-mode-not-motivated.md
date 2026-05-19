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

`get_source_map` `detail` mode is not motivated distinct from `selector` in practice

## Severity

suggestion

## File/tool

`get_source_map` `mode` parameter. Monitor MCP tool manifest mode descriptions. CLAUDE.md `## Monitor MCP vs CodeLens MCP`.

## Observed

Across Pass 19 and prior sessions I default to `selector` for file scope and never reach for `detail`. `selector` already returns stable keys, parameter types, signatures, hashes. `detail` adds parameter names, non-AI attribute argument summaries, and usings — none of which drive a different agent decision. The use cases either escalate ("I need bodies" → `get_symbol`) or are already covered by `selector`.

## Expected

Either fold the `detail` increments into `selector` (parameter names, usings) since they are cheap and concrete, or rename/repurpose `detail` to a use case agents would actually pick (e.g. `contract` for surface views including XML doc comments).

## Minimal fix

Promote parameter names + file `usings` array into `selector` mode. Deprecate `detail` or repurpose it for XML-doc-bearing contract views.

## Evidence

Pass 19 source-map of `McpAddApiFixture.cs` in selector mode returned 7 symbols with full parameter types, stable keys, and the file `usings` array — sufficient for reading and selector building. `detail` mode never invoked. Same pattern across prior passes per session memory.
