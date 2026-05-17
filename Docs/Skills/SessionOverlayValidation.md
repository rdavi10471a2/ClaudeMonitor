# Session Overlay Validation

Use for coupled multi-file C# edits.

## Rule

Stage every coupled file into the same monitor session before the first review launch. Overlay compilation must see the proposed files together, even though WinMerge review is serial.

## Flow

```text
start_monitor_session
stage file A with sessionId
stage file B with sessionId
review overlayValidation results
launch/review file A
record decision for file A
launch/review file B
record decision for file B
```

## Do Not

- Do not review file A before staging coupled file B.
- Do not treat a clean single-file overlay as enough when another staged file is required for the feature to compile.
- Do not continue to later diffs if an earlier staged item is blocked by validation or review-gate state.

## Unblock

- Stage a corrected candidate for the same blocked file.
- Or explicitly force-review the blocked item through the Host UI.
- Or abandon the chain and start a new monitor session for unrelated work.
