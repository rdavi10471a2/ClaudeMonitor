# Bug: Staged candidate bytes don't match watched-file save → happy path always classifies as `dirty-unexpected`

**Project**: `MonitorBaseClaude` (`C:\VSCodeProjects\MonitorBaseClaude`)

**Discovered against**: the new `launch_staged_diff` tool (PR #6).

**Status**: real-source repro on first end-to-end run; affects the all-or-none gate's happy path.

## Symptom

1. Model stages a candidate via `submit_file` / `submit_symbol`.
2. `launch_staged_diff` opens WinMerge correctly between the watched source and the staged candidate. ✓ tool itself works.
3. Operator chooses "Save" in WinMerge (full-candidate accept — no manual edits, no hunk picking).
4. `record_diff_decision` is called with `decision: accepted`.
5. Classification comes back as **`dirty-unexpected`**.
6. Inspection: watched-file hash after save does **not** equal the staged-candidate hash, even though the operator accepted the entire candidate.

This makes the documented all-or-none acceptance path unreachable on Windows source: every accept trips dirty-unexpected because the bytes WinMerge writes don't match the staged bytes the server generated.

## Hypotheses (need diagnosis to pick)

The staged candidate generator probably normalizes one or both of:

### A. Line endings (most likely)

Watched source on Windows is typically `CRLF`. If `submit_*` emits the candidate with `LF` (default for many text-emitting libraries — `File.WriteAllText` on .NET preserves whatever you give it, but Roslyn formatters / string builders that use `\n` literals will produce `LF`), then:

- The staged file on disk = `LF`.
- The original watched file on disk = `CRLF`.
- WinMerge, when saving "the right side" (the candidate), may use its own preserve-or-coerce setting for EOL on save. Result: watched file ends up with either `CRLF` (WinMerge coerced) or `LF` (WinMerge preserved) — and either way the post-save bytes don't equal the staged-candidate bytes that were hashed at stage time, because the candidate hash was computed against `LF` content but the saved file has been re-EOL'd, or vice versa.

### B. BOM / encoding

If watched source is UTF-8 with BOM (common for Visual Studio-generated `.cs`) and the staged candidate is written UTF-8 without BOM (default for many .NET writers), the first 3 bytes differ. Same hash mismatch result.

Either condition is sufficient to break the happy path; both may be in play.

## Why the all-or-none gate flags this

From [CLAUDE.md](../CLAUDE.md) and the tool manifest:

- `accepted` requires reported `accepted` **AND** watched hash == staged candidate hash.
- Anything else (including reported `accepted` with mismatched watched hash) → `dirty-unexpected`.

The gate is doing exactly what it's specified to do. The bug is upstream — the staged candidate was hashed against bytes the post-save watched file can't equal, no matter what the operator does in WinMerge.

## Impact

- **Severity**: blocking for the documented happy-path acceptance flow on Windows-encoded source.
- **Scope**: any watched file whose existing on-disk encoding/EOL differs from what `submit_*` produces. In practice that's most `.cs` files in Visual Studio-style projects.
- **Workaround**: none clean. Operator can manually re-save with a specific encoding/EOL to match the staged candidate, but that defeats the all-or-none guarantee and adds steps the gate's design was meant to remove.

## Recommended fixes

Either fix resolves the practical issue. The first is more correct; the second is a fallback if (1) is impractical.

### 1. `submit_*` should mirror the watched file's existing encoding and EOL (preferred)

At candidate generation time:

- Detect the watched file's BOM/encoding (UTF-8 BOM vs UTF-8 vs UTF-8 LE BOM, etc).
- Detect the watched file's dominant EOL (`CRLF` vs `LF`, with tie-break to the majority of existing lines).
- Emit the staged candidate with **the same** encoding and EOL.
- Hash both candidate and watched against bytes-on-disk, as now.

This makes the gate's hash comparison meaningful for the cases operators actually do in WinMerge. No documentation or behavior change required for `record_diff_decision`.

### 2. Document the constraint and normalize at compare time (fallback)

If (1) is impractical (e.g. Roslyn-derived candidate output is hard to re-EOL after the fact), then:

- Document explicitly that staged candidates may have differing EOL/BOM from the watched source.
- Add a `normalizedHash` field alongside `proposedHash` and `originalHash` on the staged record — computed against the watched and staged content after EOL and BOM normalization.
- Have `record_diff_decision` accept the agreement on **either** raw or normalized hash for the `accepted` classification.

This loses some of the all-or-none guarantee (a save that adds/removes whitespace within a line still classifies as dirty-unexpected, which is correct; but a save that just re-EOLs everything classifies as accepted). It's a documented compromise rather than a silent one.

## Suggested follow-up tickets

- **#A** *Staged candidate EOL preservation*: detect the watched file's dominant EOL at stage time and emit the staged candidate with the same EOL. Apply in `submit_file`, `submit_symbol`, `add_symbol`, `remove_symbol`, `add_using`, `remove_using`. Cover with a smoke test that stages and accepts a `CRLF` source file and asserts `record_diff_decision` returns `accepted`, not `dirty-unexpected`.
- **#B** *Staged candidate BOM/encoding preservation*: detect the watched file's BOM/encoding at stage time and emit the staged candidate with the same. Smoke test against a UTF-8 BOM `.cs` file.

Both tickets are small and well-bounded; either alone is a measurable improvement. (1A) alone almost certainly fixes the observed dirty-unexpected on first-real-source repro, because EOL is the more common Windows-source divergence.

## Repro context

- Watched source: `C:\Schema Studio - DBV2` (Visual Studio-generated C# project on Windows; expected CRLF + likely UTF-8-BOM).
- File involved in the repro: `SchemaStudio.Data\SchemaObjectColumnRepository.cs` (file was open in the IDE at the time of the repro — useful as a concrete diagnostic target).
- Tool path: `submit_*` → `launch_staged_diff` → WinMerge save → `record_diff_decision(accepted)`.
- Result: `dirty-unexpected`, watched hash != staged hash, watched hash != original hash. The two mismatches together imply the watched file was saved with bytes that differ from both the original baseline and the staged candidate — consistent with EOL/BOM re-encoding on save.

## Diagnosis the team can run before picking ticket

A one-time inspection answers the EOL-vs-BOM question:

1. Stage a candidate against any CRLF watched file. Don't accept yet.
2. Compare raw bytes of staged candidate vs watched original (first 4 bytes for BOM, then a grep for `\r\n` vs `\n` counts).
3. If staged has fewer `\r\n` than original, it's EOL.
4. If staged lacks the `EF BB BF` prefix the original has (or vice versa), it's BOM.
5. Likely both.

The answer drives whether ticket #A, #B, or both are needed.
