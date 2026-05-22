---
status: backlog
type: doc-suggestion
created: 2026-05-21
processed: true
processedBy: Codex
processedAt: 2026-05-21
resolution: Backlog. Interesting independent grammar-corpus idea, but current fixture, DBV2, and WebViewer smokes are enough for today's demo path.
resolutionCommit:
---

# Proposal — Source-Truth Corpus Smoke (separate project)

## Goal

Add a smoke test that validates the Monitor solution index against a corpus of C# files **using only the C# language grammar itself as ground truth**. No hand-keyed answer keys. No second semantic engine (Roslyn `SymbolFinder`, language server, etc.) acting as referee. The truth is the parse tree as defined by the EBNF productions of the C# language specification.

This is intended to live alongside the existing `--fixture-index-matrix` smoke, not replace it. The two complement each other:

- **`--fixture-index-matrix`** (current 68-row): hand-keyed answer counts on a single 3-file fixture. Catches semantic-binding cases (operator overloads, implicit conversions, `await using` dispatch) where no purely-syntactic truth exists.
- **`--corpus-source-truth`** (proposed): EBNF-grounded counts on a corpus of ~30-60 files. Catches the broad common-C# surface where syntax determines the answer.

## Why a separate project, not part of `MonitorBaseClaude.ToolSmokeTests`

Suggested layout:

```
/MonitorBaseClaude.sln
/MonitorBaseClaude.ToolSmokeTests/        # existing, unchanged
/MonitorBaseClaude.CorpusSmokeTests/      # new
    MonitorBaseClaude.CorpusSmokeTests.csproj
    Program.cs                            # entry point + EBNF production table
    SyntacticTruthCounter.cs              # the counter module
    Corpus/                               # the .cs corpus files (or referenced by path)
        PrimitiveSurface/
            M001_UniquelyNamedMethods.cs
            ...
        DispatchVariants/
            ...
        (etc.)
    README.md                             # corpus authoring + contract
```

Reasons for separation:

1. **Different test contract.** Tool smoke validates hand-keyed scenarios; corpus smoke validates against a grammar contract. Mixing them in one project muddies the "what is this test checking" question for future contributors.
2. **Different dependency surface.** Corpus smoke needs only `Microsoft.CodeAnalysis.CSharp` for parsing (no compilation, no `SymbolFinder`, no language server). Tool smoke uses the full Roslyn semantic API. Keeping the corpus smoke's dependencies minimal reinforces that it isn't using semantics anywhere — a reviewer can verify "no `GetSymbolInfo` / `GetDeclaredSymbol` / semantic-model call exists in this project" by grep alone.
3. **Different runtime profile.** Corpus smoke runs over 30-60 files of static C# text — should be cheap and fast (sub-second per run after warm-up). Tool smoke runs over the watched DBV2 solution and the live in-process Roslyn comparator — heavier. Splitting them lets CI choose which to run in which lane (corpus smoke as a fast gate; full tool smoke in a slower lane).
4. **Independent corpus lifecycle.** Corpus files probably want their own folder structure, README, and authoring guidelines. Bundling them inside `ToolSmokeTests/` puts unrelated test data into the same project.

## The truth contract — C# EBNF productions, mapped to AST node kinds

The corpus smoke's counter is a switch over `node.Kind()` matching specific EBNF productions from the C# language specification. Each row of the table maps a production to a counting rule. No `GetSymbolInfo`, no `GetDeclaredSymbol`, no semantic model access anywhere in the module.

| EBNF production (C# spec section) | AST node kind | Counts as |
|---|---|---|
| `invocation_expression: primary '(' arguments? ')'` | `InvocationExpressionSyntax` | call site against the target identifier in the primary expression; arity = `ArgumentList.Arguments.Count` |
| `object_creation_expression: 'new' type '(' arguments? ')'` | `ObjectCreationExpressionSyntax` | construction against the type's identifier; arity = argument list count |
| `object_creation_expression: 'new' '(' arguments? ')'` | `ImplicitObjectCreationExpressionSyntax` | target-typed construction; target identifier resolved from enclosing variable/field/parameter type token |
| `constructor_initializer: ':' ('this' \| 'base') '(' arguments? ')'` | `ConstructorInitializerSyntax` | chained constructor call; arity from arg list |
| `attribute_section: '[' attribute (',' attribute)* ']'` | `AttributeSyntax` | reference to attribute type, with the language rule "name → name + 'Attribute' if no Attribute suffix" applied as a fixed source rule |
| `element_access: primary '[' arguments ']'` | `ElementAccessExpressionSyntax` | indexer call site on the primary expression's identifier |
| `cast_expression: '(' type ')' unary_expression` | `CastExpressionSyntax` | explicit conversion reference to the type identifier |
| `binary_expression: unary_expr binary_operator unary_expr` | `BinaryExpressionSyntax` | operator call site (custom-operator resolution requires semantic binding so this row may be `disabled` in the table and validated only by the fixture matrix instead) |
| `member_access: primary '.' simple_name` | `MemberAccessExpressionSyntax` | reference to `simple_name` against the receiver identifier |
| `simple_name: identifier (type_argument_list)?` | `SimpleNameSyntax` (any position) | identifier reference, categorized by syntactic parent kind |
| `class_declaration: ... 'class' identifier ...` | `ClassDeclarationSyntax` | declared type |
| `struct_declaration: ... 'struct' identifier ...` | `StructDeclarationSyntax` | declared type |
| `interface_declaration: ... 'interface' identifier ...` | `InterfaceDeclarationSyntax` | declared type |
| `enum_declaration: ... 'enum' identifier ...` | `EnumDeclarationSyntax` + `EnumMemberDeclarationSyntax` | declared type + declared enum members |
| `record_declaration: ... 'record' identifier ...` | `RecordDeclarationSyntax` | declared type |
| `delegate_declaration: ... 'delegate' return_type identifier ...` | `DelegateDeclarationSyntax` | declared delegate type |
| `method_declaration: ... return_type identifier ...` | `MethodDeclarationSyntax` | declared callable; arity from parameter list |
| `constructor_declaration: ... identifier '(' parameters? ')' ...` | `ConstructorDeclarationSyntax` | declared callable; arity from parameter list |
| `property_declaration: ... type identifier '{' accessors '}'` | `PropertyDeclarationSyntax` | declared property |
| `field_declaration: ... type variable_declarators ';'` | `FieldDeclarationSyntax` + `VariableDeclaratorSyntax` per variable | declared field per declarator |
| `event_declaration / event_field_declaration` | `EventDeclarationSyntax` + `EventFieldDeclarationSyntax` | declared event |
| `using_statement: 'await'? 'using' ...` | `UsingStatementSyntax` (note `AwaitKeyword`) | resource scope; the implicit dispose isn't syntactic (requires semantic binding) and is excluded |

The table is the test contract. Each row cites its C# spec section in the proposal commit and in the counter's source code comments — auditable by anyone reading the C# language specification.

**Cases the syntactic counter cannot handle, and goes silent on:**

- Operator overload resolution (`a + b` calls which `operator +`? — needs type binding)
- Implicit conversion site selection (no surface identifier)
- Default interface method selection across multiple interface impls
- Overload resolution among same-named methods with overlapping arity (corpus authoring rule below avoids this)

These remain the responsibility of the existing `--fixture-index-matrix` smoke with hand-keyed answers. The two harnesses do not overlap on these cases.

## Smoke harness behavior

```
1. Parse every *.cs file under <corpus-dir> with Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText.
2. Build the Monitor index by pointing SolutionIndexService at <corpus-dir> as observedRoot.
3. For each *.cs file's AST root:
   a. Walk declaration nodes, build a list of {DeclaredIdentifier, AstKind, Arity}.
   b. For each declared identifier name, run the syntactic counter:
      - Iterate the listed AST node kinds from the truth table.
      - For each matching node, record (file, line, column, callerEnclosingMember).
      - Categorize by syntactic role: invocation / construction / type-ref / attribute / read / write.
   c. Produce the expected truth: a list of (target_identifier, source_position, reference_kind).
4. Query Monitor's SolutionIndexService.FindCallers / FindReferences for each declared identifier's stable key.
5. Compare the two lists position-for-position.
6. Emit a summary similar to the fixture-index-matrix smoke: pass/fail per identifier, plus a diff table for failures.
```

The harness has no compilation step, no semantic model call. Roslyn appears only as the parser — same role that `csc`'s lexer/parser would play. Substitute another conformant parser and the truth-side output should be byte-identical.

## Corpus file authoring requirements

For the syntactic counter to produce unambiguous expected counts:

1. **Globally unique identifier names within the corpus.** No two methods named `Foo` across the whole corpus. Eliminates "which `Foo` is the call resolving to" entirely.
2. **Self-contained files.** Each file compiles standalone or with only `System.*` from the BCL. No cross-file dependencies on the corpus's own types.
3. **Documented intent header.** Each corpus file has a top-of-file comment block stating which EBNF productions the file exercises. The counter ignores comments; the header is for human reviewers.
4. **No semantic-binding-dependent shapes when alternatives exist.** Use explicit cast over implicit conversion; explicit `new T()` over target-typed `new()` when testing class names; called-by-name methods over operator dispatch. The semantic cases stay in the fixture matrix.
5. **Arity disambiguation only.** If a file declares two same-named methods (testing overload-by-arity), they must differ in parameter count (not just types). The counter uses argument count at call sites.

## Corpus file sourcing — open question for Codex

The 30-60 corpus files are the bulk of the work. Three plausible sources, in increasing cost order:

- **(A) Existing language-feature test corpora**, with attribution: Roslyn's own `src/Compilers/Test/Syntax/...` tests, the C# specification's example fragments, or the `dotnet/samples` repo's individual feature samples. These exist already and exercise the grammar by design. Licensing and trimming would need review.
- **(B) Curated synthesis from canonical references**: hand-written by Codex (or anyone), one file per EBNF production cluster, with intent headers. Highest editorial control over uniqueness/self-containment rules. Estimated ~3-4 days for 35-50 files at 50-200 lines each.
- **(C) Reduced-scope synthesis tied directly to the spec example snippets**: each corpus file is a near-verbatim transcription of the example block from the corresponding C# spec section, with renamed identifiers for uniqueness. Smaller per-file effort because the snippets already exist; bookkeeping is the rename pass.

Codex has more visibility into which option is realistic. The proposal does not assume any of these — the smoke harness works with whatever corpus is checked in as long as the authoring requirements above are satisfied.

**Generation cost concern:** the proposal author (Claude) acknowledges the cost of hand-writing the corpus is the dominant cost of this effort. If Codex's available sources reduce that, the proposal becomes much cheaper. If not, the proposal can ship the smoke harness against a small starter corpus (10-15 files) and grow the corpus incrementally — the harness pays its own way at any corpus size because it adds verifiable coverage that scales linearly with files added.

## What ships in V1 of this project

Minimum viable scope, separable from corpus expansion:

1. The new project `MonitorBaseClaude.CorpusSmokeTests/` with its csproj and Program.cs entry point.
2. The `SyntacticTruthCounter` module — ~300-400 lines of pure AST traversal, table-driven against the EBNF production table above.
3. A starter corpus of 10-15 files covering the highest-frequency patterns (uniquely-named methods, ctors, properties, fields, events, attributes, simple inheritance, static vs instance receivers, partial across two files, generic methods).
4. A README in the corpus folder documenting the authoring rules.
5. The `--corpus-source-truth` mode wired up and producing a pass/fail summary in the same shape as `--fixture-index-matrix`.

V1.1 then grows the corpus to 30-60 files as sourcing becomes available.

## Decisions needed before starting

- **Corpus sourcing path** (A, B, or C above, or some hybrid). Codex input.
- **Corpus location**: inside the new project's `Corpus/` folder vs an `external` git submodule. The proposal assumes inside-project for simplicity but if Codex wants to source from a Roslyn-test mirror with attribution, a submodule might be cleaner.
- **Whether to disable the `BinaryExpressionSyntax` row** in the syntactic table by default (operator resolution can't be done syntactically; current proposal disables it, but a coarse "any binary expression of operator-shape on a custom-type receiver" rule could replace it for partial coverage).
- **Project naming**: `MonitorBaseClaude.CorpusSmokeTests` (matches existing `MonitorBaseClaude.ToolSmokeTests`) or something else (`MonitorBaseClaude.GrammarSmokeTests` to emphasize the EBNF basis).

## Estimated effort

| Component | Effort if Codex sources corpus | Effort if synthesized from scratch |
|---|---|---|
| Project scaffold + csproj + wiring | 0.5 day | 0.5 day |
| SyntacticTruthCounter module | 1.5-2 days | 1.5-2 days |
| Corpus files (10-15 starter) | 0.5-1 day (rename pass) | 1.5-2 days |
| Corpus files (30-60 full) | 1.5-2 days (rename pass) | 3-4 days |
| README + corpus authoring docs | 0.5 day | 0.5 day |
| Smoke summary + diff format | 0.5 day | 0.5 day |
| **Total V1 (starter corpus)** | **~3 days** | **~4-5 days** |
| **Total V1.1 (full corpus)** | **~5 days** | **~7-9 days** |

The variable is the corpus authoring time. Everything else is project-scaffold + an AST-walker module that's straightforward C# code.

## Why this is worth doing

The current `--fixture-index-matrix` smoke proved its value by catching seven real findings (A-G) plus 8 hand-keyed answer-key bugs from the author. But each row in that matrix required Claude to write down "I think this should be 3 callers" — which is the failure mode Operator flagged. With the EBNF-grounded corpus smoke:

- The corpus IS the test. Adding a row = dropping a `.cs` file in the folder.
- The truth is derivable from the file's parse tree by anyone who can read the C# grammar.
- No "Claude says this is right" or "Codex says this is right" in the loop.
- Regressions in indexer coverage on common-C# patterns surface automatically as the corpus grows.
- The two-harness combination (fixture matrix for semantic-binding cases + corpus for grammar cases) covers the index's full surface with appropriate truth sources for each.

## Notes for the reviewer

- The proposal author (Claude) is happy to draft V1 starting from scratch synthesis if Codex's corpus sources aren't readily available, but flags that this is the path with the highest "Claude editorial opinion" risk in the corpus content itself. Ideally the corpus comes from a source Codex selects.
- The truth-table-as-source-comments-citing-spec-sections approach should make code review of the counter module straightforward — every line of pattern-matching code corresponds to a numbered spec section that the reviewer can cross-check.
- If the project takes a different name or different package layout, the proposal is agnostic — the core contribution is the EBNF-grounded truth procedure and the separation from the existing tool-smoke harness.
