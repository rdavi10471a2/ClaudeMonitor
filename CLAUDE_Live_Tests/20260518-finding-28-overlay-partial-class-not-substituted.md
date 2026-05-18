---
status: new
type: finding
created: 2026-05-18
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---

## Summary

When a staged V1 candidate's source file is part of a C# partial class, the overlay validator does not substitute the candidate for the watched original — it adds the candidate as a second declaration in the same compilation. Every member declared in the affected partial file appears declared twice, producing dozens of phantom `CS0229` "Ambiguity between X and X" and `CS0121` "The call is ambiguous between … and …" diagnostics. The same FQN appears on both sides of every diagnostic. The real code compiles fine — proved post-accept by `get_diagnostics(severity=error)` returning `[]`. The bug is in the overlay slice's file-substitution logic, not in the spliced candidate.

## Severity

blocker (for any multi-file coupled edit that touches a partial-class file)

## File/tool

`stage_candidate_for_review` overlay validation, and the same overlay surface returned by `submit_symbol` / `submit_file` / etc. against partial-class files.

## Repro

1. Pick a watched C# file that participates in a partial class with ≥2 declarations across files (Pass 10 used `UI\MergedEditorSurface\IntegrationsViewImportControl.Loading.cs`, one of 10 partials for `IntegrationsViewImportControl`).
2. Start a monitor session.
3. Stage any V1 op against that file (e.g. `submit_symbol` on a method body) under the sessionId.
4. Observe `overlayValidation.diagnostics` in the response.

Result: 49 diagnostics in Pass 10's case. All compare a symbol to itself (`'IntegrationsViewImportControl._isInitializingForm' and 'IntegrationsViewImportControl._isInitializingForm'`, etc.). All cite line numbers in the candidate file.

Direct inspection of the staged candidate file confirms it contains only one copy of each member — no real duplicates. Direct compilation against the same source via `mcp__roslyn-codelens__get_diagnostics(severity=error)` after accept returns `[]`.

## Expected

Overlay validator should replace the watched original source for the staged path with the candidate content, then compile the union of (other staged candidates + non-staged watched sources). The partial class should compile with each partial seen exactly once: candidate for the staged file, watched for the others.

## Actual

Overlay sees both the watched original `Loading.cs` and the staged candidate `Loading.cs` as separate `SyntaxTree` inputs against the same partial type, so every member is declared twice in the same partial class. Every reference inside the candidate that hits an instance field/method/property/event collides with its own twin.

## Minimal Fix

In the overlay slice construction, when including a session's staged records:
1. For every staged record, look up the original watched path.
2. Remove the watched original `SyntaxTree` for that path from the compilation.
3. Add the staged candidate `SyntaxTree` in its place.

This is path-based substitution, not addition. Likely a one-line change in whatever currently builds the overlay `Compilation` (probably an `.AddSyntaxTrees(...)` that should be `.RemoveSyntaxTrees(originalTree).AddSyntaxTrees(candidateTree)`).

## Evidence

- Session: `monitor-20260518214439-1c46686bdd294323b` (Pass 10 multi-file run).
- Staged record: `20260518_164937803_stage_candidate_for_review_IntegrationsViewImportControl.Loading_8f6a0512`.
- Record JSON path: `Working\Staged\Records\20260518\20260518_164937803_stage_candidate_for_review_IntegrationsViewImportControl.Loading_8f6a0512.json`.
- Partial file roster: 10 `IntegrationsViewImportControl.*.cs` files under `UI\MergedEditorSurface\`, all declaring `public sealed partial class IntegrationsViewImportControl` in `namespace MergedEditorSurface;`.
- Counter-evidence: in Pass 10 the non-partial files (`DatabaseDomainRepository.cs`, `DatabaseDomainManagerForm.cs`, `DatabaseDomainTypeConverter.cs`) all overlay-validated cleanly when staged — substitution worked correctly. Only the partial-class file tripped the artifact.
- Final proof artifact is noise (not real): post-accept `get_diagnostics(severity=error)` → `[]` on the real watched build. The watched repo with all 4 Pass 10 changes applied compiles clean.

## Workaround

`launch_staged_diff(forceReviewOnOverlayErrors: true)` bypasses the overlay gate and opens WinMerge anyway. Acceptable when the operator can visually inspect the diff to confirm correctness. **Not acceptable** as a long-term protocol — overlay's primary job is catching real coupling errors before review, and a blanket force-review on partial-class edits loses that safety net.

## Notes

This bug is invisible for single-file edits where the candidate file is not partial. It only manifests once a coupled multi-file edit touches at least one partial class — which is the most-stressed shape of the V1 protocol. Discovery is easy: any V1 op against a partial-class file reproduces immediately.
