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

`get_source_map` has no namespace scope; loading types referenced through a file's `using` directives requires guessing the folder layout that hosts the namespace

## Severity

suggestion

## File/tool

`get_source_map.scope` parameter. Monitor MCP tool manifest scope descriptions.

## Observed

Available scopes are `auto`, `file`, `folder`, `project`. To load the surface of types reached via a file's `using` directives (per Finding 38 discipline: surface-load before emitting call sites), the natural request shape is "give me a source map for namespace `Foo.Bar.Repositories`." No such scope exists. Current workaround: guess the folder that hosts that namespace (often `Foo\Bar\Repositories\` but not guaranteed), then call `get_source_map(scope: folder, path: "Foo/Bar/Repositories")`. Brittle when namespace declarations and folder layout diverge (common in real projects with shared utility namespaces or top-level namespace re-rooting).

## Expected

A `scope: namespace` mode that accepts a fully-qualified namespace string and returns the source map for all files whose top-level namespace matches.

## Minimal fix

Add `scope: namespace` + `namespace: "..."` parameter on `get_source_map`. Resolve via Roslyn's symbol model (already loaded for source-map generation).

## Evidence

Pass 19 source-map of `McpAddApiFixture.cs` showed `usings: ["System.Collections.Generic"]`. To surface what types that using exposes (the Finding 38 discipline), no first-class call exists today; the agent has to compose `search_symbols` + `get_type_overview` per using, file by file.
