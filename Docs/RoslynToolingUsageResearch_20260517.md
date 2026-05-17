# Roslyn Tooling Usage Research - 2026-05-17

## Takeaway

Public Roslyn/MCP usage patterns confirm the point of this project:

- **MonitorBaseClaude should become Claude's compiler-backed navigation and safe-edit host.**
- **Roslyn Tooling** supplies compiler-backed code intelligence.
- **System Monitor** supplies Roslyn-backed source editing, watched-source staging, review, and hash gates.
- **System Monitor also supplies the after-the-fact compiler gate** by validating staged candidates with syntax and overlay compile checks before human review.

The outside world is not mainly using Roslyn MCP as "read a file better." They are using it to answer questions that text search cannot answer reliably: exact references, implementations, call hierarchy, type hierarchy, diagnostics, dependencies, and symbol context. That is exactly the navigation surface we want Claude to have inside our workflow. The matching edit-side requirement is Roslyn-backed staged editing: symbol surgery where possible, whole-file staging when necessary, compiler feedback before the Operator is asked to review, and hash-gated decision recording after review.

## Landscape Check

### Roslyn CodeLens MCP

CodeLens already covers the important read/analysis surface for this project:

- references, callers, implementations, hierarchy, call graphs
- diagnostics and analyzer-style health checks
- project/NuGet dependencies
- DI registrations
- source generator inspection
- code fixes as structured edit information

Current conclusion: keep CodeLens as the primary semantic read surface. The immediate problem is not lack of coverage; it is teaching agents to call the right tools with the right arguments before they fall back to grep.

### Symbols MCP

The `p1va/symbols` project is lighter and language-server based. Its useful idea is not deeper C# coverage; it is packaging a small navigation vocabulary around LSP concepts:

- `search`
- `outline`
- `inspect`
- `references`
- `call_hierarchy`
- `diagnostics`
- `completion`
- `rename`

It also ships agent-facing skills such as language-server navigation. That supports our direction: teach tool choice with concise descriptions, next-call hints, and possibly a small skill/playbook, not a huge manifest dump.

Current conclusion: do not integrate Symbols for C# right now. Borrow its teaching shape and minimal vocabulary.

### Visual Studio RoslynMCP

The Visual Studio RoslynMCP extension appears to go much further on mutation:

- many Roslyn navigation and understanding tools
- Visual Studio instance/dashboard attachment
- direct Roslyn/API-backed object-oriented edit actions
- statement/block navigation inside method bodies
- diagnostics after mutation
- auto-schema feedback on wrong parameters

This validates the surgical-editing idea. It also comes with tradeoffs for our project:

- likely Visual Studio coupling
- larger tool surface
- different licensing/distribution assumptions
- direct mutation model that does not automatically match our staged/hash-gated workflow

Current conclusion: treat RoslynMCP as design inspiration for future System Monitor source-edit verbs, especially block/member editing and schema feedback. Do not replace the current CodeLens + System Monitor split unless we prove a specific missing capability.

## Surgical Roslyn Editing Reference Points

The surgical approach is not exotic. It matches established Roslyn APIs and analyzer/code-fix practice:

- `SymbolFinder.FindReferencesAsync` is the official Roslyn workspace API for finding references to a resolved `ISymbol` across a `Solution`.
- `DocumentEditor` / `SyntaxEditor` are official Roslyn editing abstractions for changing a document syntax tree.
- Analyzer/code-fix workflows transform syntax trees into corrected documents, then use annotations such as formatter/simplifier annotations to scope post-processing.

Important caution from code-fix practice: formatting should be scoped to the edited node when possible. Broad formatter annotations can reformat an entire file, which is bad for diff stability and for human review.

This fits our System Monitor rule: generate a complete staged candidate, but make the actual Roslyn transformation as small and symbol/block-local as possible.

## Sources Reviewed

- Claude Code MCP docs: stdio servers are local processes; project MCP config is a first-class shape; `/mcp` verifies tool connection; tool search/server instructions matter for context control.
- Roslyn CodeLens MCP listings: advertised features include references, definitions, diagnostics, code fixes, symbol search, NuGet dependencies, attribute usages, circular dependencies, type hierarchy, call graph, DI registrations, and semantic context.
- Other Roslyn/LSP MCP projects: commonly expose symbol search, references, symbol info/inspection, dependency analysis, complexity, diagnostics, call hierarchy, rename, and language-server status/resources.
- Visual Studio CodeLens docs: the original IDE concept is "stay focused while seeing where and how code is used," especially references and history at the symbol level.
- Community discussion: developers reach for compiler-backed tools to avoid false-positive text search, for example distinguishing a specific method call from another method with the same name.

## Practical Guidance For Our Agents

Use **Roslyn Tooling** when the question is about semantic truth:

- where is this symbol used?
- who calls this method?
- what implements this interface?
- what derives from this base type?
- what diagnostics are currently present?
- what project/package dependencies matter?
- what DI registrations exist?
- what tests or uncovered symbols relate to this code?
- how broad is the impact if this signature changes?

Use **System Monitor** when the question is about protected source workflow:

- what project are we watching?
- what file/symbol shape exists in the current source?
- what stable selector should be used for a staged mutation?
- what Roslyn-backed source edit should create the candidate?
- what candidate should be staged?
- what did WinMerge review?
- did the watched hash match accept/reject intent?
- what ledgers/session history exist?

## Recommended Call Shapes

Reference question:

```text
Roslyn Tooling search_symbols(query)
Roslyn Tooling get_type_overview(typeName)
Roslyn Tooling find_references(symbol)
```

Caller question:

```text
Roslyn Tooling search_symbols(method/type)
Roslyn Tooling find_callers(symbol)
Roslyn Tooling get_call_graph(symbol, depth)
```

Edit question:

```text
System Monitor find_file
System Monitor get_source_map(mode: navigation or selector)
Roslyn Tooling find_references / analyze_change_impact
System Monitor get_symbol
System Monitor submit_symbol or submit_file
System Monitor record_diff_decision after review
```

Diagnostics question:

```text
Roslyn Tooling get_diagnostics
Roslyn Tooling get_code_fixes, if inspecting options
System Monitor staging, if a fix is actually applied to watched source
```

## Reworked Plan

1. Exploit CodeLens before adding another server.

   Verify that the existing CodeLens surface can answer the semantic questions we care about: references, callers, impact, hierarchy, diagnostics, DI, generated code, tests, and dependencies. Fill only proven gaps.

2. Improve descriptions and result hints first.

   Tool descriptions should say exact argument names and what to call next. Result payloads should include compact next-call hints. Keep this below Claude MCP budget pressure; avoid large catalogs in model context.

3. Add schema-feedback behavior where possible.

   If an agent calls a tool with `symbolName` when `symbol` is required, return an error that includes the correct argument contract and one minimal example. RoslynMCP explicitly advertises this pattern; it is worth copying.

4. Keep mutation behind System Monitor.

   Roslyn CodeLens can expose code fixes, rename, or structured edits as advice. Watched-source mutation should still become a System Monitor staged candidate with overlay compile validation and Operator review.

5. Grow System Monitor editing verbs incrementally.

   Current symbol-level verbs are enough for method/type/member edits. Future surgical verbs can be inspired by RoslynMCP:

   - statement/block insert
   - statement/block replace
   - scoped rename candidate
   - organize using candidate
   - extract method candidate
   - split class candidate

   Each must produce a staged candidate, not write watched source directly.

6. Use UI/dashboard for verbose teaching.

   The dashboard can show full schemas, examples, and traffic analysis because it is human-facing. Claude-facing helper output should remain compact and task-specific.

7. Run acceptance passes.

   Test whether Claude chooses CodeLens tools before grep after description/hint updates. Only add separate recipe helpers or skills if observed behavior still drifts.

## Warnings

- Do not let Roslyn Tooling become the write path by accident. Even when Roslyn exposes code actions or rename, watched-source edits still need Monitor staging unless the Operator explicitly suspends the workflow.
- Do not treat text search as equivalent to Roslyn references. Compiler-backed references disambiguate overloads, fields, methods, and similarly named symbols.
- Do not treat Monitor source maps as a full semantic reference index. Source maps are compact current source shape and selector identity; Roslyn Tooling answers cross-solution semantic questions.
- Do not trust a model that calls `grep` or full-file reads before trying symbol/reference tools for a semantic question.

## Fit With MITM Hub

The WinForms hub should make this split visible:

- `Roslyn Tooling` traffic should show semantic questions: `search_symbols`, `get_type_overview`, `find_references`, `find_callers`, `get_diagnostics`, etc.
- `System Monitor` traffic should show workflow and staging questions: `get_monitor_status`, `get_tool_manifest`, `find_file`, `get_source_map`, `get_symbol`, `submit_*`, `record_diff_decision`, etc.

If a task is semantic and Roslyn traffic stays empty, the agent probably missed the intended workflow.

If a task is an edit and System Monitor traffic stays empty, the agent is bypassing the safety surface.

## Source Links

- Claude Code MCP docs: https://code.claude.com/docs/en/mcp
- Roslyn CodeLens listing: https://mcp.so/server/roslyn-code-lens/MarcelRoozekrans
- PulseMCP Roslyn CodeLens listing: https://www.pulsemcp.com/servers/marcelroozekrans-roslyn-codelens
- RoslynMCP Visual Studio extension listing: https://marketplace.visualstudio.com/items?itemName=YaroslavHorokhov.RoslynMcp
- Symbols MCP language-server navigation project: https://github.com/p1va/symbols
- Roslyn `SymbolFinder.FindReferencesAsync`: https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.findsymbols.symbolfinder.findreferencesasync
- Roslyn `DocumentEditor`: https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.editing.documenteditor
- Roslyn code-fix annotations note: https://www.meziantou.net/roslyn-annotations-for-code-fix.htm
- Visual Studio CodeLens docs: https://learn.microsoft.com/en-us/visualstudio/ide/find-code-changes-and-other-history-with-codelens
- Community semantic MCP discussion: https://www.reddit.com/r/ClaudeAI/comments/1q0p2bu/mcp_servers_for_semantic_java_and_c_analysis_to/
