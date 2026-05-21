using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using MonitorBaseClaude.AI;
using MonitorBaseClaude.McpServer;
using MonitorBaseClaude.Services;

[module: AIFileContext("Program.cs", "Single-file console smoke harness for monitor tool integration checks.")]
[module: FileVersion("1.3")]

namespace MonitorBaseClaude.ToolSmokeTests;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Contains("--dbv2-index-callers", StringComparer.OrdinalIgnoreCase))
        {
            return RunDbv2IndexCallers("dbv2-index-callers", IsRepositoryOrDiscoveryMember, requireKnownCallerChecks: true);
        }

        if (args.Contains("--dbv2-index-callers-all", StringComparer.OrdinalIgnoreCase))
        {
            return RunDbv2IndexCallers("dbv2-index-callers-all", IsIndexedCallable, requireKnownCallerChecks: true);
        }

        if (args.Contains("--fixture-index-matrix", StringComparer.OrdinalIgnoreCase))
        {
            return RunFixtureIndexMatrix();
        }

        Console.WriteLine("MonitorBaseClaude tool smoke tests");
        Console.WriteLine();
        Console.WriteLine("Available modes:");
        Console.WriteLine("  --dbv2-index-callers        Cross-check repository/discovery callers.");
        Console.WriteLine("  --dbv2-index-callers-all    Cross-check every indexed method/constructor in DBV2.");
        Console.WriteLine("  --fixture-index-matrix      Build a generated fixture and verify symbol caller/reference matrix.");
        return 2;
    }

    private static int RunFixtureIndexMatrix()
    {
        MonitorServerSettings settings = MonitorServerSettings.Load();
        string runRoot = Path.Combine(
            settings.UiRoot,
            "Working",
            "History",
            "ToolSmokeTests",
            DateTime.Now.ToString("yyyyMMdd_HHmmss"),
            "fixture-index-matrix");
        string observedRoot = Path.Combine(runRoot, "FixtureProject");
        string fixtureRoot = Path.Combine(observedRoot, "McpIndexProbes");
        Directory.CreateDirectory(fixtureRoot);
        File.WriteAllText(Path.Combine(observedRoot, "FixtureProject.sln"), string.Empty);
        File.WriteAllText(Path.Combine(fixtureRoot, "McpCallerProbeFixture.A.cs"), FixtureSourceA);
        File.WriteAllText(Path.Combine(fixtureRoot, "McpCallerProbeFixture.B.cs"), FixtureSourceB);
        File.WriteAllText(Path.Combine(fixtureRoot, "McpGeneratedProbe.g.cs"), FixtureGeneratedSource);

        SolutionIndexService indexService = new(settings.UiRoot, Path.Combine(observedRoot, "FixtureProject.sln"));
        SolutionIndexBuildResult build = indexService.Rebuild();
        MatrixCheck[] checks = BuildFixtureMatrixChecks();
        IReadOnlyDictionary<string, RoslynMatrixCounts> roslynCounts = BuildRoslynFixtureMatrix(observedRoot, checks);
        SolutionIndexQueryResult index = indexService.Query("solution", maxFiles: 500, maxSymbols: 5000);
        IReadOnlyList<ModelFeatureProbeResult> featureProbeResults = BuildModelFeatureProbeResults(observedRoot, index);
        List<MatrixResult> results = [];
        foreach (MatrixCheck check in checks)
        {
            IReadOnlyList<SolutionIndexReference>? callers = check.ExpectedCallers is null
                ? null
                : indexService.FindCallers(check.StableKey, 50);
            IReadOnlyList<SolutionIndexReference>? references = check.ExpectedReferences is null
                ? null
                : indexService.FindReferences(check.StableKey, 50);
            results.Add(new MatrixResult(check, roslynCounts.GetValueOrDefault(check.StableKey), callers, references));
        }

        bool passed = results.All(result => result.Passed);
        string summary = BuildFixtureMatrixSummary(build, results, featureProbeResults, passed);
        string summaryPath = Path.Combine(runRoot, "summary.md");
        File.WriteAllText(summaryPath, summary);

        Console.WriteLine(summary);
        Console.WriteLine($"Summary: {summaryPath}");
        return passed ? 0 : 1;
    }

    private static int RunDbv2IndexCallers(
        string modeName,
        Func<SolutionIndexSymbol, bool> targetPredicate,
        bool requireKnownCallerChecks)
    {
        MonitorServerSettings settings = MonitorServerSettings.Load();
        string observedRoot = Path.GetDirectoryName(settings.WatchedSolutionPath)
            ?? throw new InvalidOperationException("Watched solution has no containing folder.");
        string runRoot = Path.Combine(
            settings.UiRoot,
            "Working",
            "History",
            "ToolSmokeTests",
            DateTime.Now.ToString("yyyyMMdd_HHmmss"),
            modeName);
        Directory.CreateDirectory(runRoot);

        if (!File.Exists(settings.WatchedSolutionPath))
        {
            Console.WriteLine($"Watched solution not found: {settings.WatchedSolutionPath}");
            return 1;
        }

        Console.WriteLine("MonitorBaseClaude DBV2 indexed caller smoke");
        Console.WriteLine($"Mode: {modeName}");
        Console.WriteLine($"Watched solution: {settings.WatchedSolutionPath}");
        Console.WriteLine($"Log root: {runRoot}");
        Console.WriteLine();

        SolutionIndexService indexService = new(settings.UiRoot, settings.WatchedSolutionPath);
        SolutionIndexBuildResult build = indexService.Rebuild();
        SolutionIndexQueryResult index = indexService.Query("solution", maxFiles: 5000, maxSymbols: 50000);
        IReadOnlyList<SolutionIndexSymbol> targets = index.Symbols
            .Where(targetPredicate)
            .OrderBy(symbol => symbol.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(symbol => symbol.StartLine)
            .ToArray();

        Dictionary<string, List<ExpectedCaller>> expected = BuildExpectedCallers(observedRoot, index.Symbols, targets);
        IReadOnlyList<SolutionIndexSymbol> dirtySignatureSymbols = FindDirtySignatureSymbols(index.Symbols);
        List<Comparison> comparisons = [];
        foreach (SolutionIndexSymbol target in targets)
        {
            List<ExpectedCaller> expectedRows = expected.GetValueOrDefault(target.StableSymbolKey) ?? [];
            IReadOnlyList<SolutionIndexReference> actualRows = indexService.FindCallers(target.StableSymbolKey, 5000);
            List<ExpectedCaller> missing = expectedRows
                .Where(expectedRow => !actualRows.Any(actual => SameCallSite(actual, expectedRow)))
                .ToList();
            List<SolutionIndexReference> unexpected = actualRows
                .Where(actual => !expectedRows.Any(expectedRow => SameCallSite(actual, expectedRow)))
                .ToList();
            comparisons.Add(new Comparison(target, expectedRows, actualRows, missing, unexpected));
        }

        bool knownCallersPassed = !requireKnownCallerChecks || KnownCallerChecksPass(indexService);
        bool passed = targets.Count > 0
            && knownCallersPassed
            && dirtySignatureSymbols.Count == 0
            && comparisons.All(item => item.Missing.Count == 0 && item.Unexpected.Count == 0);
        string summary = BuildSummary(modeName, build, targets, dirtySignatureSymbols, comparisons, knownCallersPassed, passed);
        string summaryPath = Path.Combine(runRoot, "summary.md");
        File.WriteAllText(summaryPath, summary);

        Console.WriteLine(summary);
        Console.WriteLine($"Summary: {summaryPath}");
        return passed ? 0 : 1;
    }

    private static bool IsRepositoryOrDiscoveryMember(SolutionIndexSymbol symbol)
    {
        if (symbol.Kind is not ("method" or "constructor"))
        {
            return false;
        }

        string path = symbol.RelativePath.Replace('\\', '/');
        return (path.StartsWith("Data/", StringComparison.OrdinalIgnoreCase)
                && path.EndsWith("Repository.cs", StringComparison.OrdinalIgnoreCase))
            || path.Equals("Services/SchemaDiscovery.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIndexedCallable(SolutionIndexSymbol symbol)
    {
        return symbol.Kind is "method" or "constructor";
    }

    private static IReadOnlyList<SolutionIndexSymbol> FindDirtySignatureSymbols(IReadOnlyList<SolutionIndexSymbol> symbols)
    {
        return symbols
            .Where(symbol => HasDirtySignatureTrivia(symbol.Signature))
            .OrderBy(symbol => symbol.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(symbol => symbol.StartLine)
            .ToArray();
    }

    private static bool HasDirtySignatureTrivia(string signature)
    {
        string trimmed = signature.TrimStart();
        return trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("/*", StringComparison.Ordinal)
            || trimmed.StartsWith("///", StringComparison.Ordinal)
            || trimmed.StartsWith("#region", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("#endregion", StringComparison.OrdinalIgnoreCase)
            || signature.Contains("#region", StringComparison.OrdinalIgnoreCase)
            || signature.Contains("#endregion", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, List<ExpectedCaller>> BuildExpectedCallers(
        string observedRoot,
        IReadOnlyList<SolutionIndexSymbol> indexSymbols,
        IReadOnlyList<SolutionIndexSymbol> targets)
    {
        Dictionary<string, SolutionIndexSymbol> symbolsByAnchor = indexSymbols
            .ToDictionary(symbol => BuildAnchorKey(symbol.RelativePath, symbol.StartLine, symbol.StartColumn), StringComparer.OrdinalIgnoreCase);
        HashSet<string> targetKeys = targets.Select(target => target.StableSymbolKey).ToHashSet(StringComparer.Ordinal);
        SyntaxTree[] trees = Directory.EnumerateFiles(observedRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsExcludedPath(observedRoot, path))
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path))
            .ToArray();
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Dbv2CallerVerifier",
            trees,
            GetMetadataReferences(observedRoot),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Dictionary<string, List<ExpectedCaller>> expected = [];
        foreach (SyntaxTree tree in trees)
        {
            string relativePath = Path.GetRelativePath(observedRoot, tree.FilePath);
            SemanticModel model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
            foreach (SimpleNameSyntax name in root.DescendantNodes().OfType<SimpleNameSyntax>())
            {
                InvocationExpressionSyntax? invocation = GetInvocationForName(name);
                ObjectCreationExpressionSyntax? construction = GetConstructionForName(name);
                if (invocation is null && construction is null)
                {
                    continue;
                }

                ISymbol? targetSymbol = invocation is not null
                    ? GetBestSymbol(model.GetSymbolInfo(invocation)) ?? GetBestSymbol(model.GetSymbolInfo(name))
                    : GetBestSymbol(model.GetSymbolInfo(construction!)) ?? GetBestSymbol(model.GetSymbolInfo(name));
                SolutionIndexSymbol? target = ResolveIndexSymbol(observedRoot, targetSymbol, symbolsByAnchor);
                if (target is null || !targetKeys.Contains(target.StableSymbolKey))
                {
                    continue;
                }

                MemberDeclarationSyntax? callerMember = name.Ancestors().OfType<MemberDeclarationSyntax>().FirstOrDefault(member =>
                    member is MethodDeclarationSyntax
                        or ConstructorDeclarationSyntax
                        or PropertyDeclarationSyntax
                        or EventDeclarationSyntax
                        or DelegateDeclarationSyntax);
                SolutionIndexSymbol? caller = ResolveIndexSymbolFromMember(relativePath, tree, callerMember, symbolsByAnchor);
                FileLinePositionSpan span = tree.GetLineSpan(name.Span);
                ExpectedCaller row = new(
                    NormalizeRelativePath(relativePath),
                    span.StartLinePosition.Line + 1,
                    span.StartLinePosition.Character + 1,
                    caller?.Name,
                    GetLineSnippet(tree, name.Span));
                if (!expected.TryGetValue(target.StableSymbolKey, out List<ExpectedCaller>? rows))
                {
                    rows = [];
                    expected[target.StableSymbolKey] = rows;
                }

                if (!rows.Any(existing => existing.RelativePath.Equals(row.RelativePath, StringComparison.OrdinalIgnoreCase)
                    && existing.Line == row.Line
                    && existing.Column == row.Column))
                {
                    rows.Add(row);
                }
            }

            foreach (ImplicitObjectCreationExpressionSyntax creation in root.DescendantNodes().OfType<ImplicitObjectCreationExpressionSyntax>())
            {
                ISymbol? targetSymbol = GetBestSymbol(model.GetSymbolInfo(creation));
                SolutionIndexSymbol? target = ResolveIndexSymbol(observedRoot, targetSymbol, symbolsByAnchor);
                if (target is null || !targetKeys.Contains(target.StableSymbolKey))
                {
                    continue;
                }

                MemberDeclarationSyntax? callerMember = creation.Ancestors().OfType<MemberDeclarationSyntax>().FirstOrDefault(member =>
                    member is MethodDeclarationSyntax
                        or ConstructorDeclarationSyntax
                        or PropertyDeclarationSyntax
                        or EventDeclarationSyntax
                        or DelegateDeclarationSyntax);
                SolutionIndexSymbol? caller = ResolveIndexSymbolFromMember(relativePath, tree, callerMember, symbolsByAnchor);
                FileLinePositionSpan span = tree.GetLineSpan(creation.NewKeyword.Span);
                ExpectedCaller row = new(
                    NormalizeRelativePath(relativePath),
                    span.StartLinePosition.Line + 1,
                    span.StartLinePosition.Character + 1,
                    caller?.Name,
                    GetLineSnippet(tree, creation.Span));
                if (!expected.TryGetValue(target.StableSymbolKey, out List<ExpectedCaller>? rows))
                {
                    rows = [];
                    expected[target.StableSymbolKey] = rows;
                }

                if (!rows.Any(existing => existing.RelativePath.Equals(row.RelativePath, StringComparison.OrdinalIgnoreCase)
                    && existing.Line == row.Line
                    && existing.Column == row.Column))
                {
                    rows.Add(row);
                }
            }
        }

        return expected;
    }

    private static bool KnownCallerChecksPass(SolutionIndexService indexService)
    {
        return Probe(
                indexService,
                "Data\\BaseTableRepository.cs::SchemaStudio.Data::BaseTableRepository::method::GetByDatabase(int)",
                [
                    ("EditorSurface\\ExplorerControl.cs", "InitializeLayout"),
                    ("UI\\BaseTableEditorForm.cs", "LoadData")
                ])
            && Probe(
                indexService,
                "Data\\DataBaseRepository.cs::SchemaStudio.Data::DatabaseRepository::method::Insert(DatabaseDefinition)",
                [
                    ("Data\\DataBaseRepository.cs", "SaveAll")
                ])
            && Probe(
                indexService,
                "Data\\BaseTableRepository.cs::SchemaStudio.Data::BaseTableRepository::method::Insert(BaseTableDefinition)",
                [
                    ("Data\\BaseTableRepository.cs", "SaveAll"),
                    ("UI\\BaseTableEditorForm.cs", "InitializeComponentCustom")
                ]);
    }

    private static bool Probe(
        SolutionIndexService indexService,
        string stableKey,
        IReadOnlyList<(string RelativePath, string CallerName)> expectedCallers)
    {
        IReadOnlyList<SolutionIndexReference> callers = indexService.FindCallers(stableKey, 100);
        return expectedCallers.All(expected => callers.Any(actual =>
            actual.RelativePath.Equals(expected.RelativePath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(actual.CallerName, expected.CallerName, StringComparison.Ordinal)));
    }

    private static bool SameCallSite(SolutionIndexReference actual, ExpectedCaller expected)
    {
        return NormalizeRelativePath(actual.RelativePath).Equals(expected.RelativePath, StringComparison.OrdinalIgnoreCase)
            && actual.Line == expected.Line
            && actual.Column == expected.Column
            && string.Equals(actual.CallerName, expected.CallerName, StringComparison.Ordinal);
    }

    private static SolutionIndexSymbol? ResolveIndexSymbol(
        string observedRoot,
        ISymbol? symbol,
        IReadOnlyDictionary<string, SolutionIndexSymbol> symbolsByAnchor)
    {
        symbol = NormalizeSymbol(symbol);
        if (symbol is null || !IsIndexedDeclarationSymbol(symbol))
        {
            return null;
        }

        SyntaxReference? syntaxReference = symbol?.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxReference is null)
        {
            return null;
        }

        SyntaxNode syntax = syntaxReference.GetSyntax();
        MemberDeclarationSyntax? member = syntax as MemberDeclarationSyntax
            ?? syntax.FirstAncestorOrSelf<MemberDeclarationSyntax>();
        if (member is null)
        {
            return null;
        }

        return ResolveIndexSymbolFromMember(
            Path.GetRelativePath(observedRoot, syntax.SyntaxTree.FilePath),
            syntax.SyntaxTree,
            member,
            symbolsByAnchor);
    }

    private static SolutionIndexSymbol? ResolveIndexSymbolFromMember(
        string relativePath,
        SyntaxTree tree,
        MemberDeclarationSyntax? member,
        IReadOnlyDictionary<string, SolutionIndexSymbol> symbolsByAnchor)
    {
        if (member is null)
        {
            return null;
        }

        FileLinePositionSpan span = tree.GetLineSpan(member.Span);
        return symbolsByAnchor.GetValueOrDefault(BuildAnchorKey(
            NormalizeRelativePath(relativePath),
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1));
    }

    private static ISymbol? NormalizeSymbol(ISymbol? symbol)
    {
        if (symbol is IMethodSymbol { ReducedFrom: not null } reduced)
        {
            symbol = reduced.ReducedFrom;
        }

        return symbol switch
        {
            IMethodSymbol method => method.PartialDefinitionPart ?? method.OriginalDefinition,
            INamedTypeSymbol namedType => namedType.OriginalDefinition,
            null => null,
            _ => symbol.OriginalDefinition
        };
    }

    private static bool IsIndexedDeclarationSymbol(ISymbol symbol)
    {
        return symbol is IMethodSymbol
            or INamedTypeSymbol
            or IPropertySymbol
            or IFieldSymbol
            or IEventSymbol;
    }

    private static InvocationExpressionSyntax? GetInvocationForName(SimpleNameSyntax name)
    {
        InvocationExpressionSyntax? invocation = name.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        return invocation?.Expression switch
        {
            SimpleNameSyntax simpleName when simpleName == name => invocation,
            MemberAccessExpressionSyntax memberAccess when memberAccess.Name == name => invocation,
            MemberBindingExpressionSyntax memberBinding when memberBinding.Name == name => invocation,
            _ => null
        };
    }

    private static ObjectCreationExpressionSyntax? GetConstructionForName(SimpleNameSyntax name)
    {
        ObjectCreationExpressionSyntax? construction = name.FirstAncestorOrSelf<ObjectCreationExpressionSyntax>();
        return construction is not null && construction.Type.Span.Contains(name.Span) ? construction : null;
    }

    private static ISymbol? GetBestSymbol(SymbolInfo symbolInfo)
    {
        return symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
    }

    private static string GetLineSnippet(SyntaxTree tree, TextSpan span)
    {
        SourceText text = tree.GetText();
        LinePosition linePosition = text.Lines.GetLinePosition(span.Start);
        TextLine line = text.Lines[linePosition.Line];
        return line.ToString().Trim();
    }

    private static string BuildAnchorKey(string relativePath, int startLine, int startColumn)
    {
        return $"{NormalizeRelativePath(relativePath)}:{startLine}:{startColumn}";
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        return relativePath.Replace('\\', '/');
    }

    private static bool IsExcludedPath(string observedRoot, string path)
    {
        string relative = Path.GetRelativePath(observedRoot, path);
        string[] parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part => part is ".git" or ".vs" or "bin" or "obj" or "node_modules");
    }

    private static IReadOnlyList<MetadataReference> GetMetadataReferences(string observedRoot)
    {
        Dictionary<string, MetadataReference> references = new(StringComparer.OrdinalIgnoreCase);
        string? trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (!string.IsNullOrWhiteSpace(trustedAssemblies))
        {
            foreach (string path in trustedAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                AddReference(references, path);
            }
        }

        string binRoot = Path.Combine(observedRoot, "bin");
        if (Directory.Exists(binRoot))
        {
            foreach (string path in Directory.EnumerateFiles(binRoot, "*.dll", SearchOption.AllDirectories))
            {
                AddReference(references, path);
            }
        }

        string desktopRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", "Microsoft.WindowsDesktop.App");
        if (Directory.Exists(desktopRoot))
        {
            DirectoryInfo? latest = Directory.EnumerateDirectories(desktopRoot)
                .Select(path => new DirectoryInfo(path))
                .OrderByDescending(dir => dir.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (latest is not null)
            {
                foreach (string path in Directory.EnumerateFiles(latest.FullName, "*.dll"))
                {
                    AddReference(references, path);
                }
            }
        }

        return references.Values.ToArray();
    }

    private static void AddReference(Dictionary<string, MetadataReference> references, string path)
    {
        try
        {
            if (File.Exists(path))
            {
                references[Path.GetFullPath(path)] = MetadataReference.CreateFromFile(path);
            }
        }
        catch
        {
        }
    }

    private static string BuildSummary(
        string modeName,
        SolutionIndexBuildResult build,
        IReadOnlyList<SolutionIndexSymbol> targets,
        IReadOnlyList<SolutionIndexSymbol> dirtySignatureSymbols,
        IReadOnlyList<Comparison> comparisons,
        bool knownCallersPassed,
        bool passed)
    {
        IEnumerable<Comparison> failures = comparisons.Where(item => item.Missing.Count > 0 || item.Unexpected.Count > 0);
        string coverageRows = string.Join(Environment.NewLine, comparisons
            .GroupBy(item => GetTopLevelFolder(item.Target.RelativePath), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"- `{group.Key}`: targets `{group.Count()}`, expected callers `{group.Sum(item => item.Expected.Count)}`, actual callers `{group.Sum(item => item.Actual.Count)}`"));
        string dirtyRows = FormatDirtySignatures(dirtySignatureSymbols);
        string failureRows = string.Join(Environment.NewLine + Environment.NewLine, failures.Select(item => $"""
            ## `{item.Target.StableSymbolKey}`

            Missing:
            {FormatExpected(item.Missing)}

            Unexpected:
            {FormatActual(item.Unexpected)}
            """));
        return $"""
            # DBV2 Indexed Caller Smoke

            Mode: `{modeName}`

            Passed: `{passed}`

            - Indexed files: `{build.IndexedFileCount}`
            - Indexed symbols: `{build.IndexedSymbolCount}`
            - Indexed references: `{build.IndexedReferenceCount}`
            - Indexed call sites: `{build.IndexedCallSiteCount}`
            - Target method/constructor count: `{targets.Count}`
            - Known hand-picked caller checks passed: `{knownCallersPassed}`
            - Fully matched target count: `{comparisons.Count(item => item.Missing.Count == 0 && item.Unexpected.Count == 0)}`
            - Failure count: `{comparisons.Count(item => item.Missing.Count > 0 || item.Unexpected.Count > 0)}`
            - Expected caller rows checked: `{comparisons.Sum(item => item.Expected.Count)}`
            - Actual caller rows checked: `{comparisons.Sum(item => item.Actual.Count)}`
            - Dirty comment/region signatures: `{dirtySignatureSymbols.Count}`

            ## Cross Section

            {coverageRows}

            ## Dirty Signatures

            {dirtyRows}

            {failureRows}
            """;
    }

    private static string FormatDirtySignatures(IReadOnlyList<SolutionIndexSymbol> symbols)
    {
        return symbols.Count == 0
            ? "- none"
            : string.Join(Environment.NewLine, symbols.Take(25).Select(symbol => $"- `{symbol.RelativePath}:{symbol.StartLine}` `{symbol.Signature}`"));
    }

    private static string GetTopLevelFolder(string relativePath)
    {
        string normalized = NormalizeRelativePath(relativePath);
        int slashIndex = normalized.IndexOf('/');
        return slashIndex <= 0 ? "(root)" : normalized[..slashIndex];
    }

    private static string FormatExpected(IReadOnlyList<ExpectedCaller> rows)
    {
        return rows.Count == 0
            ? "- none"
            : string.Join(Environment.NewLine, rows.Select(row => $"- `{row.RelativePath}:{row.Line}:{row.Column}` caller `{row.CallerName}` `{row.Snippet}`"));
    }

    private static string FormatActual(IReadOnlyList<SolutionIndexReference> rows)
    {
        return rows.Count == 0
            ? "- none"
            : string.Join(Environment.NewLine, rows.Select(row => $"- `{row.RelativePath}:{row.Line}:{row.Column}` caller `{row.CallerName}` `{row.Snippet}`"));
    }

    private static MatrixCheck[] BuildFixtureMatrixChecks()
    {
        const string fileA = "McpIndexProbes/McpCallerProbeFixture.A.cs";
        const string fileB = "McpIndexProbes/McpCallerProbeFixture.B.cs";
        const string fileG = "McpIndexProbes/McpGeneratedProbe.g.cs";
        string Key(string containingType, string kind, string name)
        {
            return $"{fileA}::SchemaStudio.SemanticModel.Tests::{containingType}::{kind}::{name}";
        }
        string KeyB(string containingType, string kind, string name)
        {
            return $"{fileB}::SchemaStudio.SemanticModel.Tests::{containingType}::{kind}::{name}";
        }
        string KeyG(string containingType, string kind, string name)
        {
            return $"{fileG}::SchemaStudio.SemanticModel.Tests::{containingType}::{kind}::{name}";
        }

        return
        [
            new("PublicIncrement(int)", Key("McpCallerProbeTarget", "method", "PublicIncrement(int)"), 4, 4),
            new("PrivateHelper(int)", Key("McpCallerProbeTarget", "method", "PrivateHelper(int)"), 1, 1),
            new("CallsPrivateHelper(int)", Key("McpCallerProbeTarget", "method", "CallsPrivateHelper(int)"), 1, 1),
            new("OverloadedAdd(int)", Key("McpCallerProbeTarget", "method", "OverloadedAdd(int)"), 1, 1),
            new("OverloadedAdd(int,int)", Key("McpCallerProbeTarget", "method", "OverloadedAdd(int,int)"), 1, 1),
            new("GenericIdentity<T>(T)", Key("McpCallerProbeTarget", "method", "GenericIdentity(T)"), 1, 1),
            new("StaticGate(int)", Key("McpCallerProbeTarget", "method", "StaticGate(int)"), 1, 2),
            new("MarkedMethod()", Key("McpCallerProbeTarget", "method", "MarkedMethod()"), 1, 1),
            new("GetResolverInvoke()", Key("McpCallerProbeTarget", "method", "GetResolverInvoke()"), 0, 0),
            new("RaiseProbeCompleted()", Key("McpCallerProbeTarget", "method", "RaiseProbeCompleted()"), 1, 1),
            new("McpCallerProbeTarget()", Key("McpCallerProbeTarget", "constructor", "McpCallerProbeTarget()"), 1, 1),
            new("McpCallerProbeTarget(string)", Key("McpCallerProbeTarget", "constructor", "McpCallerProbeTarget(string)"), 3, 3),
            new("IMcpProbeService.InterfaceProbe(int)", Key("IMcpProbeService", "method", "InterfaceProbe(int)"), 2, 2),
            new("McpProbeServiceImpl.InterfaceProbe(int)", Key("McpProbeServiceImpl", "method", "InterfaceProbe(int)"), 1, 1),
            new("McpProbeServiceImpl()", Key("McpProbeServiceImpl", "constructor", "McpProbeServiceImpl()"), 1, 1),
            new("ToProbeDoubled(this int)", Key("McpProbeExtensions", "method", "ToProbeDoubled(this int)"), 1, 1),
            new("ResolverProperty", Key("McpCallerProbeTarget", "property", "ResolverProperty"), null, 2),
            new("ProbeCompleted", Key("McpCallerProbeTarget", "event", "ProbeCompleted"), null, 2),
            new("Label", Key("McpCallerProbeTarget", "property", "Label"), null, 1),
            new("InstanceField", Key("McpCallerProbeTarget", "field", "InstanceField"), null, 2),
            new("StaticField", Key("McpCallerProbeTarget", "field", "StaticField"), null, 2),
            new("McpProbeMarkAttribute", Key(string.Empty, "class", "McpProbeMarkAttribute"), null, 2),
            new("McpProbeKind", Key(string.Empty, "enum", "McpProbeKind"), null, 0),
            new("IMcpProbeService", Key(string.Empty, "interface", "IMcpProbeService"), null, 5),
            new("McpProbeServiceImpl type", Key(string.Empty, "class", "McpProbeServiceImpl"), null, 1),
            new("McpCallerProbeTarget type", Key(string.Empty, "class", "McpCallerProbeTarget"), null, 8),
            new("McpProbeDelegate", Key(string.Empty, "delegate", "McpProbeDelegate(int)"), null, 2),
            new("McpProbeStruct", Key(string.Empty, "struct", "McpProbeStruct"), null, 1),
            new("McpProbeStruct(int)", Key("McpProbeStruct", "constructor", "McpProbeStruct(int)"), 1, 1),
            new("McpProbeRecord", Key(string.Empty, "record", "McpProbeRecord"), null, 2),
            new("McpProbeRecord.Value", Key("McpProbeRecord", "property", "Value"), null, 2),
            new("McpBaseProbe", Key(string.Empty, "class", "McpBaseProbe"), null, 1),
            new("McpMetadataOnlyTarget", Key(string.Empty, "class", "McpMetadataOnlyTarget"), null, 3),
            new("McpMetadataOnlyTarget.MetadataMethod()", Key("McpMetadataOnlyTarget", "method", "MetadataMethod()"), 0, 1),
            // Additional rows added by Claude on 2026-05-21 to extend matrix coverage of fixture-declared symbols
            new("IMcpFeatureContract", Key(string.Empty, "interface", "IMcpFeatureContract"), null, 2),
            new("IMcpFeatureContract.ContractProbe()", Key("IMcpFeatureContract", "method", "ContractProbe()"), 1, 1),
            new("McpFeatureContractImpl", Key(string.Empty, "class", "McpFeatureContractImpl"), null, 1),
            new("McpFeatureContractImpl.ContractProbe()", Key("McpFeatureContractImpl", "method", "ContractProbe()"), 0, 0),
            new("McpVirtualBase", Key(string.Empty, "class", "McpVirtualBase"), null, 4),
            new("McpVirtualDerived", Key(string.Empty, "class", "McpVirtualDerived"), null, 1),
            new("McpVirtualBase.VirtualProbe()", Key("McpVirtualBase", "method", "VirtualProbe()"), 2, 2),
            new("McpVirtualDerived.VirtualProbe()", Key("McpVirtualDerived", "method", "VirtualProbe()"), 0, 0),
            new("McpDerivedProbe", Key(string.Empty, "class", "McpDerivedProbe"), null, 0),
            new("McpFeatureEnum", Key(string.Empty, "enum", "McpFeatureEnum"), null, 2),
            new("McpProbeStruct.Value", Key("McpProbeStruct", "property", "Value"), null, 2),
            new("McpPartialProbe.PartA()", Key("McpPartialProbe", "method", "PartA()"), 1, 1),
            new("McpPartialProbe.PartB()", KeyB("McpPartialProbe", "method", "PartB()"), 1, 1),
            new("McpGeneratedProbe", KeyG(string.Empty, "class", "McpGeneratedProbe"), null, 0),
            new("McpGeneratedProbe.GeneratedMethod()", KeyG("McpGeneratedProbe", "method", "GeneratedMethod()"), 0, 0),
            new("McpProbeExtensions", Key(string.Empty, "class", "McpProbeExtensions"), null, 0),
            // V1 common-pattern additions (Claude 2026-05-21): async, explicit-impl, nested, generic type, ctor chaining, new-hiding
            new("McpAsyncProbe.AsyncProbe(int)", Key("McpAsyncProbe", "method", "AsyncProbe(int)"), 1, 1),
            new("McpExplicitImpl", Key(string.Empty, "class", "McpExplicitImpl"), null, 1),
            new("McpOuterProbe", Key(string.Empty, "class", "McpOuterProbe"), null, 1),
            new("McpOuterProbe.Nested", Key("McpOuterProbe", "class", "Nested"), null, 1),
            new("McpOuterProbe.Nested.NestedMethod()", Key("Nested", "method", "NestedMethod()"), 1, 1),
            new("McpGenericProbe<T>", Key(string.Empty, "class", "McpGenericProbe"), null, 1),
            new("McpGenericProbe<T>.Echo(T)", Key("McpGenericProbe", "method", "Echo(T)"), 1, 1),
            new("McpCallerProbeTarget(int) [chains to (string)]", Key("McpCallerProbeTarget", "constructor", "McpCallerProbeTarget(int)"), 1, 1),
            new("McpHidingDerived", Key(string.Empty, "class", "McpHidingDerived"), null, 2),
            new("McpHidingDerived.VirtualProbe() [new modifier]", Key("McpHidingDerived", "method", "VirtualProbe()"), 1, 1),
            // V1 gap-exposure rows for Monitor=False shapes the operator wants explicit visibility on
            new("McpIndexerProbe.this[int] [indexer]", Key("McpIndexerProbe", "indexer", "this(int)"), 2, 2),
            new("McpOperatorProbe.operator + [binary op]", Key("McpOperatorProbe", "operator", "+(McpOperatorProbe,McpOperatorProbe)"), 1, 1),
            new("McpOperatorProbe.operator int [conversion]", Key("McpOperatorProbe", "conversion", "int(McpOperatorProbe)"), 1, 1),
            new("McpFeatureEnum.FeatureAlpha [enum member]", Key(string.Empty, "enum-member", "FeatureAlpha"), null, 1),
            // V1 async-pattern completeness additions (Claude 2026-05-21): await foreach + await using
            new("McpAsyncEnumerableProbe", Key(string.Empty, "class", "McpAsyncEnumerableProbe"), null, 1),
            new("McpAsyncEnumerableProbe.EnumerateAsync()", Key("McpAsyncEnumerableProbe", "method", "EnumerateAsync()"), 1, 1),
            new("McpAsyncDisposableProbe", Key(string.Empty, "class", "McpAsyncDisposableProbe"), null, 2),
            new("McpAsyncDisposableProbe.DisposeAsync() [implicit await using]", Key("McpAsyncDisposableProbe", "method", "DisposeAsync()"), 1, 1)
        ];
    }

    private static IReadOnlyDictionary<string, RoslynMatrixCounts> BuildRoslynFixtureMatrix(
        string observedRoot,
        IReadOnlyList<MatrixCheck> checks)
    {
        SyntaxTree[] trees = Directory.EnumerateFiles(observedRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path))
            .ToArray();
        CSharpCompilation compilation = CSharpCompilation.Create(
            "FixtureMatrixRoslyn",
            trees,
            GetMetadataReferences(observedRoot),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Dictionary<string, ISymbol> targetSymbols = BuildFixtureTargetSymbols(observedRoot, compilation, checks);
        Dictionary<string, int> callerCounts = targetSymbols.ToDictionary(item => item.Key, _ => 0, StringComparer.Ordinal);
        Dictionary<string, int> referenceCounts = targetSymbols.ToDictionary(item => item.Key, _ => 0, StringComparer.Ordinal);
        HashSet<string> seenReferences = new(StringComparer.Ordinal);
        HashSet<string> seenCallers = new(StringComparer.Ordinal);

        foreach (SyntaxTree tree in trees)
        {
            SemanticModel model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
            string relativePath = NormalizeRelativePath(Path.GetRelativePath(observedRoot, tree.FilePath));
            foreach (SimpleNameSyntax name in root.DescendantNodes().OfType<SimpleNameSyntax>())
            {
                InvocationExpressionSyntax? invocation = GetInvocationForName(name);
                ObjectCreationExpressionSyntax? construction = GetConstructionForName(name);
                ISymbol? symbol = NormalizeSymbol(invocation is not null
                    ? GetBestSymbol(model.GetSymbolInfo(invocation)) ?? GetBestSymbol(model.GetSymbolInfo(name))
                    : construction is not null
                        ? GetBestSymbol(model.GetSymbolInfo(construction)) ?? GetBestSymbol(model.GetSymbolInfo(name))
                        : GetBestSymbol(model.GetSymbolInfo(name)));
                string? targetKey = FindTargetKey(symbol, targetSymbols);
                if (targetKey is null && construction is not null)
                {
                    symbol = NormalizeSymbol(GetBestSymbol(model.GetSymbolInfo(name)));
                    targetKey = FindTargetKey(symbol, targetSymbols);
                }

                if (targetKey is null)
                {
                    continue;
                }

                string referenceKind = invocation is not null
                    ? "invocation"
                    : construction is not null
                        ? "construction"
                        : ClassifyMatrixReferenceKind(name);
                string siteKey = BuildRoslynSiteKey(relativePath, tree, name.Span, referenceKind);
                if (seenReferences.Add($"{targetKey}|{siteKey}"))
                {
                    referenceCounts[targetKey]++;
                }

                if (invocation is not null || construction is not null)
                {
                    if (seenCallers.Add($"{targetKey}|{siteKey}"))
                    {
                        callerCounts[targetKey]++;
                    }
                }
            }

            foreach (ImplicitObjectCreationExpressionSyntax creation in root.DescendantNodes().OfType<ImplicitObjectCreationExpressionSyntax>())
            {
                ISymbol? symbol = NormalizeSymbol(GetBestSymbol(model.GetSymbolInfo(creation)));
                string? targetKey = FindTargetKey(symbol, targetSymbols);
                if (targetKey is null)
                {
                    continue;
                }

                string siteKey = BuildRoslynSiteKey(relativePath, tree, creation.NewKeyword.Span, "construction");
                if (seenReferences.Add($"{targetKey}|{siteKey}"))
                {
                    referenceCounts[targetKey]++;
                }

                if (seenCallers.Add($"{targetKey}|{siteKey}"))
                {
                    callerCounts[targetKey]++;
                }
            }

            foreach (AttributeSyntax attribute in root.DescendantNodes().OfType<AttributeSyntax>())
            {
                ISymbol? symbol = NormalizeSymbol(GetAttributeTypeSymbol(model, attribute));
                string? targetKey = FindTargetKey(symbol, targetSymbols);
                if (targetKey is null)
                {
                    continue;
                }

                string siteKey = BuildRoslynSiteKey(relativePath, tree, attribute.Name.Span, "attribute");
                if (seenReferences.Add($"{targetKey}|{siteKey}"))
                {
                    referenceCounts[targetKey]++;
                }
            }
        }

        Dictionary<string, RoslynMatrixCounts> counts = [];
        foreach (MatrixCheck check in checks)
        {
            counts[check.StableKey] = new RoslynMatrixCounts(
                targetSymbols.ContainsKey(check.StableKey),
                callerCounts.GetValueOrDefault(check.StableKey),
                referenceCounts.GetValueOrDefault(check.StableKey));
        }

        return counts;
    }

    private static Dictionary<string, ISymbol> BuildFixtureTargetSymbols(
        string observedRoot,
        CSharpCompilation compilation,
        IReadOnlyList<MatrixCheck> checks)
    {
        HashSet<string> requestedKeys = checks.Select(check => check.StableKey).ToHashSet(StringComparer.Ordinal);
        Dictionary<string, ISymbol> symbols = new(StringComparer.Ordinal);
        foreach (SyntaxTree tree in compilation.SyntaxTrees)
        {
            SemanticModel model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
            string relativePath = NormalizeRelativePath(Path.GetRelativePath(observedRoot, tree.FilePath));
            foreach (MemberDeclarationSyntax member in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
            {
                foreach ((ISymbol Symbol, string Kind, string KeyName, string ParameterSuffix) item in GetDeclaredMatrixSymbols(model, member))
                {
                    string key = $"{relativePath}::{GetContainingNamespace(member)}::{GetContainingType(member) ?? string.Empty}::{item.Kind}::{item.KeyName}{item.ParameterSuffix}";
                    if (requestedKeys.Contains(key))
                    {
                        symbols[key] = NormalizeSymbol(item.Symbol)!;
                    }
                }
            }
        }

        return symbols;
    }

    private static IEnumerable<(ISymbol Symbol, string Kind, string KeyName, string ParameterSuffix)> GetDeclaredMatrixSymbols(
        SemanticModel model,
        MemberDeclarationSyntax member)
    {
        switch (member)
        {
            case EnumDeclarationSyntax enumDecl when model.GetDeclaredSymbol(enumDecl) is { } enumSymbol:
                yield return (enumSymbol, "enum", enumDecl.Identifier.ValueText, string.Empty);
                foreach (EnumMemberDeclarationSyntax enumMember in enumDecl.Members)
                {
                    if (model.GetDeclaredSymbol(enumMember) is { } enumMemberSymbol)
                    {
                        yield return (enumMemberSymbol, "enum-member", enumMember.Identifier.ValueText, string.Empty);
                    }
                }

                break;
            case BaseTypeDeclarationSyntax type when model.GetDeclaredSymbol(type) is { } typeSymbol:
                yield return (typeSymbol, GetMatrixTypeKind(type), type.Identifier.ValueText, string.Empty);
                break;
            case IndexerDeclarationSyntax indexer when model.GetDeclaredSymbol(indexer) is { } indexerSymbol:
                string indexerSuffix = "(" + string.Join(",", indexer.ParameterList.Parameters.Select(p => p.Type?.ToString() ?? "?")) + ")";
                yield return (indexerSymbol, "indexer", "this", indexerSuffix);
                break;
            case OperatorDeclarationSyntax op when model.GetDeclaredSymbol(op) is { } opSymbol:
                yield return (opSymbol, "operator", op.OperatorToken.ValueText, BuildParameterSuffix(op.ParameterList));
                break;
            case ConversionOperatorDeclarationSyntax conv when model.GetDeclaredSymbol(conv) is { } convSymbol:
                yield return (convSymbol, "conversion", conv.Type.ToString(), BuildParameterSuffix(conv.ParameterList));
                break;
            case DelegateDeclarationSyntax del when model.GetDeclaredSymbol(del) is { } delegateSymbol:
                yield return (delegateSymbol, "delegate", del.Identifier.ValueText, BuildParameterSuffix(del.ParameterList));
                break;
            case MethodDeclarationSyntax method when model.GetDeclaredSymbol(method) is { } methodSymbol:
                yield return (methodSymbol, "method", method.Identifier.ValueText, BuildParameterSuffix(method.ParameterList));
                break;
            case ConstructorDeclarationSyntax constructor when model.GetDeclaredSymbol(constructor) is { } constructorSymbol:
                yield return (constructorSymbol, "constructor", constructor.Identifier.ValueText, BuildParameterSuffix(constructor.ParameterList));
                break;
            case PropertyDeclarationSyntax property when model.GetDeclaredSymbol(property) is { } propertySymbol:
                yield return (propertySymbol, "property", property.Identifier.ValueText, string.Empty);
                break;
            case EventDeclarationSyntax evt when model.GetDeclaredSymbol(evt) is { } eventSymbol:
                yield return (eventSymbol, "event", evt.Identifier.ValueText, string.Empty);
                break;
            case EventFieldDeclarationSyntax eventField:
                foreach (VariableDeclaratorSyntax variable in eventField.Declaration.Variables)
                {
                    if (model.GetDeclaredSymbol(variable) is { } eventFieldSymbol)
                    {
                        yield return (eventFieldSymbol, "event", variable.Identifier.ValueText, string.Empty);
                    }
                }

                break;
            case FieldDeclarationSyntax field:
                foreach (VariableDeclaratorSyntax variable in field.Declaration.Variables)
                {
                    if (model.GetDeclaredSymbol(variable) is { } fieldSymbol)
                    {
                        yield return (fieldSymbol, "field", variable.Identifier.ValueText, string.Empty);
                    }
                }

                break;
        }
    }

    private static string GetMatrixTypeKind(BaseTypeDeclarationSyntax type)
    {
        return type switch
        {
            ClassDeclarationSyntax => "class",
            StructDeclarationSyntax => "struct",
            InterfaceDeclarationSyntax => "interface",
            RecordDeclarationSyntax => "record",
            EnumDeclarationSyntax => "enum",
            _ => type.Kind().ToString()
        };
    }

    private static string GetContainingNamespace(SyntaxNode node)
    {
        BaseNamespaceDeclarationSyntax? namespaceDeclaration = node.AncestorsAndSelf().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
        return namespaceDeclaration?.Name.ToString() ?? string.Empty;
    }

    private static string? GetContainingType(SyntaxNode node)
    {
        return node.Parent?.AncestorsAndSelf()
            .OfType<BaseTypeDeclarationSyntax>()
            .FirstOrDefault()
            ?.Identifier.ValueText;
    }

    private static string? FindTargetKey(ISymbol? symbol, IReadOnlyDictionary<string, ISymbol> targetSymbols)
    {
        if (symbol is null)
        {
            return null;
        }

        foreach ((string key, ISymbol target) in targetSymbols)
        {
            if (SymbolEqualityComparer.Default.Equals(symbol, target))
            {
                return key;
            }

            if (symbol is IMethodSymbol { MethodKind: MethodKind.DelegateInvoke, ContainingType: not null } delegateInvoke
                && SymbolEqualityComparer.Default.Equals(delegateInvoke.ContainingType, target))
            {
                return key;
            }
        }

        return null;
    }

    private static string BuildRoslynSiteKey(string relativePath, SyntaxTree tree, TextSpan span, string kind)
    {
        FileLinePositionSpan lineSpan = tree.GetLineSpan(span);
        return $"{relativePath}:{lineSpan.StartLinePosition.Line + 1}:{lineSpan.StartLinePosition.Character + 1}:{kind}";
    }

    private static string BuildParameterSuffix(ParameterListSyntax? parameterList)
    {
        if (parameterList is null)
        {
            return string.Empty;
        }

        string[] parameterTypes = parameterList.Parameters
            .Select(GetStableKeyParameterType)
            .ToArray();
        return "(" + string.Join(",", parameterTypes) + ")";
    }

    private static string GetStableKeyParameterType(ParameterSyntax parameter)
    {
        string type = parameter.Type?.ToString() ?? string.Empty;
        string modifier = parameter.Modifiers.ToFullString().Trim();
        return string.IsNullOrWhiteSpace(modifier) ? type : $"{modifier} {type}";
    }

    private static string ClassifyMatrixReferenceKind(SimpleNameSyntax name)
    {
        SyntaxNode? parent = name.Parent;
        if (parent is AssignmentExpressionSyntax assignment && assignment.Left.Span.Contains(name.Span))
        {
            return "write";
        }

        if (name is TypeSyntax || name.Ancestors().OfType<TypeSyntax>().Any(type => type.Span.Contains(name.Span)))
        {
            return "type";
        }

        if (name.FirstAncestorOrSelf<ObjectCreationExpressionSyntax>() is not null)
        {
            return "construction";
        }

        return "read";
    }

    private static ISymbol? GetAttributeTypeSymbol(SemanticModel model, AttributeSyntax attribute)
    {
        ISymbol? symbol = GetBestSymbol(model.GetSymbolInfo(attribute));
        if (symbol is IMethodSymbol method)
        {
            return method.ContainingType;
        }

        return symbol ?? model.GetTypeInfo(attribute).Type;
    }

    private static IReadOnlyList<ModelFeatureProbeResult> BuildModelFeatureProbeResults(
        string observedRoot,
        SolutionIndexQueryResult index)
    {
        SyntaxTree[] trees = Directory.EnumerateFiles(observedRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path))
            .ToArray();
        CSharpCompilation compilation = CSharpCompilation.Create(
            "FixtureFeatureProbeRoslyn",
            trees,
            GetMetadataReferences(observedRoot),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        CompilationUnitSyntax[] roots = trees.Select(tree => tree.GetCompilationUnitRoot()).ToArray();
        bool HasSymbol(string kind, string name)
        {
            return index.Symbols.Any(symbol =>
                symbol.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)
                && symbol.Name.Equals(name, StringComparison.Ordinal));
        }

        bool HasSignature(string text)
        {
            return index.Symbols.Any(symbol => symbol.Signature.Contains(text, StringComparison.Ordinal));
        }

        bool roslynIndexer = roots.Any(root => root.DescendantNodes().OfType<IndexerDeclarationSyntax>().Any());
        bool roslynOperator = roots.Any(root => root.DescendantNodes().OfType<OperatorDeclarationSyntax>().Any());
        bool roslynConversion = roots.Any(root => root.DescendantNodes().OfType<ConversionOperatorDeclarationSyntax>().Any());
        bool roslynEnumMember = roots.Any(root => root.DescendantNodes().OfType<EnumMemberDeclarationSyntax>().Any(member => member.Identifier.ValueText == "FeatureAlpha"));
        bool roslynLocalFunction = roots.Any(root => root.DescendantNodes().OfType<LocalFunctionStatementSyntax>().Any(local => local.Identifier.ValueText == "McpLocalProbe"));
        bool roslynLambda = roots.Any(root => root.DescendantNodes().OfType<ParenthesizedLambdaExpressionSyntax>().Any());
        bool roslynPartial = roots.Count(root => root.DescendantNodes().OfType<ClassDeclarationSyntax>().Any(type =>
            type.Identifier.ValueText == "McpPartialProbe"
            && type.Modifiers.Any(SyntaxKind.PartialKeyword))) == 2;
        bool roslynGenerated = roots.Any(root => root.GetLeadingTrivia().Any(trivia =>
            trivia.ToFullString().Contains("<auto-generated", StringComparison.OrdinalIgnoreCase)));
        bool roslynOverride = roots.Any(root => root.DescendantNodes().OfType<MethodDeclarationSyntax>().Any(method =>
            method.Identifier.ValueText == "VirtualProbe"
            && method.Modifiers.Any(SyntaxKind.OverrideKeyword)));
        bool roslynInterfaceImplementation = HasImplicitInterfaceImplementation(compilation, "IMcpFeatureContract", "McpFeatureContractImpl", "ContractProbe");

        return
        [
            new("indexer declaration", roslynIndexer, HasSignature("this[int index]"), false, "IndexerDeclarationSyntax is present; current Monitor symbol model has no indexer kind."),
            new("operator overload declaration", roslynOperator, HasSignature("operator +"), false, "OperatorDeclarationSyntax is present; current Monitor symbol model has no operator kind."),
            new("conversion operator declaration", roslynConversion, HasSignature("operator int"), false, "ConversionOperatorDeclarationSyntax is present; current Monitor symbol model has no conversion kind."),
            new("enum member declaration", roslynEnumMember, HasSymbol("field", "FeatureAlpha"), false, "Enum member exists in Roslyn as a field-like symbol; current Monitor indexes the enum type, not members."),
            new("local function declaration", roslynLocalFunction, HasSymbol("method", "McpLocalProbe"), false, "LocalFunctionStatementSyntax exists; current Monitor only indexes member declarations."),
            new("lambda caller identity", roslynLambda, false, false, "Lambda body exists and can contain calls; current caller identity is nearest indexed member, not a lambda symbol."),
            new("partial declaration merge", roslynPartial, index.Symbols.Count(symbol => symbol.Name == "McpPartialProbe") == 1, false, "Two partial declarations exist; current Monitor stores physical declarations, not one merged type row."),
            new("generated-file policy", roslynGenerated, index.Files.Any(file => file.RelativePath.Contains("McpGeneratedProbe.g.cs", StringComparison.OrdinalIgnoreCase)), true, "Generated-looking file is included today; this row locks current behavior until a generated policy exists."),
            new("override relationship row", roslynOverride, false, false, "Override method exists; current Monitor records references/callers, not override relationship rows."),
            new("interface implementation relationship row", roslynInterfaceImplementation, false, false, "Implicit interface implementation exists; current Monitor records references/callers, not implementation relationship rows.")
        ];
    }

    private static bool HasImplicitInterfaceImplementation(
        CSharpCompilation compilation,
        string interfaceName,
        string implementationName,
        string methodName)
    {
        INamedTypeSymbol? interfaceSymbol = GetAllNamedTypes(compilation.GlobalNamespace)
            .FirstOrDefault(type => type.Name == interfaceName);
        INamedTypeSymbol? implementationSymbol = GetAllNamedTypes(compilation.GlobalNamespace)
            .FirstOrDefault(type => type.Name == implementationName);
        IMethodSymbol? interfaceMethod = interfaceSymbol?.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault();
        return interfaceMethod is not null
            && implementationSymbol?.FindImplementationForInterfaceMember(interfaceMethod) is not null;
    }

    private static IEnumerable<INamedTypeSymbol> GetAllNamedTypes(INamespaceSymbol namespaceSymbol)
    {
        foreach (INamedTypeSymbol type in namespaceSymbol.GetTypeMembers())
        {
            yield return type;
            foreach (INamedTypeSymbol nested in GetNestedTypes(type))
            {
                yield return nested;
            }
        }

        foreach (INamespaceSymbol childNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            foreach (INamedTypeSymbol type in GetAllNamedTypes(childNamespace))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetNestedTypes(INamedTypeSymbol type)
    {
        foreach (INamedTypeSymbol nested in type.GetTypeMembers())
        {
            yield return nested;
            foreach (INamedTypeSymbol nestedChild in GetNestedTypes(nested))
            {
                yield return nestedChild;
            }
        }
    }

    private static string BuildFixtureMatrixSummary(
        SolutionIndexBuildResult build,
        IReadOnlyList<MatrixResult> results,
        IReadOnlyList<ModelFeatureProbeResult> featureProbeResults,
        bool passed)
    {
        string rows = string.Join(Environment.NewLine, results.Select(result =>
        {
            string roslynCallers = result.Check.ExpectedCallers is null
                ? "n/a"
                : $"{result.Roslyn?.CallerCount ?? 0}/{result.Check.ExpectedCallers}";
            string monitorCallers = result.Check.ExpectedCallers is null
                ? "n/a"
                : $"{result.ActualCallers?.Count ?? 0}/{result.Check.ExpectedCallers}";
            string roslynReferences = result.Check.ExpectedReferences is null
                ? "n/a"
                : $"{result.Roslyn?.ReferenceCount ?? 0}/{result.Check.ExpectedReferences}";
            string monitorReferences = result.Check.ExpectedReferences is null
                ? "n/a"
                : $"{result.ActualReferences?.Count ?? 0}/{result.Check.ExpectedReferences}";
            return $"- `{result.Check.Name}` Roslyn callers `{roslynCallers}`, Monitor callers `{monitorCallers}`, Roslyn refs `{roslynReferences}`, Monitor refs `{monitorReferences}`, passed `{result.Passed}`";
        }));
        string failures = string.Join(Environment.NewLine + Environment.NewLine, results
            .Where(result => !result.Passed)
            .Select(result => $"""
                ## `{result.Check.Name}`

                Stable key: `{result.Check.StableKey}`

                Roslyn target resolved: `{result.Roslyn?.TargetResolved ?? false}`
                Roslyn callers: `{result.Roslyn?.CallerCount ?? 0}`
                Roslyn references: `{result.Roslyn?.ReferenceCount ?? 0}`

                Callers:
                {FormatActual(result.ActualCallers ?? [])}

                References:
                {FormatActual(result.ActualReferences ?? [])}
                """));
        string featureProbeRows = string.Join(Environment.NewLine, featureProbeResults.Select(result =>
            $"- `{result.Name}` Roslyn present `{result.RoslynPresent}`, Monitor indexed `{result.MonitorIndexed}`, current expectation `{result.ExpectedMonitorIndexed}`, note `{result.Note}`"));
        int featureProbeExpectationFailures = featureProbeResults.Count(result => result.MonitorIndexed != result.ExpectedMonitorIndexed);
        return $"""
            # Fixture Index Matrix Smoke

            Passed: `{passed}`

            - Indexed files: `{build.IndexedFileCount}`
            - Indexed symbols: `{build.IndexedSymbolCount}`
            - Indexed references: `{build.IndexedReferenceCount}`
            - Indexed call sites: `{build.IndexedCallSiteCount}`
            - Matrix checks: `{results.Count}`
            - Fully matched checks: `{results.Count(result => result.Passed)}`
            - Failure count: `{results.Count(result => !result.Passed)}`
            - Roslyn target resolution failures: `{results.Count(result => result.Roslyn?.TargetResolved != true)}`
            - Current model feature probes: `{featureProbeResults.Count}`
            - Current model feature expectation failures: `{featureProbeExpectationFailures}`

            ## Search Model Coverage

            Covered:
            - methods: instance, private, static, generic, overloads, method-group references, declared-only zero-count methods
            - constructors: explicit `new`, target-typed `new`, class and struct constructors
            - member references: properties, fields, events, record initializer properties, static member receivers
            - type references: class, interface, struct, record, delegate, base-class list, `typeof`, `nameof`
            - call binding: interface dispatch, implementation calls, extension methods, delegate invocation
            - metadata syntax: attributes, `nameof(Member)`, `typeof(Type)`

            Current model feature probes in this fixture:
            {featureProbeRows}

            ## Matrix

            {rows}

            {failures}
            """;
    }

    private const string FixtureSourceA =
        """
        using System;

        namespace SchemaStudio.SemanticModel.Tests;

        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
        public sealed class McpProbeMarkAttribute : Attribute { }

        public enum McpProbeKind
        {
            None = 0,
            Alpha = 1,
            Beta = 2
        }

        public enum McpFeatureEnum
        {
            FeatureNone = 0,
            FeatureAlpha = 1
        }

        public delegate int McpProbeDelegate(int seed);

        public interface IMcpProbeService
        {
            int InterfaceProbe(int seed);
        }

        public sealed class McpProbeServiceImpl : IMcpProbeService
        {
            public McpProbeServiceImpl()
            {
            }

            public int InterfaceProbe(int seed) => seed + 1;
        }

        public readonly struct McpProbeStruct
        {
            public McpProbeStruct(int value)
            {
                Value = value;
            }

            public int Value { get; }
        }

        public sealed record McpProbeRecord
        {
            public int Value { get; init; }
        }

        public abstract class McpBaseProbe
        {
        }

        public sealed class McpDerivedProbe : McpBaseProbe
        {
        }

        public sealed class McpMetadataOnlyTarget
        {
            public void MetadataMethod()
            {
            }
        }

        public interface IMcpFeatureContract
        {
            int ContractProbe();
        }

        public sealed class McpFeatureContractImpl : IMcpFeatureContract
        {
            public int ContractProbe() => 19;
        }

        public abstract class McpVirtualBase
        {
            public virtual int VirtualProbe() => 1;
        }

        public sealed class McpVirtualDerived : McpVirtualBase
        {
            public override int VirtualProbe() => 2;
        }

        public sealed partial class McpPartialProbe
        {
            public int PartA() => 1;
        }

        public sealed class McpIndexerProbe
        {
            public int this[int index]
            {
                get => index;
                set { }
            }
        }

        public readonly struct McpOperatorProbe
        {
            private readonly int value;

            public McpOperatorProbe(int value)
            {
                this.value = value;
            }

            public static McpOperatorProbe operator +(McpOperatorProbe left, McpOperatorProbe right)
            {
                return new McpOperatorProbe(left.value + right.value);
            }

            public static implicit operator int(McpOperatorProbe probe)
            {
                return probe.value;
            }
        }

        [McpProbeMark]
        public sealed class McpCallerProbeTarget
        {
            public int InstanceField;

            public static int StaticField;

            public int PublicIncrement(int value) => value + 1;

            private int PrivateHelper(int v) => v * 2;

            public int CallsPrivateHelper(int v) => PrivateHelper(v);

            public int OverloadedAdd(int a) => a;

            public int OverloadedAdd(int a, int b) => a + b;

            public T GenericIdentity<T>(T value) => value;

            public static int StaticGate(int seed) => seed * 10;

            [McpProbeMark]
            public int MarkedMethod() => 7;

            public static Func<int>? ResolverProperty { get; set; }

            public static int GetResolverInvoke() => ResolverProperty?.Invoke() ?? 0;

            public event EventHandler? ProbeCompleted;

            public void RaiseProbeCompleted() => ProbeCompleted?.Invoke(this, EventArgs.Empty);

            public McpCallerProbeTarget()
            {
            }

            public McpCallerProbeTarget(string label)
            {
                Label = label;
            }

            public McpCallerProbeTarget(int seed) : this(seed.ToString())
            {
            }

            public string? Label { get; }
        }

        public static class McpProbeExtensions
        {
            public static int ToProbeDoubled(this int v) => v * 2;
        }

        public sealed class McpAsyncProbe
        {
            public async System.Threading.Tasks.Task<int> AsyncProbe(int seed)
            {
                return await System.Threading.Tasks.Task.FromResult(seed + 1);
            }
        }

        public sealed class McpExplicitImpl : IMcpProbeService
        {
            int IMcpProbeService.InterfaceProbe(int seed) => seed + 100;
        }

        public sealed class McpOuterProbe
        {
            public sealed class Nested
            {
                public int NestedMethod() => 1;
            }
        }

        public sealed class McpGenericProbe<T>
        {
            public T Echo(T value) => value;
        }

        public sealed class McpHidingDerived : McpVirtualBase
        {
            public new int VirtualProbe() => 9;
        }

        public sealed class McpAsyncEnumerableProbe
        {
            public async System.Collections.Generic.IAsyncEnumerable<int> EnumerateAsync()
            {
                await System.Threading.Tasks.Task.Yield();
                yield return 1;
            }
        }

        public sealed class McpAsyncDisposableProbe : System.IAsyncDisposable
        {
            public async System.Threading.Tasks.ValueTask DisposeAsync()
            {
                await System.Threading.Tasks.Task.Yield();
            }
        }
        """;

    private const string FixtureSourceB =
        """
        using System;

        namespace SchemaStudio.SemanticModel.Tests;

        public sealed partial class McpPartialProbe
        {
            public int PartB() => 2;
        }

        public sealed class McpCallerProbeCallers
        {
            private static readonly McpCallerProbeTarget _target = new McpCallerProbeTarget();
            private static readonly McpCallerProbeTarget _targetExplicit = new McpCallerProbeTarget("explicit");
            private static readonly McpCallerProbeTarget _targetTargetTyped = new("targettyped");
            private static readonly McpProbeServiceImpl _impl = new McpProbeServiceImpl();
            private static readonly IMcpProbeService _via = _impl;
            private static readonly McpProbeStruct _struct = new McpProbeStruct(5);
            private static readonly McpProbeRecord _record = new McpProbeRecord { Value = 6 };
            private static readonly McpProbeDelegate _delegate = McpCallerProbeTarget.StaticGate;
            private static readonly McpIndexerProbe _indexer = new McpIndexerProbe();
            private static readonly McpOperatorProbe _operatorLeft = new McpOperatorProbe(1);
            private static readonly McpOperatorProbe _operatorRight = new McpOperatorProbe(2);
            private static readonly IMcpFeatureContract _contract = new McpFeatureContractImpl();
            private static readonly McpVirtualBase _virtual = new McpVirtualDerived();

            public int SimpleCaller() => _target.PublicIncrement(1);

            public int OtherCaller() => _target.PublicIncrement(2);

            public int CallsBothOverloads() => _target.OverloadedAdd(1) + _target.OverloadedAdd(1, 2);

            public int CallsGeneric() => _target.GenericIdentity<int>(42);

            public int CallsStatic() => McpCallerProbeTarget.StaticGate(3);

            public int CallsMarked() => _target.MarkedMethod();

            public int CallsInterfaceViaInterfaceVar() => _via.InterfaceProbe(5);

            public int CallsInterfaceImplDirect() => _impl.InterfaceProbe(7);

            public int CallsExtension() => 9.ToProbeDoubled();

            public int ReadsFields() => _target.InstanceField + McpCallerProbeTarget.StaticField;

            public void WritesFields()
            {
                _target.InstanceField = _struct.Value;
                McpCallerProbeTarget.StaticField = _record.Value;
            }

            public int CallsDelegate() => _delegate(4);

            public string UsesMetadataRefs()
            {
                Type marker = typeof(McpMetadataOnlyTarget);
                return marker.Name + nameof(McpMetadataOnlyTarget) + nameof(McpMetadataOnlyTarget.MetadataMethod);
            }

            public int UsesCurrentModelFeatureShapes()
            {
                int McpLocalProbe(int value) => value + 1;
                Func<int, int> lambda = value => _target.PublicIncrement(value);
                _indexer[0] = 9;
                McpOperatorProbe combined = _operatorLeft + _operatorRight;
                int converted = combined;
                McpFeatureEnum enumMember = McpFeatureEnum.FeatureAlpha;
                McpPartialProbe partial = new McpPartialProbe();
                return McpLocalProbe(1)
                    + lambda(2)
                    + _indexer[0]
                    + converted
                    + (int)enumMember
                    + partial.PartA()
                    + partial.PartB()
                    + _contract.ContractProbe()
                    + _virtual.VirtualProbe();
            }

            public void WritesProperty()
            {
                McpCallerProbeTarget.ResolverProperty = () => 11;
            }

            public void SubscribesAndRaises()
            {
                _target.ProbeCompleted += (_, _) => { };
                _target.RaiseProbeCompleted();
            }

            public int CallsPrivateHelperWrapper() => _target.CallsPrivateHelper(10);

            public async System.Threading.Tasks.Task<int> CallsAsync() => await new McpAsyncProbe().AsyncProbe(3);

            public int CallsExplicitImpl()
            {
                IMcpProbeService viaInterfaceOnly = new McpExplicitImpl();
                return viaInterfaceOnly.InterfaceProbe(9);
            }

            public int CallsNested() => new McpOuterProbe.Nested().NestedMethod();

            public int CallsGenericType() => new McpGenericProbe<int>().Echo(7);

            public int CallsCtorChain() => new McpCallerProbeTarget(42).PublicIncrement(0);

            public int CallsHidden()
            {
                var hide = new McpHidingDerived();
                int viaDerived = hide.VirtualProbe();
                McpVirtualBase asBase = hide;
                return viaDerived + asBase.VirtualProbe();
            }

            public async System.Threading.Tasks.Task<int> CallsAwaitForeach()
            {
                int sum = 0;
                await foreach (int item in new McpAsyncEnumerableProbe().EnumerateAsync())
                {
                    sum += item;
                }

                return sum;
            }

            public async System.Threading.Tasks.Task CallsAwaitUsing()
            {
                await using (var probe = new McpAsyncDisposableProbe())
                {
                    await System.Threading.Tasks.Task.Yield();
                }
            }
        }
        """;

    private const string FixtureGeneratedSource =
        """
        // <auto-generated/>
        namespace SchemaStudio.SemanticModel.Tests;

        public sealed class McpGeneratedProbe
        {
            public int GeneratedMethod() => 1;
        }
        """;

    private sealed record MatrixCheck(string Name, string StableKey, int? ExpectedCallers, int? ExpectedReferences);

    private sealed record MatrixResult(
        MatrixCheck Check,
        RoslynMatrixCounts? Roslyn,
        IReadOnlyList<SolutionIndexReference>? ActualCallers,
        IReadOnlyList<SolutionIndexReference>? ActualReferences)
    {
        public bool Passed =>
            (Check.ExpectedCallers is null || ActualCallers?.Count == Check.ExpectedCallers)
            && (Check.ExpectedReferences is null || ActualReferences?.Count == Check.ExpectedReferences)
            && (Check.ExpectedCallers is null || Roslyn?.CallerCount == Check.ExpectedCallers)
            && (Check.ExpectedReferences is null || Roslyn?.ReferenceCount == Check.ExpectedReferences);
    }

    private sealed record RoslynMatrixCounts(bool TargetResolved, int CallerCount, int ReferenceCount);

    private sealed record ModelFeatureProbeResult(
        string Name,
        bool RoslynPresent,
        bool MonitorIndexed,
        bool ExpectedMonitorIndexed,
        string Note);

    private sealed record ExpectedCaller(string RelativePath, int Line, int Column, string? CallerName, string Snippet);

    private sealed record Comparison(
        SolutionIndexSymbol Target,
        IReadOnlyList<ExpectedCaller> Expected,
        IReadOnlyList<SolutionIndexReference> Actual,
        IReadOnlyList<ExpectedCaller> Missing,
        IReadOnlyList<SolutionIndexReference> Unexpected);
}
