# Finding 50 — `find_indexed_references` returns intra-body rows for method/constructor targets, correct rows for property targets

**Severity:** blocker (the tool's result shape is wrong for the most common symbol kinds)

**File/tool:** `mcp__monitor-base-claude__find_indexed_references`

**Observed:** For method and constructor `stableSymbolKey`s, every returned row has `callerStableSymbolKey == targetStableSymbolKey` and the row is positioned **inside the target symbol's own body**. The snippets are identifiers from the body (parameter type tokens, column names, local variable accesses), not references to the target itself.

Example — `SchemaObjectColumnRepository.SaveAll(IEnumerable<SchemaObjectColumnDefinition>)`:

- 30+ rows returned, every row at lines 149-202 (SaveAll's body spans lines 147-204)
- Each row's `callerStableSymbolKey` equals SaveAll's stableSymbolKey
- Sample snippets: `var items = models.Where(...)`, `data.Columns.Add("SchemaObjectColumnId"...)`, `model.SchemaObjectColumnId,` — none are references to SaveAll

Same pattern for `SchemaObjectColumnRepository(string)` constructor (1 intra-body row).

**Property targets behave correctly.** `PropertyGridDataContext.ConnectionStringResolver` returned 3 rows where `callerStableSymbolKey` correctly names the real consumer (`GetConnectionString` body, plus two ctor setters in IntegrationsViewImportControl files).

**Expected:** All symbol kinds return rows whose `targetStableSymbolKey` matches the queried key and whose `callerStableSymbolKey` is the containing symbol of each real reference site.

**Minimal fix:** Investigate the reference-row join condition for method/ctor kinds. Property handling appears to query on `target_symbol_id`; method/ctor handling appears to inadvertently match `containing_symbol_id == target_symbol_id` rows.

**Evidence:** Sessionless `find_indexed_references` calls 2026-05-21 ~15:00 UTC, post-fix binary, fresh index (see Finding 49 for binary/index provenance).
