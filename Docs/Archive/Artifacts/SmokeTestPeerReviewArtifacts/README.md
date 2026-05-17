# Smoke Test Peer Review Artifacts

Generated: 2026-05-15

This package is a compact review set for the current MonitorBaseClaude smoke surface. It intentionally includes summaries instead of raw JSON logs so Claude/ChatGPT can review the behavior without swallowing full tool payloads.

## Deterministic Smokes

These passed.

| Smoke | Result | What It Covers |
| --- | --- | --- |
| `dotnet build MonitorBaseClaude.slnx` | passed | UI, MCP server, and smoke project compile cleanly. |
| `--fixture-decision-gate-smoke` | passed | no-op staging, accepted, rejected, accept-not-applied, reject-after-save, and external dirty edit classifications. |
| `--fixture-roslyn-surgery-smoke` | passed | `get_source_map`, stable-key `get_symbol`, `add_using`, `submit_symbol`, `add_symbol`, `remove_symbol`, `remove_using`, and vote-plus-hash acceptance. |
| `--source-map-smoke EditorSurface --scope folder` | passed | DBV2 EditorSurface folder source map with parse status, diagnostics, attributes, return/parameter fields, base types, events, and field/property distinctions. |

## Ollama Route-Only Smokes

These are model-behavior probes, not deterministic Monitor correctness tests. They deliberately do not execute the chosen tools. The harness discovers the real MCP tool surface, sends the prompt to Ollama, then compares the chosen server/tool against the expected route kept in test metadata.

| Model | Result | Notes |
| --- | --- | --- |
| `qwen2:0.5b` | failed route expectations | Mostly answered instead of choosing tools; did not crash the harness. |
| `llama3.2:1b` | failed route expectations | Hallucinated tool names such as `get_monitor_sessions`; harness caught them as non-executable. |
| `llama3.2:3b` | partial route success | Correctly routed `find_file` and `start_monitor_session`; still missed source-map and symbol-body discipline. |

The useful finding: the Host-side route validator works, and small Ollama models are acceptable for mechanical harness testing but not for judging source-map-first discipline or candidate quality.

## Ollama Fake-Router Drills

These are even smaller local-model tests. They do not expose the real MCP manifest. They ask the model to classify the next workflow action from:

```text
SOURCE_MAP
GET_SYMBOL
STAGE_FILE
RECORD_DECISION
REFUSE_UNSAFE
ASK_NARROWING_QUESTION
```

| Model | Result | Notes |
| --- | --- | --- |
| `qwen2:0.5b` | 2/7 | Understands the first source-map step, but fails unsafe/direct-write and decision-state cases. |
| `llama3.2:3b` | 5/7 | Handles stage/decision/refuse cases, but still misses source-map-to-symbol sequencing. |

The useful finding: fake-router drills are a better local-Ollama starting point than the real manifest. They can test whether a local model is viable as a cheap workflow sentinel before letting it see the full tool surface.

## Included Files

- `fixture-decision-gate-summary.md`
- `fixture-roslyn-surgery-summary.md`
- `dbv2-editor-surface-source-map-summary.md`
- `ollama-route-qwen2-0.5b-summary.md`
- `ollama-route-llama3.2-1b-summary.md`
- `ollama-route-llama3.2-3b-summary.md`
- `ollama-router-drill-qwen2-0.5b-summary.md`
- `ollama-router-drill-llama3.2-3b-summary.md`
- `ollama-route-prompts-and-expected.md`

## Review Questions

1. Does the deterministic fixture coverage prove the vote-plus-hash decision gate?
2. Does the Roslyn surgery fixture prove enough for the next symbol-tool hardening pass?
3. Is the source-map summary compact enough for agent navigation without full-file reads?
4. Are the Ollama route failures useful evidence that the tool descriptions need stronger client-facing guidance, or simply that tiny local models are too weak?
