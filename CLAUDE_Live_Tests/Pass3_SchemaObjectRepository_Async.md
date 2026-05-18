# Pass 3 — SchemaObjectRepository Async-Only

## Operator Prompt (verbatim)

> we need to do a simplet test use one of the other files in teh repoistory seciotn and just do async

Followed by, after staging:

> tjhe point is roslnn is supposed to save tokens

## Interpretation

- Pick a different repository in `SchemaStudio.Data\`. Convert to async only.
- No SQL dictionary. No regions. No null guards added beyond baseline. Single-axis diff so WinMerge is readable (lesson from Pass 2 rerun).
- Use Roslyn first for discovery to (a) honor the Roslyn-first rule, (b) actually exercise the telemetry proxy so the Operator can see it, and (c) save tokens vs reading whole files when a compact semantic view suffices.

## Scope

- Target: [SchemaStudio.Data\SchemaObjectRepository.cs](C:/Schema Studio - DBV2/SchemaStudio.Data/SchemaObjectRepository.cs), 3838 bytes, 5 public methods.
- Out of scope: SQL extraction, regions, null guards, caller updates.

## Workflow

1. Roslyn discovery (this is where the user wanted feedback):
   - `search_symbols("SchemaObjectRepository")` — found the class plus a `_schemaObjectRepository` field in `IntegrationsViewImportControl` (UI project).
   - `get_type_overview("SchemaStudio.Data.SchemaObjectRepository")` — confirmed 5 public methods, no interfaces, ctor takes `string connectionString`, no diagnostics.
   - `find_callers` on each public method × 4 — all empty.
   - `find_references` on the type and on `GetByDatabase` — also empty, despite the field above. See Finding 11.
2. Monitor read:
   - `get_file` on the baseline. (I also called `get_source_map` selector but that was redundant for a whole-file submit — see Finding 12.)
3. Author candidate: rename each public method to `*Async`, change return types to `Task` / `Task<T>`, add `CancellationToken cancellationToken = default`, swap Dapper calls for their `*Async` siblings wrapped in `CommandDefinition` with the token. Keep inline `const string sql = @"..."` blocks. No other changes.
4. Stage: `submit_file` → record `20260517_183111241_submit_file_SchemaObjectRepository_03226b7b`. Overlay validation: 82 syntax trees, **2 overlay files** (consumer slice picked up), **0 diagnostics**. Confirms that `IntegrationsViewImportControl` does not invoke the renamed methods (the field is held but unused, consistent with the empty `find_callers`).
5. `launch_staged_diff` → WinMerge, PID 31120. Operator saved.
6. `record_diff_decision(accepted)`:
   - operatorDecision: `accepted`
   - classification: **`accepted-normalized`** (same line-ending normalization as Pass 2 rerun)
   - decisionMatchesClassification: `true`
   - stagedNormalizedHash == currentNormalizedHash (`05318b53...`)

## Hashes

| | Value |
|---|---|
| originalHash | `fcba6d39b60bb09147808f4c9501df7423a5489e51d464484d9963963de25fa5` |
| stagedHash | `73c0f748d7265b8a17e3fa429e1135aad6d124684fbd3a52f1693bb8d3333779` |
| currentHash | `7fd577b6832f394f76134f4a1018dfd8a40b0fff88d52a04cf8ffaca6c387320` |
| stagedNormalizedHash | `05318b53fd56795f9cae652a5fa6bf951f234d6e3500ca5bf1e4973ae84d5de6` |
| currentNormalizedHash | `05318b53fd56795f9cae652a5fa6bf951f234d6e3500ca5bf1e4973ae84d5de6` |

## What Pass 3 Validated

- Roslyn-first discovery on a fresh, untouched repository.
- Overlay compile across a wider solution slice (2 overlay files, vs 1 for Pass 2 rerun) — caught any consumer breakage before WinMerge.
- Single-axis change (async only) producing a readable WinMerge diff: every method block changed but each change is locally tight (sig + Dapper call + await), no structural reshuffling.
- Vote-plus-hash classification path `accepted-normalized` again — confirms the line-ending-normalization classifier is stable across two consecutive accepts in the same session.
