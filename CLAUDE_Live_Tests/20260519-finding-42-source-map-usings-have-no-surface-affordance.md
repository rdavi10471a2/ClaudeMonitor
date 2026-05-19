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

A file's source map exposes its `usings` array but provides no affordance for inspecting the types those usings reach

## Severity

suggestion

## File/tool

`get_source_map` response shape — specifically the relationship between `files[].usings[]` and the rest of the response (including `suggestedNextCalls`).

## Observed

File-scope source maps include `usings` as a flat string array. Nothing in the response surfaces what each using exposes or where it points in the watched solution. Combined with Finding 38 (discipline: load referenced surface from usings before emitting call sites), the agent currently has to manually compose a 2-step navigation per using: `search_symbols` for each consumed type → `get_type_overview`. The source map already has the Roslyn symbol model loaded; it could pre-stage this navigation as a ranked next-call.

## Expected

Either (a) an optional `includeReferencedSurfacePreview: true` parameter that returns lightweight contract summaries (signatures only, no bodies) for the public types in each used namespace, or (b) a `suggestedNextCalls` entry per `using` of the form `get_source_map(scope: namespace, namespace: "...")` once the namespace scope from Finding 40 lands.

## Minimal fix

Option (b) is the smallest diff and pairs cleanly with the `scope: namespace` from Finding 40. Add one ranked suggestedNextCalls entry per using, low-rank (after symbol body next-calls), labeled with `reason: "inspect-referenced-namespace-surface"`.

## Evidence

Pass 19 source-map response for `McpAddApiFixture.cs` listed `usings: ["System.Collections.Generic"]` with no affordance for "and here is how to inspect what that using exposes." Multiplied across a realistic 1200-line WinForms/Razor screen with 10-20 usings, this is meaningful agent navigation friction.
