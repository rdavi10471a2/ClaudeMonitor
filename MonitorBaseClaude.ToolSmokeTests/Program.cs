using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using MonitorBaseClaude.AI;
using MonitorBaseClaude.McpServer;
using MonitorBaseClaude.Services;

[module: AIFileContext("Program.cs", "Single-file console smoke harness for monitor tool integration checks.")]
[module: FileVersion("1.2")]

namespace MonitorBaseClaude.ToolSmokeTests;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Contains("--dbv2-index-callers", StringComparer.OrdinalIgnoreCase))
        {
            return RunIndexedCallerSmoke(
                "dbv2-index-callers",
                null,
                IsRepositoryOrDiscoveryMember,
                requireDbv2KnownCallerChecks: true);
        }

        if (args.Contains("--dbv2-index-callers-all", StringComparer.OrdinalIgnoreCase))
        {
            return RunIndexedCallerSmoke(
                "dbv2-index-callers-all",
                null,
                IsIndexedCallable,
                requireDbv2KnownCallerChecks: true);
        }

        if (args.Contains("--webviewer-index-callers-all", StringComparer.OrdinalIgnoreCase))
        {
            return RunIndexedCallerSmoke(
                "webviewer-index-callers-all",
                @"C:\SchemaStudioWebViewer\SchemaStudioWebViewer.sln",
                IsIndexedCallable,
                requireDbv2KnownCallerChecks: false);
        }

        Console.WriteLine("MonitorBaseClaude tool smoke tests");
        Console.WriteLine();
        Console.WriteLine("Available modes:");
        Console.WriteLine("  --dbv2-index-callers        Cross-check repository/discovery callers.");
        Console.WriteLine("  --dbv2-index-callers-all    Cross-check every indexed method/constructor in DBV2.");
        Console.WriteLine("  --webviewer-index-callers-all    Cross-check every indexed method/constructor in C:\\SchemaStudioWebViewer.");
        return 2;
    }

    private static int RunIndexedCallerSmoke(
        string modeName,
        string? solutionPathOverride,
        Func<SolutionIndexSymbol, bool> targetPredicate,
        bool requireDbv2KnownCallerChecks)
    {
        MonitorServerSettings settings = MonitorServerSettings.Load();
        string watchedSolutionPath = solutionPathOverride ?? settings.WatchedSolutionPath;
        string observedRoot = Path.GetDirectoryName(watchedSolutionPath)
            ?? throw new InvalidOperationException("Watched solution has no containing folder.");
        string runRoot = Path.Combine(
            settings.UiRoot,
            "Working",
            "History",
            "ToolSmokeTests",
            DateTime.Now.ToString("yyyyMMdd_HHmmss"),
            modeName);
        Directory.CreateDirectory(runRoot);

        if (!File.Exists(watchedSolutionPath))
        {
            Console.WriteLine($"Watched solution not found: {watchedSolutionPath}");
            return 1;
        }

        Console.WriteLine("MonitorBaseClaude indexed caller smoke");
        Console.WriteLine($"Mode: {modeName}");
        Console.WriteLine($"Watched solution: {watchedSolutionPath}");
        Console.WriteLine($"Log root: {runRoot}");
        Console.WriteLine();

        SolutionIndexService indexService = new(settings.UiRoot, watchedSolutionPath);
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

        bool knownCallersPassed = !requireDbv2KnownCallerChecks || KnownCallerChecksPass(indexService);
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
            # Indexed Caller Smoke

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

    private sealed record ExpectedCaller(string RelativePath, int Line, int Column, string? CallerName, string Snippet);

    private sealed record Comparison(
        SolutionIndexSymbol Target,
        IReadOnlyList<ExpectedCaller> Expected,
        IReadOnlyList<SolutionIndexReference> Actual,
        IReadOnlyList<ExpectedCaller> Missing,
        IReadOnlyList<SolutionIndexReference> Unexpected);
}
