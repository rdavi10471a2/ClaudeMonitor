# Smoke Test Coverage Review Prompt

Use this prompt with the attached `MonitorBaseClaude_SmokeTestCoverageReviewPackage.zip`.

```text
Please review this MonitorBaseClaude smoke-test coverage package.

I am not asking whether the architecture is good in the abstract. I am asking whether the current deterministic smoke tests cover the safety-critical workflow well enough for the next Claude-runtime trial.

Context:

MonitorBaseClaude is a local Monitor MCP Tool Server and WinForms Host being built so Claude can later work against a watched project through constrained tools.

Core invariants:

  - Monitor Tool Server stages candidates but does not directly overwrite watched source.
  - WinMerge is the Host-owned review/save surface.
  - Operator accepts all or rejects all.
  - Operator does not hand-edit or partially merge hunks.
  - record_diff_decision classifies by vote-plus-hash agreement.
  - accepted = reported accepted + watched hash equals staged candidate hash.
  - rejected = reported rejected + watched hash equals original baseline hash.
  - mismatches classify dirty-unexpected and block further AI edits.
  - get_source_map is read-only discovery, not an accept/reject gate.
  - C# edits should narrow through navigation/selector source maps and get_symbol before staging.
  - Real watched DBV2 source-map tests are read-only; mutation tests should use disposable fixtures.

Fresh deterministic smokes run on 2026-05-16:

  - dotnet build .\MonitorBaseClaude.slnx
  - --fixture-decision-gate-smoke
  - --fixture-roslyn-surgery-smoke
  - --fixture-razor-smoke
  - --source-map-smoke EditorSurface --scope folder --mode navigation

Please evaluate:

  1. What workflow/tool coverage is proven by these smokes?
  2. What coverage is still missing or weak?
  3. Which missing coverage should block first real Claude MCP staging, if any?
  4. Are any tests redundant or misleading?
  5. Are fixture tests sufficiently representative of real DBV2 mutation risk?
  6. Is the Razor safe-mode lane correctly bounded?
  7. Is the source-map navigation/selector/get_symbol hierarchy covered enough?
  8. What are the next 3 highest-value smoke tests to add?

Please separate:

  - deterministic Monitor correctness coverage
  - Claude/client behavior coverage
  - local Ollama/model-behavior probes
  - future nice-to-have telemetry or UI coverage

Do not recommend broad architecture churn unless a coverage gap shows a real safety risk.
```
