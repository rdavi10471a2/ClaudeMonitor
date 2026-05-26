# Partial Class Refactor

Use when a human-guided refactor intentionally moves related members into companion partial files.

## Rules

- Treat companion partial extraction as an advanced refactor, not the default path.
- If the original type is not partial, stage `set_type_partial` as its own declaration-level operation.
- Do not smuggle type declaration modifier changes into unrelated symbol edits.
- Add new members to the companion partial through normal `add_symbol` staging.
- Preserve the baseline option: a same-file dictionary/constant block may be better for small changes.
- For Razor components with inline `@code`, do not extract manually — use `split_razor_code_to_companion`. It stages the `.razor` markup and the `.razor.cs` partial-class companion together in one monitor session, with the Razor SDK's implicit `Microsoft.AspNetCore.Components` and ancestor `_Imports.razor` directives merged into the companion's using list. Brand-new Razor components should be authored directly in two-file form (see CLAUDE.md "Razor Files"); reserve the split tool for legacy single-file migration.

## Usual Flow

```text
get_source_map for original file
get_source_map for companion partial file, if present
set_type_partial on original type, if needed
add_symbol / submit_symbol in the companion partial
submit_symbol replacements in original methods
```

Long references:

- `Docs/CodexMcpSurgeryDrill.md`
- `Docs/OracleFormattingFixtureCorpus.md`
