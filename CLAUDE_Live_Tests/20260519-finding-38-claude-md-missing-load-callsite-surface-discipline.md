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

CLAUDE.md does not plainly state the discipline of loading a referenced type's surface from `using` directives **before** emitting any call site against that type. The discipline is operator-confirmed in Pass 19 conversation, but my written instructions only describe consumer-side discovery (find_callers/find_references for MY change), not consumed-surface discovery (load `_repo.GetX(...)` target's actual signatures before writing the call).

## Severity

confusing

## File/tool

`CLAUDE.md` — sections "Discovery Discipline", "Read And Narrow Before Editing", and "Monitor MCP vs CodeLens MCP". The rule is implied by "Always prefer Roslyn tools over text or grep search for C# symbol discovery" but never named directly.

## Observed

`grep -in 'using|surface|call site|caller' CLAUDE.md` returns matches only on (a) consumer discovery (finding callers of my changing symbol), (b) `add_using` / `remove_using` tool descriptions, and (c) generic "Roslyn discovery before edits." Nothing plainly says: "before writing `someObject.SomeMethod(...)` against a type imported via a `using`, run `search_symbols` + `get_type_overview` on that type and write the call against real method names, parameter types, and overloads."

The architectural consequence is that an agent reading CLAUDE.md alone might assume "overlay compile will catch CS0103/CS1061" and skip the proactive surface load. Overlay catching it is the safety floor, not the workflow. The proactive load is also cheaper than an overlay round-trip + re-discovery + re-stage on a call-site error.

## Expected

CLAUDE.md should plainly state, in either "Discovery Discipline" or "Required Edit Loop," something like:

> Before emitting any call site to a type imported via a `using` directive, run `search_symbols` for the type and `get_type_overview` (or `get_source_map`) to load its real member surface. Write the call site against actual method names, parameter types, return types, and overloads — not against invented signatures. Overlay compile validation is the safety floor for this discipline, not a substitute for it.

## Minimal fix

Add the paragraph above to CLAUDE.md "Discovery Discipline" section (around current line 95). Companion update: the "Read And Narrow Before Editing" example at current line ~178 could include a second example showing surface-load before a call site, alongside the existing `LoadTable` body-read example.

## Evidence

- `grep -in "using|surface|call site|callsite|caller" CLAUDE.md` (Pass 19, 2026-05-19): only matches are on consumer discovery, tool descriptions, and generic "Roslyn before edits."
- Memory `feedback-load-surface-from-usings-before-callsites.md` (this session) records the operator correction that prompted this finding.
- Related operator-confirmed framing: layered defense (Roslyn discovery → overlay compile gate → vote-plus-hash + WinMerge) treats overlay as a catch-net, not a workflow primary; the proactive surface load is what keeps overlay from having to fire in the common case.
