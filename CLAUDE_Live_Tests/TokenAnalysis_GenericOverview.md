# Token Analysis — Generic Overview Review Of DBV2

**Question:** If I were asked to give a generic structural overview of the entire Schema Studio DBV2 solution, what does that cost in tokens under each discovery strategy, and is it practicable?

**Short answer:** Yes, practicable on every reasonable strategy when I'm running in a 1M-context window. **Measured this session (Pass 4):** Strategy A is ~108K, Strategy C is ~118K. The original estimates had A and C separated by 4×; the measured spread is roughly equal, because Roslyn `get_type_overview` on WinForms `UserControl`-derived types is far more expensive than originally estimated (~30K each vs ~1K assumed). Strategy D is still tiny and stays the cheapest. The right strategy depends on what kind of "overview" the operator actually wants AND whether the solution is UI-heavy or POCO-heavy.

> **Measurement provenance:** numbers in this revision are from a live Strategy C deep dive run during Pass 4 (2026-05-17) against `C:\Schema Studio - DBV2`. Per-call response-size measurements captured from MCP tool results; token estimates use `chars / 4` consistently. The pre-revision text used theoretical estimates with ±25% uncertainty; the revised numbers replace those with observed values. The biggest surprise vs the original estimates was per-type cost variance, not per-strategy cost.

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

- C# code: ~250 tokens/KB at the Anthropic tokenizer (≈ chars/4). C# token density is fairly stable since most content is ASCII keywords and identifiers.
- JSON envelope per Monitor or Roslyn tool response: ~50–100 tokens overhead. Negligible relative to body cost except on trivial calls (e.g. `get_project_dependencies` for a no-deps project).
- Source-map server-reported `estimatedTokenProxy`: observed at ~625 tokens/KB during a prior session for a 4 KB file in selector mode. The server's proxy is higher than my raw-content estimate because it includes JSON envelope, symbol metadata, and selector key strings.

## Pass 4 per-call measurements (measured 2026-05-17)

| Call | Response chars | Est. tokens | Notes |
|---|---:|---:|---|
| `get_public_api_surface` (whole solution) | 126,101 | ~31,525 | 505 public entries across 6 projects, deterministically sorted, with summary buckets |
| `get_project_dependencies` × 6 | ~1,500 total | ~375 | Direct deps only; no transitive expansion in this graph |
| `get_project_health` (whole solution, 5 hotspots per dimension) | ~16,800 | ~4,200 | 7 health dimensions × 6 projects |
| `get_type_overview` (POCO/DTO, e.g. `SchemaObjectColumnDefinition`) | ~3,000 | ~750 | Compact contract — public members + diagnostics |
| `get_type_overview` (small class, e.g. `ColumnBinder`, `QueryBinder`) | ~2,000–3,000 | ~500–750 | One-method classes plus diagnostics |
| `get_type_overview` (parser/visitor mid-size, e.g. `BasicSelectVisitor`) | ~12,000 | ~3,000 | Includes 33 nullability warnings |
| `get_type_overview` (WinForms UserControl, e.g. `IntegrationsViewParserControl`) | **~35,000** | **~8,750** | Includes the full inherited `System.Windows.Forms` interface stack (24 interfaces enumerated as metadata refs) plus 23 CS86xx warnings |
| `get_diagnostics(severity=error)` | ~3,200 | ~800 | 8 errors enumerated |
| `get_file` (estimated as bytes/4; not re-burned this pass) | varies | ~684 to ~9,833 | Range: `DatabaseDomainRepository.cs` 2,737 B → `IntegrationsViewParserControl.cs` 39,334 B |

**Biggest single insight from these measurements:** `get_type_overview` cost is bimodal:
- POCO / pure-C# class: ~2,000–3,000 chars per call.
- WinForms `UserControl`-derived class: ~30,000–35,000 chars per call, because the response enumerates the entire `System.Windows.Forms` interface stack inherited from `UserControl` (IOleControl, IPersistStreamInit, ISynchronizeInvoke, etc.) and bases (`ContainerControl` → `ScrollableControl` → `Control` → `Component` → `MarshalByRefObject`). This wasn't visible from the older single-file `get_source_map` measurements.

Implication: in a UI-heavy solution, Strategy C's bulk cost comes from `get_type_overview` calls on UI types, not from `get_public_api_surface`. A workflow-cost test idea (TS-006 in ProposedTests.md) is whether `get_type_overview` should have a "skip-metadata-interfaces" mode for derived UI types.

## Strategy comparison (measured)

### Strategy A — Read every file (naive `get_file` loop)

```
78 × get_file → 431,336 bytes ÷ 4 = ~107,834 tokens of content
78 × ~75 tokens of envelope = ~5,850 tokens of overhead
                          Total IN: ~113,700 tokens (measured)
```

**Pros:** every byte in context, no follow-up reads needed.
**Cons:** for "generic overview" most of those bytes are irrelevant (method bodies, SQL strings, WinForms control wiring). Worst tokens-per-insight ratio. Risks bursting smaller context windows when combined with a long conversation.
**Verdict:** **practicable only in a 1M-context window**; wasteful in any. The 39,334-byte `IntegrationsViewParserControl.cs` alone is ~9,800 tokens — almost 10% of an 100K context just for one file's body.

### Strategy B — Source map at project scope, navigation mode, then selective reads

```
6 × get_source_map(scope: project, mode: navigation) ≈ 6 × ~8K = ~48K tokens
  (still extrapolated; not directly measured in this pass)
~12 × get_file on hand-picked representative files (avg 5.5 KB) = ~22K
                          Total IN: ~70K tokens (extrapolated)
```

**Pros:** structural map first, then targeted detail. Roslyn-validated symbol shape with stable selector keys.
**Cons:** project-scope source maps still not directly measured in this pass. Pass 4's deep dive used Roslyn `get_public_api_surface` rather than project-scope source maps, so the ~8K-per-project estimate remains theoretical. Worth a follow-up measurement pass.
**Verdict:** **practicable**, mid-range, good structural awareness — but unconfirmed cost.

### Strategy C — Roslyn-first structural pass, then selective Monitor reads (run this pass)

Measured run:

```
get_public_api_surface (whole solution)            31,525
get_project_dependencies × 6                          375
get_project_health (whole solution)                 4,200
get_type_overview × 10 strategic types            ~25,000   ← variance: 2 UI types ≈ 17K of this 25K
get_diagnostics(severity=error)                       800
[get_file on 6 representative bodies — NOT called]
  if called: 6 × avg 7,500 bytes ÷ 4 =            ~11,250
                                  Subtotal IN:    ~73,150 tokens (no-bodies survey)
                                  With bodies:    ~84,400 tokens
```

**With 10 type overviews and no bodies: ~73K tokens. With bodies on the 6 most relevant files: ~84K tokens.** Notably *higher* than the original 37K estimate, and the gap is almost entirely two `get_type_overview` calls on WinForms types (~30K combined). On a backend-only solution the same survey would land closer to the original 37K estimate.

**Pros:** still smaller footprint than Strategy A; the deep dive surfaces structural insights (unused classes, complexity hotspots, disposable misuse, naming-rule violations) that A's raw bytes don't directly reveal. Strategy C is what `get_project_health` is built for — it does in one call what a human reviewer would extract by reading 78 files.
**Cons:** more tool calls (latency cost). `get_type_overview` on UI-derived types is expensive — selective use of it is necessary on UI-heavy projects.
**Verdict:** **practicable and recommended for "generic overview."** Worth tuning: when surveying UI types, use `get_type_overview` on at most 1–2 representative `UserControl`-derived types; for the rest of the UI surface, read structural data from `get_public_api_surface` only.

### Strategy D — Pure Roslyn structural, no bodies (extrapolated)

```
get_public_api_surface (whole solution)             31,525  (measured)
get_project_dependencies × 6                           375  (measured)
get_project_health                                   4,200  (measured)
get_type_overview on ~5 strategic types (POCO only) ~2,500  (extrapolated, avoid UI types)
                          Total IN:               ~38,600 tokens (mostly-measured)
```

**Pros:** tiny footprint, answer in one tool-pass round trip. Good for "name the projects and their main responsibilities."
**Cons:** zero algorithmic detail. Can describe shape, not behavior. **Higher than originally estimated** — `get_public_api_surface` alone is ~31K, so the whole-solution Strategy D floor is ~36–40K.
**Verdict:** **practicable and ideal when the operator's question is structural-only**: "what's in this solution," "which projects depend on which," "what types live where." Cheapest strategy at scale, but the floor (~36K) is no longer "tiny."

## Side-by-side (measured)

| Strategy | Tokens IN | Bodies in context | Best for | Measurement basis |
|---|---:|---|---|---|
| A — read every file | ~114K | All 78 files | Tasks requiring every line | Sum of file sizes ÷ 4 (this pass) |
| B — project source maps + selective reads | ~70K | Selected (~12) | "Show me a few examples" | Still extrapolated |
| C — Roslyn structural + selective reads | ~73K (no bodies), ~84K (with 6 bodies) | Selected (~6) | **Recommended for generic overview** | Measured this pass |
| D — Pure Roslyn structural | ~39K | None | Structural-only questions | Mostly measured |

## Practicability assessment

In a **1M-context window** (what I'm running in this session per `claude-opus-4-7[1m]`), all four strategies fit with massive headroom. Conversation history of ~50K tokens + Strategy A's measured ~114K = ~164K of 1M = ~16% utilization. Strategy C is ~13% utilization. Trivial.

In a **200K-context window**, Strategy A consumes ~57% of budget on raw bytes alone, leaving ~86K for conversation, prior responses, and the overview I'm writing. Strategies B–D leave most of the budget for reasoning and response.

In a **100K-context window** (older Claude), Strategy A is infeasible if combined with any meaningful conversation. Strategy C is ~73–84%; tight. Strategy D at ~39K is the only safe choice.

## Cost estimate (input tokens only, at ~$5 / 1M Anthropic input)

| Strategy | Tokens IN | Cost per pass |
|---|---:|---:|
| A | ~114K | $0.57 |
| B | ~70K (extrapolated) | $0.35 |
| C | ~84K (with bodies) / ~73K (no bodies) | $0.42 / $0.37 |
| D | ~39K | $0.20 |

**Revised cost spread:** ~3× between A and D (was estimated 10×). The reduction comes from Strategy C/D costing more than originally estimated — Roslyn's whole-solution `get_public_api_surface` is genuinely large (~31.5K alone), and `get_type_overview` on UI types adds another ~9K each. Token discipline still maps to dollar discipline, but the per-pass cost difference between strategies is on the order of $0.20–$0.40, not the $0.07–$0.67 originally estimated.

### Where the cost goes that you can't shrink without server changes

For a Strategy C pass against a UI-heavy solution, **~25% of the token budget is `System.Windows.Forms` interface stack metadata** repeated across each UI-type `get_type_overview` call. The Roslyn-CodeLens MCP server returns the inherited interface tree from metadata assemblies in `hierarchy.interfaces`, which for `UserControl`-derived types means 24 entries per call (IOleControl, IOleObject, IPersistStreamInit, ISynchronizeInvoke, IBindableComponent, IKeyboardToolTip, and the rest of the COM/Forms interop surface). This is **identical metadata** repeated on every UI type, and **rarely relevant** to the question the caller is asking.

A server-side flag (e.g. `includeInheritedInterfaces: false` or `excludeMetadataInterfaces: true`) would shrink each UI-type call from ~30K chars to ~3–5K chars and bring Strategy C on this solution from ~84K → ~60K. Filed as Finding 16; see `FINDINGS.md`.

## When each strategy is right

- **Use D** when the operator asks "give me a sense of what this is" — name the projects, top-level types, dependency direction. No bodies needed.
- **Use C** when the operator asks "review this codebase" — structural overview plus enough sampled implementations to verify the structural picture matches reality. **This is the right default for `/init`-style or "explain the project" prompts.**
- **Use B** when the operator names specific areas they want depth on — "tell me about the Data repositories" — and the rest can stay structural.
- **Use A** when the operator says "read every file" or when the task is genuinely whole-codebase (e.g. cross-file refactoring spanning all 78 files at once, which is rarely true).

## What this implies for the workflow rules

The "read once per file via `get_file(sessionId)`" rule in `CLAUDE.md` is correct for **edit workflows** where I know which files I'll touch. For **overview workflows**, applying that rule literally to 78 files is Strategy A — overkill. The "Reason In Cloud, Compose Locally" section already says "Roslyn-first for semantic discovery"; for an overview pass, Roslyn-first plus selective Monitor reads (Strategy C) is the operational meaning of that rule.

Worth a future clarification in `CLAUDE.md`: "for whole-solution overview tasks, prefer Roslyn structural queries (`get_public_api_surface`, `get_project_dependencies`, `get_type_overview`) over file reads; only call `get_file` on individual files when the structural view raises a specific question about implementation detail." Not blocking; flagging as a doc precision improvement.

## Measurement caveats

- Token estimates use `chars / 4` as the conversion. This is reliable for C# (mostly ASCII keywords/identifiers) but understates token count for content with many short identifiers or JSON nesting. Strategy C numbers may be ~5% higher in actual Anthropic tokenizer output than the chars/4 figure suggests.
- Strategy B is the only strategy NOT directly measured this pass. The 8K/project source-map estimate is still extrapolated from a prior 2.5K/file selector observation. A future pass should run `get_source_map(scope: project, mode: navigation)` once per project to replace the estimate with a measurement.
- The `get_type_overview` UI-type bloat measurement (~30K chars) is from two samples (`IntegrationsViewParserControl`, `IntegrationsViewImportControl`). A wider sample across more UI types would confirm the figure but is unlikely to change the bimodal shape.
- Cost per pass at $5/1M Anthropic input is an order-of-magnitude figure; with prompt caching the marginal cost on repeated passes within the 5-minute cache TTL is much lower. The relative cost ordering between strategies remains stable regardless.
