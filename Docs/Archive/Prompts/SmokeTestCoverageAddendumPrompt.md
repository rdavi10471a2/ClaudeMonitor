# Smoke Test Coverage Addendum Prompt

Use this with `MonitorBaseClaude_SmokeTestCoverageAddendum.zip`.

```text
Please review this addendum to the MonitorBaseClaude smoke coverage review.

The main correction is about serverDerivedMetadata:

  - add_symbol does populate symbolsAdded.
  - remove_symbol does populate symbolsRemoved.
  - add_using/remove_using populate using metadata.
  - submit_symbol replacement leaves symbolsAdded/symbolsRemoved empty, which may be correct because replacement preserves symbol identity.

Please evaluate whether the revised next-smoke priorities are right:

  1. syntax-error rejection smoke
  2. dirty-unexpected recovery smoke
  3. submit_symbol changed/replaced metadata smoke

Please separate:

  - safety blockers before first Claude read-only/runtime pass
  - blockers before first real watched-source mutation
  - useful telemetry/post-decision-verification improvements

Assume the core vote-plus-hash truth table, Razor safe-mode lane, Roslyn add/remove symbol staging, and source-map navigation hierarchy are already covered by the previous package.
```
