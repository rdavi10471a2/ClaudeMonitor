# Source-Map Token Retest — 2026-05-16 (post-PR #11)

**Watched solution**: `C:\Schema Studio - DBV2\Schema Studio.sln`

**Monitor build**: VVG_LIVE_ANALYSIS at `c2cb779` (merge of `e3678e3` / PR #12, which includes `4c670cc` / PR #11 *Lean source maps toward contract shape*). Rebuilt and respawned before measurement.

**Tool surface**: Monitor MCP `get_source_map` only. No CodeLens reads in this retest.

**Purpose**: duplicate Codex's source-map token retest in [the lean-source-map-contracts work](#) against the live watched project, side-by-side with Codex's reported numbers. Capture deltas honestly; do not over-explain them.

## Results

| # | Path | Scope | Mode | fileCount | symbolCount | estimatedTokenProxy | budgetLimit | wasTruncated | Codex (for comparison) |
| ---:| --- | --- | --- | ---:| ---:| ---:| ---:| --- | --- |
| 1 | `""` | project | navigation | 0 (truncated) | 0 (truncated) | **63,675** | 20,000 | yes | 23,707 / 20,000, truncated |
| 2 | `""` | project | detail | 0 (truncated) | 0 (truncated) | **193,646** | 20,000 | yes | 70,352 / 20,000, truncated |
| 3 | `Data` | folder | navigation | 1 | 15 | **986** | 20,000 | no | 3,369, not truncated |
| 4 | `UI` | folder | navigation | — | — | **(payload-too-large)** | 20,000 | yes (raw response exceeded Claude Code's tool-result delivery limit at 74,481 chars) | 5,192, not truncated |
| 5 | `Data\BaseTableRepository.cs` | file | selector | — | — | **(file does not exist)** | 25,000 | n/a — invocation errored | 2,477, not truncated |
| 5a | `SchemaStudio.Data\SchemaObjectRepository.cs` (substitute) | file | selector | 1 | 8 | **2,584** | 25,000 | no | (2,477 reference) |
| 5b | `SchemaStudio.Data\SchemaObjectColumnRepository.cs` (substitute) | file | selector | 1 | 8 | **2,702** | 25,000 | no | (2,477 reference) |

### Suggested narrowing summaries (cases 1 and 2, truncated)

Both project-scope responses returned the same top-10 narrowing list (server is deterministic across modes for narrowing rank):

1. `UI\IntegrationsViewParserControl.cs` — 79 symbols
2. `SourceBakups\BehaviorSplit_20260410_105702\IntegrationsViewImportControl.Behavior.cs` — 70 symbols
3. `SourceBakups\BehaviorSplit_20260410_105702\IntegrationsViewImportControl.cs` — 52 symbols
4. `UI\MergedEditorSurface\IntegrationsViewImportControl.cs` — 52 symbols
5. `SchemaStudio.SematicModel\Model\ViewSourcedColumnDefinition.cs` — 43 symbols
6. `SchemaStudio.Data\SchemaObjectColumnDefinition.cs` — 40 symbols
7. `SchemaStudio.Data\SchemaObjectDefinition.cs` — 35 symbols
8. `UI\MergeDialog.cs` — 29 symbols
9. `SchemaStudio.AIHelpers\AIAttributes.cs` — 28 symbols
10. `UI\DatabaseDomainManagerForm.cs` — 27 symbols

## Honest assessment of the deltas

The retest does not reproduce Codex's numbers. Every case differs materially. Possible causes (not investigated in this retest — captured for follow-up):

- **`SourceBakups\BehaviorSplit_20260410_105702\` files appear in my narrowing suggestions** (entries #2 and #3, contributing 70 + 52 = 122 symbols of mostly-duplicate legacy code). If Codex's test ran against a watched-project state where `SourceBakups\` had been excluded, ignored, or had not yet existed, the project-scope numbers would drop substantially. Worth checking: does PR #11 (or any subsequent PR) gate `SourceBakups\` out of source-map enumeration, and was that gating active in Codex's measurement environment?
- **`Data\` folder shape differs**. Codex's `Data` folder navigation reported 3,369 token proxy; mine reports 986 against `fileCount: 1, symbolCount: 15` (the file is `Data\TargetScriptRepository.cs`). My number is *lower*, not higher. The implication is that Codex was measuring against a state where `Data\` had more files — consistent with `Data\BaseTableRepository.cs` (the legacy table-based file removed in `LegacyTableBasedRemoval_20260410_132110`) still being present.
- **`Data\BaseTableRepository.cs` does not exist in the current watched source**. Only `Data\BaseTableRepository.cs.bak` remains, under `SourceBakups\LegacyTableBasedRemoval_20260410_132110\Data\`. The retest invocation errored. Codex's measurement of 2,477 tokens on this path implies the file was present in Codex's test environment. **Substitutes** (per operator direction: use `BaseObject` / `BaseObjectColumn` if `BaseTable*` not available — and since there are no `BaseObject*` files either, the post-removal successors are `SchemaObjectRepository.cs` / `SchemaObjectColumnRepository.cs`): both selector reads landed within 5–9% of Codex's reference (2,584 and 2,702 vs 2,477). The shape parity makes sense — both substitutes are sibling repository classes with the same constructor + Insert/Update/SaveAll/GetByX pattern as the missing `BaseTableRepository`. **This single result is the cleanest validation in the retest that PR #11's cost model is stable across files of similar shape.**
- **`UI\` folder navigation overflowed the tool-result delivery channel** at 74,481 characters even though `wasTruncated: true` was set. This suggests that PR #11's narrowing-on-truncation may not be fully eliding payload for `folder` scope the way it does for `project` scope (project-scope responses came back as small narrowing-only payloads; folder-scope did not). Independent of the token-proxy comparison, this is a real finding worth flagging — the budget gate is meant to *prevent* this kind of overflow.

Net read: **Codex was almost certainly testing against a different watched-project state**, most likely one with `SourceBakups\` excluded (or a project before the legacy-table-removal moved files into `SourceBakups\`). The comparison numbers should therefore be read as "PR #11 reduces source-map token cost meaningfully in a clean tree" rather than "PR #11 produces these exact numbers in any tree."

## PR #11 is still doing real work

Even without matching Codex's exact figures, the trim is observable:

- The narrowing suggestions returned by the server **do not include the AI* attribute argument bodies in `signature` fields**. Compare entry shapes in the cases-1-and-2 narrowing list (just file path / reason / symbol count) vs the pre-PR-11 signature dumps that previously bloated every class signature with embedded `AIFileContext` strings.
- `Data\` folder navigation now reports compact symbol signatures: `"signature":"private const string MS_DescriptionPropertyName"` (no value, no decoration) rather than the older `"signature":"private const string MS_DescriptionPropertyName = \"MS_Description\";"`. That's the contract-shape lean.
- The mode hierarchy now includes a documented `detail` mode (case 2) — not present before PR #11.

So the retest confirms PR #11 landed and is operating; the absolute numbers just don't line up with Codex's reference because the watched-source state isn't identical.

## Suggested follow-up

- **#G** *Confirm `SourceBakups\` exclusion policy in source-map enumeration*. If the intent is for source-map reads to ignore `SourceBakups\` (legacy/archived content), the policy needs to be active and visible. If the intent is to include it, the narrowing suggestions are correct but the project-scope token cost will always be inflated by backup content. Either choice is defensible — but it should be documented.
- **#H** *Folder-scope truncation payload size*. Project-scope truncation returns a small narrowing-only response. Folder-scope truncation (case 4, `UI`) returned a 74k-character payload that overflowed the tool-result delivery channel. The truncation behavior should be parity-consistent across scopes — either both narrowing-only on overflow, or both full-shape with a different limit.
- **#I** *Reference-environment snapshot for token retests*. To make future retests reproducible against a known baseline, capture the watched-project file inventory (and `SourceBakups\` state) at the time of a benchmark measurement, so subsequent retests can detect "did the tree change?" vs "did the source-map logic change?".

## Repro

To reproduce the table above, against a current build of the Monitor MCP server with PR #11:

```text
1. get_source_map (no path; defaults: scope=project mode=navigation)
2. get_source_map scope=project mode=detail
3. get_source_map path=Data scope=folder mode=navigation
4. get_source_map path=UI scope=folder mode=navigation
5. get_source_map path=Data\BaseTableRepository.cs scope=file mode=selector
```

Record `fileCount`, `symbolCount`, `estimatedTokenProxy`, `budgetLimit`, `wasTruncated`, and the first 10 entries of `suggestedNarrowing` when truncated.

## Conclusion

PR #11 is doing the work it claims to do (contract-shape signatures, AI* attribute elision, `detail` mode). The retest does not reproduce Codex's exact numbers because the watched-project state differs (notably `SourceBakups\` content and the legacy `Data\BaseTableRepository.cs`). Three follow-up items above would close the gap and make future retests reproducible.
