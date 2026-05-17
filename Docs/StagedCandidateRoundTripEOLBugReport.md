# Bug: WinMerge-save round-trip drops byte-shape, so accepted candidates still classify as `dirty-unexpected`

**Project**: `MonitorBaseClaude` (`C:\VSCodeProjects\MonitorBaseClaude`)

**Follow-up to**: [StagedCandidateEncodingMismatchBugReport.md](StagedCandidateEncodingMismatchBugReport.md) (the staging-emitter direction, fixed by PR #7).

**Discovered against**: live workflow run on `SchemaStudio.Data\DatabaseDomainRepository.cs::Insert`, this session.

**Status**: open; reproducible in this session; documented temporary workflow accommodation below.

## Symptom

The happy-path acceptance loop completes semantically but the gate continues to classify it as `dirty-unexpected`:

1. Model calls `submit_symbol` → staged candidate is written with PR #7's byte-shape preservation.
2. Operator opens WinMerge against (staged, watched). PR #7 ensures the staged file matches the watched file's BOM and dominant EOL conventions.
3. Operator copies left (staged) into right (watched) and saves the right pane in WinMerge.
4. The change **does** land in the watched source — semantically correct, syntactically valid, intended behavior present.
5. `record_diff_decision(stagedRecordId, accepted)` returns:
   - `classification: dirty-unexpected`
   - `currentHash` ≠ `originalHash` (watched **was** modified — save did work)
   - `currentHash` ≠ `stagedHash` (but post-save bytes ≠ staged-candidate bytes)
   - `decisionMatchesClassification: false`
   - `queueStatus: blocked-dirty-unexpected`
   - `blocksFurtherEdits: true`

In other words: **WinMerge's save writes the file with its own byte-shape conventions, which differ from the staged candidate's bytes that the gate hashes against.** PR #7 fixed the *stage* side of the byte preservation. The *save-back-through-WinMerge* side is the remaining half.

## Live evidence from this session

Watched file: `C:\Schema Studio - DBV2\SchemaStudio.Data\DatabaseDomainRepository.cs`

Staged record id: `20260516_172321597_submit_symbol_DatabaseDomainRepository_8625f2d1`

Change: add `ArgumentNullException.ThrowIfNull(model);` to `Insert(DatabaseDomainDefinition model)`.

| Hash | Value |
| --- | --- |
| `originalHash` (baseline before edit) | `63c78e72ffc0f27cc255d1d7160a99d3187ed12b8052e4afdc2d544219cab026` |
| `stagedHash` (proposal as written by submit_symbol after PR #7) | `79a955a45f947270b8639777ea7ec673889666cee30010e145f9b3ac163624ea` |
| `currentHash` (watched after WinMerge save) | `1328f29145311d119bb5dcff4d57d00bbf53772a9080e0b427a5449bb6aa81ce` |

Three distinct hashes. The watched file ends up at a hash that matches neither the original nor the staged candidate, even though:

- The staged file on disk had the correct content (verified before save).
- The operator's WinMerge action was the canonical "copy left to right, save right" sequence.
- The watched file contents post-save semantically equal the staged candidate (verified by reading the file — `ArgumentNullException.ThrowIfNull(model);` is at the right place, only byte-shape differs).

This is the WinMerge-save direction re-emitting the file with its own (configurable, but defaulted) EOL/encoding behavior. Common WinMerge defaults that cause this:

- Save uses the destination pane's existing EOL style, not the source pane's.
- Save normalizes BOM presence/absence per its own preferences.
- Save may normalize trailing newline, mixed-EOL files (see also the per-line-EOL corner case observed against `SchemaObjectColumnRepository.cs` in this session, where the file has 206 LFs + 1 terminal CRLF — different from the dominant-EOL heuristic).

## Why this isn't fixed by PR #7

PR #7's scope was the **staging emitter**: the bytes the Monitor server *writes* into the staged candidate file. It correctly mirrors the watched file's BOM and dominant EOL.

What PR #7 cannot control:

- What WinMerge writes during save. WinMerge is a third-party GUI tool; its save behavior is governed by its own settings (`Editor → File Format`, `Editor → Line Format`, etc.) and defaults.
- The user's WinMerge configuration may even change between sessions, between team members, or between WinMerge versions.

So even with a perfect staging emitter, the round-trip through WinMerge can still drift byte-shape on the way back to disk.

## Recommended fixes

These can land independently; either is a real improvement.

### 1. Hash against a normalized form (preferred near-term)

Compute a `normalizedHash` alongside the existing raw hashes on staged records. Normalization rules:

- Strip BOM.
- Replace all `\r\n` and lone `\r` with `\n`.
- Strip trailing whitespace at end of file (optional, but common WinMerge mutation).

On `record_diff_decision(accepted)`:

- If `currentHash == stagedHash` raw → classify `accepted` (existing path).
- Else if `currentNormalizedHash == stagedNormalizedHash` → classify `accepted-normalized` (new), with the decision record explicitly noting the byte-shape divergence so it's auditable.
- Else → `dirty-unexpected` (existing path, but now actually meaningful — bytes AND normalized-bytes both disagree).

This gives operators back the "happy-path accept" without weakening the gate's protection against actual content drift. A WinMerge save that adds or removes a *real* character still trips dirty-unexpected. Only byte-shape-only mutations get the `accepted-normalized` classification.

This is essentially the option-2 fallback from the original encoding-mismatch report, now justified by the post-PR-7 evidence that we still need it.

### 2. Detect WinMerge's save EOL conventions and pre-match the staged candidate to them

Empirically harder. Would require either:

- Probing WinMerge config (registry / config file) to know its save defaults.
- Pre-saving the staged file through WinMerge once (using its CLI) to capture how it would write the bytes.
- Or shipping a WinMerge config alongside the project that pins save behavior to a known shape.

This is the "make the round-trip byte-exact" approach. More work, more fragile, more dependent on a specific GUI tool. The normalized-hash path (#1) doesn't have these dependencies and ages better.

### 3. Replace WinMerge as the GUI step (longer-term)

If a Razor-style review surface lands later, or if the project moves to a custom diff/save tool that emits with controlled byte-shape, the round-trip gap closes structurally. Out of scope here, listed for completeness.

## Temporary workflow accommodation (until #1 or #2 lands)

For now, the documented workflow needs to acknowledge this gap or operators will reasonably conclude the gate is broken. Two operator-facing rules:

1. **A `dirty-unexpected` classification immediately after a `record_diff_decision(accepted)` call is expected** when the operator believes the WinMerge save was correct. Don't panic; don't re-vote; verify semantically:
   - Read the relevant symbol in the watched file (via `get_symbol` or the IDE) and confirm the intended change is present.
   - If present: the change *did* land. The gate refused to upgrade the classification because of byte-shape drift, not because the change went wrong.
   - If absent: real failure — the save didn't go through (wrong copy direction in WinMerge, focus on wrong pane, etc.). Re-stage and retry.

2. **Recover the staged record explicitly**, don't silently re-vote. The manifest already says `blocked-dirty-unexpected -> do not re-vote`. The recovery path during this window is:
   - Confirm the change is semantically present (rule 1 above).
   - Note in any follow-up record that the dirty-unexpected was a byte-shape-only divergence, with the watched file's intended change verified by symbol read.
   - Move on. Do not re-stage a no-op against the watched file just to "clear" the gate — that would propagate the same byte-shape drift in the opposite direction.

This is a temporary accommodation, not a permanent rule. It expires when fix #1 (normalized-hash classification) lands.

## Suggested follow-up tickets

- **#C** *Normalized-hash classification path*: implement the rules in fix #1 above. Cover with a smoke test: stage against a CRLF-BOM source, save via WinMerge, assert classification is `accepted` (or `accepted-normalized`, depending on chosen naming) and not `dirty-unexpected`.
- **#D** *Per-line EOL preservation in staging emitter*: even with PR #7, mixed-EOL files (most LF + terminal CRLF, observed against `SchemaObjectColumnRepository.cs` in this session) still trip a one-byte staged-vs-original mismatch at stage time. Independent of WinMerge. Lower priority than #C because #C absorbs the symptom downstream, but worth fixing for correctness on the no-op detection path.

## Repro context

- Watched source: `C:\Schema Studio - DBV2\SchemaStudio.Data\DatabaseDomainRepository.cs` — UTF-8 with BOM, mixed LF body + terminal CRLF.
- Staging emitter build: includes PR #7 (`Preserve staged candidate text shape`).
- WinMerge version: 2.x default save behavior on Windows 11.
- Tool path that surfaced this: `submit_symbol` → manual WinMerge launch (the `launch_staged_diff` tool was not surfaced in this Claude Code session's deferred tool list because the MCP server was respawned mid-session; semantically equivalent).
- Operator action: `Merge → Copy All from Left to Right`, focus right pane, `Ctrl+S`.
- Result: `dirty-unexpected`, watched semantically contains the change, three distinct hashes as listed in the live evidence table.
