# Finding 49 — `find_indexed_callers` returns empty for every tested target after the "Fix indexed caller resolution" commit

**Severity:** blocker (the tool's primary advertised function is non-functional)

**File/tool:** `mcp__monitor-base-claude__find_indexed_callers`

**Observed:** Four call-site targets on the watched solution `C:\Schema Studio - DBV2\Schema Studio.sln` (84 files, 1103 symbols, 7447 references indexed, `staleFileCount: 0`, indexed 2026-05-21 14:51 UTC). Each `find_indexed_callers(stableSymbolKey)` returned `[]`:

| Target | Kind | Roslyn `find_callers` | Index `find_indexed_callers` |
|---|---|---|---|
| `SchemaObjectColumnRepository.SaveAll(IEnumerable<SchemaObjectColumnDefinition>)` | method | 2 callers | `[]` |
| `IntegrationsViewImportControl.AddSelectedView()` | method | 1 caller | `[]` |
| `SchemaObjectColumnRepository(string)` | constructor | 0 (loaded compilation) | `[]` |
| `PropertyGridDataContext.ConnectionStringResolver` | property | 1 reference | `[]` |

**Expected:** When Roslyn returns N>0 callers for a symbol, the index returns at least the same N rows on a freshly-indexed solution.

**Minimal fix:** Investigate the `find_indexed_callers` SQL/lookup against the indexer's call-site table. The tool currently returns nothing where the index records 496 call sites total.

**Evidence:** Binary `MonitorBaseClaude.McpServer.dll` LastWriteTime 2026-05-21 10:43:37, McpServer process started 10:49:57 — both fix commits `8191f8e` "Fix indexed caller resolution" (10:07) and `279f809` "Clean index signatures and insertion trivia" (10:24) are in the running binary. Index reports `callSiteCount: 496` so the indexer is populating rows; the lookup is what returns empty.
