# Smoke Test Catalog

This is a debug/maintainer catalog. It lists the current runnable smoke-test modes in `MonitorBaseClaude.ToolSmokeTests`.

Normal Claude review and edit planning should use `tools/list`, `get_tool_manifest`, and the staging guide instead. Smoke tests are for reproducing failures, proving fixes, and extending regression coverage.

Run from the repository root:

```powershell
dotnet run --project MonitorBaseClaude.ToolSmokeTests/MonitorBaseClaude.ToolSmokeTests.csproj -- <mode>
```

Smoke logs are written under:

```text
Working\History\ToolSmokeTests\<run-id>\
```

## Current Modes

| Mode | Fixture / Target | Primary coverage | Output |
| --- | --- | --- | --- |
| default, no flag | Configured watched solution and local Ollama | LLM routing over discovered MCP surfaces; asks default smoke questions and executes selected tools. | `summary.md` |
| `--scripted` | Configured watched solution | Deterministic monitor tool loop: status, workflow, session, source map, symbol read, staged `submit_file`, rejected decision. | `scripted-summary.md` |
| `--stage-comment-diff` | Configured watched solution | Stages a harmless `Program.cs` comment candidate with `submit_file`; optional `--wait` can pause for operator review. | JSON step log |
| `--record-decision <id> <accepted\|rejected>` | Existing staged record | Calls `record_diff_decision` directly for manual sidecar/operator workflows. Optional: `--note`, `--session-id`. | JSON step log |
| `--fixture-accept-smoke` | Disposable DBV2-shaped C# fixture | Happy-path `submit_file` stage, simulated operator save, `record_diff_decision` accepted classification. | `fixture-accept-summary.md` |
| `--fixture-decision-gate-smoke` | Disposable DBV2-shaped C# fixture | Vote-plus-hash classifications: accepted, accepted-normalized, rejected, dirty-unexpected, blocked re-vote behavior. | `fixture-decision-gate-summary.md` |
| `--fixture-roslyn-surgery-smoke` | Disposable DBV2-shaped C# fixture | System Monitor Roslyn-backed edits: `get_source_map`, stable-key `get_symbol`, `add_using`, `submit_symbol`, `add_symbol`, `remove_symbol`, `remove_using`, accepted decisions. | `fixture-roslyn-surgery-summary.md` |
| `--fixture-codex-surgery-drill` | Disposable DBV2-shaped C# fixture | Multi-step partial-class refactor: source maps, stable-key symbol read, `set_type_partial`, `add_symbol`, `submit_symbol`, simulated accepted decisions, final source-map verification. | `fixture-codex-surgery-drill-summary.md` |
| `--fixture-property-placement-smoke` | Disposable DBV2-shaped C# fixture | Property insertion placement/trivia through `add_symbol`; verifies neighboring spacing and source-map result. | `fixture-property-placement-summary.md` |
| `--fixture-method-replacement-smoke` | Disposable DBV2-shaped C# fixture | Method replacement through `submit_symbol`; verifies body read, localized formatting, non-target preservation, source-map result. | `fixture-method-replacement-summary.md` |
| `--fixture-field-insertion-smoke` | Disposable DBV2-shaped C# fixture | Field insertion through `add_symbol`; verifies placement around field group and non-target preservation. | `fixture-field-insertion-summary.md` |
| `--fixture-field-removal-smoke` | Disposable DBV2-shaped C# fixture | Clean field removal through `remove_symbol`; verifies neighboring field/method text is preserved. | `fixture-field-removal-summary.md` |
| `--fixture-field-removal-dependency-smoke` | Disposable DBV2-shaped C# fixture | Stages removal of a field that still has dependents; verifies overlay compile errors are reported on the staged candidate. | `fixture-field-removal-dependency-summary.md` |
| `--fixture-overlay-gate-smoke` | Disposable DBV2-shaped C# fixture | Reuses dependency-removal setup and calls `launch_staged_diff`; verifies overlay-error review gate returns cancelled or host-unavailable instead of silently launching review. | `fixture-overlay-gate-summary.md` |
| `--fixture-overlay-queue-block-smoke` | Disposable DBV2-shaped C# fixture | Session queue enforcement: blocked overlay review marks record blocked and later clean staged diff in same session returns `review-chain-blocked`. | `fixture-overlay-queue-block-summary.md` |
| `--fixture-template-class-smoke` | Disposable DBV2-shaped C# fixture with placeholder file | Generated class shape through whole-file `submit_file`; verifies region order, nested types, final source map. This is not true new-file staging. | `fixture-template-class-summary.md` |
| `--fixture-new-file-smoke` | Disposable DBV2-shaped C# fixture with nonexistent target paths | True new-file staging through `submit_file`; verifies `<new-file>` baseline hashes, simulated accepted creation, rejected absent-file classification, and source-map read after creation. | `fixture-new-file-summary.md` |
| `--fixture-razor-smoke` | Disposable Razor fixture | Text `submit_file` path for `.razor`; verifies syntax/overlay behavior is Razor-pending and accepted decision path. | `fixture-razor-summary.md` |
| `--fixture-roslyn-semantic-smoke` | Configured `CodeLensSolutionPath` | External Roslyn CodeLens semantic ladder: `tools/list`, `list_solutions`, `get_diagnostics`, `search_symbols(query)`, `get_type_overview(typeName)`, `find_references(symbol)`, `find_callers(symbol)`, `find_implementations(symbol)`, `analyze_change_impact(symbol)`. Optional: `--symbol-query`, `--type-name`, `--symbol`, `--implementation-symbol`. | `fixture-roslyn-semantic-summary.md` |
| `--source-map-smoke [path]` | Configured watched solution | Single `get_source_map` call with optional `--scope` and `--mode`; writes raw and summary artifacts. | `source-map-summary.md` |
| `--source-map-budget-smoke [path]` | Configured watched solution | Same as source-map smoke, but expects an over-budget/truncated response. Defaults: `scope=project`, `mode=full`. | `source-map-summary.md` |
| `--source-map-corpus-smoke [path]` | Configured watched solution | Walks C# files under a target folder/project and records source-map size, token proxy, symbol counts, diagnostics, compact map, and navigation index. | `source-map-corpus-summary.md` |
| `--ollama-route-smoke` | Configured watched solution and local Ollama | Model routing probe: asks route-only questions and checks expected server/tool decisions. Optional: `--model`, `--pause`. | `ollama-route-*-summary.md` |
| `--ollama-router-drill-smoke` | Local Ollama | Fake-router drill for normalized action labels such as `GET_SYMBOL`; checks model can choose expected high-level action. Optional: `--model`, `--pause`. | `ollama-router-drill-*-summary.md` |

## Coverage By Area

### Monitor MCP safety and decision gates

- `--fixture-accept-smoke`
- `--fixture-decision-gate-smoke`
- `--fixture-overlay-gate-smoke`
- `--fixture-overlay-queue-block-smoke`
- `--fixture-new-file-smoke`
- `--stage-comment-diff`
- `--record-decision`

### System Monitor Roslyn-backed C# editing

- `--fixture-roslyn-surgery-smoke`
- `--fixture-codex-surgery-drill`
- `--fixture-property-placement-smoke`
- `--fixture-method-replacement-smoke`
- `--fixture-field-insertion-smoke`
- `--fixture-field-removal-smoke`
- `--fixture-field-removal-dependency-smoke`
- `--fixture-template-class-smoke`

### Source-map and context-budget behavior

- `--source-map-smoke`
- `--source-map-budget-smoke`
- `--source-map-corpus-smoke`

### External Roslyn CodeLens semantic behavior

- `--fixture-roslyn-semantic-smoke`

### Non-C# and model-routing probes

- `--fixture-razor-smoke`
- `--ollama-route-smoke`
- `--ollama-router-drill-smoke`
- default LLM mode

## Known Gaps

- True new-file staging is covered by `--fixture-new-file-smoke`, but the live WinMerge GUI path for reviewing a missing watched source path still needs a manual operator pass.
- CodeLens semantic smoke currently uses the configured live `CodeLensSolutionPath`; reference/caller/implementation counts are not deterministic.
- No overload-specific CodeLens reference smoke exists yet.
- No async/API impact smoke exists yet.
- No generated-code/source-generator CodeLens smoke exists yet.
- No diagnostic delta smoke exists yet for before/after staged candidates.
- No negative argument-shape smoke exists yet for mistakes such as `symbolName` passed to `find_references`.
- Typed insertion tools are exposed, but only generic `add_symbol` paths are strongly smoke-covered for several member kinds; direct `add_field`, `add_property`, `add_method`, `add_constructor`, and `add_nested_type` smokes should be added.
