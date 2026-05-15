using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ModelContextProtocol.Server;

namespace MonitorBaseClaude.McpServer;

public sealed class MonitorWorkflowService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
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
        MonitorFileContext context = ResolveFileContext(sourceFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(context.WorkingFilePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(context.RefreshStatePath)!);
        File.Copy(context.SourceFilePath, context.WorkingFilePath, overwrite: true);

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

            RefreshFile(context.SourceFilePath);
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
                "No differences found between source and Working copy.");
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
        ProcessStartInfo startInfo = BuildWinMergeStartInfo(winMergePath, context.SourceFilePath, proposedFilePath);
        Process? process = Process.Start(startInfo);

        return new MonitorFileCompareResult(
            process is null ? "launch-failed" : "winmerge-launched",
            context.SourceFilePath,
            context.WorkingFilePath,
            proposedFilePath,
            process?.Id,
            winMergePath,
            FormatArgumentList(startInfo),
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

    public MonitorSymbolReadResult GetSymbol(string sourceFilePath, string symbolName)
    {
        MonitorFileContext context = ResolveFileContext(sourceFilePath);
        if (!context.SourceFilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("get_symbol currently supports C# source files only.");
        }

        SyntaxTree tree = CSharpSyntaxTree.ParseText(File.ReadAllText(context.SourceFilePath), path: context.SourceFilePath);
        MemberDeclarationSyntax member = tree.GetCompilationUnitRoot()
            .DescendantNodes()
            .OfType<MemberDeclarationSyntax>()
            .FirstOrDefault(member => SymbolName(member).Equals(symbolName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Symbol '{symbolName}' was not found in {context.RelativeSourcePath}.");

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

    public MonitorFileSubmitResult SubmitFile(string sourceFilePath, string content, string? sessionId = null, string? manifestJson = null, bool launchDiff = false)
    {
        MonitorFileContext context = ResolveFileContext(sourceFilePath);
        MonitorSyntaxValidationResult validation = ValidateSyntaxIfCSharp(context.SourceFilePath, content);
        string recordId = CreateStagedRecordId("submit_file", Path.GetFileNameWithoutExtension(context.SourceFilePath));
        string stagedPath = CreateStagedFile(context, content, recordId);
        MonitorOverlayValidationResult overlayValidation = ValidateOverlayCompilation(context, stagedPath, sessionId);
        StagedEditRecord stagedRecord = CreateStagedEditRecord(
            recordId,
            sessionId,
            "submit_file",
            context,
            stagedPath,
            manifestJson,
            content,
            validation,
            overlayValidation);
        string stagedRecordPath = WriteStagedEditRecord(stagedRecord);
        string? diffToolPath = null;
        int? processId = null;
        string? diffArguments = null;

        if (launchDiff)
        {
            diffToolPath = ResolveWinMergePath();
            if (diffToolPath is not null)
            {
                ProcessStartInfo startInfo = BuildWinMergeStartInfo(diffToolPath, context.SourceFilePath, stagedPath);
                Process? process = Process.Start(startInfo);
                processId = process?.Id;
                diffArguments = FormatArgumentList(startInfo);
            }
        }

        return new MonitorFileSubmitResult(
            validation.HasErrors ? "staged-with-syntax-errors" : "staged",
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
            launchDiff,
            diffToolPath,
            diffArguments,
            processId);
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
            string ledgerDir = Path.Combine(historyRoot, "Ledgers", context.ObservedRootKey, Path.GetDirectoryName(context.RelativeSourcePath) ?? string.Empty);
            Directory.CreateDirectory(ledgerDir);
            string ledgerPath = Path.Combine(ledgerDir, Path.GetFileNameWithoutExtension(context.RelativeSourcePath) + ".md");
            File.AppendAllText(ledgerPath, $"{DateTimeOffset.Now:O} - {ledgerSummary}{Environment.NewLine}");
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
        File.WriteAllText(stagedPath, content);
        return stagedPath;
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
            ComputeSha256Text(stagedContent),
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

    private static ProcessStartInfo BuildWinMergeStartInfo(string winMergePath, string originalFilePath, string proposedFilePath)
    {
        string fileName = Path.GetFileName(originalFilePath);
        string relativeHint = Path.GetFileName(Path.GetDirectoryName(originalFilePath) ?? string.Empty);
        string displayName = string.IsNullOrWhiteSpace(relativeHint) ? fileName : $"{relativeHint}\\{fileName}";
        ProcessStartInfo startInfo = new()
        {
            FileName = winMergePath,
            UseShellExecute = true
        };
        AddArguments(
            startInfo,
            "/u",
            "/ignoreeol:1",
            "/dl",
            $"New/Proposed (Working) - {displayName}",
            "/dr",
            $"Existing Source (Right) - {displayName}",
            proposedFilePath,
            originalFilePath);
        return startInfo;
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
            EventDeclarationSyntax evt => evt.Identifier.ValueText,
            DelegateDeclarationSyntax del => del.Identifier.ValueText,
            _ => member.Kind().ToString()
        };
    }

    private static string BuildSignature(MemberDeclarationSyntax member)
    {
        MemberDeclarationSyntax signatureOnly = member switch
        {
            ClassDeclarationSyntax type => type.WithMembers(default).NormalizeWhitespace(),
            StructDeclarationSyntax type => type.WithMembers(default).NormalizeWhitespace(),
            InterfaceDeclarationSyntax type => type.WithMembers(default).NormalizeWhitespace(),
            RecordDeclarationSyntax type => type.WithMembers(default).NormalizeWhitespace(),
            MethodDeclarationSyntax method => method.WithBody(null).WithExpressionBody(null).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)).NormalizeWhitespace(),
            ConstructorDeclarationSyntax constructor => constructor.WithBody(null).WithExpressionBody(null).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)).NormalizeWhitespace(),
            PropertyDeclarationSyntax property => property.WithAccessorList(null).WithExpressionBody(null).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)).NormalizeWhitespace(),
            _ => member.NormalizeWhitespace()
        };

        return signatureOnly.ToFullString().Replace(Environment.NewLine, " ", StringComparison.Ordinal).Trim();
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
        string message)
    {
        return new MonitorFileCompareResult(status, sourceFilePath, workingFilePath, proposedFilePath, null, null, message, false);
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
