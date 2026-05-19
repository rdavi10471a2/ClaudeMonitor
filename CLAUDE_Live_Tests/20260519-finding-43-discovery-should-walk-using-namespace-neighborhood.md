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

Discovery discipline should plainly expand from explicit `using` directives to the namespace neighborhood (same prefix + sibling + parent namespaces)

## Severity

suggestion

## File/tool

`CLAUDE.md` — "Discovery Discipline" section. Implicit in the Finding 38 surface-load rule but not stated as its own discipline. Pairs with Finding 40 (proposed `scope: namespace` source-map mode) and Finding 42 (using-array affordance).

## Observed

The current operator-confirmed discipline (Finding 38) is to load the surface of types referenced via explicit `using` directives before emitting call sites. But a file's usings imply more than the explicit imports: they anchor a **namespace neighborhood**. A file with `using Foo.Bar.Repositories;` is overwhelmingly likely to also need types in `Foo.Bar.Services` and `Foo.Bar.Models` for the feature it implements, even when those are not in the import block (resolved via FQN, partial-class continuation in another file, or because the file's own namespace declaration lives at `Foo.Bar.Something`).

Today no plain rule tells the agent to walk that neighborhood during context building. Empirically, real WinForms/Razor screens that hit a repository also hit DTOs, options, and services living one namespace step away.

## Expected

A plain paragraph in CLAUDE.md "Discovery Discipline":

> The file's `using` directives anchor a namespace neighborhood. When building context for a feature edit, walk same-prefix and sibling namespaces (`Foo.Bar.*` and `Foo.Bar`) as well as the explicit usings. Roslyn `search_symbols` accepts namespace-qualified queries; the structural form once available is `get_source_map(scope: namespace, ...)` per Finding 40.

## Minimal fix

Add the paragraph above to "Discovery Discipline" in CLAUDE.md, adjacent to the Finding 38 surface-load rule once that rule lands.

## Evidence

Pass 19 discovery was confined to the file under edit (`McpAddApiFixture.cs`), whose usings were minimal — so the neighborhood walk was not exercised. For realistic WinForms/Razor screens hitting repositories the neighborhood walk is the natural extension of Finding 38 and prevents the "wrote a call site to a type that exists in a sibling namespace the agent never inspected" failure mode.
