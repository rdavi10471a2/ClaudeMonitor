# Token Analysis — Generic Overview Review Of DBV2

**Question:** If I were asked to give a generic structural overview of the entire Schema Studio DBV2 solution, what does that cost in tokens under each discovery strategy, and is it practicable?

**Short answer:** Yes, practicable on every reasonable strategy when I'm running in a 1M-context window. Bytes-only strategy A is wasteful but fits; structural strategies B–D all comfortably fit in any modern context window and beat A by 2–10×. The right strategy depends on what kind of "overview" the operator actually wants.

## Solution scale (measured)

```
Projects:                6 (.csproj files, excl. test/Designer)
C# source files:         78
Razor files:             0
Total source bytes:      431,336 (~421 KB)
Average file size:       5,529 bytes
Largest file:            39,334 bytes — UI/IntegrationsViewParserControl.cs
Top-10 largest files:    all UI/MergedEditorSurface controls or SemanticModel parsers
```

**Per-project breakdown** (computed by find walks; the root `SchemaStudio` project sweeps everything else in subdirs because its csproj sits at the repo root, so its raw `find` count is the solution total):

| Project | Files | Bytes |
|---|---:|---:|
| SchemaStudio (root, UI/Models/Services/EditorSurface/AI) | ~30 (derived) | ~235K (derived) |
| SchemaStudio.SemanticModel | 27 | 134K |
| SchemaStudio.Data | 14 | 48K |
| SchemaStudio.UIShared | 2 | 8K |
| SchemaStudio.AIHelpers | 1 | 4K |
| SchemaStudio.ModelSupport | 4 | 3K |
| **Solution total** | **78** | **~431K** |

Note: the find walk overcounts SchemaStudio when run at its root; the derived row above subtracts the other projects to estimate its real footprint.

## Token estimation method

- C# code: empirically ~280–320 tokens per KB at the Anthropic tokenizer. I'll use **300 tokens/KB** as midpoint.
- JSON envelope per Monitor or Roslyn tool response: ~50–100 tokens overhead.
- Source-map server-reported `estimatedTokenProxy`: observed at ~625 tokens/KB during this session for a 4 KB file in selector mode. The server's proxy is higher than my raw-content estimate because it includes JSON envelope, symbol metadata, and selector key strings. I'll use it where applicable.
- Roslyn responses (`get_type_overview`, `search_symbols`): observed at ~200–400 tokens per type for compact contract data.

Numbers below are estimates with ±25% uncertainty — adequate for choosing a strategy, not a precise budget.

## Strategy comparison

### Strategy A — Read every file (naive `get_file` loop)

```
78 × get_file → 431K × 300 tokens/KB = ~129K tokens of content
78 × ~75 tokens of envelope = ~6K tokens of overhead
                          Total IN: ~135K tokens
```

**Pros:** every byte in context, no follow-up reads needed.
**Cons:** for "generic overview" most of those bytes are irrelevant (method bodies, SQL strings, XAML-style UI control wiring). Worst tokens-per-insight ratio. Risks bursting smaller context windows when combined with a long conversation.
**Verdict:** **practicable only in a 1M-context window**; wasteful in any. Use only if the task genuinely requires every line, which "generic overview" doesn't.

### Strategy B — Source map at project scope, navigation mode, then selective reads

```
6 × get_source_map(scope: project, mode: navigation) ≈ 6 × ~8K = ~48K tokens
  (navigation mode returns file/type names without bodies)
~12 × get_file on hand-picked representative files (avg 5.5 KB) = ~22K
                          Total IN: ~70K tokens
```

**Pros:** structural map first, then targeted detail. Roslyn-validated symbol shape with stable selector keys.
**Cons:** project-scope source maps haven't been measured precisely in this session; the 8K-per-project estimate could be off by a factor of 2 either way. Worth measuring before committing to the strategy.
**Verdict:** **practicable**, mid-range cost, good structural awareness. Best when the next likely operator question is "show me the implementation of X" — Strategy B gets you the map fast and the body cheap.

### Strategy C — Roslyn-first structural pass, then selective Monitor reads

```
list_solutions                                   ~500
get_project_dependencies                        ~1.5K
6 × get_file_overview (per project entry point)  ~6K
~40 × get_type_overview on public top-level types ~10K
get_di_registrations                            ~1K
get_public_api_surface per project              ~5K
~8 × get_file on the most-relevant bodies         ~13K
                          Total IN: ~37K tokens
```

**Pros:** smallest footprint that still reaches into specific bodies. Uses Roslyn's compact semantic views for the structural pass — every call returns just contract data, no bodies. Selective Monitor reads only where a body is needed.
**Cons:** more tool calls (latency cost rather than token cost). Some operator questions can't be answered without bodies (e.g. "how does the SQL get assembled?") and would trigger additional reads.
**Verdict:** **practicable and recommended for "generic overview."** This is the strategy that demonstrates the architecture's intent — compact semantic queries instead of byte-shipping.

### Strategy D — Pure Roslyn structural, no bodies

```
get_public_api_surface × 6 projects             ~5K
get_type_hierarchy on key bases                 ~2K
get_di_registrations                            ~1K
get_project_dependencies                        ~1K
search_symbols surveys (Repository / Service / Manager / Form / Control) ~5K
                          Total IN: ~14K tokens
```

**Pros:** tiny footprint, answer in one tool-pass round trip. Good for "name the projects and their main responsibilities."
**Cons:** zero algorithmic detail. Can describe shape, not behavior.
**Verdict:** **practicable and ideal when the operator's question is structural-only**: "what's in this solution," "which projects depend on which," "what types live where."

## Side-by-side

| Strategy | Tokens IN | Bodies in context | Best for |
|---|---:|---|---|
| A — read every file | ~135K | All | Tasks requiring every line (rarely "overview") |
| B — project source maps + selective reads | ~70K | Selected | "Show me a few examples" overviews |
| C — Roslyn structural + selective reads | ~37K | ~8 hand-picked | Recommended for generic overview |
| D — Pure Roslyn structural | ~14K | None | Structural-only questions |

## Practicability assessment

In a **1M-context window** (what I'm running in this session per `claude-opus-4-7[1m]`), all four strategies fit with massive headroom. Conversation history of ~50K tokens + Strategy A's ~135K = ~185K of 1M = 19% utilization. Strategy C is 9% utilization. Trivial.

In a **200K-context window**, Strategy A would consume 67% of budget on raw bytes alone, leaving ~65K for conversation, prior responses, and the actual overview I'm writing. Strategies B–D leave most of the budget for reasoning and response.

In a **100K-context window** (older Claude), Strategy A is infeasible if combined with any meaningful conversation. Strategies C and D are the only safe choices.

## Cost estimate (input tokens only, at ~$5 / 1M Anthropic input)

| Strategy | Tokens IN | Cost per pass |
|---|---:|---:|
| A | 135K | $0.67 |
| B | 70K | $0.35 |
| C | 37K | $0.18 |
| D | 14K | $0.07 |

The cost spread is 10× between A and D for what is fundamentally the same task at different levels of resolution. Token discipline maps directly to dollar discipline.

## When each strategy is right

- **Use D** when the operator asks "give me a sense of what this is" — name the projects, top-level types, dependency direction. No bodies needed.
- **Use C** when the operator asks "review this codebase" — structural overview plus enough sampled implementations to verify the structural picture matches reality. **This is the right default for `/init`-style or "explain the project" prompts.**
- **Use B** when the operator names specific areas they want depth on — "tell me about the Data repositories" — and the rest can stay structural.
- **Use A** when the operator says "read every file" or when the task is genuinely whole-codebase (e.g. cross-file refactoring spanning all 78 files at once, which is rarely true).

## What this implies for the workflow rules

The "read once per file via `get_file(sessionId)`" rule in `CLAUDE.md` is correct for **edit workflows** where I know which files I'll touch. For **overview workflows**, applying that rule literally to 78 files is Strategy A — overkill. The "Reason In Cloud, Compose Locally" section already says "Roslyn-first for semantic discovery"; for an overview pass, Roslyn-first plus selective Monitor reads (Strategy C) is the operational meaning of that rule.

Worth a future clarification in `CLAUDE.md`: "for whole-solution overview tasks, prefer Roslyn structural queries (`get_public_api_surface`, `get_project_dependencies`, `get_type_overview`) over file reads; only call `get_file` on individual files when the structural view raises a specific question about implementation detail." Not blocking; flagging as a doc precision improvement.

## Measurement caveats

- All token figures except the explicit `estimatedTokenProxy` data point are model estimates. A precise measurement would require running Strategy C on DBV2 once and tracking actual token consumption per call.
- Source-map `estimatedTokenProxy` is the server's own estimate, not Anthropic's tokenizer output. They're correlated but not identical.
- I didn't measure source-map at project scope in this session; the 8K/project estimate is extrapolated from the 2.5K/file selector observation and may be conservative or aggressive by 2×.
- For a real budget commitment, run Strategy C on DBV2 once and tally actual usage. That's a candidate for a one-off measurement pass not in the test catalog.
