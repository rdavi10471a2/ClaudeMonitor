using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ModelContextProtocol.Server;

namespace MonitorBaseClaude.McpServer;

public sealed partial class MonitorWorkflowService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions SourceMapResponseJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly string[] WinMergeCandidates =
    [
        @"C:\Program Files\WinMerge\WinMergeU.exe",
        @"C:\Program Files (x86)\WinMerge\WinMergeU.exe"
    ];

    private readonly MonitorServerSettings settings;

    public MonitorWorkflowService(MonitorServerSettings settings)
    {
        this.settings = settings;
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

    public MonitorSourceMapResult GetSourceMap(string? path = null, string scope = "auto", string mode = "auto")
    {
        string watchedProjectFolder = Path.GetDirectoryName(settings.WatchedSolutionPath)
            ?? throw new InvalidOperationException("Watched solution path does not have a containing folder.");
        string observedRoot = TrimDirectorySeparator(Path.GetFullPath(watchedProjectFolder));
        string? requestedPath = string.IsNullOrWhiteSpace(path) ? null : path;
        string normalizedScope = ResolveEffectiveSourceMapScope(observedRoot, requestedPath, NormalizeSourceMapScope(scope));
        string normalizedMode = ResolveEffectiveSourceMapMode(normalizedScope, mode);
        string[] sourceFiles = ResolveSourceMapFiles(observedRoot, requestedPath, normalizedScope).ToArray();
        MonitorSourceMapFile[] files = sourceFiles
            .Select(path => BuildSourceMapFile(observedRoot, path))
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

    public MonitorFileSubmitResult SubmitFile(string sourceFilePath, string content, string? sessionId = null, string? manifestJson = null, bool launchDiff = false)
    {
        MonitorFileContext context = ResolveFileContext(sourceFilePath);
        return StageFileReplacement("submit_file", context, content, sessionId, manifestJson, launchDiff);
    }

    public MonitorFileSubmitResult SubmitSymbol(string sourceFilePath, string symbolSelectorJson, string code, string? sessionId = null, string? manifestJson = null)
    {
        MonitorFileContext context = ResolveCSharpFileContext(sourceFilePath, "submit_symbol");
        CompilationUnitSyntax root = ParseCompilationUnit(context);
        MonitorSymbolSelector selector = ParseSymbolSelector(symbolSelectorJson);
        MemberDeclarationSyntax target = ResolveSingleMember(root, selector, context.RelativeSourcePath);
        MemberDeclarationSyntax replacement = ParseMemberDeclaration(code, "replacement symbol")
            .WithLeadingTrivia(target.GetLeadingTrivia())
            .WithTrailingTrivia(target.GetTrailingTrivia());
        CompilationUnitSyntax newRoot = root.ReplaceNode(target, replacement);
        return StageFileReplacement("submit_symbol", context, newRoot.ToFullString(), sessionId, manifestJson, launchDiff: false);
    }

    public MonitorFileSubmitResult AddUsing(string sourceFilePath, string @namespace, string? sessionId = null, string? manifestJson = null)
    {
        MonitorFileContext context = ResolveCSharpFileContext(sourceFilePath, "add_using");
        CompilationUnitSyntax root = ParseCompilationUnit(context);
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
        return StageFileReplacement("add_using", context, newRoot.ToFullString(), sessionId, manifestJson, launchDiff: false);
    }

    public MonitorFileSubmitResult RemoveUsing(string sourceFilePath, string @namespace, string? sessionId = null, string? manifestJson = null)
    {
        MonitorFileContext context = ResolveCSharpFileContext(sourceFilePath, "remove_using");
        CompilationUnitSyntax root = ParseCompilationUnit(context);
        UsingDirectiveSyntax? target = root.Usings.FirstOrDefault(usingDirective => string.Equals(usingDirective.Name?.ToString(), @namespace, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Using '{@namespace}' was not found in {context.RelativeSourcePath}.");
        CompilationUnitSyntax newRoot = root.RemoveNode(target, SyntaxRemoveOptions.KeepNoTrivia)
            ?? throw new InvalidOperationException($"Using '{@namespace}' could not be removed from {context.RelativeSourcePath}.");
        return StageFileReplacement("remove_using", context, newRoot.ToFullString(), sessionId, manifestJson, launchDiff: false);
    }

    public MonitorFileSubmitResult AddSymbol(
        string sourceFilePath,
        string containingType,
        string symbolType,
        string code,
        string? afterSymbol = null,
        string? sessionId = null,
        string? manifestJson = null)
    {
        MonitorFileContext context = ResolveCSharpFileContext(sourceFilePath, "add_symbol");
        CompilationUnitSyntax root = ParseCompilationUnit(context);
        TypeDeclarationSyntax type = ResolveSingleType(root, containingType);
        MemberDeclarationSyntax newMember = ParseMemberDeclaration(code, "new symbol");
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

        TypeDeclarationSyntax newType = type.WithMembers(members.Insert(insertIndex, newMember));
        CompilationUnitSyntax newRoot = root.ReplaceNode(type, newType);
        return StageFileReplacement("add_symbol", context, newRoot.ToFullString(), sessionId, manifestJson, launchDiff: false);
    }

    public MonitorFileSubmitResult RemoveSymbol(string sourceFilePath, string symbolSelectorJson, string? sessionId = null, string? manifestJson = null)
    {
        MonitorFileContext context = ResolveCSharpFileContext(sourceFilePath, "remove_symbol");
        CompilationUnitSyntax root = ParseCompilationUnit(context);
        MonitorSymbolSelector selector = ParseSymbolSelector(symbolSelectorJson);
        MemberDeclarationSyntax target = ResolveSingleMember(root, selector, context.RelativeSourcePath);
        CompilationUnitSyntax newRoot = root.RemoveNode(target, SyntaxRemoveOptions.KeepNoTrivia)
            ?? throw new InvalidOperationException($"Symbol '{selector.Name}' could not be removed from {context.RelativeSourcePath}.");
        return StageFileReplacement("remove_symbol", context, newRoot.ToFullString(), sessionId, manifestJson, launchDiff: false);
    }

    private MonitorFileSubmitResult StageFileReplacement(
        string operation,
        MonitorFileContext context,
        string content,
        string? sessionId,
        string? manifestJson,
        bool launchDiff)
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
        MonitorOverlayValidationResult overlayValidation = ValidateOverlayCompilation(context, stagedPath, sessionId);
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
        bool isNoOp = stagedRecord.OriginalHash.Equals(stagedRecord.StagedHash, StringComparison.OrdinalIgnoreCase);
        string stagedRecordPath = WriteStagedEditRecord(stagedRecord);
        string? diffToolPath = null;
        int? processId = null;
        string? diffArguments = null;

        if (launchDiff && !isNoOp)
        {
            diffToolPath = ResolveWinMergePath();
            if (diffToolPath is not null)
            {
                DiffLaunchResult launchResult = LaunchWinMergeDetached(diffToolPath, context.SourceFilePath, stagedPath);
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

        string effectiveSessionId = string.IsNullOrWhiteSpace(sessionId) ? record.SessionId ?? string.Empty : sessionId;
        (string currentHash, string classification) = ClassifyStrictDiffDecision(record, normalizedDecision);
        bool decisionMatchesClassification = normalizedDecision.Equals(classification, StringComparison.OrdinalIgnoreCase);
        bool blocksFurtherEdits = classification.Equals("dirty-unexpected", StringComparison.OrdinalIgnoreCase);
        string queueStatus = blocksFurtherEdits ? "blocked-dirty-unexpected" : classification;
        StagedEditRecord updatedRecord = record with { QueueStatus = queueStatus };
        File.WriteAllText(recordPath, JsonSerializer.Serialize(updatedRecord, JsonOptions));

        MonitorDiffDecisionResult result = new(
            record.RecordId,
            string.IsNullOrWhiteSpace(effectiveSessionId) ? null : effectiveSessionId,
            record.SourceFilePath,
            record.RelativeSourcePath,
            recordPath,
            record.ServerDerivedMetadata.StagedFilePath,
            normalizedDecision,
            classification,
            decisionMatchesClassification,
            blocksFurtherEdits,
            record.OriginalHash,
            record.StagedHash,
            currentHash,
            queueStatus,
            note,
            DateTimeOffset.UtcNow);

        string decisionRecordPath = WriteDiffDecisionRecord(result);
        return result with { DecisionRecordPath = decisionRecordPath };
    }

    public MonitorStagedDiffLaunchResult LaunchStagedDiff(string stagedRecordId)
    {
        (StagedEditRecord record, string recordPath) = ReadStagedEditRecord(stagedRecordId);
        string stagedFilePath = record.ServerDerivedMetadata.StagedFilePath;
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

        if (!File.Exists(record.SourceFilePath))
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

        DiffLaunchResult launchResult = LaunchWinMergeDetached(winMergePath, record.SourceFilePath, stagedFilePath);
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
            "After operator review, call record_diff_decision with accepted only if WinMerge saved the full staged candidate; otherwise call rejected.");
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

    private MonitorFileContext ResolveFileContext(string sourceFilePath)
    {
        string watchedProjectFolder = Path.GetDirectoryName(settings.WatchedSolutionPath)
            ?? throw new InvalidOperationException("Watched solution path does not have a containing folder.");
        string sourcePath = Path.IsPathRooted(sourceFilePath)
            ? Path.GetFullPath(sourceFilePath)
            : Path.GetFullPath(Path.Combine(watchedProjectFolder, sourceFilePath));
        if (!File.Exists(sourcePath))
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
        SyntaxTree tree = CSharpSyntaxTree.ParseText(File.ReadAllText(context.SourceFilePath), path: context.SourceFilePath);
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
        TypeDeclarationSyntax[] matches = root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Where(type => type.Identifier.ValueText.Equals(containingType, StringComparison.Ordinal)
                || BuildContainingType(type)?.EndsWith(containingType, StringComparison.Ordinal) == true)
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
        return normalized is "auto" or "file" or "folder" or "project"
            ? normalized
            : throw new InvalidOperationException("Source map scope must be auto, file, folder, or project.");
    }

    private static string NormalizeSourceMapMode(string? mode)
    {
        string normalized = string.IsNullOrWhiteSpace(mode) ? "auto" : mode.Trim().ToLowerInvariant();
        return normalized is "auto" or "navigation" or "selector" or "full"
            ? normalized
            : throw new InvalidOperationException("Source map mode must be auto, navigation, selector, or full.");
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
            : 15000;
    }

    private static string GetSourceMapModePurpose(string mode)
    {
        return mode.Equals("navigation", StringComparison.OrdinalIgnoreCase) ? "broad-orientation"
            : mode.Equals("selector", StringComparison.OrdinalIgnoreCase) ? "stable-symbol-selection"
            : "audit-debug";
    }

    private static IEnumerable<string> ResolveSourceMapFiles(string observedRoot, string? path, string scope)
    {
        if (scope.Equals("project", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(path))
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
        TextFileShape sourceShape = DetectTextFileShape(context.SourceFilePath);
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
        string originalContent = File.ReadAllText(context.SourceFilePath);
        StagedEditMetadata metadata = DeriveStagedEditMetadata(context.SourceFilePath, originalContent, stagedFilePath, stagedContent);
        return new StagedEditRecord(
            recordId,
            sessionId,
            context.SourceFilePath,
            context.RelativeSourcePath,
            operation,
            DateTimeOffset.UtcNow,
            ComputeSha256(context.SourceFilePath),
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

    private static (string CurrentHash, string Classification) ClassifyStrictDiffDecision(StagedEditRecord record, string normalizedDecision)
    {
        string currentHash = ComputeSha256(record.SourceFilePath);
        if (normalizedDecision.Equals("rejected", StringComparison.OrdinalIgnoreCase))
        {
            return (
                currentHash,
                currentHash.Equals(record.OriginalHash, StringComparison.OrdinalIgnoreCase)
                    ? "rejected"
                    : "dirty-unexpected");
        }

        return (
            currentHash,
            currentHash.Equals(record.StagedHash, StringComparison.OrdinalIgnoreCase)
                ? "accepted"
                : "dirty-unexpected");
    }

    private static DiffLaunchResult LaunchWinMergeDetached(
        string winMergePath,
        string originalFilePath,
        string proposedFilePath)
    {
        string displayName = BuildWinMergeDisplayName(originalFilePath);
        Process? existing = FindOpenWinMergeReview(displayName);
        if (existing is not null)
        {
            return new DiffLaunchResult(existing.Id, $"already-open: {existing.MainWindowTitle}");
        }

        string launcherPath = WriteWinMergeLauncher(winMergePath, originalFilePath, proposedFilePath, displayName);
        ProcessStartInfo startInfo = BuildWinMergeStartInfo(launcherPath);
        Process? process = Process.Start(startInfo);
        return new DiffLaunchResult(process?.Id, $"{launcherPath} :: {File.ReadAllText(launcherPath)}");
    }

    private static ProcessStartInfo BuildWinMergeStartInfo(string launcherPath)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "cmd.exe",
            Arguments = $"/k call \"{launcherPath}\"",
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
        string displayName)
    {
        string launcherRoot = Path.Combine(Path.GetTempPath(), "MonitorBaseClaude", "DiffLaunchers");
        Directory.CreateDirectory(launcherRoot);
        string launcherPath = Path.Combine(launcherRoot, $"launch-{DateTime.Now:yyyyMMdd-HHmmssfff}-{SanitizeForFileName(Path.GetFileNameWithoutExtension(originalFilePath))}.cmd");

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
            QuoteCommandArgument($"Existing Source (Right) - {displayName}"),
            QuoteCommandArgument(proposedFilePath),
            QuoteCommandArgument(originalFilePath)
        ]);

        string content = string.Join(Environment.NewLine,
        [
            "@echo off",
            $"title MonitorBaseClaude diff - {Path.GetFileName(originalFilePath)}",
            "echo MonitorBaseClaude diff review",
            $"echo Proposed: {proposedFilePath}",
            $"echo Source:   {originalFilePath}",
            "echo.",
            winMergeCommand,
            "set WINMERGE_EXIT=%ERRORLEVEL%",
            "echo.",
            "echo WinMerge exited with code %WINMERGE_EXIT%.",
            "echo Press any key after operator review.",
            "pause > nul"
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

    private MonitorOverlayValidationResult ValidateOverlayCompilation(
        MonitorFileContext context,
        string stagedFilePath,
        string? sessionId)
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
            Dictionary<string, string> overlays = BuildSessionOverlayMap(context, stagedFilePath, sessionId);
            string[] overlayRelatives = overlays.Keys.ToArray();
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
                assemblyName: "MonitorBaseClaudeOverlayValidation",
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

            return new MonitorOverlayValidationResult(
                diagnostics.Length > 0 ? "compiled-with-errors" : "compiled",
                diagnostics.Length > 0,
                trees.Count,
                overlayFileCount,
                diagnostics);
        }
        catch (Exception ex)
        {
            return new MonitorOverlayValidationResult(
                "validation-failed",
                true,
                0,
                0,
                [new MonitorOverlayDiagnostic("MONITOR_OVERLAY", ex.Message, context.SourceFilePath, 0, 0)]);
        }
    }

    private Dictionary<string, string> BuildSessionOverlayMap(MonitorFileContext context, string stagedFilePath, string? sessionId)
    {
        Dictionary<string, string> overlays = new(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            foreach (StagedEditRecord record in ReadSessionStagedRecords(sessionId))
            {
                if (record.Operation != "submit_file")
                {
                    continue;
                }

                string? recordStagedPath = record.ServerDerivedMetadata.StagedFilePath;
                if (!File.Exists(recordStagedPath))
                {
                    continue;
                }

                overlays[NormalizePath(record.RelativeSourcePath)] = recordStagedPath;
            }
        }

        overlays[NormalizePath(context.RelativeSourcePath)] = stagedFilePath;
        return overlays;
    }

    private IEnumerable<StagedEditRecord> ReadSessionStagedRecords(string sessionId)
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
                yield return record;
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
        StagedSymbolMetadata[] originalSymbols = GetSymbolMetadata(originalRoot.SyntaxTree, originalRoot).ToArray();
        StagedSymbolMetadata[] stagedSymbols = GetSymbolMetadata(stagedRoot.SyntaxTree, stagedRoot).ToArray();
        HashSet<string> originalKeys = originalSymbols.Select(SymbolKey).ToHashSet(StringComparer.Ordinal);
        HashSet<string> stagedKeys = stagedSymbols.Select(SymbolKey).ToHashSet(StringComparer.Ordinal);
        string[] originalUsings = GetUsings(originalRoot);
        string[] stagedUsings = GetUsings(stagedRoot);

        return new StagedEditMetadata(
            stagedSymbols.Where(symbol => !originalKeys.Contains(SymbolKey(symbol))).ToArray(),
            originalSymbols.Where(symbol => !stagedKeys.Contains(SymbolKey(symbol))).ToArray(),
            stagedUsings.Except(originalUsings, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            originalUsings.Except(stagedUsings, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            stagedFilePath);
    }

    private static IEnumerable<StagedSymbolMetadata> GetSymbolMetadata(SyntaxTree tree, CompilationUnitSyntax root)
    {
        return root.DescendantNodes()
            .OfType<MemberDeclarationSyntax>()
            .Where(IsOutlineMember)
            .Select(member =>
            {
                FileLinePositionSpan span = tree.GetLineSpan(member.Span);
                string text = member.NormalizeWhitespace().ToFullString();
                return new StagedSymbolMetadata(
                    SymbolName(member),
                    SymbolKind(member),
                    span.StartLinePosition.Line + 1,
                    span.EndLinePosition.Line + 1,
                    ComputeSha256Text(text));
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
                Attributes = NullIfEmpty(symbol.Attributes),
                Modifiers = NullIfEmpty(symbol.Modifiers),
                ParameterTypes = NullIfEmpty(symbol.ParameterTypes),
                ParameterNames = NullIfEmpty(symbol.ParameterNames)
            };
        }

        return symbol with
        {
            StableSymbolKey = null,
            Namespace = string.IsNullOrWhiteSpace(symbol.Namespace) ? null : symbol.Namespace,
            BaseTypes = NullIfEmpty(symbol.BaseTypes),
            Attributes = NullIfEmpty(symbol.Attributes?.Select(attribute => new MonitorSourceMapAttribute(attribute.Name, null)).ToArray()),
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
            SyntaxKind = null
        };
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
            MonitorSourceMapNextCall[] calls = files
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
                .ToArray();
            return NullIfEmpty(calls);
        }

        if (mode.Equals("selector", StringComparison.OrdinalIgnoreCase))
        {
            MonitorSourceMapNextCall[] calls = files
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
                        ["symbolSelectorJson"] = BuildSymbolSelectorJson(item.Symbol)
                    }))
                .ToArray();
            return NullIfEmpty(calls);
        }

        return null;
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
            BuildSignature(member),
            BuildNamespace(member),
            BuildContainingType(member),
            BuildBaseTypes(member),
            GetAttributeSummaries(member),
            span.StartLinePosition.Line + 1,
            span.EndLinePosition.Line + 1,
            HasLeadingDocumentation(member),
            HasAttributes(member),
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
            member.Kind().ToString());
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

    private static bool HasAttributes(MemberDeclarationSyntax member)
    {
        return member.AttributeLists.Count > 0;
    }

    private static IReadOnlyList<MonitorSourceMapAttribute> GetAttributeSummaries(MemberDeclarationSyntax member)
    {
        return member.AttributeLists
            .SelectMany(list => list.Attributes)
            .Select(attribute => new MonitorSourceMapAttribute(
                attribute.Name.ToString(),
                attribute.ArgumentList?.Arguments.ToFullString().Trim()))
            .ToArray();
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

    private static string SymbolKey(StagedSymbolMetadata symbol)
    {
        return $"{symbol.Kind}:{symbol.Name}";
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

    private static string BuildPropertySignature(PropertyDeclarationSyntax property)
    {
        string modifiers = property.Modifiers.ToFullString().Trim();
        string prefix = string.IsNullOrWhiteSpace(modifiers) ? string.Empty : $"{modifiers} ";
        string accessors = property.AccessorList is null
            ? "get;"
            : string.Join(" ", property.AccessorList.Accessors.Select(BuildAccessorSignature));
        return $"{prefix}{property.Type} {property.Identifier.ValueText} {{ {accessors} }}";
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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SyntaxKind);

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
    int? ProcessId);

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
    string? DecisionRecordPath = null);

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
    string NextStep)
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
    string QueueStatus);

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

public sealed record StagedSymbolMetadata(
    string Name,
    string Kind,
    int StartLine,
    int EndLine,
    string TextHash);

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
    IReadOnlyList<MonitorOverlayDiagnostic> Diagnostics);

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
