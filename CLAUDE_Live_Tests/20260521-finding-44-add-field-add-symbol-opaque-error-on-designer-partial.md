---
status: new
type: finding
created: 2026-05-21
processed: false
processedBy:
processedAt:
resolution:
resolutionCommit:
---

## Summary

`add_field` and `add_symbol` return a generic `An error occurred invoking 'add_field'.` / `An error occurred invoking 'add_symbol'.` with no diagnostic detail when the target file is `SchemaStudio.Designer.cs` (a WinForms designer partial), even though the file IS loaded in the active Roslyn solution and `submit_symbol` against the same file works in the same session. This is distinct from [Finding 29](20260518-finding-29-add-method-opaque-error-on-non-watched-file.md) (file-not-in-solution); here the file is in-solution and Roslyn-discovered.

## Repro

Session: `monitor-20260521051831-354bb0f7692f4b6fb`. Branch `claude/live-test-notes-20260517`, HEAD `1d61e52`.

Target file `C:\Schema Studio - DBV2\SchemaStudio.Designer.cs` (1872 bytes, sha `3e4192dc`). Confirmed in-solution via:
- Monitor `find_file(*.Designer.cs)` → 1 hit at this path
- Monitor `get_source_map(file, selector)` → 5 symbols including `SchemaStudio` class declaration at line 5, `MainViewer` field at line 58
- Roslyn `get_type_overview(SchemaStudio.SchemaStudio)` → confirms partial class with this file as one of the declarations

Three failing calls, all on this file under session `monitor-20260521051831-354bb0f7692f4b6fb`:

1. `add_field(path: "SchemaStudio.Designer.cs", containingType: "SchemaStudio", declaration: "private Button GreetingButton;", afterSymbol: "MainViewer")` → `An error occurred invoking 'add_field'.`
2. `add_field(path: same, containingType: same, declaration: same)` — removed `afterSymbol` to rule it out → same opaque error.
3. `add_symbol(path: same, containingType: same, symbolType: "field", code: "private Button GreetingButton;")` → `An error occurred invoking 'add_symbol'.`

Then on the same file, same session, same target type:

4. `submit_symbol(path: "SchemaStudio.Designer.cs", symbolSelectorJson: '{"stableSymbolKey":"SchemaStudio.Designer.cs::SchemaStudio::SchemaStudio::method::InitializeComponent()"}', code: <replacement body>)` → **OK**. `status: candidate-updated`, opCount=1, candidateHash `a959b30b`, no errors.

Same file, same session, `submit_symbol` succeeds where `add_field`/`add_symbol` fail opaquely.

## Expected

A specific, descriptive error message naming the precondition violated, or — if the failure is internal — the underlying exception type and message rather than the wrapper. The current generic error wrapper exactly mirrors Finding 29's UX problem on a different precondition.

## Actual

`An error occurred invoking 'add_field'.` / `An error occurred invoking 'add_symbol'.` — string only, no detail. The session log was not inspected for these failures (out of pass scope), but Finding 29 reported that session-event recording is also missing for opaque-failure paths.

## Evidence

- Pass 20 transcript / [evidence note](20260521-pass20-real-form-partial-class.md) — section "Candidate composition" lists the exact three failing calls and the succeeding `submit_symbol` call interleaved with the working `add_field` and `add_method` calls on the sibling partial `SchemaStudio.cs`.
- `SchemaStudio.cs` (sibling partial of the same `SchemaStudio` class) accepted `add_field` and `add_method` cleanly in the same session — proves the failure is not a session/Roslyn/type-resolution issue, it's specific to the Designer.cs file.
- File contents of `SchemaStudio.Designer.cs`: starts with a using directive, then namespace, then `partial class SchemaStudio` (no access modifier), `///` doc-commented members, **and contains a `#region Windows Form Designer generated code` ... `#endregion` block** wrapping `InitializeComponent()`. The sibling main partial (`SchemaStudio.cs`) has none of these — no docs, no region, declared `public partial`.

## Suspected cause

Pivot-by-elimination narrows it to one of:

1. The `add_*` insertion path doesn't correctly handle insertion adjacent to or within a `#region` boundary (the most distinctive structural difference).
2. The `add_*` insertion path can't resolve a partial-class declaration that lacks an explicit access modifier (`partial class SchemaStudio` vs `public partial class SchemaStudio`).
3. Doc-comment trivia (`///` blocks) trip the insertion-point selector.

`submit_symbol` works because it replaces an *existing* symbol whose location is fully determined by the stable selector — it never has to *choose* an insertion point inside the file's structural layout. The `add_*` family does have to choose, and that's where Designer.cs trips it.

## Minimal fix

1. Detect the precondition failure inside `add_field` / `add_symbol` / `add_method` / `add_property` etc. and return a specific error naming what failed (e.g. `cannot determine insertion point in <path>: <reason>`).
2. Once the root cause is identified, fix the insertion-point handling for files with `#region` blocks or modifier-less partial declarations — both are common WinForms designer patterns and will recur on every real form in the watched solution.
3. As with Finding 29, log the failure into the session event stream so the Operator can retrace agent behavior.

## Severity

**confusing** — workaround exists (pivot the member into a sibling partial, or use `submit_symbol` to replace an existing member instead of adding), but the opaque error wastes probes and the workaround pollutes the target file's logical layout (button declaration ends up in the main partial rather than the designer partial, contrary to WinForms convention).

## Notes

- Related: [Finding 29](20260518-finding-29-add-method-opaque-error-on-non-watched-file.md) — same opaque-error UX, different precondition (file-not-in-solution vs. structural-pattern-not-handled).
- Real-form impact: WinForms designer partials are the canonical multi-file partial-class pattern in DBV2 and almost every other WinForms project. Any "add a button to a form" task that wants to follow the convention of declaring the field in `*.Designer.cs` will hit this. The pass-20 workaround (declare the field in the main partial) is acceptable for a test but not for production-quality codegen.
