using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using ModelContextProtocol.Server;
using MonitorBaseClaude.Services;

namespace MonitorBaseClaude.McpServer;

public sealed partial class MonitorWorkflowService
{
    private const string NewFileOriginalHash = "<new-file>";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions SourceMapResponseJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly SyntaxAnnotation FormatAnnotation = new("MonitorBaseClaudeFormat");

    private static readonly string[] WinMergeCandidates =
    [
        @"C:\Program Files\WinMerge\WinMergeU.exe",
        @"C:\Program Files (x86)\WinMerge\WinMergeU.exe"
    ];

    private readonly MonitorServerSettings settings;
    private readonly SolutionIndexService? solutionIndexService;
    private readonly Dictionary<string, MonitorOverlayValidationResult> overlayValidationCache = new(StringComparer.Ordinal);

    public MonitorWorkflowService(MonitorServerSettings settings)
        : this(settings, null)
    {
    }

    public MonitorWorkflowService(MonitorServerSettings settings, SolutionIndexService? solutionIndexService)
    {
        this.settings = settings;
        this.solutionIndexService = solutionIndexService;
    }

    public MonitorFileRefreshResult RefreshFile(string sourceFilePath)
    {
        return RefreshFile(sourceFilePath, recoverDirtyUnexpected: true);
    }

    private MonitorFileRefreshResult RefreshFile(string sourceFilePath, bool recoverDirtyUnexpected)
    {
        MonitorFileContext context = ResolveFileContext(sourceFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(context.WorkingFilePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(context.RefreshStatePath)!);
        File.Copy(context.SourceFilePath, context.WorkingFilePath, overwrite: true);
        ClearCandidateState(context);
        if (recoverDirtyUnexpected)
        {
            RecoverBlockedDirtyUnexpectedRecords(context.SourceFilePath);
        }

        MonitorRefreshState state = new(
            context.SourceFilePath,
            File.GetLastWriteTimeUtc(context.SourceFilePath),
            new FileInfo(context.SourceFilePath).Length,
            DateTimeOffset.UtcNow);
        File.WriteAllText(context.RefreshStatePath, JsonSerializer.Serialize(state, JsonOptions));

        return new MonitorFileRefreshResult(
            "refreshed",
            context.SourceFilePath,
            context.WatchedProjectFolder,
            context.ObservedRootKey,
            context.RelativeSourcePath,
            context.WorkingFilePath,
            context.RefreshStatePath);
    }

    public MonitorFileCompareResult CompareFile(string sourceFilePath, string? ledgerSummary = null, bool refreshIfMissing = true)
    {
        MonitorFileContext context = ResolveFileContext(sourceFilePath);
        bool refreshed = false;
        if (!File.Exists(context.WorkingFilePath))
        {
            if (!refreshIfMissing)
            {
                return MonitorFileCompareResult.NotLaunched(
                    "working-copy-missing",
                    context.SourceFilePath,
                    context.WorkingFilePath,
                    string.Empty,
                    "Working copy is missing. Run refresh_file first.");
            }

            RefreshFile(context.SourceFilePath, recoverDirtyUnexpected: false);
            refreshed = true;
        }

        if (!IsRefreshStateCurrent(context.RefreshStatePath, context.SourceFilePath))
        {
            return MonitorFileCompareResult.NotLaunched(
                "refresh-state-stale",
                context.SourceFilePath,
                context.WorkingFilePath,
                string.Empty,
                "Working copy refresh state is stale. Run refresh_file before compare_file.");
        }

        if (FilesAreIdentical(context.SourceFilePath, context.WorkingFilePath))
        {
            return MonitorFileCompareResult.NotLaunched(
                refreshed ? "refreshed-identical" : "identical",
                context.SourceFilePath,
                context.WorkingFilePath,
                string.Empty,
                "No differences found between source and Working copy.",
                refreshed);
        }

        string? winMergePath = ResolveWinMergePath();
        if (winMergePath is null)
        {
            return MonitorFileCompareResult.NotLaunched(
                "winmerge-not-found",
                context.SourceFilePath,
                context.WorkingFilePath,
                string.Empty,
                "WinMerge was not found. Install WinMerge or add it to the standard Program Files path.");
        }

        string proposedFilePath = CreateProposedSnapshot(context, ledgerSummary);
        DiffLaunchResult launchResult = LaunchWinMergeDetached(winMergePath, context.SourceFilePath, proposedFilePath);

        return new MonitorFileCompareResult(
            launchResult.ProcessId is null ? "launch-failed" : "winmerge-launched",
            context.SourceFilePath,
            context.WorkingFilePath,
            proposedFilePath,
            launchResult.ProcessId,
            winMergePath,
            launchResult.Arguments,
            refreshed);
    }

    public MonitorWorkflowStatus GetWorkflowStatus()
    {
        string watchedProjectFolder = Path.GetDirectoryName(settings.WatchedSolutionPath) ?? string.Empty;
        return new MonitorWorkflowStatus(
            settings.UiRoot,
            settings.McpServerRoot,
            settings.LegacyMonitorRoot,
            settings.WatchedSolutionPath,
            watchedProjectFolder,
            Path.Combine(settings.UiRoot, "Working"),
            ResolveWinMergePath(),
            File.Exists(settings.WatchedSolutionPath),
            Directory.Exists(watchedProjectFolder));
    }

    public MonitorFileReadResult GetFile(string sourceFilePath)
    {
        MonitorFileContext context = ResolveFileContext(sourceFilePath);
        string text = File.ReadAllText(context.SourceFilePath);

        return new MonitorFileReadResult(
            context.SourceFilePath,
            context.WatchedProjectFolder,
            context.RelativeSourcePath,
            text,
            text.Length);
    }

    public MonitorFileHashInfo GetFileHashInfo(string sourceFilePath)
    {
        MonitorFileContext context = ResolveFileContext(sourceFilePath);
        FileInfo info = new(context.SourceFilePath);
        return new MonitorFileHashInfo(
            context.SourceFilePath,
            context.WatchedProjectFolder,
            context.RelativeSourcePath,
            ComputeSha256(context.SourceFilePath),
            info.Length,
            info.LastWriteTimeUtc);
    }

    public MonitorFileOutlineResult GetFileOutline(string sourceFilePath)
    {
        MonitorFileContext context = ResolveFileContext(sourceFilePath);
        if (!context.SourceFilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return new MonitorFileOutlineResult(context.SourceFilePath, context.RelativeSourcePath, []);
        }

        SyntaxTree tree = CSharpSyntaxTree.ParseText(File.ReadAllText(context.SourceFilePath), path: context.SourceFilePath);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
        return new MonitorFileOutlineResult(
            context.SourceFilePath,
            context.RelativeSourcePath,
            root.DescendantNodes()
                .OfType<MemberDeclarationSyntax>()
                .Where(IsOutlineMember)
                .Select(member => ToSymbolOutline(tree, member))
                .OrderBy(symbol => symbol.StartLine)
                .ToArray());
    }

    public MonitorSourceMapResult GetSourceMap(string? path = null, string scope = "auto", string mode = "auto", string? namespaceName = null)
    {
        string watchedProjectFolder = Path.GetDirectoryName(settings.WatchedSolutionPath)
            ?? throw new InvalidOperationException("Watched solution path does not have a containing folder.");
        string observedRoot = TrimDirectorySeparator(Path.GetFullPath(watchedProjectFolder));
        string? requestedPath = string.IsNullOrWhiteSpace(path) ? null : path;
        string? requestedNamespace = string.IsNullOrWhiteSpace(namespaceName) ? null : namespaceName.Trim();
        string normalizedScope = ResolveEffectiveSourceMapScope(observedRoot, requestedPath, NormalizeSourceMapScope(scope));
        if (requestedNamespace is not null && !normalizedScope.Equals("namespace", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("namespaceName is only valid when source map scope is namespace.");
        }

        string normalizedMode = ResolveEffectiveSourceMapMode(normalizedScope, mode);
        string? effectiveNamespace = ResolveRequestedNamespace(normalizedScope, requestedPath, requestedNamespace);
        string[] sourceFiles = ResolveSourceMapFiles(observedRoot, requestedPath, normalizedScope).ToArray();
        MonitorSourceMapFile[] files = sourceFiles
            .Select(path => BuildSourceMapFile(observedRoot, path))
            .Where(file => SourceMapFileMatchesNamespace(file, normalizedScope, effectiveNamespace))
            .Select(file => ShapeSourceMapFile(file, normalizedMode))
            .OrderBy(file => file.RelativeSourcePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string modePurpose = GetSourceMapModePurpose(normalizedMode);
        string watchedProjectAlias = new DirectoryInfo(observedRoot).Name;
        string? sourceRoot = normalizedMode.Equals("full", StringComparison.OrdinalIgnoreCase) ? observedRoot : null;
        int budgetLimit = GetSourceMapBudgetLimit(normalizedMode);
        MonitorSourceMapNextCall[] nextCalls = BuildSourceMapNextCalls(files, normalizedMode)?.ToArray() ?? [];

        MonitorSourceMapResult fullResult = new(
            normalizedScope,
            normalizedMode,
            modePurpose,
            requestedPath,
            effectiveNamespace,
            watchedProjectAlias,
            sourceRoot,
            files.Length,
            files.Sum(file => file.Symbols.Count),
            0,
            budgetLimit,
            false,
            null,
            nextCalls,
            files);

        long estimatedTokenProxy = EstimateSourceMapTokenProxy(fullResult);
        if (estimatedTokenProxy <= budgetLimit)
        {
            return fullResult with { EstimatedTokenProxy = estimatedTokenProxy };
        }

        IReadOnlyList<MonitorSourceMapNarrowingSuggestion> suggestions = BuildSourceMapNarrowingSuggestions(files);
        return new MonitorSourceMapResult(
            normalizedScope,
            normalizedMode,
            modePurpose,
            requestedPath,
            effectiveNamespace,
            watchedProjectAlias,
            sourceRoot,
            0,
            0,
            estimatedTokenProxy,
            budgetLimit,
            true,
            suggestions,
            nextCalls,
            []);
    }

    public MonitorSymbolReadResult GetSymbol(string sourceFilePath, string? symbolName = null, string? symbolSelectorJson = null)
    {
        MonitorFileContext context = ResolveCSharpFileContext(sourceFilePath, "get_symbol");

        SyntaxTree tree = CSharpSyntaxTree.ParseText(File.ReadAllText(context.SourceFilePath), path: context.SourceFilePath);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
        MemberDeclarationSyntax member;
        if (!string.IsNullOrWhiteSpace(symbolSelectorJson))
        {
            MonitorSymbolSelector selector = ParseSymbolSelector(symbolSelectorJson);
            member = ResolveSingleMember(root, selector, context.RelativeSourcePath);
        }
        else if (!string.IsNullOrWhiteSpace(symbolName))
        {
            member = ResolveSymbolByName(root, symbolName, context.RelativeSourcePath);
        }
        else
        {
            throw new ArgumentException("Provide symbolName or symbolSelectorJson.");
        }

        FileLinePositionSpan span = tree.GetLineSpan(member.Span);
        return new MonitorSymbolReadResult(
            context.SourceFilePath,
            context.RelativeSourcePath,
            SymbolKind(member),
            SymbolName(member),
            span.StartLinePosition.Line + 1,
            span.EndLinePosition.Line + 1,
            member.ToFullString());
    }

    private static MemberDeclarationSyntax ResolveSymbolByName(CompilationUnitSyntax root, string symbolName, string relativeSourcePath)
    {
        MemberDeclarationSyntax[] matches = root
            .DescendantNodes()
            .OfType<MemberDeclarationSyntax>()
            .Where(IsOutlineMember)
            .Where(member => SymbolName(member).Equals(symbolName, StringComparison.Ordinal))
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"Symbol '{symbolName}' was not found in {relativeSourcePath}."),
            _ => throw new InvalidOperationException($"Symbol name '{symbolName}' is ambiguous in {relativeSourcePath}. Use symbolSelectorJson from get_source_map.")
        };
    }

    public MonitorSessionStagedRecordsResult ListSessionStagedRecords(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("sessionId is required.", nameof(sessionId));
        }

        MonitorSessionStagedRecordSummary[] records = ReadSessionStagedRecordEntries(sessionId)
            .Select(item => new MonitorSessionStagedRecordSummary(
                item.Record.RecordId,
                item.Record.SessionId,
                item.Record.RelativeSourcePath,
                item.Record.SourceFilePath,
                item.Record.Operation,
                item.Record.CreatedAt,
                item.Record.QueueStatus,
                item.Record.ServerDerivedMetadata.StagedFilePath,
                item.Record.OriginalHash,
                item.Record.StagedHash,
                item.Record.SyntaxValidation.HasErrors,
                item.Record.OverlayValidation.Status,
                item.Record.OverlayValidation.HasErrors,
                item.Record.OverlayValidation.OverlayFileCount,
                item.RecordPath,
                item.Record.NewFileReviewBaselinePath,
                item.Record.ManifestJson))
            .ToArray();

        return new MonitorSessionStagedRecordsResult(sessionId, records.Length, records);
    }

    public MonitorCandidateEditResult SubmitFile(string sourceFilePath, string content, string? sessionId = null, string? manifestJson = null)
    {
        MonitorFileContext context = ResolveFileContext(sourceFilePath, allowMissing: true);
        return WriteCandidateFile("submit_file", context, content, sessionId, manifestJson);
    }

    public RazorCompanionSplitStageResult SplitRazorCodeToCompanion(
        string sourceFilePath,
        string? namespaceName = null,
        bool leaveEmptyCodeBlock = false,
        string? sessionId = null,
        string? manifestJson = null)
    {
        MonitorFileContext sourceContext = ResolveFileContext(sourceFilePath, allowMissing: false);
        if (!sourceContext.SourceFilePath.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
            && !sourceContext.SourceFilePath.EndsWith(".razor.cs", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("split_razor_code_to_companion supports .razor and legacy hybrid .razor.cs files only.");
        }

        string effectiveSessionId = string.IsNullOrWhiteSpace(sessionId)
            ? $"razor-split-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}"
            : sessionId;
        RazorCompanionSplitResult split = RazorCompanionSplitter.Split(
            sourceContext.WatchedProjectFolder,
            sourceContext.RelativeSourcePath,
            File.ReadAllText(sourceContext.SourceFilePath),
            new RazorCompanionSplitOptions(leaveEmptyCodeBlock, namespaceName));

        MonitorFileContext razorContext = ResolveFileContext(split.RazorRelativePath, allowMissing: true);
        MonitorFileContext companionContext = ResolveFileContext(split.CompanionRelativePath, allowMissing: true);
        EnsureNoCandidateInProgress(razorContext, "Razor markup output");
        EnsureNoCandidateInProgress(companionContext, "Razor companion output");

        string combinedManifest = string.Join(
            Environment.NewLine,
            new[]
            {
                manifestJson,
                $"splitRazorSource={sourceContext.RelativeSourcePath}",
                $"splitRazorMarkup={split.RazorRelativePath}",
                $"splitRazorCompanion={split.CompanionRelativePath}"
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

        MonitorCandidateEditResult razorCandidate = WriteCandidateFile(
            "split_razor_code_to_companion",
            razorContext,
            split.OriginalRazorText,
            effectiveSessionId,
            combinedManifest);
        MonitorFileSubmitResult razorStage = StageCandidateForReview(
            split.RazorRelativePath,
            effectiveSessionId,
            combinedManifest);

        MonitorCandidateEditResult companionCandidate = WriteCandidateFile(
            "split_razor_code_to_companion",
            companionContext,
            split.CompanionCsText,
            effectiveSessionId,
            combinedManifest);
        MonitorFileSubmitResult companionStage = StageCandidateForReview(
            split.CompanionRelativePath,
            effectiveSessionId,
            combinedManifest);

        return new RazorCompanionSplitStageResult(
            "staged",
            effectiveSessionId,
            sourceContext.SourceFilePath,
            split.RazorRelativePath,
            split.CompanionRelativePath,
            razorCandidate,
            razorStage,
            companionCandidate,
            companionStage,
            split.Warnings);
    }

    public MonitorCandidateEditResult ReplaceSpanInFile(
        string sourceFilePath,
        int startLine,
        int startColumn,
        int endLine,
        int endColumn,
        string newText,
        string? expectedFileHash = null,
        string? expectedOldTextHash = null,
        string? expectedOldText = null,
        string? sessionId = null,
        string? manifestJson = null)
    {
        MonitorFileContext context = ResolveFileContext(sourceFilePath, allowMissing: false);
        try
        {
            string editBasePath = ResolveCandidateEditBasePath(context, sessionId);
            string baseText = File.ReadAllText(editBasePath);
            string baseHash = ComputeSha256(editBasePath);
            if (!string.IsNullOrWhiteSpace(expectedFileHash)
                && !expectedFileHash.Equals(baseHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"replace_span_in_file hash mismatch for {context.RelativeSourcePath}: expected {expectedFileHash}, actual {baseHash}.");
            }

            int startOffset = GetOffsetFromLineColumn(baseText, startLine, startColumn, nameof(startLine), nameof(startColumn));
            int endOffset = GetOffsetFromLineColumn(baseText, endLine, endColumn, nameof(endLine), nameof(endColumn));
            if (endOffset < startOffset)
            {
                throw new InvalidOperationException("replace_span_in_file end position must be greater than or equal to start position.");
            }

            string oldText = baseText[startOffset..endOffset];
            if (expectedOldText is not null && !oldText.Equals(expectedOldText, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"replace_span_in_file old text mismatch for {context.RelativeSourcePath} at {startLine}:{startColumn}-{endLine}:{endColumn}.");
            }

            if (!string.IsNullOrWhiteSpace(expectedOldTextHash))
            {
                string actualOldTextHash = ComputeSha256Text(oldText);
                if (!expectedOldTextHash.Equals(actualOldTextHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"replace_span_in_file old text hash mismatch for {context.RelativeSourcePath}: expected {expectedOldTextHash}, actual {actualOldTextHash}.");
                }
            }

            string updatedText = baseText[..startOffset] + newText + baseText[endOffset..];
            return WriteCandidateFile("replace_span_in_file", context, updatedText, sessionId, manifestJson);
        }
        catch (Exception ex) when (IsStructuredCandidateError(ex))
        {
            return CreateCandidateEditErrorResult(context, ClassifyCandidateError(ex), ex.Message);
        }
    }

    public MonitorTextSpanResult FindTextSpan(
        string sourceFilePath,
        string findText,
        int occurrenceIndex = 0,
        string? expectedFileHash = null,
        string? sessionId = null)
    {
        if (string.IsNullOrEmpty(findText))
        {
            throw new ArgumentException("findText must not be empty.", nameof(findText));
        }

        if (occurrenceIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(occurrenceIndex), "Occurrence index is 0-based.");
        }

        MonitorFileContext context = ResolveFileContext(sourceFilePath, allowMissing: false);
        string editBasePath = ResolveCandidateEditBasePath(context, sessionId);
        string baseText = File.ReadAllText(editBasePath);
        string baseHash = ComputeSha256(editBasePath);
        if (!string.IsNullOrWhiteSpace(expectedFileHash)
            && !expectedFileHash.Equals(baseHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"find_text_span hash mismatch for {context.RelativeSourcePath}: expected {expectedFileHash}, actual {baseHash}.");
        }

        TextMatchSet matches = FindTextMatches(baseText, findText, occurrenceIndex);
        return CreateTextSpanResult(context, editBasePath, baseHash, findText, matches);
    }

    public MonitorCandidateEditResult ReplaceTextInFile(
        string sourceFilePath,
        string oldText,
        string newText,
        int expectedMatches = 1,
        int occurrenceIndex = 0,
        string? expectedFileHash = null,
        string? expectedOldTextHash = null,
        string? sessionId = null,
        string? manifestJson = null)
    {
        if (string.IsNullOrEmpty(oldText))
        {
            throw new ArgumentException("oldText must not be empty.", nameof(oldText));
        }

        if (expectedMatches < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedMatches), "Expected matches must be at least 1.");
        }

        if (occurrenceIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(occurrenceIndex), "Occurrence index is 0-based.");
        }

        MonitorFileContext context = ResolveFileContext(sourceFilePath, allowMissing: false);
        try
        {
            string editBasePath = ResolveCandidateEditBasePath(context, sessionId);
            string baseText = File.ReadAllText(editBasePath);
            string baseHash = ComputeSha256(editBasePath);
            if (!string.IsNullOrWhiteSpace(expectedFileHash)
                && !expectedFileHash.Equals(baseHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"replace_text_in_file hash mismatch for {context.RelativeSourcePath}: expected {expectedFileHash}, actual {baseHash}.");
            }

            if (!string.IsNullOrWhiteSpace(expectedOldTextHash))
            {
                string actualOldTextHash = ComputeSha256Text(oldText);
                if (!expectedOldTextHash.Equals(actualOldTextHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"replace_text_in_file old text hash mismatch for {context.RelativeSourcePath}: expected {expectedOldTextHash}, actual {actualOldTextHash}.");
                }
            }

            TextMatchSet matches = FindTextMatches(baseText, oldText, occurrenceIndex);
            if (matches.Count != expectedMatches)
            {
                throw new InvalidOperationException(
                    $"replace_text_in_file expected {expectedMatches} match(es) in {context.RelativeSourcePath}, found {matches.Count}.");
            }

            int startOffset = matches.SelectedOffset;
            int endOffset = startOffset + oldText.Length;
            string updatedText = baseText[..startOffset] + newText + baseText[endOffset..];
            return WriteCandidateFile("replace_text_in_file", context, updatedText, sessionId, manifestJson);
        }
        catch (Exception ex) when (IsStructuredCandidateError(ex))
        {
            return CreateCandidateEditErrorResult(context, ClassifyCandidateError(ex), ex.Message);
        }
    }

    public MonitorCandidateEditResult AddSymbol(
        string sourceFilePath,
        string containingType,
        string symbolType,
        string code,
        string? afterSymbol = null,
        string? sessionId = null,
        string? manifestJson = null)
    {
        MonitorFileContext context = ResolveCSharpFileContext(sourceFilePath, "add_symbol");
        string editBasePath = ResolveCandidateEditBasePath(context, sessionId);
        CompilationUnitSyntax root = ParseCompilationUnit(context, editBasePath);
        TypeDeclarationSyntax type = ResolveSingleType(root, containingType);
        MemberDeclarationSyntax newMember = ParseMemberDeclaration(code, "new candidate symbol");
        if (!SymbolKind(newMember).Equals(symbolType, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"New symbol kind '{SymbolKind(newMember)}' does not match requested kind '{symbolType}'.");
        }

        SyntaxList<MemberDeclarationSyntax> members = type.Members;
        int insertIndex = members.Count;
        if (!string.IsNullOrWhiteSpace(afterSymbol))
        {
            int afterIndex = IndexOfMember(members, afterSymbol);
            if (afterIndex < 0)
            {
                throw new InvalidOperationException($"afterSymbol '{afterSymbol}' was not found in type '{containingType}'.");
            }

            insertIndex = afterIndex + 1;
        }

        newMember = ApplyInsertionTrivia(newMember, type, insertIndex)
            .WithAdditionalAnnotations(FormatAnnotation);
        TypeDeclarationSyntax newType = type.WithMembers(members.Insert(insertIndex, newMember));
        CompilationUnitSyntax newRoot = root.ReplaceNode(type, newType);
        newRoot = FormatAnnotatedNodes(newRoot);
        return WriteCandidateFile("add_symbol", context, newRoot.ToFullString(), sessionId, manifestJson);
    }

    public MonitorCandidateEditResult SubmitSymbol(string sourceFilePath, string symbolSelectorJson, string code, string? sessionId = null, string? manifestJson = null)
    {
        MonitorFileContext context = ResolveCSharpFileContext(sourceFilePath, "submit_symbol");
        string editBasePath = ResolveCandidateEditBasePath(context, sessionId);
        CompilationUnitSyntax root = ParseCompilationUnit(context, editBasePath);
        MonitorSymbolSelector selector = ParseSymbolSelector(symbolSelectorJson);
        MemberDeclarationSyntax target = ResolveSingleMember(root, selector, context.RelativeSourcePath);
        MemberDeclarationSyntax replacement = ParseMemberDeclaration(code, "replacement symbol")
            .WithLeadingTrivia(target.GetLeadingTrivia())
            .WithTrailingTrivia(target.GetTrailingTrivia())
            .WithAdditionalAnnotations(FormatAnnotation);
        CompilationUnitSyntax newRoot = root.ReplaceNode(target, replacement);
        newRoot = FormatAnnotatedNodes(newRoot);
        return WriteCandidateFile("submit_symbol", context, newRoot.ToFullString(), sessionId, manifestJson);
    }

    public MonitorCandidateEditResult AddUsing(string sourceFilePath, string @namespace, string? sessionId = null, string? manifestJson = null)
    {
        MonitorFileContext context = ResolveCSharpFileContext(sourceFilePath, "add_using");
        string editBasePath = ResolveCandidateEditBasePath(context, sessionId);
        CompilationUnitSyntax root = ParseCompilationUnit(context, editBasePath);
        if (root.Usings.Any(usingDirective => string.Equals(usingDirective.Name?.ToString(), @namespace, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Using '{@namespace}' already exists in {context.RelativeSourcePath}.");
        }

        UsingDirectiveSyntax newUsing = SyntaxFactory.ParseCompilationUnit($"using {@namespace};{Environment.NewLine}")
            .Usings
            .Single();
        UsingDirectiveSyntax[] usings = root.Usings
            .Add(newUsing)
            .OrderBy(usingDirective => usingDirective.Name?.ToString(), StringComparer.Ordinal)
            .ToArray();
        CompilationUnitSyntax newRoot = root.WithUsings(SyntaxFactory.List(usings));
        return WriteCandidateFile("add_using", context, newRoot.ToFullString(), sessionId, manifestJson);
    }

    public MonitorCandidateEditResult RemoveUsing(string sourceFilePath, string @namespace, string? sessionId = null, string? manifestJson = null)
    {
        MonitorFileContext context = ResolveCSharpFileContext(sourceFilePath, "remove_using");
        string editBasePath = ResolveCandidateEditBasePath(context, sessionId);
        CompilationUnitSyntax root = ParseCompilationUnit(context, editBasePath);
        UsingDirectiveSyntax? target = root.Usings.FirstOrDefault(usingDirective => string.Equals(usingDirective.Name?.ToString(), @namespace, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Using '{@namespace}' was not found in {context.RelativeSourcePath}.");
        CompilationUnitSyntax newRoot = root.RemoveNode(target, SyntaxRemoveOptions.KeepNoTrivia)
            ?? throw new InvalidOperationException($"Using '{@namespace}' could not be removed from {context.RelativeSourcePath}.");
        return WriteCandidateFile("remove_using", context, newRoot.ToFullString(), sessionId, manifestJson);
    }

    public MonitorCandidateEditResult SetTypePartial(string sourceFilePath, string containingType, bool isPartial = true, string? sessionId = null, string? manifestJson = null)
    {
        MonitorFileContext context = ResolveCSharpFileContext(sourceFilePath, "set_type_partial");
        string editBasePath = ResolveCandidateEditBasePath(context, sessionId);
        CompilationUnitSyntax root = ParseCompilationUnit(context, editBasePath);
        TypeDeclarationSyntax type = ResolveSingleType(root, containingType);
        bool currentlyPartial = type.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword));
        if (currentlyPartial == isPartial)
        {
            return WriteCandidateFile("set_type_partial", context, root.ToFullString(), sessionId, manifestJson);
        }

        TypeDeclarationSyntax newType = isPartial
            ? type.WithModifiers(type.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.PartialKeyword).WithTrailingTrivia(SyntaxFactory.Space)))
            : type.WithModifiers(SyntaxFactory.TokenList(type.Modifiers.Where(modifier => !modifier.IsKind(SyntaxKind.PartialKeyword))));
        newType = newType.WithAdditionalAnnotations(FormatAnnotation);
        CompilationUnitSyntax newRoot = root.ReplaceNode(type, newType);
        newRoot = FormatAnnotatedNodes(newRoot);
        return WriteCandidateFile("set_type_partial", context, newRoot.ToFullString(), sessionId, manifestJson);
    }

    public MonitorCandidateEditResult AddField(string sourceFilePath, string containingType, string declaration, string? afterSymbol = null, string? sessionId = null, string? manifestJson = null)
    {
        return AddSymbol(sourceFilePath, containingType, "field", declaration, afterSymbol, sessionId, manifestJson);
    }

    public MonitorCandidateEditResult AddMethod(string sourceFilePath, string containingType, string declaration, string? afterSymbol = null, string? sessionId = null, string? manifestJson = null)
    {
        return AddSymbol(sourceFilePath, containingType, "method", declaration, afterSymbol, sessionId, manifestJson);
    }

    public MonitorFileSubmitResult StageCandidateForReview(string sourceFilePath, string? sessionId = null, string? manifestJson = null)
    {
        MonitorFileContext context = ResolveFileContext(sourceFilePath, allowMissing: true);
        try
        {
            CandidateEditState state = ReadCurrentCandidateState(context);
            EnsureCandidateBaselineIsCurrent(context, state);
            if (!File.Exists(context.WorkingFilePath))
            {
                throw new FileNotFoundException("Candidate Working file was not found. Create a candidate before staging for review.", context.WorkingFilePath);
            }

            string content = File.ReadAllText(context.WorkingFilePath);
            MonitorOverlayValidationResult overlayValidation = ValidateCandidateOverlayCompilation(context, context.WorkingFilePath);
            return StageFileReplacement("stage_candidate_for_review", context, content, sessionId, manifestJson, launchDiff: false, overlayValidation);
        }
        catch (Exception ex) when (IsStructuredCandidateError(ex))
        {
            return CreateStageErrorResult(context, ClassifyCandidateError(ex), ex.Message);
        }
    }

    public MonitorCandidateEditResult AddProperty(string sourceFilePath, string containingType, string declaration, string? afterSymbol = null, string? sessionId = null, string? manifestJson = null)
    {
        return AddSymbol(sourceFilePath, containingType, "property", declaration, afterSymbol, sessionId, manifestJson);
    }

    public MonitorCandidateEditResult AddConstructor(string sourceFilePath, string containingType, string declaration, string? afterSymbol = null, string? sessionId = null, string? manifestJson = null)
    {
        return AddSymbol(sourceFilePath, containingType, "constructor", declaration, afterSymbol, sessionId, manifestJson);
    }

    public MonitorCandidateEditResult AddNestedType(string sourceFilePath, string containingType, string declaration, string? afterSymbol = null, string? sessionId = null, string? manifestJson = null)
    {
        MemberDeclarationSyntax member = ParseMemberDeclaration(declaration, "new nested type");
        string kind = SymbolKind(member);
        if (kind is not ("class" or "struct" or "interface" or "record" or "enum"))
        {
            throw new InvalidOperationException($"Nested type declaration must be class, struct, interface, record, or enum. Actual kind: '{kind}'.");
        }

        return AddSymbol(sourceFilePath, containingType, kind, declaration, afterSymbol, sessionId, manifestJson);
    }

    public MonitorCandidateEditResult RemoveSymbol(string sourceFilePath, string symbolSelectorJson, string? sessionId = null, string? manifestJson = null)
    {
        MonitorFileContext context = ResolveCSharpFileContext(sourceFilePath, "remove_symbol");
        string editBasePath = ResolveCandidateEditBasePath(context, sessionId);
        CompilationUnitSyntax root = ParseCompilationUnit(context, editBasePath);
        MonitorSymbolSelector selector = ParseSymbolSelector(symbolSelectorJson);
        MemberDeclarationSyntax target = ResolveSingleMember(root, selector, context.RelativeSourcePath);
        CompilationUnitSyntax newRoot = root.RemoveNode(target, SyntaxRemoveOptions.KeepNoTrivia)
            ?? throw new InvalidOperationException($"Symbol '{selector.Name}' could not be removed from {context.RelativeSourcePath}.");
        return WriteCandidateFile("remove_symbol", context, newRoot.ToFullString(), sessionId, manifestJson);
    }

    private MonitorFileSubmitResult StageFileReplacement(
        string operation,
        MonitorFileContext context,
        string content,
        string? sessionId,
        string? manifestJson,
        bool launchDiff,
        MonitorOverlayValidationResult overlayValidation)
    {
        EnsureFileIsNotBlockedByDirtyUnexpected(context);
        MonitorSyntaxValidationResult validation = ValidateSyntaxIfCSharp(context.SourceFilePath, content);
        if (validation.HasErrors)
        {
            MonitorSyntaxDiagnostic first = validation.Diagnostics[0];
            throw new InvalidOperationException(
                $"C# syntax validation failed for {context.RelativeSourcePath} at line {first.Line}, column {first.Column}: {first.Id} {first.Message}");
        }

        string recordId = CreateStagedRecordId(operation, Path.GetFileNameWithoutExtension(context.SourceFilePath));
        string stagedPath = CreateStagedFile(context, content, recordId);
        SupersedePriorSameFileSessionRecords(sessionId, context);
        StagedEditRecord stagedRecord = CreateStagedEditRecord(
            recordId,
            sessionId,
            operation,
            context,
            stagedPath,
            manifestJson,
            content,
            validation,
            overlayValidation);
        bool isNoOp = !IsNewFileRecord(stagedRecord)
            && stagedRecord.OriginalHash.Equals(stagedRecord.StagedHash, StringComparison.OrdinalIgnoreCase);
        string stagedRecordPath = WriteStagedEditRecord(stagedRecord);
        string? diffToolPath = null;
        int? processId = null;
        string? diffArguments = null;

        if (launchDiff && !isNoOp)
        {
            diffToolPath = ResolveWinMergePath();
            if (diffToolPath is not null)
            {
                string reviewSourcePath = context.SourceFilePath;
                if (IsNewFileRecord(stagedRecord))
                {
                    reviewSourcePath = CreateNewFileReviewBaseline(stagedRecord);
                    stagedRecord = stagedRecord with { NewFileReviewBaselinePath = reviewSourcePath };
                    File.WriteAllText(stagedRecordPath, JsonSerializer.Serialize(stagedRecord, JsonOptions));
                }

                DiffLaunchResult launchResult = LaunchWinMergeDetached(
                    diffToolPath,
                    reviewSourcePath,
                    stagedPath,
                    context.SourceFilePath,
                    IsNewFileRecord(stagedRecord) ? "New File Baseline (Right)" : "Existing Source (Right)");
                processId = launchResult.ProcessId;
                diffArguments = launchResult.Arguments;
            }
        }

        return new MonitorFileSubmitResult(
            isNoOp ? "no-op-staged" : "staged",
            context.SourceFilePath,
            context.RelativeSourcePath,
            stagedPath,
            stagedRecord.RecordId,
            stagedRecordPath,
            stagedRecord.OriginalHash,
            stagedRecord.StagedHash,
            stagedRecord.ServerDerivedMetadata,
            validation,
            overlayValidation,
            launchDiff && !isNoOp,
            diffToolPath,
            diffArguments,
            processId);
    }

    private string ResolveEditBasePath(MonitorFileContext context, string? sessionId)
    {
        (StagedEditRecord Record, string RecordPath)? latest = FindLatestSameFileSessionRecord(sessionId, context);
        string? stagedPath = latest?.Record.ServerDerivedMetadata.StagedFilePath;
        return !string.IsNullOrWhiteSpace(stagedPath) && File.Exists(stagedPath)
            ? stagedPath
            : context.SourceFilePath;
    }

    private string ResolveCandidateEditBasePath(MonitorFileContext context, string? sessionId)
    {
        EnsureCandidateInitialized(context, sessionId);
        return context.WorkingFilePath;
    }

    private MonitorCandidateEditResult WriteCandidateFile(
        string operation,
        MonitorFileContext context,
        string content,
        string? sessionId,
        string? manifestJson)
    {
        CandidateEditState state = EnsureCandidateInitialized(context, sessionId);
        MonitorSyntaxValidationResult validation = ValidateSyntaxIfCSharp(context.SourceFilePath, content);
        if (validation.HasErrors)
        {
            MonitorSyntaxDiagnostic first = validation.Diagnostics[0];
            throw new InvalidOperationException(
                $"C# syntax validation failed for {context.RelativeSourcePath} at line {first.Line}, column {first.Column}: {first.Id} {first.Message}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(context.WorkingFilePath)!);
        TextFileShape sourceShape = File.Exists(context.SourceFilePath)
            ? DetectTextFileShape(context.SourceFilePath)
            : new TextFileShape(new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), Environment.NewLine);
        File.WriteAllText(context.WorkingFilePath, NormalizeLineEndings(content, sourceShape.NewLine), sourceShape.Encoding);
        MonitorOverlayValidationResult overlayValidation = ValidateCandidateOverlayCompilation(context, context.WorkingFilePath);

        CandidateEditState updatedState = state with
        {
            SessionId = sessionId,
            Operation = operation,
            ManifestJson = manifestJson,
            CandidateHash = ComputeSha256(context.WorkingFilePath),
            CandidateLength = new FileInfo(context.WorkingFilePath).Length,
            UpdatedAt = DateTimeOffset.UtcNow,
            OperationCount = state.OperationCount + 1
        };
        WriteCandidateState(context, updatedState);

        return new MonitorCandidateEditResult(
            "candidate-updated",
            context.SourceFilePath,
            context.WatchedProjectFolder,
            context.ObservedRootKey,
            context.RelativeSourcePath,
            context.WorkingFilePath,
            GetCandidateStatePath(context),
            updatedState.BaselineHash,
            updatedState.CandidateHash,
            updatedState.OperationCount,
            validation,
            overlayValidation);
    }

    private CandidateEditState EnsureCandidateInitialized(MonitorFileContext context, string? sessionId)
    {
        CandidateEditState? existing = TryReadCandidateState(context);
        if (existing is not null)
        {
            EnsureCandidateBaselineIsCurrent(context, existing);
            if (!File.Exists(context.WorkingFilePath))
            {
                throw new FileNotFoundException("Candidate state exists but the Working candidate file is missing.", context.WorkingFilePath);
            }

            return existing;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(context.WorkingFilePath)!);
        CandidateEditState state;
        if (File.Exists(context.SourceFilePath))
        {
            File.Copy(context.SourceFilePath, context.WorkingFilePath, overwrite: true);
            FileInfo sourceInfo = new(context.SourceFilePath);
            state = new CandidateEditState(
                sessionId,
                context.SourceFilePath,
                context.RelativeSourcePath,
                context.WorkingFilePath,
                ComputeSha256(context.SourceFilePath),
                sourceInfo.Length,
                sourceInfo.LastWriteTimeUtc,
                ComputeSha256(context.WorkingFilePath),
                new FileInfo(context.WorkingFilePath).Length,
                DateTimeOffset.UtcNow,
                "candidate-initialized",
                null,
                0);
        }
        else
        {
            state = new CandidateEditState(
                sessionId,
                context.SourceFilePath,
                context.RelativeSourcePath,
                context.WorkingFilePath,
                NewFileOriginalHash,
                0,
                DateTime.MinValue,
                NewFileOriginalHash,
                0,
                DateTimeOffset.UtcNow,
                "candidate-initialized-new-file",
                null,
                0);
        }

        WriteCandidateState(context, state);
        return state;
    }

    private CandidateEditState ReadCurrentCandidateState(MonitorFileContext context)
    {
        return TryReadCandidateState(context)
            ?? throw new FileNotFoundException("Candidate state was not found. Create a candidate before staging for review.", GetCandidateStatePath(context));
    }

    private static bool IsStructuredCandidateError(Exception ex)
    {
        return ex is FileNotFoundException
            || ex is InvalidOperationException invalidOperation
                && (invalidOperation.Message.StartsWith("candidate-baseline-stale:", StringComparison.Ordinal)
                    || invalidOperation.Message.Contains("hash mismatch", StringComparison.OrdinalIgnoreCase)
                    || invalidOperation.Message.Contains("old text mismatch", StringComparison.OrdinalIgnoreCase)
                    || invalidOperation.Message.Contains("old text hash mismatch", StringComparison.OrdinalIgnoreCase)
                    || invalidOperation.Message.Contains("occurrence", StringComparison.OrdinalIgnoreCase)
                    || invalidOperation.Message.Contains("expected ", StringComparison.OrdinalIgnoreCase)
                        && invalidOperation.Message.Contains("match(es)", StringComparison.OrdinalIgnoreCase));
    }

    private static string ClassifyCandidateError(Exception ex)
    {
        if (ex is FileNotFoundException fileNotFound
            && fileNotFound.Message.Contains("Candidate state was not found", StringComparison.OrdinalIgnoreCase))
        {
            return "no-active-candidate";
        }

        if (ex is FileNotFoundException fileNotFoundWorking
            && fileNotFoundWorking.Message.Contains("Working", StringComparison.OrdinalIgnoreCase))
        {
            return "candidate-working-missing";
        }

        if (ex.Message.StartsWith("candidate-baseline-stale:", StringComparison.Ordinal))
        {
            return "candidate-baseline-stale";
        }

        if (ex.Message.Contains("old text mismatch", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("old text hash mismatch", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("occurrence", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("match(es)", StringComparison.OrdinalIgnoreCase))
        {
            return "text-match-mismatch";
        }

        if (ex.Message.Contains("hash mismatch", StringComparison.OrdinalIgnoreCase))
        {
            return "hash-mismatch";
        }

        return "candidate-error";
    }

    private MonitorCandidateEditResult CreateCandidateEditErrorResult(MonitorFileContext context, string status, string message)
    {
        return new MonitorCandidateEditResult(
            status,
            context.SourceFilePath,
            context.WatchedProjectFolder,
            context.ObservedRootKey,
            context.RelativeSourcePath,
            context.WorkingFilePath,
            GetCandidateStatePath(context),
            string.Empty,
            File.Exists(context.WorkingFilePath) ? ComputeSha256(context.WorkingFilePath) : string.Empty,
            0,
            new MonitorSyntaxValidationResult(false, []),
            new MonitorOverlayValidationResult("not-run", false, 0, 0, []),
            status,
            message);
    }

    private MonitorFileSubmitResult CreateStageErrorResult(MonitorFileContext context, string status, string message)
    {
        return new MonitorFileSubmitResult(
            status,
            context.SourceFilePath,
            context.RelativeSourcePath,
            string.Empty,
            string.Empty,
            string.Empty,
            File.Exists(context.SourceFilePath) ? ComputeSha256(context.SourceFilePath) : string.Empty,
            File.Exists(context.WorkingFilePath) ? ComputeSha256(context.WorkingFilePath) : string.Empty,
            new StagedEditMetadata([], [], [], [], string.Empty),
            new MonitorSyntaxValidationResult(false, []),
            new MonitorOverlayValidationResult("not-run", false, 0, 0, []),
            false,
            null,
            message,
            null,
            status,
            message);
    }

    private CandidateEditState? TryReadCandidateState(MonitorFileContext context)
    {
        string path = GetCandidateStatePath(context);
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<CandidateEditState>(File.ReadAllText(path), JsonOptions);
    }

    private void WriteCandidateState(MonitorFileContext context, CandidateEditState state)
    {
        string path = GetCandidateStatePath(context);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(state, JsonOptions));
    }

    private void ClearCandidateState(MonitorFileContext context)
    {
        string path = GetCandidateStatePath(context);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string GetCandidateStatePath(MonitorFileContext context)
    {
        return Path.Combine(
            settings.UiRoot,
            "Working",
            ".state",
            "Candidates",
            context.ObservedRootKey,
            context.RelativeSourcePath + ".candidate.json");
    }

    private static void EnsureCandidateBaselineIsCurrent(MonitorFileContext context, CandidateEditState state)
    {
        if (state.BaselineHash.Equals(NewFileOriginalHash, StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(context.SourceFilePath))
            {
                throw new InvalidOperationException($"candidate-baseline-stale: {context.RelativeSourcePath} was created as a new-file candidate, but the watched source file now exists.");
            }

            return;
        }

        if (!File.Exists(context.SourceFilePath))
        {
            throw new InvalidOperationException($"candidate-baseline-stale: {context.RelativeSourcePath} existed when the candidate was created, but the watched source file is now missing.");
        }

        FileInfo sourceInfo = new(context.SourceFilePath);
        string currentHash = ComputeSha256(context.SourceFilePath);
        if (!state.BaselineHash.Equals(currentHash, StringComparison.OrdinalIgnoreCase)
            || state.BaselineLength != sourceInfo.Length
            || state.BaselineLastWriteUtc != sourceInfo.LastWriteTimeUtc)
        {
            throw new InvalidOperationException($"candidate-baseline-stale: {context.RelativeSourcePath} changed after the Working candidate was initialized. Refresh or discard the candidate before continuing.");
        }
    }

    private static CompilationUnitSyntax FormatAnnotatedNodes(CompilationUnitSyntax root)
    {
        using AdhocWorkspace workspace = new();
        SyntaxNode formatted = Formatter.Format(root, FormatAnnotation, workspace);
        return (CompilationUnitSyntax)formatted;
    }

    public MonitorDiffDecisionResult RecordDiffDecision(
        string stagedRecordId,
        string decision,
        string? note = null,
        string? sessionId = null)
    {
        string normalizedDecision = NormalizeOperatorDecision(decision);
        (StagedEditRecord record, string recordPath) = ReadStagedEditRecord(stagedRecordId);
        if (IsBlockedDirtyUnexpected(record))
        {
            throw new InvalidOperationException(
                $"Staged record {record.RecordId} is blocked-dirty-unexpected and cannot be reclassified. Run refresh_file after Host/Operator inspection before staging another candidate.");
        }

        if (IsSupersededRecord(record))
        {
            string currentHash = File.Exists(record.SourceFilePath)
                ? ComputeSha256(record.SourceFilePath)
                : NewFileOriginalHash;
            MonitorDiffDecisionResult supersededResult = new(
                record.RecordId,
                string.IsNullOrWhiteSpace(sessionId) ? record.SessionId : sessionId,
                record.SourceFilePath,
                record.RelativeSourcePath,
                recordPath,
                record.ServerDerivedMetadata.StagedFilePath,
                normalizedDecision,
                "staged-record-superseded",
                false,
                false,
                record.OriginalHash,
                record.StagedHash,
                currentHash,
                record.QueueStatus,
                "This staged record was superseded by a later same-file candidate. Use list_session_staged_records to locate the current staged record for this session.",
                DateTimeOffset.UtcNow);
            string supersededDecisionRecordPath = WriteDiffDecisionRecord(supersededResult);
            return supersededResult with { DecisionRecordPath = supersededDecisionRecordPath };
        }

        string effectiveSessionId = string.IsNullOrWhiteSpace(sessionId) ? record.SessionId ?? string.Empty : sessionId;
        MaterializeAcceptedNewFileReview(record, normalizedDecision);
        DiffDecisionClassification classificationResult = ClassifyStrictDiffDecision(record, normalizedDecision);
        bool decisionMatchesClassification =
            normalizedDecision.Equals(classificationResult.Classification, StringComparison.OrdinalIgnoreCase)
            || (normalizedDecision.Equals("accepted", StringComparison.OrdinalIgnoreCase)
                && classificationResult.Classification.Equals("accepted-normalized", StringComparison.OrdinalIgnoreCase));
        bool blocksFurtherEdits = classificationResult.Classification.Equals("dirty-unexpected", StringComparison.OrdinalIgnoreCase);
        string queueStatus = blocksFurtherEdits ? "blocked-dirty-unexpected" : classificationResult.Classification;
        StagedEditRecord updatedRecord = record with { QueueStatus = queueStatus };
        File.WriteAllText(recordPath, JsonSerializer.Serialize(updatedRecord, JsonOptions));

        MonitorIndexRefreshDecisionResult? indexRefresh = RefreshIndexAfterAcceptedDecision(updatedRecord, effectiveSessionId, classificationResult.Classification);

        MonitorDiffDecisionResult result = new(
            record.RecordId,
            string.IsNullOrWhiteSpace(effectiveSessionId) ? null : effectiveSessionId,
            record.SourceFilePath,
            record.RelativeSourcePath,
            recordPath,
            record.ServerDerivedMetadata.StagedFilePath,
            normalizedDecision,
            classificationResult.Classification,
            decisionMatchesClassification,
            blocksFurtherEdits,
            record.OriginalHash,
            record.StagedHash,
            classificationResult.CurrentHash,
            queueStatus,
            note,
            DateTimeOffset.UtcNow)
        {
            OriginalNormalizedHash = classificationResult.OriginalNormalizedHash,
            StagedNormalizedHash = classificationResult.StagedNormalizedHash,
            CurrentNormalizedHash = classificationResult.CurrentNormalizedHash,
            IndexRefresh = indexRefresh
        };

        string decisionRecordPath = WriteDiffDecisionRecord(result);
        ClearCandidateStateAfterCompletedDecision(record, classificationResult.Classification);
        return result with { DecisionRecordPath = decisionRecordPath };
    }

    private MonitorIndexRefreshDecisionResult? RefreshIndexAfterAcceptedDecision(
        StagedEditRecord record,
        string? effectiveSessionId,
        string classification)
    {
        if (solutionIndexService is null)
        {
            return null;
        }

        if (!IsAcceptedClassification(classification))
        {
            return new MonitorIndexRefreshDecisionResult(
                "not-run",
                $"Index refresh only runs after accepted decisions; classification was {classification}.",
                "none",
                record.SourceFilePath);
        }

        if (!string.IsNullOrWhiteSpace(effectiveSessionId))
        {
            (StagedEditRecord Record, string RecordPath)[] sessionRecords = ReadSessionStagedRecordEntries(effectiveSessionId).ToArray();
            bool hasPendingRecords = sessionRecords.Any(item => IsPendingReviewQueueStatus(item.Record.QueueStatus));
            bool sessionHasMultipleRecords = sessionRecords
                .Select(item => item.Record.RecordId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Skip(1)
                .Any();

            if (sessionHasMultipleRecords && hasPendingRecords)
            {
                return new MonitorIndexRefreshDecisionResult(
                    "deferred",
                    "Accepted edit is part of a multi-file session; index refresh will run after the remaining staged records are decided.",
                    "session",
                    record.SourceFilePath,
                    PendingSessionRecordCount: sessionRecords.Count(item => IsPendingReviewQueueStatus(item.Record.QueueStatus)));
            }

            SolutionIndexBuildResult sessionBuild = solutionIndexService.Rebuild();
            return BuildIndexRefreshResult(
                sessionHasMultipleRecords ? "rebuilt-session-complete" : "rebuilt-single-session-file",
                sessionHasMultipleRecords
                    ? "All staged records in this session are terminal; rebuilt the solution index once for the completed chain."
                    : "Accepted single-file session edit; rebuilt the solution index.",
                sessionHasMultipleRecords ? "session" : "file",
                record.SourceFilePath,
                sessionBuild);
        }

        if (Path.GetExtension(record.SourceFilePath).Equals(".cs", StringComparison.OrdinalIgnoreCase))
        {
            SolutionIndexFileRefreshResult fileRefresh = solutionIndexService.RefreshFile(record.SourceFilePath);
            return new MonitorIndexRefreshDecisionResult(
                "refreshed-file",
                "Accepted single C# file edit; refreshed the solution index for the accepted file.",
                "file",
                record.SourceFilePath,
                fileRefresh.Status.FileCount,
                fileRefresh.Status.SymbolCount,
                fileRefresh.Status.DiagnosticCount,
                fileRefresh.Status.ReferenceCount,
                fileRefresh.Status.CallSiteCount,
                fileRefresh.Status.StaleFileCount,
                fileRefresh.Status.LastIndexedAtUtc);
        }

        SolutionIndexBuildResult build = solutionIndexService.Rebuild();
        return BuildIndexRefreshResult(
            "rebuilt-non-csharp-accept",
            "Accepted non-C# file edit; rebuilt the solution index so monitor-owned index state is fresh.",
            "solution",
            record.SourceFilePath,
            build);
    }

    private static MonitorIndexRefreshDecisionResult BuildIndexRefreshResult(
        string status,
        string reason,
        string scope,
        string sourceFilePath,
        SolutionIndexBuildResult build)
    {
        return new MonitorIndexRefreshDecisionResult(
            status,
            reason,
            scope,
            sourceFilePath,
            build.IndexedFileCount,
            build.IndexedSymbolCount,
            build.IndexedDiagnosticCount,
            build.IndexedReferenceCount,
            build.IndexedCallSiteCount,
            build.Status.StaleFileCount,
            build.Status.LastIndexedAtUtc,
            build.DurationMilliseconds);
    }

    private static bool IsAcceptedClassification(string classification)
    {
        return classification.Equals("accepted", StringComparison.OrdinalIgnoreCase)
            || classification.Equals("accepted-normalized", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPendingReviewQueueStatus(string queueStatus)
    {
        return queueStatus.Equals("staged", StringComparison.OrdinalIgnoreCase)
            || queueStatus.Equals("force-review-launched", StringComparison.OrdinalIgnoreCase)
            || queueStatus.Equals("blocked-overlay-validation", StringComparison.OrdinalIgnoreCase);
    }

    private void ClearCandidateStateAfterCompletedDecision(StagedEditRecord record, string classification)
    {
        if (!record.Operation.Equals("stage_candidate_for_review", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (classification.Equals("dirty-unexpected", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        MonitorFileContext context = ResolveFileContext(record.SourceFilePath, allowMissing: true);
        string statePath = GetCandidateStatePath(context);
        if (File.Exists(statePath))
        {
            File.Delete(statePath);
        }
    }

    public MonitorStagedDiffLaunchResult LaunchStagedDiff(string stagedRecordId, bool forceReviewOnOverlayErrors = false)
    {
        (StagedEditRecord record, string recordPath) = ReadStagedEditRecord(stagedRecordId);
        string stagedFilePath = record.ServerDerivedMetadata.StagedFilePath;
        if (IsSupersededRecord(record))
        {
            return MonitorStagedDiffLaunchResult.NotLaunched(
                "staged-record-superseded",
                record,
                recordPath,
                stagedFilePath,
                ResolveWinMergePath(),
                "This staged record was superseded by a later same-file candidate. Use list_session_staged_records to locate and review the current staged record.");
        }

        (StagedEditRecord BlockedRecord, string BlockedRecordPath)? blockedReview = FindBlockedReviewRecord(record);
        if (blockedReview is not null)
        {
            return MonitorStagedDiffLaunchResult.NotLaunched(
                "review-chain-blocked",
                record,
                recordPath,
                stagedFilePath,
                ResolveWinMergePath(),
                $"Review queue is blocked by staged record {blockedReview.Value.BlockedRecord.RecordId} ({blockedReview.Value.BlockedRecord.RelativeSourcePath}). Fix or force-review the blocked item before launching another diff.")
                with
                {
                    ValidationGateStatus = "blocked-by-prior-review-gate",
                    ValidationGateDecision = "cancel_for_fix",
                    ValidationGateMessage = $"Blocked by {blockedReview.Value.BlockedRecord.RecordId}."
                };
        }

        string? winMergePath = ResolveWinMergePath();
        if (winMergePath is null)
        {
            return MonitorStagedDiffLaunchResult.NotLaunched(
                "winmerge-not-found",
                record,
                recordPath,
                stagedFilePath,
                null,
                "WinMerge was not found. Install WinMerge or add it to the standard Program Files path.");
        }

        if (!File.Exists(record.SourceFilePath) && !IsNewFileRecord(record))
        {
            return MonitorStagedDiffLaunchResult.NotLaunched(
                "source-missing",
                record,
                recordPath,
                stagedFilePath,
                winMergePath,
                "Watched source file was not found.");
        }

        if (!File.Exists(stagedFilePath))
        {
            return MonitorStagedDiffLaunchResult.NotLaunched(
                "staged-file-missing",
                record,
                recordPath,
                stagedFilePath,
                winMergePath,
                "Staged candidate file was not found.");
        }

        if (record.OverlayValidation.HasErrors && !forceReviewOnOverlayErrors)
        {
            HostValidationGateDecision gateDecision = RequestOverlayValidationReview(record);
            if (!gateDecision.Decision.Equals("force_review", StringComparison.OrdinalIgnoreCase))
            {
                StagedEditRecord blockedRecord = record with { QueueStatus = "blocked-overlay-validation" };
                File.WriteAllText(recordPath, JsonSerializer.Serialize(blockedRecord, JsonOptions));
                return MonitorStagedDiffLaunchResult.NotLaunched(
                    gateDecision.Status.Equals("host-unavailable", StringComparison.OrdinalIgnoreCase)
                        ? "overlay-errors-host-unavailable"
                        : "overlay-errors-review-cancelled",
                    record,
                    recordPath,
                    stagedFilePath,
                    winMergePath,
                    gateDecision.Message)
                    with
                    {
                        ValidationGateStatus = gateDecision.Status,
                        ValidationGateDecision = gateDecision.Status.Equals("host-unavailable", StringComparison.OrdinalIgnoreCase)
                            ? "host_unavailable"
                            : gateDecision.Decision,
                        ValidationGateMessage = gateDecision.Message
                    };
            }
        }

        if (record.QueueStatus.Equals("blocked-overlay-validation", StringComparison.OrdinalIgnoreCase))
        {
            record = record with { QueueStatus = "force-review-launched" };
            File.WriteAllText(recordPath, JsonSerializer.Serialize(record, JsonOptions));
        }

        ClearSupersededBlockedOverlayRecords(record);
        string reviewSourcePath = record.SourceFilePath;
        if (IsNewFileRecord(record))
        {
            reviewSourcePath = CreateNewFileReviewBaseline(record);
            record = record with { NewFileReviewBaselinePath = reviewSourcePath };
            File.WriteAllText(recordPath, JsonSerializer.Serialize(record, JsonOptions));
        }

        DiffLaunchResult launchResult = LaunchWinMergeDetached(
            winMergePath,
            reviewSourcePath,
            stagedFilePath,
            record.SourceFilePath,
            IsNewFileRecord(record) ? "New File Baseline (Right)" : "Existing Source (Right)");
        return new MonitorStagedDiffLaunchResult(
            launchResult.ProcessId is null ? "launch-failed" : "winmerge-launched",
            record.RecordId,
            record.SourceFilePath,
            record.RelativeSourcePath,
            recordPath,
            stagedFilePath,
            winMergePath,
            launchResult.ProcessId,
            launchResult.Arguments,
            IsNewFileRecord(record)
                ? "After operator review, call record_diff_decision with accepted only if WinMerge saved the full staged candidate into the blank right-side baseline; the server will then create the watched file."
                : "After operator review, call record_diff_decision with accepted only if WinMerge saved the full staged candidate; otherwise call rejected.",
            record.OverlayValidation.HasErrors ? "completed" : null,
            record.OverlayValidation.HasErrors ? "force_review" : null,
            record.OverlayValidation.HasErrors ? "Overlay compile errors were force-reviewed before WinMerge launch." : null);
    }

    private (StagedEditRecord BlockedRecord, string BlockedRecordPath)? FindBlockedReviewRecord(StagedEditRecord currentRecord)
    {
        if (string.IsNullOrWhiteSpace(currentRecord.SessionId))
        {
            return null;
        }

        foreach ((StagedEditRecord record, string recordPath) in ReadSessionStagedRecordEntries(currentRecord.SessionId))
        {
            if (record.RecordId.Equals(currentRecord.RecordId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (record.RelativeSourcePath.Equals(currentRecord.RelativeSourcePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (record.QueueStatus.Equals("blocked-overlay-validation", StringComparison.OrdinalIgnoreCase))
            {
                return (record, recordPath);
            }
        }

        return null;
    }

    private void ClearSupersededBlockedOverlayRecords(StagedEditRecord currentRecord)
    {
        if (string.IsNullOrWhiteSpace(currentRecord.SessionId))
        {
            return;
        }

        foreach ((StagedEditRecord record, string recordPath) in ReadSessionStagedRecordEntries(currentRecord.SessionId))
        {
            if (record.RecordId.Equals(currentRecord.RecordId, StringComparison.OrdinalIgnoreCase)
                || !record.RelativeSourcePath.Equals(currentRecord.RelativeSourcePath, StringComparison.OrdinalIgnoreCase)
                || !record.QueueStatus.Equals("blocked-overlay-validation", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            StagedEditRecord superseded = ArchiveSupersededStagedFile(record, "superseded-by-corrected-candidate");
            File.WriteAllText(recordPath, JsonSerializer.Serialize(superseded, JsonOptions));
        }
    }

    private static HostValidationGateDecision RequestOverlayValidationReview(StagedEditRecord record)
    {
        string diagnostics = string.Join(
            Environment.NewLine,
            record.OverlayValidation.Diagnostics.Take(12).Select(diagnostic =>
                $"{diagnostic.Id} {diagnostic.FilePath}({diagnostic.Line},{diagnostic.Column}): {diagnostic.Message}"));
        if (record.OverlayValidation.Diagnostics.Count > 12)
        {
            diagnostics += Environment.NewLine + $"...and {record.OverlayValidation.Diagnostics.Count - 12} more diagnostic(s).";
        }

        try
        {
            using NamedPipeClientStream pipe = new(".", "MonitorBaseClaude.McpProxyHub", PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect(2500);
            using StreamReader reader = new(pipe, Encoding.UTF8, leaveOpen: true);
            using StreamWriter writer = new(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

            string request = JsonSerializer.Serialize(new
            {
                kind = "hostRequest",
                requestType = "overlayValidationReview",
                stagedRecordId = record.RecordId,
                relativeSourcePath = record.RelativeSourcePath,
                sourceFilePath = record.SourceFilePath,
                diagnosticCount = record.OverlayValidation.Diagnostics.Count,
                diagnostics
            });

            writer.WriteLine(request);
            string? responseLine = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(responseLine))
            {
                return new HostValidationGateDecision("host-empty-response", "cancel_for_fix", "WinForms host returned no validation gate decision. Review was not launched.");
            }

            HostValidationGateDecision? response = JsonSerializer.Deserialize<HostValidationGateDecision>(responseLine, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return response ?? new HostValidationGateDecision("host-invalid-response", "cancel_for_fix", "WinForms host returned an invalid validation gate decision. Review was not launched.");
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or InvalidOperationException)
        {
            return new HostValidationGateDecision(
                "host-unavailable",
                "host_unavailable",
                "Overlay compile validation has errors and the WinForms host was not available to approve force review. Start MonitorBaseClaude WinForms or fix diagnostics before launching review.");
        }
    }

    public IReadOnlyList<MonitorFileMatch> FindFile(string fileNameOrPattern, int maxResults = 25)
    {
        string watchedProjectFolder = Path.GetDirectoryName(settings.WatchedSolutionPath)
            ?? throw new InvalidOperationException("Watched solution path does not have a containing folder.");
        string pattern = string.IsNullOrWhiteSpace(fileNameOrPattern) ? "*" : fileNameOrPattern;
        if (!pattern.Contains('*', StringComparison.Ordinal) && !pattern.Contains('?', StringComparison.Ordinal))
        {
            pattern = "*" + pattern + "*";
        }

        return Directory
            .EnumerateFiles(watchedProjectFolder, pattern, SearchOption.AllDirectories)
            .Where(path => !IsInIgnoredDirectory(path))
            .OrderBy(path => Path.GetFileName(path).Equals(fileNameOrPattern, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(maxResults, 1, 200))
            .Select(path => new MonitorFileMatch(
                path,
                Path.GetRelativePath(watchedProjectFolder, path),
                new FileInfo(path).Length))
            .ToArray();
    }

    private MonitorFileContext ResolveFileContext(string sourceFilePath, bool allowMissing = false)
    {
        string watchedProjectFolder = Path.GetDirectoryName(settings.WatchedSolutionPath)
            ?? throw new InvalidOperationException("Watched solution path does not have a containing folder.");
        string sourcePath = Path.IsPathRooted(sourceFilePath)
            ? Path.GetFullPath(sourceFilePath)
            : Path.GetFullPath(Path.Combine(watchedProjectFolder, sourceFilePath));
        if (!allowMissing && !File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Source file was not found.", sourcePath);
        }

        string observedRoot = TrimDirectorySeparator(Path.GetFullPath(watchedProjectFolder));
        string relativeSourcePath = Path.GetRelativePath(observedRoot, sourcePath);
        if (relativeSourcePath.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativeSourcePath))
        {
            throw new InvalidOperationException($"Source file is not under watched project folder. Source: {sourcePath} | WatchedProjectFolder: {observedRoot}");
        }

        string observedRootKey = BuildObservedRootKey(observedRoot);
        string workingRelativePath = Path.Combine(observedRootKey, relativeSourcePath);
        string workingRoot = Path.Combine(settings.UiRoot, "Working");
        string workingFilePath = Path.Combine(workingRoot, workingRelativePath);
        string refreshStatePath = Path.Combine(workingRoot, ".state", workingRelativePath + ".refresh.state");

        return new MonitorFileContext(
            sourcePath,
            observedRoot,
            observedRootKey,
            relativeSourcePath,
            workingFilePath,
            refreshStatePath);
    }

    private MonitorFileContext ResolveCSharpFileContext(string sourceFilePath, string toolName)
    {
        MonitorFileContext context = ResolveFileContext(sourceFilePath);
        if (!context.SourceFilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{toolName} currently supports C# source files only.");
        }

        return context;
    }

    private static CompilationUnitSyntax ParseCompilationUnit(MonitorFileContext context)
    {
        return ParseCompilationUnit(context, context.SourceFilePath);
    }

    private static CompilationUnitSyntax ParseCompilationUnit(MonitorFileContext context, string filePath)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(File.ReadAllText(filePath), path: context.SourceFilePath);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
        Diagnostic[] diagnostics = root.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        if (diagnostics.Length > 0)
        {
            throw new InvalidOperationException($"C# parse failed for {context.RelativeSourcePath}: {diagnostics[0].GetMessage()}");
        }

        return root;
    }

    private static MonitorSymbolSelector ParseSymbolSelector(string symbolSelectorJson)
    {
        JsonSerializerOptions options = new(JsonOptions)
        {
            PropertyNameCaseInsensitive = true
        };
        return JsonSerializer.Deserialize<MonitorSymbolSelector>(symbolSelectorJson, options)
            ?? throw new InvalidOperationException("symbolSelectorJson could not be parsed.");
    }

    private static MemberDeclarationSyntax ParseMemberDeclaration(string code, string label)
    {
        MemberDeclarationSyntax member = SyntaxFactory.ParseMemberDeclaration(code)
            ?? throw new InvalidOperationException($"{label} is not a complete C# member declaration.");
        Diagnostic[] diagnostics = member.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        if (diagnostics.Length > 0)
        {
            throw new InvalidOperationException($"{label} has syntax errors: {diagnostics[0].GetMessage()}");
        }

        return member;
    }

    private static MemberDeclarationSyntax ResolveSingleMember(CompilationUnitSyntax root, MonitorSymbolSelector selector, string relativeSourcePath)
    {
        MemberDeclarationSyntax[] matches = root.DescendantNodes()
            .OfType<MemberDeclarationSyntax>()
            .Where(IsOutlineMember)
            .Where(member => MatchesSelector(member, selector, relativeSourcePath))
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"Symbol '{selector.Name}' was not found."),
            _ => throw new InvalidOperationException($"Symbol selector for '{selector.Name}' is ambiguous. Add containingType, memberKind, parameterTypes, or stableSymbolKey.")
        };
    }

    private static bool MatchesSelector(MemberDeclarationSyntax member, MonitorSymbolSelector selector, string relativeSourcePath)
    {
        if (!string.IsNullOrWhiteSpace(selector.StableSymbolKey)
            && !BuildStableSymbolKey(relativeSourcePath, member).Equals(selector.StableSymbolKey, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(selector.MemberKind)
            && !SymbolKind(member).Equals(selector.MemberKind, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(selector.Name)
            && !SymbolName(member).Equals(selector.Name, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(selector.ContainingNamespace)
            && !BuildNamespace(member).Equals(selector.ContainingNamespace, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(selector.ContainingType)
            && !string.Equals(BuildContainingType(member), selector.ContainingType, StringComparison.Ordinal))
        {
            return false;
        }

        if (selector.Arity is not null && GetArity(member) != selector.Arity.Value)
        {
            return false;
        }

        if (selector.ParameterTypes is { Count: > 0 } && !ParameterTypesMatch(member, selector.ParameterTypes))
        {
            return false;
        }

        return true;
    }

    private static TypeDeclarationSyntax ResolveSingleType(CompilationUnitSyntax root, string containingType)
    {
        bool expectsQualifiedName = containingType.Contains('.', StringComparison.Ordinal);
        TypeDeclarationSyntax[] matches = root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Where(type => expectsQualifiedName
                ? string.Equals(BuildContainingType(type), containingType, StringComparison.Ordinal)
                : type.Identifier.ValueText.Equals(containingType, StringComparison.Ordinal))
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"Containing type '{containingType}' was not found."),
            _ => throw new InvalidOperationException($"Containing type '{containingType}' is ambiguous.")
        };
    }

    private static int IndexOfMember(SyntaxList<MemberDeclarationSyntax> members, string symbolName)
    {
        for (int i = 0; i < members.Count; i++)
        {
            if (SymbolName(members[i]).Equals(symbolName, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static MemberDeclarationSyntax ApplyInsertionTrivia(MemberDeclarationSyntax newMember, TypeDeclarationSyntax type, int insertIndex)
    {
        SyntaxList<MemberDeclarationSyntax> members = type.Members;
        if (members.Count == 0)
        {
            return newMember
                .WithoutLeadingTrivia()
                .WithLeadingTrivia(SyntaxFactory.Whitespace("        "))
                .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
        }

        MemberDeclarationSyntax indentationSource = insertIndex < members.Count
            ? members[insertIndex]
            : members[^1];
        string indentation = GetDeclarationIndentation(indentationSource);

        return newMember
            .WithoutLeadingTrivia()
            .WithLeadingTrivia(SyntaxFactory.CarriageReturnLineFeed, SyntaxFactory.Whitespace(indentation))
            .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
    }

    private static string GetDeclarationIndentation(MemberDeclarationSyntax member)
    {
        string leadingText = member.GetLeadingTrivia().ToFullString();
        int lineStart = Math.Max(leadingText.LastIndexOf('\n'), leadingText.LastIndexOf('\r'));
        string indentation = lineStart >= 0 ? leadingText[(lineStart + 1)..] : leadingText;
        return !string.IsNullOrEmpty(indentation) && indentation.All(char.IsWhiteSpace)
            ? indentation
            : "        ";
    }

    private static int GetArity(MemberDeclarationSyntax member)
    {
        return member switch
        {
            MethodDeclarationSyntax method => method.TypeParameterList?.Parameters.Count ?? 0,
            TypeDeclarationSyntax type => type.TypeParameterList?.Parameters.Count ?? 0,
            DelegateDeclarationSyntax del => del.TypeParameterList?.Parameters.Count ?? 0,
            _ => 0
        };
    }

    private static bool ParameterTypesMatch(MemberDeclarationSyntax member, IReadOnlyList<string> expected)
    {
        SeparatedSyntaxList<ParameterSyntax>? parameters = member switch
        {
            MethodDeclarationSyntax method => method.ParameterList.Parameters,
            ConstructorDeclarationSyntax constructor => constructor.ParameterList.Parameters,
            DelegateDeclarationSyntax del => del.ParameterList.Parameters,
            _ => null
        };
        if (parameters is null || parameters.Value.Count != expected.Count)
        {
            return false;
        }

        for (int i = 0; i < expected.Count; i++)
        {
            string actualType = parameters.Value[i].Type?.ToString() ?? string.Empty;
            if (!actualType.Equals(expected[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeSourceMapScope(string? scope)
    {
        string normalized = string.IsNullOrWhiteSpace(scope) ? "auto" : scope.Trim().ToLowerInvariant();
        return normalized is "auto" or "file" or "folder" or "namespace" or "project"
            ? normalized
            : throw new InvalidOperationException("Source map scope must be auto, file, folder, namespace, or project.");
    }

    private static string NormalizeSourceMapMode(string? mode)
    {
        string normalized = string.IsNullOrWhiteSpace(mode) ? "auto" : mode.Trim().ToLowerInvariant();
        return normalized is "auto" or "navigation" or "selector" or "detail" or "full"
            ? normalized
            : throw new InvalidOperationException("Source map mode must be auto, navigation, selector, detail, or full.");
    }

    private static string ResolveEffectiveSourceMapScope(string observedRoot, string? path, string scope)
    {
        if (!scope.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return scope;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return "project";
        }

        string targetPath = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(observedRoot, path));
        EnsurePathIsUnderObservedRoot(observedRoot, targetPath);

        if (File.Exists(targetPath))
        {
            return "file";
        }

        if (Directory.Exists(targetPath))
        {
            return "folder";
        }

        throw new FileNotFoundException("Source map target file or folder was not found.", targetPath);
    }

    private static string ResolveEffectiveSourceMapMode(string scope, string? mode)
    {
        string normalized = NormalizeSourceMapMode(mode);
        if (!normalized.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        return scope.Equals("file", StringComparison.OrdinalIgnoreCase)
            ? "selector"
            : "navigation";
    }

    private static int GetSourceMapBudgetLimit(string mode)
    {
        return mode.Equals("navigation", StringComparison.OrdinalIgnoreCase) ? 20000
            : mode.Equals("selector", StringComparison.OrdinalIgnoreCase) ? 25000
            : mode.Equals("detail", StringComparison.OrdinalIgnoreCase) ? 20000
            : 15000;
    }

    private static string GetSourceMapModePurpose(string mode)
    {
        return mode.Equals("navigation", StringComparison.OrdinalIgnoreCase) ? "broad-orientation"
            : mode.Equals("selector", StringComparison.OrdinalIgnoreCase) ? "stable-symbol-selection"
            : mode.Equals("detail", StringComparison.OrdinalIgnoreCase) ? "contract-detail"
            : "audit-debug";
    }

    private static IEnumerable<string> ResolveSourceMapFiles(string observedRoot, string? path, string scope)
    {
        if (scope.Equals("project", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(path))
        {
            return EnumerateObservedSourceFiles(observedRoot);
        }

        if (scope.Equals("namespace", StringComparison.OrdinalIgnoreCase))
        {
            return EnumerateObservedSourceFiles(observedRoot);
        }

        string targetPath = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(observedRoot, path));
        EnsurePathIsUnderObservedRoot(observedRoot, targetPath);

        if (File.Exists(targetPath))
        {
            if (!targetPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("get_source_map currently supports C# source files only.");
            }

            return [targetPath];
        }

        if (Directory.Exists(targetPath))
        {
            return EnumerateObservedSourceFiles(targetPath);
        }

        throw new FileNotFoundException("Source map target file or folder was not found.", targetPath);
    }

    private static string? ResolveRequestedNamespace(string scope, string? path, string? namespaceName)
    {
        if (!scope.Equals("namespace", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return !string.IsNullOrWhiteSpace(namespaceName)
            ? namespaceName.Trim()
            : string.IsNullOrWhiteSpace(path) ? null : path.Trim();
    }

    private static bool SourceMapFileMatchesNamespace(MonitorSourceMapFile file, string scope, string? namespaceName)
    {
        if (!scope.Equals("namespace", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(namespaceName))
        {
            throw new InvalidOperationException("Namespace scope requires namespaceName or path.");
        }

        return file.Namespaces?.Any(candidate => string.Equals(candidate, namespaceName, StringComparison.Ordinal)) == true;
    }

    private static void EnsurePathIsUnderObservedRoot(string observedRoot, string path)
    {
        string relativePath = Path.GetRelativePath(observedRoot, path);
        if (relativePath.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException($"Path is not under watched project folder. Path: {path} | WatchedProjectFolder: {observedRoot}");
        }
    }

    private static string BuildObservedRootKey(string observedRoot)
    {
        string fullPath = TrimDirectorySeparator(Path.GetFullPath(observedRoot));
        string leafName = new DirectoryInfo(fullPath).Name;
        if (string.IsNullOrWhiteSpace(leafName))
        {
            leafName = "ObservedRoot";
        }

        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(fullPath.ToUpperInvariant()));
        string hash = Convert.ToHexString(hashBytes)[..12].ToLowerInvariant();
        return $"{SanitizeForFileName(leafName)}_{hash}";
    }

    private string CreateProposedSnapshot(MonitorFileContext context, string? ledgerSummary)
    {
        string historyRoot = Path.Combine(settings.UiRoot, "Working", "History");
        string historyRelativeDir = Path.GetDirectoryName(Path.Combine(context.ObservedRootKey, context.RelativeSourcePath)) ?? string.Empty;
        string historyTargetDir = Path.Combine(historyRoot, historyRelativeDir);
        Directory.CreateDirectory(historyTargetDir);

        string baseName = Path.GetFileNameWithoutExtension(context.WorkingFilePath);
        string extension = Path.GetExtension(context.WorkingFilePath);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmssfff");
        string proposedFilePath = Path.Combine(historyTargetDir, $"{baseName}_{stamp}{extension}");
        File.Copy(context.WorkingFilePath, proposedFilePath, overwrite: false);

        if (!string.IsNullOrWhiteSpace(ledgerSummary))
        {
            AppendFileLedgerEntry(context, proposedFilePath, ledgerSummary);
        }

        return proposedFilePath;
    }

    private string CreateStagedFile(MonitorFileContext context, string content, string recordId)
    {
        string stagedRoot = Path.Combine(settings.UiRoot, "Working", "Staged");
        string stagedRelativeDir = Path.GetDirectoryName(Path.Combine(context.ObservedRootKey, context.RelativeSourcePath)) ?? string.Empty;
        string stagedTargetDir = Path.Combine(stagedRoot, stagedRelativeDir);
        Directory.CreateDirectory(stagedTargetDir);

        string baseName = Path.GetFileNameWithoutExtension(context.SourceFilePath);
        string extension = Path.GetExtension(context.SourceFilePath);
        string stagedPath = Path.Combine(stagedTargetDir, $"{SanitizeForFileName(recordId)}{extension}");
        TextFileShape sourceShape = File.Exists(context.SourceFilePath)
            ? DetectTextFileShape(context.SourceFilePath)
            : new TextFileShape(new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), Environment.NewLine);
        File.WriteAllText(stagedPath, NormalizeLineEndings(content, sourceShape.NewLine), sourceShape.Encoding);
        return stagedPath;
    }

    private static TextFileShape DetectTextFileShape(string sourceFilePath)
    {
        byte[] bytes = File.ReadAllBytes(sourceFilePath);
        Encoding encoding = DetectEncoding(bytes);
        string text = encoding.GetString(StripPreamble(bytes, encoding));
        return new TextFileShape(encoding, DetectDominantNewLine(text));
    }

    private static Encoding DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return new UnicodeEncoding(bigEndian: true, byteOrderMark: true);
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }

    private static byte[] StripPreamble(byte[] bytes, Encoding encoding)
    {
        byte[] preamble = encoding.GetPreamble();
        if (preamble.Length == 0 || bytes.Length < preamble.Length)
        {
            return bytes;
        }

        for (int i = 0; i < preamble.Length; i++)
        {
            if (bytes[i] != preamble[i])
            {
                return bytes;
            }
        }

        return bytes[preamble.Length..];
    }

    private static string DetectDominantNewLine(string text)
    {
        int crlf = 0;
        int lf = 0;
        int cr = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                {
                    cr++;
                }
            }
            else if (text[i] == '\n')
            {
                lf++;
            }
        }

        if (crlf >= lf && crlf >= cr && crlf > 0)
        {
            return "\r\n";
        }

        if (lf >= cr && lf > 0)
        {
            return "\n";
        }

        return cr > 0 ? "\r" : Environment.NewLine;
    }

    private static string NormalizeLineEndings(string content, string newLine)
    {
        string normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
        return newLine.Equals("\n", StringComparison.Ordinal)
            ? normalized
            : normalized.Replace("\n", newLine, StringComparison.Ordinal);
    }

    private StagedEditRecord CreateStagedEditRecord(
        string recordId,
        string? sessionId,
        string operation,
        MonitorFileContext context,
        string stagedFilePath,
        string? manifestJson,
        string stagedContent,
        MonitorSyntaxValidationResult validation,
        MonitorOverlayValidationResult overlayValidation)
    {
        string originalContent = File.Exists(context.SourceFilePath) ? File.ReadAllText(context.SourceFilePath) : string.Empty;
        StagedEditMetadata metadata = DeriveStagedEditMetadata(context.SourceFilePath, originalContent, stagedFilePath, stagedContent);
        return new StagedEditRecord(
            recordId,
            sessionId,
            context.SourceFilePath,
            context.RelativeSourcePath,
            operation,
            DateTimeOffset.UtcNow,
            File.Exists(context.SourceFilePath) ? ComputeSha256(context.SourceFilePath) : NewFileOriginalHash,
            ComputeSha256(stagedFilePath),
            manifestJson,
            metadata,
            validation,
            overlayValidation,
            "staged");
    }

    private string WriteStagedEditRecord(StagedEditRecord record)
    {
        string recordsRoot = Path.Combine(settings.UiRoot, "Working", "Staged", "Records", DateTime.Now.ToString("yyyyMMdd"));
        Directory.CreateDirectory(recordsRoot);
        string recordPath = Path.Combine(recordsRoot, SanitizeForFileName(record.RecordId) + ".json");
        File.WriteAllText(recordPath, JsonSerializer.Serialize(record, JsonOptions));
        return recordPath;
    }

    private void EnsureFileIsNotBlockedByDirtyUnexpected(MonitorFileContext context)
    {
        (StagedEditRecord Record, string RecordPath)? blocked = FindBlockedDirtyUnexpectedRecord(context.SourceFilePath);
        if (blocked is null)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Further staged edits are blocked for {context.RelativeSourcePath} because staged record {blocked.Value.Record.RecordId} is dirty-unexpected. Run refresh_file after Host/Operator inspection before staging another candidate.");
    }

    private void EnsureNoCandidateInProgress(MonitorFileContext context, string label)
    {
        if (TryReadCandidateState(context) is not null)
        {
            throw new InvalidOperationException($"{label} already has a monitor Working candidate in progress: {context.RelativeSourcePath}");
        }
    }

    private void RecoverBlockedDirtyUnexpectedRecords(string sourceFilePath)
    {
        foreach ((StagedEditRecord record, string recordPath) in ReadStagedEditRecordsForSource(sourceFilePath))
        {
            if (!IsBlockedDirtyUnexpected(record))
            {
                continue;
            }

            StagedEditRecord recoveredRecord = record with { QueueStatus = "recovered-by-refresh" };
            File.WriteAllText(recordPath, JsonSerializer.Serialize(recoveredRecord, JsonOptions));
        }
    }

    private (StagedEditRecord Record, string RecordPath)? FindBlockedDirtyUnexpectedRecord(string sourceFilePath)
    {
        foreach ((StagedEditRecord record, string recordPath) in ReadStagedEditRecordsForSource(sourceFilePath)
            .Where(item => IsBlockedDirtyUnexpected(item.Record))
            .OrderByDescending(item => item.Record.CreatedAt))
        {
            return (record, recordPath);
        }

        return null;
    }

    private static bool IsBlockedDirtyUnexpected(StagedEditRecord record)
    {
        return record.QueueStatus.Equals("blocked-dirty-unexpected", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupersededRecord(StagedEditRecord record)
    {
        return record.QueueStatus.StartsWith("superseded-", StringComparison.OrdinalIgnoreCase);
    }

    private (StagedEditRecord Record, string RecordPath)? FindLatestSameFileSessionRecord(string? sessionId, MonitorFileContext context)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        string fullSourcePath = Path.GetFullPath(context.SourceFilePath);
        foreach ((StagedEditRecord record, string recordPath) in ReadSessionStagedRecordEntries(sessionId)
            .Where(item => Path.GetFullPath(item.Record.SourceFilePath).Equals(fullSourcePath, StringComparison.OrdinalIgnoreCase))
            .Where(item => item.Record.QueueStatus.Equals("staged", StringComparison.OrdinalIgnoreCase)
                || item.Record.QueueStatus.Equals("force-review-launched", StringComparison.OrdinalIgnoreCase))
            .Where(item => File.Exists(item.Record.ServerDerivedMetadata.StagedFilePath))
            .OrderByDescending(item => item.Record.CreatedAt))
        {
            return (record, recordPath);
        }

        return null;
    }

    private void SupersedePriorSameFileSessionRecords(string? sessionId, MonitorFileContext context)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        string fullSourcePath = Path.GetFullPath(context.SourceFilePath);
        foreach ((StagedEditRecord record, string recordPath) in ReadSessionStagedRecordEntries(sessionId)
            .Where(item => Path.GetFullPath(item.Record.SourceFilePath).Equals(fullSourcePath, StringComparison.OrdinalIgnoreCase))
            .Where(item => item.Record.QueueStatus.Equals("staged", StringComparison.OrdinalIgnoreCase)
                || item.Record.QueueStatus.Equals("force-review-launched", StringComparison.OrdinalIgnoreCase)))
        {
            StagedEditRecord superseded = ArchiveSupersededStagedFile(record, "superseded-by-later-same-file-candidate");
            File.WriteAllText(recordPath, JsonSerializer.Serialize(superseded, JsonOptions));
        }
    }

    private StagedEditRecord ArchiveSupersededStagedFile(StagedEditRecord record, string queueStatus)
    {
        string stagedFilePath = record.ServerDerivedMetadata.StagedFilePath;
        if (!File.Exists(stagedFilePath))
        {
            return record with { QueueStatus = queueStatus };
        }

        string archiveRoot = Path.Combine(
            settings.UiRoot,
            "Working",
            "Staged",
            "Superseded",
            record.CreatedAt.ToString("yyyyMMdd"),
            SanitizeForFileName(record.RecordId));
        string relativeDirectory = Path.GetDirectoryName(record.RelativeSourcePath) ?? string.Empty;
        string archiveDirectory = Path.Combine(archiveRoot, relativeDirectory);
        Directory.CreateDirectory(archiveDirectory);

        string archivePath = Path.Combine(archiveDirectory, Path.GetFileName(stagedFilePath));
        if (!Path.GetFullPath(stagedFilePath).Equals(Path.GetFullPath(archivePath), StringComparison.OrdinalIgnoreCase))
        {
            File.Move(stagedFilePath, archivePath, overwrite: true);
        }

        return record with
        {
            QueueStatus = queueStatus,
            ServerDerivedMetadata = record.ServerDerivedMetadata with { StagedFilePath = archivePath }
        };
    }

    private IEnumerable<(StagedEditRecord Record, string RecordPath)> ReadStagedEditRecordsForSource(string sourceFilePath)
    {
        string recordsRoot = Path.Combine(settings.UiRoot, "Working", "Staged", "Records");
        if (!Directory.Exists(recordsRoot))
        {
            yield break;
        }

        string fullSourcePath = Path.GetFullPath(sourceFilePath);
        foreach (string recordPath in Directory.EnumerateFiles(recordsRoot, "*.json", SearchOption.AllDirectories))
        {
            StagedEditRecord? record = null;
            try
            {
                record = JsonSerializer.Deserialize<StagedEditRecord>(File.ReadAllText(recordPath), JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (record is not null
                && Path.GetFullPath(record.SourceFilePath).Equals(fullSourcePath, StringComparison.OrdinalIgnoreCase))
            {
                yield return (record, recordPath);
            }
        }
    }

    private (StagedEditRecord Record, string RecordPath) ReadStagedEditRecord(string stagedRecordId)
    {
        string recordsRoot = Path.Combine(settings.UiRoot, "Working", "Staged", "Records");
        if (!Directory.Exists(recordsRoot))
        {
            throw new DirectoryNotFoundException($"Staged record root was not found: {recordsRoot}");
        }

        string safeRecordId = SanitizeForFileName(stagedRecordId);
        string? recordPath = Directory
            .EnumerateFiles(recordsRoot, "*.json", SearchOption.AllDirectories)
            .FirstOrDefault(path => Path.GetFileNameWithoutExtension(path).Equals(safeRecordId, StringComparison.OrdinalIgnoreCase));
        if (recordPath is null)
        {
            throw new FileNotFoundException("Staged edit record was not found.", stagedRecordId);
        }

        StagedEditRecord record = JsonSerializer.Deserialize<StagedEditRecord>(File.ReadAllText(recordPath), JsonOptions)
            ?? throw new InvalidOperationException($"Staged edit record could not be read: {recordPath}");
        return (record, recordPath);
    }

    private string WriteDiffDecisionRecord(MonitorDiffDecisionResult result)
    {
        string decisionRoot = Path.Combine(settings.UiRoot, "Working", "Staged", "Decisions", DateTime.Now.ToString("yyyyMMdd"));
        Directory.CreateDirectory(decisionRoot);
        string decisionRecordPath = Path.Combine(
            decisionRoot,
            $"{SanitizeForFileName(result.StagedRecordId)}_{DateTime.Now:HHmmssfff}_{SanitizeForFileName(result.Classification)}.json");
        File.WriteAllText(decisionRecordPath, JsonSerializer.Serialize(result, JsonOptions));
        return decisionRecordPath;
    }

    private string CreateNewFileReviewBaseline(StagedEditRecord record)
    {
        string baselineRoot = Path.Combine(settings.UiRoot, "Working", "Staged", "NewFileBaselines", SanitizeForFileName(record.RecordId));
        string baselinePath = Path.Combine(baselineRoot, record.RelativeSourcePath);
        Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
        if (!File.Exists(baselinePath))
        {
            File.WriteAllText(baselinePath, string.Empty, Encoding.UTF8);
        }

        return baselinePath;
    }

    private static void MaterializeAcceptedNewFileReview(StagedEditRecord record, string normalizedDecision)
    {
        if (!normalizedDecision.Equals("accepted", StringComparison.OrdinalIgnoreCase)
            || !IsNewFileRecord(record)
            || File.Exists(record.SourceFilePath)
            || string.IsNullOrWhiteSpace(record.NewFileReviewBaselinePath)
            || !File.Exists(record.NewFileReviewBaselinePath))
        {
            return;
        }

        string reviewBaselineHash = ComputeSha256(record.NewFileReviewBaselinePath);
        if (!reviewBaselineHash.Equals(record.StagedHash, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(record.SourceFilePath)!);
        File.Copy(record.NewFileReviewBaselinePath, record.SourceFilePath, overwrite: false);
    }

    private static string NormalizeOperatorDecision(string decision)
    {
        string normalized = decision.Trim().ToLowerInvariant();
        return normalized switch
        {
            "accept" or "accepted" => "accepted",
            "reject" or "rejected" => "rejected",
            _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "Decision must be accepted or rejected.")
        };
    }

    private static DiffDecisionClassification ClassifyStrictDiffDecision(StagedEditRecord record, string normalizedDecision)
    {
        if (IsNewFileRecord(record))
        {
            bool sourceExists = File.Exists(record.SourceFilePath);
            string newFileCurrentHash = sourceExists ? ComputeSha256(record.SourceFilePath) : NewFileOriginalHash;
            if (normalizedDecision.Equals("rejected", StringComparison.OrdinalIgnoreCase))
            {
                return new DiffDecisionClassification(
                    newFileCurrentHash,
                    sourceExists ? "dirty-unexpected" : "rejected",
                    OriginalNormalizedHash: null,
                    StagedNormalizedHash: null,
                    CurrentNormalizedHash: null);
            }

            return new DiffDecisionClassification(
                newFileCurrentHash,
                sourceExists && newFileCurrentHash.Equals(record.StagedHash, StringComparison.OrdinalIgnoreCase)
                    ? "accepted"
                    : "dirty-unexpected",
                OriginalNormalizedHash: null,
                StagedNormalizedHash: null,
                CurrentNormalizedHash: null);
        }

        string currentHash = ComputeSha256(record.SourceFilePath);
        if (normalizedDecision.Equals("rejected", StringComparison.OrdinalIgnoreCase))
        {
            return new DiffDecisionClassification(
                currentHash,
                currentHash.Equals(record.OriginalHash, StringComparison.OrdinalIgnoreCase)
                    ? "rejected"
                    : "dirty-unexpected",
                OriginalNormalizedHash: null,
                StagedNormalizedHash: null,
                CurrentNormalizedHash: null);
        }

        if (currentHash.Equals(record.StagedHash, StringComparison.OrdinalIgnoreCase))
        {
            return new DiffDecisionClassification(
                currentHash,
                "accepted",
                OriginalNormalizedHash: null,
                StagedNormalizedHash: null,
                CurrentNormalizedHash: null);
        }

        string currentNormalizedHash = ComputeNormalizedFileHash(record.SourceFilePath);
        string stagedNormalizedHash = ComputeNormalizedFileHash(record.ServerDerivedMetadata.StagedFilePath);
        if (currentNormalizedHash.Equals(stagedNormalizedHash, StringComparison.OrdinalIgnoreCase))
        {
            return new DiffDecisionClassification(
                currentHash,
                "accepted-normalized",
                OriginalNormalizedHash: null,
                StagedNormalizedHash: stagedNormalizedHash,
                CurrentNormalizedHash: currentNormalizedHash);
        }

        return new DiffDecisionClassification(
            currentHash,
            "dirty-unexpected",
            OriginalNormalizedHash: null,
            StagedNormalizedHash: stagedNormalizedHash,
            CurrentNormalizedHash: currentNormalizedHash);
    }

    private static bool IsNewFileRecord(StagedEditRecord record)
    {
        return record.OriginalHash.Equals(NewFileOriginalHash, StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeNormalizedFileHash(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Encoding encoding = DetectEncoding(bytes);
        string text = encoding.GetString(StripPreamble(bytes, encoding));
        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static DiffLaunchResult LaunchWinMergeDetached(
        string winMergePath,
        string originalFilePath,
        string proposedFilePath,
        string? displayFilePath = null,
        string originalLabelPrefix = "Existing Source (Right)")
    {
        string displayName = BuildWinMergeDisplayName(displayFilePath ?? originalFilePath);
        Process? existing = FindOpenWinMergeReview(displayName);
        if (existing is not null)
        {
            return new DiffLaunchResult(existing.Id, $"already-open: {existing.MainWindowTitle}");
        }

        string launcherPath = WriteWinMergeLauncher(winMergePath, originalFilePath, proposedFilePath, displayName, originalLabelPrefix, displayFilePath ?? originalFilePath);
        ProcessStartInfo startInfo = BuildWinMergeStartInfo(launcherPath);
        Process? process = Process.Start(startInfo);
        return new DiffLaunchResult(process?.Id, $"{launcherPath} :: {File.ReadAllText(launcherPath)}");
    }

    private static ProcessStartInfo BuildWinMergeStartInfo(string launcherPath)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "cmd.exe",
            Arguments = $"/c call \"{launcherPath}\"",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal,
            WorkingDirectory = Path.GetDirectoryName(launcherPath) ?? Environment.CurrentDirectory
        };
        return startInfo;
    }

    private static string WriteWinMergeLauncher(
        string winMergePath,
        string originalFilePath,
        string proposedFilePath,
        string displayName,
        string originalLabelPrefix,
        string displayFilePath)
    {
        string launcherRoot = Path.Combine(Path.GetTempPath(), "MonitorBaseClaude", "DiffLaunchers");
        Directory.CreateDirectory(launcherRoot);
        string launcherPath = Path.Combine(launcherRoot, $"launch-{DateTime.Now:yyyyMMdd-HHmmssfff}-{SanitizeForFileName(Path.GetFileNameWithoutExtension(displayFilePath))}.cmd");

        string winMergeCommand = string.Join(" ",
        [
            "start",
            "\"\"",
            "/wait",
            QuoteCommandArgument(winMergePath),
            "/u",
            "/maximize",
            "/ignoreeol:1",
            "/dl",
            QuoteCommandArgument($"New/Proposed (Working) - {displayName}"),
            "/dr",
            QuoteCommandArgument($"{originalLabelPrefix} - {displayName}"),
            QuoteCommandArgument(proposedFilePath),
            QuoteCommandArgument(originalFilePath)
        ]);

        string content = string.Join(Environment.NewLine,
        [
            "@echo off",
            $"title MonitorBaseClaude diff - {Path.GetFileName(displayFilePath)}",
            "echo MonitorBaseClaude diff review",
            $"echo Proposed: {proposedFilePath}",
            $"echo Source:   {displayFilePath}",
            $"echo Review baseline: {originalFilePath}",
            "echo.",
            winMergeCommand,
            "set WINMERGE_EXIT=%ERRORLEVEL%",
            "echo.",
            "echo WinMerge exited with code %WINMERGE_EXIT%."
        ]);
        File.WriteAllText(launcherPath, content);
        return launcherPath;
    }

    private static string BuildWinMergeDisplayName(string originalFilePath)
    {
        string fileName = Path.GetFileName(originalFilePath);
        string relativeHint = Path.GetFileName(Path.GetDirectoryName(originalFilePath) ?? string.Empty);
        return string.IsNullOrWhiteSpace(relativeHint) ? fileName : $"{relativeHint}\\{fileName}";
    }

    private static Process? FindOpenWinMergeReview(string displayName)
    {
        try
        {
            return Process.GetProcessesByName("WinMergeU")
                .Concat(Process.GetProcessesByName("WinMerge"))
                .FirstOrDefault(process =>
                {
                    try
                    {
                        return process.MainWindowTitle.Contains(displayName, StringComparison.OrdinalIgnoreCase);
                    }
                    catch (InvalidOperationException)
                    {
                        return false;
                    }
                });
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string? ResolveWinMergePath()
    {
        return WinMergeCandidates.FirstOrDefault(File.Exists);
    }

    private static bool IsRefreshStateCurrent(string refreshStatePath, string sourceFilePath)
    {
        if (!File.Exists(refreshStatePath))
        {
            return false;
        }

        try
        {
            MonitorRefreshState? state = JsonSerializer.Deserialize<MonitorRefreshState>(File.ReadAllText(refreshStatePath), JsonOptions);
            return state is not null
                && string.Equals(Path.GetFullPath(state.SourceFilePath), Path.GetFullPath(sourceFilePath), StringComparison.OrdinalIgnoreCase)
                && state.SourceLastWriteUtc == File.GetLastWriteTimeUtc(sourceFilePath)
                && state.SourceLength == new FileInfo(sourceFilePath).Length;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool FilesAreIdentical(string leftPath, string rightPath)
    {
        FileInfo left = new(leftPath);
        FileInfo right = new(rightPath);
        if (left.Length != right.Length)
        {
            return false;
        }

        return File.ReadAllBytes(leftPath).SequenceEqual(File.ReadAllBytes(rightPath));
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] hashBytes = SHA256.HashData(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static string ComputeSha256Text(string text)
    {
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static TextMatchSet FindTextMatches(string text, string findText, int occurrenceIndex)
    {
        List<int> offsets = [];
        int searchStart = 0;
        while (searchStart <= text.Length)
        {
            int offset = text.IndexOf(findText, searchStart, StringComparison.Ordinal);
            if (offset < 0)
            {
                break;
            }

            offsets.Add(offset);
            searchStart = offset + findText.Length;
        }

        if (occurrenceIndex >= offsets.Count)
        {
            throw new InvalidOperationException(
                $"Requested occurrence index {occurrenceIndex} but only {offsets.Count} occurrence(s) were found.");
        }

        return new TextMatchSet(offsets.Count, occurrenceIndex, offsets[occurrenceIndex]);
    }

    private static MonitorTextSpanResult CreateTextSpanResult(
        MonitorFileContext context,
        string editBasePath,
        string editBaseHash,
        string findText,
        TextMatchSet matches)
    {
        string baseText = File.ReadAllText(editBasePath);
        (int StartLine, int StartColumn) startPosition = GetLineColumnFromOffset(baseText, matches.SelectedOffset);
        (int EndLine, int EndColumn) endPosition = GetLineColumnFromOffset(baseText, matches.SelectedOffset + findText.Length);
        return new MonitorTextSpanResult(
            "found",
            context.SourceFilePath,
            context.WatchedProjectFolder,
            context.ObservedRootKey,
            context.RelativeSourcePath,
            editBasePath,
            editBaseHash,
            ComputeSha256Text(findText),
            matches.Count,
            matches.OccurrenceIndex,
            startPosition.StartLine,
            startPosition.StartColumn,
            endPosition.EndLine,
            endPosition.EndColumn);
    }

    private static (int StartLine, int StartColumn) GetLineColumnFromOffset(string text, int offset)
    {
        if (offset < 0 || offset > text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset is outside the file text.");
        }

        int line = 1;
        int column = 1;
        for (int index = 0; index < offset; index++)
        {
            char c = text[index];
            if (c == '\r')
            {
                if (index + 1 < offset && text[index + 1] == '\n')
                {
                    index++;
                }

                line++;
                column = 1;
                continue;
            }

            if (c == '\n')
            {
                line++;
                column = 1;
                continue;
            }

            column++;
        }

        return (line, column);
    }

    private static int GetOffsetFromLineColumn(string text, int line, int column, string lineParameterName, string columnParameterName)
    {
        if (line < 1)
        {
            throw new ArgumentOutOfRangeException(lineParameterName, "Line numbers are 1-based.");
        }

        if (column < 1)
        {
            throw new ArgumentOutOfRangeException(columnParameterName, "Column numbers are 1-based.");
        }

        int currentLine = 1;
        int currentColumn = 1;
        for (int index = 0; index < text.Length; index++)
        {
            if (currentLine == line && currentColumn == column)
            {
                return index;
            }

            char c = text[index];
            if (c == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                currentLine++;
                currentColumn = 1;
                continue;
            }

            if (c == '\n')
            {
                currentLine++;
                currentColumn = 1;
                continue;
            }

            currentColumn++;
        }

        if (currentLine == line && currentColumn == column)
        {
            return text.Length;
        }

        string invalidParameterName = line > currentLine ? lineParameterName : columnParameterName;
        throw new ArgumentOutOfRangeException(
            invalidParameterName,
            $"Position {line}:{column} is outside the file text.");
    }

    private static bool IsInIgnoredDirectory(string path)
    {
        string normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        string separator = Path.DirectorySeparatorChar.ToString();
        return normalized.Contains($"{separator}bin{separator}", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains($"{separator}obj{separator}", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains($"{separator}.git{separator}", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains($"{separator}.vs{separator}", StringComparison.OrdinalIgnoreCase);
    }

    private static MonitorSyntaxValidationResult ValidateSyntaxIfCSharp(string sourceFilePath, string content)
    {
        if (!sourceFilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return new MonitorSyntaxValidationResult(false, []);
        }

        SyntaxTree tree = CSharpSyntaxTree.ParseText(content, path: sourceFilePath);
        MonitorSyntaxDiagnostic[] diagnostics = tree.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic =>
            {
                FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
                return new MonitorSyntaxDiagnostic(
                    diagnostic.Id,
                    diagnostic.GetMessage(),
                    span.StartLinePosition.Line + 1,
                    span.StartLinePosition.Character + 1);
            })
            .ToArray();

        return new MonitorSyntaxValidationResult(diagnostics.Length > 0, diagnostics);
    }

    private MonitorOverlayValidationResult ValidateCandidateOverlayCompilation(
        MonitorFileContext context,
        string candidateFilePath)
    {
        if (context.SourceFilePath.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
            || context.SourceFilePath.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase))
        {
            return new MonitorOverlayValidationResult("razor-validation-pending", false, 0, 0, []);
        }

        if (!context.SourceFilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return new MonitorOverlayValidationResult("skipped-non-csharp", false, 0, 0, []);
        }

        try
        {
            string observedRoot = Path.GetFullPath(context.WatchedProjectFolder);
            CSharpParseOptions parseOptions = new(
                languageVersion: LanguageVersion.Latest,
                kind: SourceCodeKind.Regular,
                documentationMode: DocumentationMode.Parse);
            Dictionary<string, string> overlays = BuildCandidateOverlayMap(context, candidateFilePath);
            string[] overlayRelatives = overlays.Keys.ToArray();
            string cacheKey = BuildOverlayValidationCacheKey(context, overlays);
            if (overlayValidationCache.TryGetValue(cacheKey, out MonitorOverlayValidationResult? cached))
            {
                return cached with { FromCache = true };
            }

            List<SyntaxTree> trees = [];
            int overlayFileCount = 0;

            foreach (string sourcePath in EnumerateObservedSourceFiles(observedRoot))
            {
                string relative = NormalizePath(Path.GetRelativePath(observedRoot, sourcePath));
                string textPath = sourcePath;
                if (overlays.TryGetValue(relative, out string? overlayPath))
                {
                    textPath = overlayPath;
                    overlayFileCount++;
                    overlays.Remove(relative);
                }

                string treePath = Path.GetFullPath(sourcePath);
                trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(textPath), parseOptions, treePath));
            }

            foreach (KeyValuePair<string, string> overlay in overlays)
            {
                string treePath = Path.GetFullPath(Path.Combine(
                    observedRoot,
                    overlay.Key.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)));
                trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(overlay.Value), parseOptions, treePath));
                overlayFileCount++;
            }

            trees.Add(BuildImplicitGlobalUsingsTree(parseOptions, observedRoot));
            trees.Add(BuildWinFormsBootstrapTree(parseOptions));

            CSharpCompilation compilation = CSharpCompilation.Create(
                assemblyName: "MonitorBaseClaudeCandidateOverlayValidation",
                syntaxTrees: trees,
                references: GetMetadataReferences(observedRoot),
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            string[] overlayTreePaths = BuildOverlayTreePaths(observedRoot, context, overlayRelatives)
                .Append(Path.GetFullPath(context.SourceFilePath))
                .ToArray();
            HashSet<string> diagnosticPaths = overlayTreePaths
                .Select(Path.GetFullPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            MonitorOverlayDiagnostic[] diagnostics = compilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Where(diagnostic => diagnostic.Location.IsInSource)
                .Where(diagnostic =>
                {
                    string path = diagnostic.Location.GetLineSpan().Path;
                    return !string.IsNullOrWhiteSpace(path)
                        && diagnosticPaths.Contains(Path.GetFullPath(path));
                })
                .Take(50)
                .Select(diagnostic =>
                {
                    FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
                    return new MonitorOverlayDiagnostic(
                        diagnostic.Id,
                        diagnostic.GetMessage(),
                        span.Path,
                        span.StartLinePosition.Line + 1,
                        span.StartLinePosition.Character + 1);
                })
                .ToArray();

            MonitorOverlayValidationResult result = new(
                diagnostics.Length > 0 ? "compiled-with-errors" : "compiled",
                diagnostics.Length > 0,
                trees.Count,
                overlayFileCount,
                diagnostics);
            overlayValidationCache[cacheKey] = result;
            return result;
        }
        catch (Exception ex)
        {
            return new MonitorOverlayValidationResult(
                "validation-failed",
                true,
                0,
                0,
                [new MonitorOverlayDiagnostic("MONITOR_CANDIDATE_OVERLAY", ex.Message, context.SourceFilePath, 0, 0)]);
        }
    }

    private static string BuildOverlayValidationCacheKey(MonitorFileContext context, IReadOnlyDictionary<string, string> overlays)
    {
        StringBuilder builder = new();
        builder.Append(context.ObservedRootKey);
        foreach (KeyValuePair<string, string> overlay in overlays.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append('|');
            builder.Append(NormalizePath(overlay.Key));
            builder.Append('=');
            builder.Append(File.Exists(overlay.Value) ? ComputeSha256(overlay.Value) : "<missing>");
        }

        return builder.ToString();
    }

    private Dictionary<string, string> BuildCandidateOverlayMap(MonitorFileContext context, string candidateFilePath)
    {
        Dictionary<string, string> overlays = new(StringComparer.OrdinalIgnoreCase)
        {
            [NormalizePath(context.RelativeSourcePath)] = candidateFilePath
        };
        string candidateStateRoot = Path.Combine(settings.UiRoot, "Working", ".state", "Candidates", context.ObservedRootKey);
        if (!Directory.Exists(candidateStateRoot))
        {
            return overlays;
        }

        foreach (string statePath in Directory.EnumerateFiles(candidateStateRoot, "*.candidate.json", SearchOption.AllDirectories))
        {
            CandidateEditState? state;
            try
            {
                state = JsonSerializer.Deserialize<CandidateEditState>(File.ReadAllText(statePath), JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (state is null
                || !File.Exists(state.CandidateFilePath)
                || string.Equals(NormalizePath(state.RelativeSourcePath), NormalizePath(context.RelativeSourcePath), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!IsCandidateBaselineCurrent(state))
            {
                continue;
            }

            overlays[NormalizePath(state.RelativeSourcePath)] = state.CandidateFilePath;
        }

        return overlays;
    }

    private static bool IsCandidateBaselineCurrent(CandidateEditState state)
    {
        if (state.BaselineHash.Equals(NewFileOriginalHash, StringComparison.OrdinalIgnoreCase))
        {
            return !File.Exists(state.SourceFilePath);
        }

        if (!File.Exists(state.SourceFilePath))
        {
            return false;
        }

        FileInfo sourceInfo = new(state.SourceFilePath);
        return state.BaselineLength == sourceInfo.Length
            && state.BaselineLastWriteUtc == sourceInfo.LastWriteTimeUtc
            && state.BaselineHash.Equals(ComputeSha256(state.SourceFilePath), StringComparison.OrdinalIgnoreCase);
    }

    private IEnumerable<(StagedEditRecord Record, string RecordPath)> ReadSessionStagedRecordEntries(string sessionId)
    {
        string recordsRoot = Path.Combine(settings.UiRoot, "Working", "Staged", "Records");
        if (!Directory.Exists(recordsRoot))
        {
            yield break;
        }

        foreach (string recordPath in Directory.EnumerateFiles(recordsRoot, "*.json", SearchOption.AllDirectories)
            .OrderBy(File.GetLastWriteTimeUtc))
        {
            StagedEditRecord? record = null;
            try
            {
                record = JsonSerializer.Deserialize<StagedEditRecord>(File.ReadAllText(recordPath), JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (record is not null && string.Equals(record.SessionId, sessionId, StringComparison.Ordinal))
            {
                yield return (record, recordPath);
            }
        }
    }

    private static IEnumerable<string> BuildOverlayTreePaths(
        string observedRoot,
        MonitorFileContext context,
        IEnumerable<string> remainingOverlayRelatives)
    {
        yield return Path.GetFullPath(context.SourceFilePath);
        foreach (string relative in remainingOverlayRelatives)
        {
            yield return Path.GetFullPath(Path.Combine(
                observedRoot,
                relative.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)));
        }
    }

    private static IEnumerable<string> EnumerateObservedSourceFiles(string observedRoot)
    {
        Stack<string> pending = new();
        pending.Push(observedRoot);

        while (pending.Count > 0)
        {
            string current = pending.Pop();
            foreach (string dir in Directory.EnumerateDirectories(current))
            {
                if (IsIgnoredSourceDirectory(dir))
                {
                    continue;
                }

                pending.Push(dir);
            }

            foreach (string file in Directory.EnumerateFiles(current, "*.cs"))
            {
                yield return file;
            }
        }
    }

    private static bool IsIgnoredSourceDirectory(string path)
    {
        string name = Path.GetFileName(path);
        return name.Equals(".git", StringComparison.OrdinalIgnoreCase)
            || name.Equals(".vs", StringComparison.OrdinalIgnoreCase)
            || name.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || name.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || name.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
            || name.Equals("packages", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Working", StringComparison.OrdinalIgnoreCase)
            || name.Equals("archive", StringComparison.OrdinalIgnoreCase);
    }

    private static SyntaxTree BuildImplicitGlobalUsingsTree(CSharpParseOptions parseOptions, string observedRoot)
    {
        HashSet<string> usings = new(StringComparer.Ordinal)
        {
            "global using System;",
            "global using System.Collections.Generic;",
            "global using System.ComponentModel;",
            "global using System.IO;",
            "global using System.Linq;",
            "global using System.Net.Http;",
            "global using System.Threading;",
            "global using System.Threading.Tasks;"
        };

        foreach (string generatedUsing in LoadGeneratedGlobalUsings(observedRoot))
        {
            usings.Add(generatedUsing);
        }

        string text = string.Join(Environment.NewLine, usings.OrderBy(value => value, StringComparer.Ordinal));
        return CSharpSyntaxTree.ParseText(text, parseOptions, path: "<MonitorBaseClaude_ImplicitGlobalUsings.g.cs>");
    }

    private static SyntaxTree BuildWinFormsBootstrapTree(CSharpParseOptions parseOptions)
    {
        const string text = """
            namespace System.Windows.Forms
            {
                internal static class ApplicationConfiguration
                {
                    public static void Initialize() { }
                }
            }
            """;
        return CSharpSyntaxTree.ParseText(text, parseOptions, path: "<MonitorBaseClaude_WinFormsBootstrap.g.cs>");
    }

    private static IEnumerable<string> LoadGeneratedGlobalUsings(string observedRoot)
    {
        IEnumerable<string> candidates;
        try
        {
            candidates = Directory.EnumerateFiles(observedRoot, "*GlobalUsings.g.cs", SearchOption.AllDirectories)
                .Where(path => path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
        catch
        {
            yield break;
        }

        foreach (string path in candidates)
        {
            foreach (string line in File.ReadLines(path))
            {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith("global using ", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return trimmed.EndsWith(';') ? trimmed : trimmed + ";";
            }
        }
    }

    private static List<MetadataReference> GetMetadataReferences(string observedRoot)
    {
        Dictionary<string, MetadataReference> references = new(StringComparer.OrdinalIgnoreCase);
        string? tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (!string.IsNullOrWhiteSpace(tpa))
        {
            foreach (string path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                AddReference(references, path);
            }
        }

        foreach (string path in EnumerateObservedBinaryReferences(observedRoot))
        {
            AddReference(references, path);
        }

        foreach (string path in EnumerateWindowsDesktopReferences())
        {
            AddReference(references, path);
        }

        return references.Values.ToList();
    }

    private static IEnumerable<string> EnumerateObservedBinaryReferences(string observedRoot)
    {
        string binRoot = Path.Combine(observedRoot, "bin");
        if (!Directory.Exists(binRoot))
        {
            yield break;
        }

        foreach (string dllPath in Directory.EnumerateFiles(binRoot, "*.dll", SearchOption.AllDirectories))
        {
            yield return dllPath;
        }
    }

    private static IEnumerable<string> EnumerateWindowsDesktopReferences()
    {
        string desktopRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "dotnet",
            "shared",
            "Microsoft.WindowsDesktop.App");
        if (!Directory.Exists(desktopRoot))
        {
            yield break;
        }

        DirectoryInfo? latest = Directory.EnumerateDirectories(desktopRoot)
            .Select(path => new DirectoryInfo(path))
            .OrderByDescending(dir => dir.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (latest is null)
        {
            yield break;
        }

        foreach (string dllPath in Directory.EnumerateFiles(latest.FullName, "*.dll"))
        {
            yield return dllPath;
        }
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
            // Best-effort validation: a bad binary reference should not prevent all syntax trees from being checked.
        }
    }

    private static StagedEditMetadata DeriveStagedEditMetadata(
        string sourceFilePath,
        string originalContent,
        string stagedFilePath,
        string stagedContent)
    {
        if (!sourceFilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return new StagedEditMetadata([], [], [], [], stagedFilePath);
        }

        CompilationUnitSyntax originalRoot = CSharpSyntaxTree.ParseText(originalContent, path: sourceFilePath).GetCompilationUnitRoot();
        CompilationUnitSyntax stagedRoot = CSharpSyntaxTree.ParseText(stagedContent, path: stagedFilePath).GetCompilationUnitRoot();
        StagedSymbolMetadataEntry[] originalSymbols = GetSymbolMetadata(originalRoot.SyntaxTree, originalRoot).ToArray();
        StagedSymbolMetadataEntry[] stagedSymbols = GetSymbolMetadata(stagedRoot.SyntaxTree, stagedRoot).ToArray();
        HashSet<string> originalKeys = originalSymbols.Select(symbol => symbol.Key).ToHashSet(StringComparer.Ordinal);
        HashSet<string> stagedKeys = stagedSymbols.Select(symbol => symbol.Key).ToHashSet(StringComparer.Ordinal);
        string[] originalUsings = GetUsings(originalRoot);
        string[] stagedUsings = GetUsings(stagedRoot);

        return new StagedEditMetadata(
            stagedSymbols.Where(symbol => !originalKeys.Contains(symbol.Key)).Select(symbol => symbol.Metadata).ToArray(),
            originalSymbols.Where(symbol => !stagedKeys.Contains(symbol.Key)).Select(symbol => symbol.Metadata).ToArray(),
            stagedUsings.Except(originalUsings, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            originalUsings.Except(stagedUsings, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            stagedFilePath);
    }

    private static IEnumerable<StagedSymbolMetadataEntry> GetSymbolMetadata(SyntaxTree tree, CompilationUnitSyntax root)
    {
        return root.DescendantNodes()
            .OfType<MemberDeclarationSyntax>()
            .Where(IsOutlineMember)
            .Select(member =>
            {
                FileLinePositionSpan span = tree.GetLineSpan(member.Span);
                string text = member.NormalizeWhitespace().ToFullString();
                StagedSymbolMetadata metadata = new(
                    SymbolName(member),
                    SymbolKind(member),
                    span.StartLinePosition.Line + 1,
                    span.EndLinePosition.Line + 1,
                    ComputeSha256Text(text));
                return new StagedSymbolMetadataEntry(metadata, BuildStagedSymbolKey(member));
            });
    }

    private static string[] GetUsings(CompilationUnitSyntax root)
    {
        return root.Usings
            .Select(usingDirective => usingDirective.Name?.ToString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] GetDeclaredNamespaces(CompilationUnitSyntax root)
    {
        return root.DescendantNodes()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Select(BuildDeclaredNamespaceName)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static string BuildDeclaredNamespaceName(BaseNamespaceDeclarationSyntax namespaceDeclaration)
    {
        string[] containingNames = namespaceDeclaration.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Reverse()
            .Select(ancestor => ancestor.Name.ToString())
            .ToArray();
        string ownName = namespaceDeclaration.Name.ToString();
        return containingNames.Length == 0 ? ownName : string.Join(".", containingNames.Append(ownName));
    }

    private static MonitorSourceMapFile BuildSourceMapFile(string observedRoot, string sourcePath)
    {
        string text = File.ReadAllText(sourcePath);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(text, path: sourcePath);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
        Diagnostic[] diagnostics = root.GetDiagnostics().ToArray();
        MonitorSourceMapSymbol[] symbols = root.DescendantNodes()
            .OfType<MemberDeclarationSyntax>()
            .Where(IsOutlineMember)
            .Select(member => ToSourceMapSymbol(tree, member, Path.GetRelativePath(observedRoot, sourcePath)))
            .OrderBy(symbol => symbol.StartLine)
            .ThenBy(symbol => symbol.Kind, StringComparer.Ordinal)
            .ThenBy(symbol => symbol.Name, StringComparer.Ordinal)
            .ToArray();

        return new MonitorSourceMapFile(
            sourcePath,
            Path.GetRelativePath(observedRoot, sourcePath),
            ComputeSha256(sourcePath),
            new FileInfo(sourcePath).Length,
            GetParseStatus(diagnostics),
            diagnostics.Length,
            diagnostics.Select(ToSourceMapDiagnostic).Take(10).ToArray(),
            GetUsings(root),
            GetDeclaredNamespaces(root),
            symbols);
    }

    private static MonitorSourceMapFile ShapeSourceMapFile(MonitorSourceMapFile file, string mode)
    {
        if (mode.Equals("full", StringComparison.OrdinalIgnoreCase))
        {
            return file;
        }

        MonitorSourceMapSymbol[] symbols = file.Symbols
            .Select(symbol => ShapeSourceMapSymbol(symbol, mode))
            .ToArray();

        if (mode.Equals("selector", StringComparison.OrdinalIgnoreCase))
        {
            return file with
            {
                SourceFilePath = null,
                DiagnosticsSummary = file.DiagnosticCount > 0 ? file.DiagnosticsSummary : null,
                Usings = NullIfEmpty(file.Usings),
                Namespaces = NullIfEmpty(file.Namespaces),
                Symbols = symbols
            };
        }

        if (mode.Equals("detail", StringComparison.OrdinalIgnoreCase))
        {
            return file with
            {
                SourceFilePath = null,
                DiagnosticsSummary = file.DiagnosticCount > 0 ? file.DiagnosticsSummary : null,
                Usings = NullIfEmpty(file.Usings),
                Namespaces = NullIfEmpty(file.Namespaces),
                Symbols = symbols
            };
        }

        return file with
        {
            SourceFilePath = null,
            Sha256 = null,
            Length = null,
            DiagnosticsSummary = file.DiagnosticCount > 0 ? file.DiagnosticsSummary : null,
            Usings = null,
            Namespaces = NullIfEmpty(file.Namespaces),
            Symbols = symbols
        };
    }

    private static MonitorSourceMapSymbol ShapeSourceMapSymbol(MonitorSourceMapSymbol symbol, string mode)
    {
        if (mode.Equals("full", StringComparison.OrdinalIgnoreCase))
        {
            return symbol;
        }

        if (mode.Equals("selector", StringComparison.OrdinalIgnoreCase))
        {
            return symbol with
            {
                BaseTypes = NullIfEmpty(symbol.BaseTypes),
                Attributes = NullIfEmpty(ToAttributeNamesOnly(symbol.Attributes)),
                Modifiers = NullIfEmpty(symbol.Modifiers),
                ParameterTypes = NullIfEmpty(symbol.ParameterTypes),
                ParameterNames = NullIfEmpty(symbol.ParameterNames),
                IsPartial = symbol.IsPartial == true ? true : null
            };
        }

        if (mode.Equals("detail", StringComparison.OrdinalIgnoreCase))
        {
            return symbol with
            {
                BaseTypes = NullIfEmpty(symbol.BaseTypes),
                Attributes = NullIfEmpty(symbol.Attributes),
                Modifiers = NullIfEmpty(symbol.Modifiers),
                ParameterTypes = NullIfEmpty(symbol.ParameterTypes),
                ParameterNames = NullIfEmpty(symbol.ParameterNames),
                IsPartial = symbol.IsPartial == true ? true : null
            };
        }

        return symbol with
        {
            StableSymbolKey = null,
            Namespace = string.IsNullOrWhiteSpace(symbol.Namespace) ? null : symbol.Namespace,
            BaseTypes = NullIfEmpty(symbol.BaseTypes),
            Attributes = NullIfEmpty(ToAttributeNamesOnly(symbol.Attributes)),
            HasDocumentation = null,
            HasAttributes = symbol.HasAttributes == true ? true : null,
            TextHash = null,
            Modifiers = null,
            ReturnType = null,
            ParameterTypes = null,
            ParameterNames = null,
            Arity = null,
            IsStatic = null,
            IsAsync = null,
            IsOverride = null,
            IsVirtual = null,
            SyntaxKind = null,
            IsPartial = symbol.IsPartial == true ? true : null
        };
    }

    private static IReadOnlyList<MonitorSourceMapAttribute>? ToAttributeNamesOnly(IReadOnlyList<MonitorSourceMapAttribute>? attributes)
    {
        return attributes?.Select(attribute => new MonitorSourceMapAttribute(attribute.Name, null)).ToArray();
    }

    private static IReadOnlyList<T>? NullIfEmpty<T>(IReadOnlyList<T>? values)
    {
        return values is null || values.Count == 0 ? null : values;
    }

    private static long EstimateSourceMapTokenProxy(MonitorSourceMapResult result)
    {
        string json = JsonSerializer.Serialize(result, SourceMapResponseJsonOptions);
        return Math.Max(1, (json.Length + 3L) / 4L);
    }

    private static IReadOnlyList<MonitorSourceMapNarrowingSuggestion> BuildSourceMapNarrowingSuggestions(IReadOnlyList<MonitorSourceMapFile> files)
    {
        return files
            .OrderByDescending(file => file.Symbols.Count)
            .ThenBy(file => file.RelativeSourcePath, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .Select(file => new MonitorSourceMapNarrowingSuggestion(
                file.RelativeSourcePath,
                file.DiagnosticCount > 0 ? "diagnostics-present" : "high-symbol-count",
                file.Symbols.Count,
                file.DiagnosticCount))
            .ToArray();
    }

    private static IReadOnlyList<MonitorSourceMapNextCall>? BuildSourceMapNextCalls(IReadOnlyList<MonitorSourceMapFile> files, string mode)
    {
        if (mode.Equals("navigation", StringComparison.OrdinalIgnoreCase))
        {
            List<MonitorSourceMapNextCall> calls = files
                .OrderByDescending(file => file.DiagnosticCount)
                .ThenByDescending(file => file.Symbols.Count)
                .ThenBy(file => file.RelativeSourcePath, StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .Select((file, index) => new MonitorSourceMapNextCall(
                    index + 1,
                    "get_source_map",
                    file.DiagnosticCount > 0 ? "inspect-file-with-diagnostics" : "inspect-file-selectors",
                    new Dictionary<string, string>
                    {
                        ["path"] = file.RelativeSourcePath,
                        ["scope"] = "file",
                        ["mode"] = "selector"
                    }))
                .ToList();
            AddUsingNamespaceNextCalls(calls, files, calls.Count + 1);
            return NullIfEmpty(calls);
        }

        if (mode.Equals("selector", StringComparison.OrdinalIgnoreCase))
        {
            List<MonitorSourceMapNextCall> calls = files
                .SelectMany(file => file.Symbols
                    .Where(symbol => !string.IsNullOrWhiteSpace(symbol.StableSymbolKey))
                    .Where(symbol => symbol.Kind is "method" or "constructor" or "property" or "event" or "field")
                    .OrderBy(symbol => SourceMapSymbolNextCallRank(symbol.Kind))
                    .ThenBy(symbol => symbol.StartLine)
                    .Select(symbol => new { File = file, Symbol = symbol }))
                .Take(10)
                .Select((item, index) => new MonitorSourceMapNextCall(
                    index + 1,
                    "get_symbol",
                    "read-selected-symbol-body",
                    new Dictionary<string, string>
                    {
                        ["path"] = item.File.RelativeSourcePath,
                        ["symbolSelectorJson"] = BuildStableKeySelectorJson(item.Symbol)
                    }))
                .ToList();
            AddUsingNamespaceNextCalls(calls, files, calls.Count + 1);
            return NullIfEmpty(calls);
        }

        return null;
    }

    private static void AddUsingNamespaceNextCalls(List<MonitorSourceMapNextCall> calls, IReadOnlyList<MonitorSourceMapFile> files, int startRank)
    {
        foreach (string usingNamespace in files
            .SelectMany(file => file.Usings ?? [])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .Take(6))
        {
            calls.Add(new MonitorSourceMapNextCall(
                startRank++,
                "get_source_map",
                "inspect-referenced-namespace-surface",
                new Dictionary<string, string>
                {
                    ["scope"] = "namespace",
                    ["namespaceName"] = usingNamespace,
                    ["mode"] = "navigation"
                }));
        }
    }

    private static int SourceMapSymbolNextCallRank(string kind)
    {
        return kind switch
        {
            "method" => 0,
            "constructor" => 1,
            "property" => 2,
            "event" => 3,
            "field" => 4,
            _ => 9
        };
    }

    private static string BuildSymbolSelectorJson(MonitorSourceMapSymbol symbol)
    {
        Dictionary<string, object?> selector = [];
        AddSelectorValue(selector, "stableSymbolKey", symbol.StableSymbolKey);
        AddSelectorValue(selector, "memberKind", symbol.Kind);
        AddSelectorValue(selector, "containingNamespace", symbol.Namespace);
        AddSelectorValue(selector, "containingType", symbol.ContainingType);
        AddSelectorValue(selector, "name", symbol.Name);
        AddSelectorValue(selector, "parameterTypes", symbol.ParameterTypes);
        AddSelectorValue(selector, "arity", symbol.Arity);
        return JsonSerializer.Serialize(selector, SourceMapResponseJsonOptions);
    }

    private static string BuildStableKeySelectorJson(MonitorSourceMapSymbol symbol)
    {
        Dictionary<string, object?> selector = [];
        AddSelectorValue(selector, "stableSymbolKey", symbol.StableSymbolKey);
        return JsonSerializer.Serialize(selector, SourceMapResponseJsonOptions);
    }

    private static void AddSelectorValue(Dictionary<string, object?> selector, string key, object? value)
    {
        if (value is null)
        {
            return;
        }

        if (value is string text && string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (value is IReadOnlyCollection<string> list && list.Count == 0)
        {
            return;
        }

        selector[key] = value;
    }

    private static MonitorSourceMapSymbol ToSourceMapSymbol(SyntaxTree tree, MemberDeclarationSyntax member, string relativeSourcePath)
    {
        FileLinePositionSpan span = tree.GetLineSpan(member.Span);
        string text = member.NormalizeWhitespace().ToFullString();
        return new MonitorSourceMapSymbol(
            SymbolKind(member),
            SymbolName(member),
            BuildStableSymbolKey(relativeSourcePath, member),
            BuildContractSignature(member),
            BuildNamespace(member),
            BuildContainingType(member),
            BuildBaseTypes(member),
            GetAttributeSummaries(member),
            span.StartLinePosition.Line + 1,
            span.EndLinePosition.Line + 1,
            HasLeadingDocumentation(member),
            HasVisibleAttributes(member),
            ComputeSha256Text(text),
            GetModifiers(member),
            GetReturnType(member),
            GetParameterTypes(member),
            GetParameterNames(member),
            GetArity(member),
            HasModifier(member, SyntaxKind.StaticKeyword),
            HasModifier(member, SyntaxKind.AsyncKeyword),
            HasModifier(member, SyntaxKind.OverrideKeyword),
            HasModifier(member, SyntaxKind.VirtualKeyword),
            member.Kind().ToString(),
            IsPartialTypeDeclaration(member));
    }

    private static string GetParseStatus(IReadOnlyList<Diagnostic> diagnostics)
    {
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return "parse-error";
        }

        return diagnostics.Count == 0 ? "ok" : "parse-warning";
    }

    private static MonitorSourceMapDiagnostic ToSourceMapDiagnostic(Diagnostic diagnostic)
    {
        int line = 0;
        int column = 0;
        if (diagnostic.Location.IsInSource)
        {
            FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
            line = span.StartLinePosition.Line + 1;
            column = span.StartLinePosition.Character + 1;
        }

        return new MonitorSourceMapDiagnostic(
            diagnostic.Severity.ToString(),
            diagnostic.Id,
            line,
            column,
            diagnostic.GetMessage());
    }

    private static string BuildStableSymbolKey(string relativeSourcePath, MemberDeclarationSyntax member)
    {
        string namespaceName = BuildNamespace(member);
        string containingType = BuildContainingType(member) ?? string.Empty;
        string signatureKey = member switch
        {
            MethodDeclarationSyntax method => $"{method.Identifier.ValueText}({string.Join(",", method.ParameterList.Parameters.Select(ParameterKey))})",
            ConstructorDeclarationSyntax constructor => $"{constructor.Identifier.ValueText}({string.Join(",", constructor.ParameterList.Parameters.Select(ParameterKey))})",
            DelegateDeclarationSyntax del => $"{del.Identifier.ValueText}({string.Join(",", del.ParameterList.Parameters.Select(ParameterKey))})",
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            EventDeclarationSyntax evt => evt.Identifier.ValueText,
            EventFieldDeclarationSyntax eventField => string.Join(",", eventField.Declaration.Variables.Select(variable => variable.Identifier.ValueText)),
            FieldDeclarationSyntax field => string.Join(",", field.Declaration.Variables.Select(variable => variable.Identifier.ValueText)),
            BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
            _ => SymbolName(member)
        };

        return $"{NormalizePath(relativeSourcePath)}::{namespaceName}::{containingType}::{SymbolKind(member)}::{signatureKey}";
    }

    private static string ParameterKey(ParameterSyntax parameter)
    {
        string modifier = parameter.Modifiers.ToFullString().Trim();
        string type = parameter.Type?.ToString() ?? string.Empty;
        return string.IsNullOrWhiteSpace(modifier) ? type : $"{modifier} {type}";
    }

    private static string BuildNamespace(SyntaxNode node)
    {
        string[] names = node.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Reverse()
            .Select(namespaceDeclaration => namespaceDeclaration.Name.ToString())
            .ToArray();
        return string.Join(".", names);
    }

    private static string? BuildContainingType(MemberDeclarationSyntax member)
    {
        string[] names = member.Ancestors()
            .OfType<BaseTypeDeclarationSyntax>()
            .Reverse()
            .Select(type => type.Identifier.ValueText)
            .ToArray();
        return names.Length == 0 ? null : string.Join(".", names);
    }

    private static IReadOnlyList<string> BuildBaseTypes(MemberDeclarationSyntax member)
    {
        if (member is not BaseTypeDeclarationSyntax type || type.BaseList is null)
        {
            return Array.Empty<string>();
        }

        return type.BaseList.Types
            .Select(baseType => baseType.Type.ToString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    private static bool HasLeadingDocumentation(MemberDeclarationSyntax member)
    {
        return member.GetLeadingTrivia().Any(trivia =>
            trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
            || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia)
            || trivia.GetStructure() is DocumentationCommentTriviaSyntax);
    }

    private static bool HasVisibleAttributes(MemberDeclarationSyntax member)
    {
        return member.AttributeLists
            .SelectMany(list => list.Attributes)
            .Any(attribute => !ShouldSkipSourceMapAttribute(attribute.Name.ToString()));
    }

    private static IReadOnlyList<MonitorSourceMapAttribute> GetAttributeSummaries(MemberDeclarationSyntax member)
    {
        return member.AttributeLists
            .SelectMany(list => list.Attributes)
            .Where(attribute => !ShouldSkipSourceMapAttribute(attribute.Name.ToString()))
            .Select(attribute => new MonitorSourceMapAttribute(
                attribute.Name.ToString(),
                attribute.ArgumentList?.Arguments.ToFullString().Trim()))
            .ToArray();
    }

    private static bool ShouldSkipSourceMapAttribute(string attributeName)
    {
        string name = attributeName.EndsWith("Attribute", StringComparison.Ordinal)
            ? attributeName[..^"Attribute".Length]
            : attributeName;
        if (name.Equals("AIFileContext", StringComparison.Ordinal)
            || name.Equals("FileVersion", StringComparison.Ordinal))
        {
            return false;
        }

        return name.Equals("AIChange", StringComparison.Ordinal)
            || name.Equals("AIHistory", StringComparison.Ordinal)
            || name.Equals("AIInstructions", StringComparison.Ordinal)
            || name.Equals("UserHistory", StringComparison.Ordinal)
            || name.StartsWith("AI", StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> GetModifiers(MemberDeclarationSyntax member)
    {
        SyntaxTokenList modifiers = member switch
        {
            BaseTypeDeclarationSyntax type => type.Modifiers,
            BaseMethodDeclarationSyntax method => method.Modifiers,
            EventDeclarationSyntax evt => evt.Modifiers,
            EventFieldDeclarationSyntax eventField => eventField.Modifiers,
            BasePropertyDeclarationSyntax property => property.Modifiers,
            FieldDeclarationSyntax field => field.Modifiers,
            DelegateDeclarationSyntax del => del.Modifiers,
            _ => default
        };

        return modifiers.Select(modifier => modifier.ValueText).ToArray();
    }

    private static bool HasModifier(MemberDeclarationSyntax member, SyntaxKind modifierKind)
    {
        SyntaxTokenList modifiers = member switch
        {
            BaseTypeDeclarationSyntax type => type.Modifiers,
            BaseMethodDeclarationSyntax method => method.Modifiers,
            EventDeclarationSyntax evt => evt.Modifiers,
            EventFieldDeclarationSyntax eventField => eventField.Modifiers,
            BasePropertyDeclarationSyntax property => property.Modifiers,
            FieldDeclarationSyntax field => field.Modifiers,
            DelegateDeclarationSyntax del => del.Modifiers,
            _ => default
        };

        return modifiers.Any(modifier => modifier.IsKind(modifierKind));
    }

    private static string? GetReturnType(MemberDeclarationSyntax member)
    {
        return member switch
        {
            MethodDeclarationSyntax method => method.ReturnType.ToString(),
            PropertyDeclarationSyntax property => property.Type.ToString(),
            FieldDeclarationSyntax field => field.Declaration.Type.ToString(),
            EventFieldDeclarationSyntax eventField => eventField.Declaration.Type.ToString(),
            EventDeclarationSyntax evt => evt.Type.ToString(),
            DelegateDeclarationSyntax del => del.ReturnType.ToString(),
            _ => null
        };
    }

    private static IReadOnlyList<string> GetParameterTypes(MemberDeclarationSyntax member)
    {
        return GetParameters(member)
            .Select(parameter => parameter.Type?.ToString() ?? string.Empty)
            .ToArray();
    }

    private static IReadOnlyList<string> GetParameterNames(MemberDeclarationSyntax member)
    {
        return GetParameters(member)
            .Select(parameter => parameter.Identifier.ValueText)
            .ToArray();
    }

    private static IEnumerable<ParameterSyntax> GetParameters(MemberDeclarationSyntax member)
    {
        return member switch
        {
            MethodDeclarationSyntax method => method.ParameterList.Parameters,
            ConstructorDeclarationSyntax constructor => constructor.ParameterList.Parameters,
            DelegateDeclarationSyntax del => del.ParameterList.Parameters,
            _ => []
        };
    }

    private static string BuildStagedSymbolKey(MemberDeclarationSyntax member)
    {
        string namespaceName = BuildNamespace(member);
        string containingType = BuildContainingType(member) ?? string.Empty;
        string signatureKey = member switch
        {
            MethodDeclarationSyntax method => $"{method.Identifier.ValueText}({string.Join(",", method.ParameterList.Parameters.Select(ParameterKey))})",
            ConstructorDeclarationSyntax constructor => $"{constructor.Identifier.ValueText}({string.Join(",", constructor.ParameterList.Parameters.Select(ParameterKey))})",
            DelegateDeclarationSyntax del => $"{del.Identifier.ValueText}({string.Join(",", del.ParameterList.Parameters.Select(ParameterKey))})",
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            EventDeclarationSyntax evt => evt.Identifier.ValueText,
            EventFieldDeclarationSyntax eventField => string.Join(",", eventField.Declaration.Variables.Select(variable => variable.Identifier.ValueText)),
            FieldDeclarationSyntax field => string.Join(",", field.Declaration.Variables.Select(variable => variable.Identifier.ValueText)),
            BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
            _ => SymbolName(member)
        };
        return $"{namespaceName}|{containingType}|{SymbolKind(member)}|{signatureKey}";
    }

    private static bool IsOutlineMember(MemberDeclarationSyntax member)
    {
        return member is BaseTypeDeclarationSyntax
            or MethodDeclarationSyntax
            or ConstructorDeclarationSyntax
            or PropertyDeclarationSyntax
            or FieldDeclarationSyntax
            or EventFieldDeclarationSyntax
            or EventDeclarationSyntax
            or DelegateDeclarationSyntax;
    }

    private static bool IsPartialTypeDeclaration(MemberDeclarationSyntax member)
    {
        return member is BaseTypeDeclarationSyntax && HasModifier(member, SyntaxKind.PartialKeyword);
    }

    private static MonitorSymbolOutline ToSymbolOutline(SyntaxTree tree, MemberDeclarationSyntax member)
    {
        FileLinePositionSpan span = tree.GetLineSpan(member.Span);
        return new MonitorSymbolOutline(
            SymbolKind(member),
            SymbolName(member),
            span.StartLinePosition.Line + 1,
            span.EndLinePosition.Line + 1,
            BuildSignature(member));
    }

    private static string SymbolKind(MemberDeclarationSyntax member)
    {
        return member switch
        {
            ClassDeclarationSyntax => "class",
            StructDeclarationSyntax => "struct",
            InterfaceDeclarationSyntax => "interface",
            RecordDeclarationSyntax => "record",
            EnumDeclarationSyntax => "enum",
            MethodDeclarationSyntax => "method",
            ConstructorDeclarationSyntax => "constructor",
            PropertyDeclarationSyntax => "property",
            FieldDeclarationSyntax => "field",
            EventFieldDeclarationSyntax => "event",
            EventDeclarationSyntax => "event",
            DelegateDeclarationSyntax => "delegate",
            _ => member.Kind().ToString()
        };
    }

    private static string SymbolName(MemberDeclarationSyntax member)
    {
        return member switch
        {
            BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            FieldDeclarationSyntax field => string.Join(", ", field.Declaration.Variables.Select(variable => variable.Identifier.ValueText)),
            EventFieldDeclarationSyntax eventField => string.Join(", ", eventField.Declaration.Variables.Select(variable => variable.Identifier.ValueText)),
            EventDeclarationSyntax evt => evt.Identifier.ValueText,
            DelegateDeclarationSyntax del => del.Identifier.ValueText,
            _ => member.Kind().ToString()
        };
    }

    private static string BuildSignature(MemberDeclarationSyntax member)
    {
        MemberDeclarationSyntax cleanMember = member.WithoutLeadingTrivia();
        if (cleanMember is PropertyDeclarationSyntax property)
        {
            return BuildPropertySignature(property);
        }

        MemberDeclarationSyntax signatureOnly = cleanMember switch
        {
            ClassDeclarationSyntax type => type.WithMembers(default).NormalizeWhitespace(),
            StructDeclarationSyntax type => type.WithMembers(default).NormalizeWhitespace(),
            InterfaceDeclarationSyntax type => type.WithMembers(default).NormalizeWhitespace(),
            RecordDeclarationSyntax type => type.WithMembers(default).NormalizeWhitespace(),
            MethodDeclarationSyntax method => method.WithBody(null).WithExpressionBody(null).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)).NormalizeWhitespace(),
            ConstructorDeclarationSyntax constructor => constructor.WithBody(null).WithExpressionBody(null).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)).NormalizeWhitespace(),
            EventDeclarationSyntax evt => evt.WithAccessorList(null).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)).NormalizeWhitespace(),
            _ => cleanMember.NormalizeWhitespace()
        };

        return signatureOnly.ToFullString().Replace(Environment.NewLine, " ", StringComparison.Ordinal).Trim();
    }

    private static string BuildContractSignature(MemberDeclarationSyntax member)
    {
        string modifiers = string.Join(" ", GetModifiers(member));
        string prefix = string.IsNullOrWhiteSpace(modifiers) ? string.Empty : $"{modifiers} ";
        return member switch
        {
            MethodDeclarationSyntax method => $"{prefix}{method.ReturnType} {method.Identifier.ValueText}({BuildParameterList(method.ParameterList.Parameters)})",
            ConstructorDeclarationSyntax constructor => $"{prefix}{constructor.Identifier.ValueText}({BuildParameterList(constructor.ParameterList.Parameters)})",
            PropertyDeclarationSyntax property => BuildPropertySignature(property),
            FieldDeclarationSyntax field => $"{prefix}{field.Declaration.Type} {BuildVariableList(field.Declaration.Variables)}",
            EventFieldDeclarationSyntax eventField => $"{prefix}event {eventField.Declaration.Type} {BuildVariableList(eventField.Declaration.Variables)}",
            EventDeclarationSyntax evt => $"{prefix}event {evt.Type} {evt.Identifier.ValueText}",
            DelegateDeclarationSyntax del => $"{prefix}delegate {del.ReturnType} {del.Identifier.ValueText}({BuildParameterList(del.ParameterList.Parameters)})",
            BaseTypeDeclarationSyntax type => $"{prefix}{GetTypeDeclarationKeyword(type)} {type.Identifier.ValueText}{BuildBaseListSuffix(type)}",
            _ => BuildSignature(member)
        };
    }

    private static string BuildParameterList(SeparatedSyntaxList<ParameterSyntax> parameters)
    {
        return string.Join(", ", parameters.Select(parameter =>
        {
            string modifiers = parameter.Modifiers.ToFullString().Trim();
            string prefix = string.IsNullOrWhiteSpace(modifiers) ? string.Empty : $"{modifiers} ";
            string defaultValue = parameter.Default is null ? string.Empty : $" = {parameter.Default.Value}";
            return $"{prefix}{parameter.Type} {parameter.Identifier.ValueText}{defaultValue}";
        }));
    }

    private static string BuildVariableList(SeparatedSyntaxList<VariableDeclaratorSyntax> variables)
    {
        return string.Join(", ", variables.Select(variable => variable.ToString()));
    }

    private static string BuildBaseListSuffix(BaseTypeDeclarationSyntax type)
    {
        return type.BaseList is null ? string.Empty : $" {type.BaseList}";
    }

    private static string GetTypeDeclarationKeyword(BaseTypeDeclarationSyntax type)
    {
        return type switch
        {
            ClassDeclarationSyntax => "class",
            StructDeclarationSyntax => "struct",
            InterfaceDeclarationSyntax => "interface",
            RecordDeclarationSyntax record => record.ClassOrStructKeyword.ValueText.Length == 0
                ? "record"
                : $"record {record.ClassOrStructKeyword.ValueText}",
            _ => type.Kind().ToString()
        };
    }

    private static string BuildPropertySignature(PropertyDeclarationSyntax property)
    {
        string modifiers = property.Modifiers.ToFullString().Trim();
        string prefix = string.IsNullOrWhiteSpace(modifiers) ? string.Empty : $"{modifiers} ";
        string accessors = property.AccessorList is null
            ? "get;"
            : string.Join(" ", property.AccessorList.Accessors.Select(BuildAccessorSignature));
        string initializer = property.Initializer is null ? string.Empty : $" {property.Initializer}";
        string terminator = property.Initializer is null ? string.Empty : ";";
        return $"{prefix}{property.Type} {property.Identifier.ValueText} {{ {accessors} }}{initializer}{terminator}";
    }

    private static string BuildAccessorSignature(AccessorDeclarationSyntax accessor)
    {
        string modifiers = accessor.Modifiers.ToFullString().Trim();
        string prefix = string.IsNullOrWhiteSpace(modifiers) ? string.Empty : $"{modifiers} ";
        return $"{prefix}{accessor.Keyword.ValueText};";
    }

    private static void AddArguments(ProcessStartInfo startInfo, params string[] arguments)
    {
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
    }

    private static string FormatArgumentList(ProcessStartInfo startInfo)
    {
        return string.Join(" ", startInfo.ArgumentList.Select(argument => argument.Contains(' ') ? $"\"{argument}\"" : argument));
    }

    private static string QuoteCommandArgument(string argument)
    {
        return "\"" + argument.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static string TrimDirectorySeparator(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static string SanitizeForFileName(string value)
    {
        StringBuilder builder = new(value.Length);
        foreach (char c in value)
        {
            builder.Append(Path.GetInvalidFileNameChars().Contains(c) ? '_' : c);
        }

        return builder.ToString();
    }

    private static string CreateStagedRecordId(string operation, string sourceName)
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmssfff");
        string suffix = Guid.NewGuid().ToString("N")[..8];
        return $"{stamp}_{operation}_{SanitizeForFileName(sourceName)}_{suffix}";
    }
}

[Description("Refresh a watched source file into the monitor-owned Working folder.")]
public sealed record MonitorFileRefreshResult(
    string Status,
    string SourceFilePath,
    string WatchedProjectFolder,
    string ObservedRootKey,
    string RelativeSourcePath,
    string WorkingFilePath,
    string RefreshStatePath);

public sealed record MonitorCandidateEditResult(
    string Status,
    string SourceFilePath,
    string WatchedProjectFolder,
    string ObservedRootKey,
    string RelativeSourcePath,
    string CandidateFilePath,
    string CandidateStatePath,
    string BaselineHash,
    string CandidateHash,
    int OperationCount,
    MonitorSyntaxValidationResult SyntaxValidation,
    MonitorOverlayValidationResult OverlayValidation,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record RazorCompanionSplitStageResult(
    string Status,
    string SessionId,
    string SourceFilePath,
    string RazorRelativePath,
    string CompanionRelativePath,
    MonitorCandidateEditResult RazorCandidate,
    MonitorFileSubmitResult RazorStage,
    MonitorCandidateEditResult CompanionCandidate,
    MonitorFileSubmitResult CompanionStage,
    IReadOnlyList<string> Warnings);

public sealed record MonitorTextSpanResult(
    string Status,
    string SourceFilePath,
    string WatchedProjectFolder,
    string ObservedRootKey,
    string RelativeSourcePath,
    string EditBaseFilePath,
    string EditBaseHash,
    string FoundTextHash,
    int OccurrenceCount,
    int OccurrenceIndex,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn);

[Description("Compare a monitor Working file with the watched source file using WinMerge.")]
public sealed record MonitorFileCompareResult(
    string Status,
    string SourceFilePath,
    string WorkingFilePath,
    string ProposedFilePath,
    int? ProcessId,
    string? DiffToolPath,
    string? DiffToolArguments,
    bool RefreshedBeforeCompare)
{
    public static MonitorFileCompareResult NotLaunched(
        string status,
        string sourceFilePath,
        string workingFilePath,
        string proposedFilePath,
        string message,
        bool refreshedBeforeCompare = false)
    {
        return new MonitorFileCompareResult(status, sourceFilePath, workingFilePath, proposedFilePath, null, null, message, refreshedBeforeCompare);
    }
}

public sealed record MonitorWorkflowStatus(
    string UiRoot,
    string McpServerRoot,
    string LegacyMonitorRoot,
    string WatchedSolutionPath,
    string WatchedProjectFolder,
    string WorkingRoot,
    string? WinMergePath,
    bool WatchedSolutionExists,
    bool WatchedProjectFolderExists);

[Description("Read a watched source file through the Monitor MCP server.")]
public sealed record MonitorFileReadResult(
    string SourceFilePath,
    string WatchedProjectFolder,
    string RelativeSourcePath,
    string Text,
    int TextLength);

[Description("Current hash and timestamp for a watched source file.")]
public sealed record MonitorFileHashInfo(
    string SourceFilePath,
    string WatchedProjectFolder,
    string RelativeSourcePath,
    string Sha256,
    long Length,
    DateTime LastWriteUtc);

[Description("C# file outline with signatures and line spans but without method bodies.")]
public sealed record MonitorFileOutlineResult(
    string SourceFilePath,
    string RelativeSourcePath,
    IReadOnlyList<MonitorSymbolOutline> Symbols);

public sealed record MonitorSymbolOutline(
    string Kind,
    string Name,
    int StartLine,
    int EndLine,
    string Signature);

[Description("Roslyn-derived source map for a C# file, folder, or watched project.")]
public sealed record MonitorSourceMapResult(
    string Scope,
    string Mode,
    string ModePurpose,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RequestedPath,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RequestedNamespace,
    string WatchedProjectAlias,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? WatchedProjectFolder,
    int FileCount,
    int SymbolCount,
    long EstimatedTokenProxy,
    int BudgetLimit,
    bool WasTruncated,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<MonitorSourceMapNarrowingSuggestion>? SuggestedNarrowing,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<MonitorSourceMapNextCall>? SuggestedNextCalls,
    IReadOnlyList<MonitorSourceMapFile> Files);

public sealed record MonitorSourceMapFile(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SourceFilePath,
    string RelativeSourcePath,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Sha256,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? Length,
    string ParseStatus,
    int DiagnosticCount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<MonitorSourceMapDiagnostic>? DiagnosticsSummary,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Usings,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Namespaces,
    IReadOnlyList<MonitorSourceMapSymbol> Symbols);

public sealed record MonitorSourceMapDiagnostic(
    string Severity,
    string Id,
    int Line,
    int Column,
    string Message);

public sealed record MonitorSourceMapSymbol(
    string Kind,
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? StableSymbolKey,
    string Signature,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Namespace,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ContainingType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? BaseTypes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<MonitorSourceMapAttribute>? Attributes,
    int StartLine,
    int EndLine,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? HasDocumentation,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? HasAttributes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TextHash,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Modifiers,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReturnType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? ParameterTypes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? ParameterNames,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Arity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IsStatic,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IsAsync,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IsOverride,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IsVirtual,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SyntaxKind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IsPartial);

public sealed record MonitorSourceMapAttribute(
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ArgumentsSummary);

public sealed record MonitorSourceMapNarrowingSuggestion(
    string RelativeSourcePath,
    string Reason,
    int SymbolCount,
    int DiagnosticCount);

public sealed record MonitorSourceMapNextCall(
    int Rank,
    string Tool,
    string Reason,
    IReadOnlyDictionary<string, string> Arguments);

public sealed record MonitorSessionStagedRecordsResult(
    string SessionId,
    int Count,
    IReadOnlyList<MonitorSessionStagedRecordSummary> Records);

public sealed record MonitorSessionStagedRecordSummary(
    string RecordId,
    string? SessionId,
    string RelativeSourcePath,
    string SourceFilePath,
    string Operation,
    DateTimeOffset CreatedAt,
    string QueueStatus,
    string StagedFilePath,
    string OriginalHash,
    string StagedHash,
    bool SyntaxHasErrors,
    string OverlayStatus,
    bool OverlayHasErrors,
    int OverlayFileCount,
    string StagedRecordPath,
    string? NewFileReviewBaselinePath,
    string? ManifestJson);

public sealed record MonitorSymbolSelector(
    string? ContainingNamespace = null,
    string? ContainingType = null,
    string? MemberKind = null,
    string? Name = null,
    IReadOnlyList<string>? ParameterTypes = null,
    int? Arity = null,
    string? StableSymbolKey = null);

[Description("One symbol body read from a watched C# source file.")]
public sealed record MonitorSymbolReadResult(
    string SourceFilePath,
    string RelativeSourcePath,
    string Kind,
    string Name,
    int StartLine,
    int EndLine,
    string Text);

[Description("Stages a full-file replacement under monitor-owned Working\\Staged.")]
public sealed record MonitorFileSubmitResult(
    string Status,
    string SourceFilePath,
    string RelativeSourcePath,
    string StagedFilePath,
    string StagedRecordId,
    string StagedRecordPath,
    string OriginalHash,
    string StagedHash,
    StagedEditMetadata ServerDerivedMetadata,
    MonitorSyntaxValidationResult SyntaxValidation,
    MonitorOverlayValidationResult OverlayValidation,
    bool DiffRequested,
    string? DiffToolPath,
    string? DiffToolArguments,
    int? ProcessId,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record MonitorDiffDecisionResult(
    string StagedRecordId,
    string? SessionId,
    string SourceFilePath,
    string RelativeSourcePath,
    string StagedRecordPath,
    string StagedFilePath,
    string OperatorDecision,
    string Classification,
    bool DecisionMatchesClassification,
    bool BlocksFurtherEdits,
    string OriginalHash,
    string StagedHash,
    string CurrentHash,
    string QueueStatus,
    string? Note,
    DateTimeOffset DecidedAt,
    string? DecisionRecordPath = null,
    string? OriginalNormalizedHash = null,
    string? StagedNormalizedHash = null,
    string? CurrentNormalizedHash = null,
    MonitorIndexRefreshDecisionResult? IndexRefresh = null);

public sealed record MonitorIndexRefreshDecisionResult(
    string Status,
    string Reason,
    string Scope,
    string SourceFilePath,
    int? FileCount = null,
    int? SymbolCount = null,
    int? DiagnosticCount = null,
    int? ReferenceCount = null,
    int? CallSiteCount = null,
    int? StaleFileCount = null,
    DateTimeOffset? IndexedAtUtc = null,
    double? DurationMilliseconds = null,
    int? PendingSessionRecordCount = null);

public sealed record MonitorStagedDiffLaunchResult(
    string Status,
    string StagedRecordId,
    string SourceFilePath,
    string RelativeSourcePath,
    string StagedRecordPath,
    string StagedFilePath,
    string? DiffToolPath,
    int? ProcessId,
    string? DiffToolArguments,
    string NextStep,
    string? ValidationGateStatus = null,
    string? ValidationGateDecision = null,
    string? ValidationGateMessage = null)
{
    public static MonitorStagedDiffLaunchResult NotLaunched(
        string status,
        StagedEditRecord record,
        string recordPath,
        string stagedFilePath,
        string? diffToolPath,
        string message)
    {
        return new MonitorStagedDiffLaunchResult(
            status,
            record.RecordId,
            record.SourceFilePath,
            record.RelativeSourcePath,
            recordPath,
            stagedFilePath,
            diffToolPath,
            null,
            message,
            "Review was not launched. Fix the reported issue, then retry launch_staged_diff before calling record_diff_decision.");
    }
}

internal sealed record HostValidationGateDecision(
    string Status,
    string Decision,
    string Message);

public sealed record StagedEditRecord(
    string RecordId,
    string? SessionId,
    string SourceFilePath,
    string RelativeSourcePath,
    string Operation,
    DateTimeOffset CreatedAt,
    string OriginalHash,
    string StagedHash,
    string? ManifestJson,
    StagedEditMetadata ServerDerivedMetadata,
    MonitorSyntaxValidationResult SyntaxValidation,
    MonitorOverlayValidationResult OverlayValidation,
    string QueueStatus,
    string? NewFileReviewBaselinePath = null);

public sealed record StagedEditMetadata(
    IReadOnlyList<StagedSymbolMetadata> SymbolsAdded,
    IReadOnlyList<StagedSymbolMetadata> SymbolsRemoved,
    IReadOnlyList<string> UsingsAdded,
    IReadOnlyList<string> UsingsRemoved,
    string StagedFilePath);

public sealed record DiffLaunchResult(
    int? ProcessId,
    string Arguments);

internal sealed record TextFileShape(
    Encoding Encoding,
    string NewLine);

internal sealed record DiffDecisionClassification(
    string CurrentHash,
    string Classification,
    string? OriginalNormalizedHash,
    string? StagedNormalizedHash,
    string? CurrentNormalizedHash);

public sealed record StagedSymbolMetadata(
    string Name,
    string Kind,
    int StartLine,
    int EndLine,
    string TextHash);

internal sealed record StagedSymbolMetadataEntry(
    StagedSymbolMetadata Metadata,
    string Key);

public sealed record MonitorSyntaxValidationResult(
    bool HasErrors,
    IReadOnlyList<MonitorSyntaxDiagnostic> Diagnostics);

public sealed record MonitorSyntaxDiagnostic(
    string Id,
    string Message,
    int Line,
    int Column);

public sealed record MonitorOverlayValidationResult(
    string Status,
    bool HasErrors,
    int SyntaxTreeCount,
    int OverlayFileCount,
    IReadOnlyList<MonitorOverlayDiagnostic> Diagnostics,
    bool FromCache = false);

public sealed record MonitorOverlayDiagnostic(
    string Id,
    string Message,
    string FilePath,
    int Line,
    int Column);

[Description("Find files under the watched project folder.")]
public sealed record MonitorFileMatch(
    string FullPath,
    string RelativePath,
    long Length);

internal sealed record MonitorFileContext(
    string SourceFilePath,
    string WatchedProjectFolder,
    string ObservedRootKey,
    string RelativeSourcePath,
    string WorkingFilePath,
    string RefreshStatePath);

internal sealed record MonitorRefreshState(
    string SourceFilePath,
    DateTime SourceLastWriteUtc,
    long SourceLength,
    DateTimeOffset RefreshedAt);

public sealed record CandidateEditState(
    string? SessionId,
    string SourceFilePath,
    string RelativeSourcePath,
    string CandidateFilePath,
    string BaselineHash,
    long BaselineLength,
    DateTime BaselineLastWriteUtc,
    string CandidateHash,
    long CandidateLength,
    DateTimeOffset UpdatedAt,
    string Operation,
    string? ManifestJson,
    int OperationCount);

internal sealed record TextMatchSet(int Count, int OccurrenceIndex, int SelectedOffset);
