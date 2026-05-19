---
status: new
type: finding
created: 2026-05-18
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---

## Title

Add a `split_file_by_symbols` primitive so logical-split refactors don't require the assistant to re-emit bytes that already exist watched-side

## Severity

suggestion

## File/tool

`MonitorBaseClaude.McpServer` — new tool addition.

## Observed

End-of-task refactors are typically **logical splits**: one accumulated source file gets reorganized into N smaller properly-sized files. No content generation is happening — the bodies already exist in the source. But the current tool surface forces the assistant to re-emit those bodies as `submit_file` content for each new target file, just so Roslyn can splice them. Cost on the model-output side ≈ one whole-file rewrite even though the assistant has no new design intent to express.

## Expected

A primitive that takes the relocation map as data, not as re-emitted bodies:

```
split_file_by_symbols(
  sourcePath,
  splits: [
    { targetPath: "ChildA.cs", symbols: ["MethodA", "MethodB"] },
    { targetPath: "ChildB.cs", symbols: ["TypeC"] }
  ],
  sessionId
)
```

Server parses source via Roslyn, emits each target file (namespace + class shell + moved member bodies copied byte-for-byte from source), and removes the moved members from source. All composed under sessionId as Working candidates; overlay validates the union of `{new files + shrunk source}` before any review.

## Minimal fix

Implement on top of existing primitives: server-side it's roughly N × `submit_file` (with body extracted from source via Roslyn) plus N × `remove_symbol` on the source, all under one session. The novelty is purely the API shape — assistant sends symbol *names* (~150 bytes per split) instead of symbol *bodies* (~filesize / N bytes each).

## Evidence

Discussed against the create-iterate-refactor pattern after Variants A/B/C closed. Today's whole-file emissions across a typical split: ~1× original-filesize in assistant output tokens. With this primitive: ~N × 150 bytes ≈ a few hundred output tokens regardless of file size. Closes the gap between "logical split semantics" (zero generation) and "current mechanics" (full re-emission).
