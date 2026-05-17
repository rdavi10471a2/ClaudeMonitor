# Partial Class Refactor

Use when a human-guided refactor intentionally moves related members into companion partial files.

## Rules

- Treat companion partial extraction as an advanced refactor, not the default path.
- If the original type is not partial, stage `set_type_partial` as its own declaration-level operation.
- Do not smuggle type declaration modifier changes into unrelated symbol edits.
- Add new members to the companion partial through normal `add_symbol` staging.
- Preserve the baseline option: a same-file dictionary/constant block may be better for small changes.

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
