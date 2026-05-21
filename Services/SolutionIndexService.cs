using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Data.Sqlite;
using MonitorBaseClaude.AI;

namespace MonitorBaseClaude.Services;

[AIFileContext("SolutionIndexService.cs", "Builds and queries the monitor-owned SQLite index for watched C# solution structure.")]
[FileVersion("1.6")]
public sealed class SolutionIndexService
{
    private static readonly string[] ExcludedDirectoryNames =
    [
        ".git",
        ".vs",
        "bin",
        "obj",
        "node_modules",
        "SourceBakups",
        "SourceBackups"
    ];

    private readonly string uiRoot;
    private readonly string watchedSolutionPath;

    public SolutionIndexService(string uiRoot, string watchedSolutionPath)
    {
        this.uiRoot = Path.GetFullPath(uiRoot);
        this.watchedSolutionPath = Path.GetFullPath(watchedSolutionPath);
    }

    public SolutionIndexStatus GetStatus()
    {
        string dbPath = GetIndexDatabasePath();
        string observedRoot = GetObservedRoot();
        string observedRootKey = BuildObservedRootKey(observedRoot);
        if (!File.Exists(dbPath))
        {
            return new SolutionIndexStatus(
                dbPath,
                watchedSolutionPath,
                observedRoot,
                observedRootKey,
                null,
                0,
                0,
                0,
                0,
                0,
                0,
                true);
        }

        using SqliteConnection connection = OpenConnection(dbPath);
        DateTimeOffset? indexedAt = ReadLastIndexedAt(connection);
        int fileCount = ExecuteScalarInt(connection, "select count(*) from files;");
        int symbolCount = ExecuteScalarInt(connection, "select count(*) from symbols;");
        int diagnosticCount = ExecuteScalarInt(connection, "select count(*) from diagnostics;");
        int referenceCount = TableExists(connection, "symbol_references")
            ? ExecuteScalarInt(connection, "select count(*) from symbol_references;")
            : 0;
        int callSiteCount = TableExists(connection, "call_sites")
            ? ExecuteScalarInt(connection, "select count(*) from call_sites;")
            : 0;
        int staleFileCount = CountStaleFiles(connection, observedRoot);
        return new SolutionIndexStatus(
            dbPath,
            watchedSolutionPath,
            observedRoot,
            observedRootKey,
            indexedAt,
            fileCount,
            symbolCount,
            diagnosticCount,
            referenceCount,
            callSiteCount,
            staleFileCount,
            false);
    }

    public SolutionIndexBuildResult Rebuild()
    {
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        string observedRoot = GetObservedRoot();
        string observedRootKey = BuildObservedRootKey(observedRoot);
        string dbPath = GetIndexDatabasePath();
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        using SqliteConnection connection = OpenConnection(dbPath);
        InitializeSchema(connection);

        using SqliteTransaction transaction = connection.BeginTransaction();
        ExecuteNonQuery(connection, transaction, "delete from call_sites;");
        ExecuteNonQuery(connection, transaction, "delete from symbol_references;");
        ExecuteNonQuery(connection, transaction, "delete from diagnostics;");
        ExecuteNonQuery(connection, transaction, "delete from symbols;");
        ExecuteNonQuery(connection, transaction, "delete from files;");
        ExecuteNonQuery(connection, transaction, "delete from index_runs;");

        long runId = InsertIndexRun(connection, transaction, observedRoot, observedRootKey, watchedSolutionPath, startedAt);
        IReadOnlyList<IndexedFileBuild> files = EnumerateSourceFiles(observedRoot)
            .Select(sourceFilePath => BuildFileIndex(observedRoot, sourceFilePath))
            .ToArray();
        IReadOnlyList<IndexedReferenceBuild> references = BuildReferenceIndex(observedRoot, files);

        Dictionary<string, long> fileIdsByRelativePath = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, long> symbolIdsByStableKey = new(StringComparer.Ordinal);
        int indexedSymbols = 0;
        int indexedDiagnostics = 0;
        int indexedReferences = 0;
        int indexedCallSites = 0;

        foreach (IndexedFileBuild file in files)
        {
            long fileId = InsertFile(connection, transaction, runId, file);
            fileIdsByRelativePath[file.RelativePath] = fileId;
            foreach (IndexedSymbolBuild symbol in file.Symbols)
            {
                long symbolId = InsertSymbol(connection, transaction, fileId, symbol);
                symbolIdsByStableKey[symbol.StableKey] = symbolId;
                indexedSymbols++;
            }

            foreach (IndexedDiagnosticBuild diagnostic in file.Diagnostics)
            {
                InsertDiagnostic(connection, transaction, fileId, diagnostic);
                indexedDiagnostics++;
            }
        }

        foreach (IndexedReferenceBuild reference in references)
        {
            if (!fileIdsByRelativePath.TryGetValue(reference.RelativePath, out long fileId)
                || !symbolIdsByStableKey.TryGetValue(reference.TargetStableKey, out long targetSymbolId))
            {
                continue;
            }

            long? callerSymbolId = null;
            if (!string.IsNullOrWhiteSpace(reference.CallerStableKey)
                && symbolIdsByStableKey.TryGetValue(reference.CallerStableKey, out long resolvedCallerSymbolId))
            {
                callerSymbolId = resolvedCallerSymbolId;
            }

            InsertSymbolReference(connection, transaction, fileId, targetSymbolId, callerSymbolId, reference);
            indexedReferences++;
            if (reference.IsCallSite)
            {
                InsertCallSite(connection, transaction, fileId, targetSymbolId, callerSymbolId, reference);
                indexedCallSites++;
            }
        }

        transaction.Commit();
        ExecuteNonQuery(connection, null, "pragma wal_checkpoint(passive);");

        TimeSpan duration = DateTimeOffset.UtcNow - startedAt;
        SolutionIndexStatus status = GetStatus();
        return new SolutionIndexBuildResult(
            status,
            startedAt,
            DateTimeOffset.UtcNow,
            duration.TotalMilliseconds,
            files.Count,
            indexedSymbols,
            indexedDiagnostics,
            indexedReferences,
            indexedCallSites);
    }

    public SolutionIndexFileRefreshResult RefreshFile(string path)
    {
        string observedRoot = GetObservedRoot();
        string sourceFilePath = ResolveSourceFilePath(observedRoot, path);
        string relativePath = Path.GetRelativePath(observedRoot, sourceFilePath);
        Rebuild();
        SolutionIndexQueryResult fileQuery = Query("file", relativePath, maxFiles: 1, maxSymbols: 5000);
        return new SolutionIndexFileRefreshResult(
            GetStatus(),
            fileQuery.Files.FirstOrDefault(),
            fileQuery.Symbols,
            fileQuery.Files.FirstOrDefault()?.DiagnosticCount ?? 0);
    }

    public SolutionIndexQueryResult Query(string scope = "solution", string? value = null, int maxFiles = 200, int maxSymbols = 500)
    {
        string dbPath = GetIndexDatabasePath();
        if (!File.Exists(dbPath))
        {
            return new SolutionIndexQueryResult(scope, value, true, [], []);
        }

        string normalizedScope = NormalizeScope(scope);
        string? normalizedValue = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        maxFiles = Math.Clamp(maxFiles, 1, 5000);
        maxSymbols = Math.Clamp(maxSymbols, 1, 50000);

        using SqliteConnection connection = OpenConnection(dbPath);
        IReadOnlyList<SolutionIndexFile> files = QueryFiles(connection, normalizedScope, normalizedValue, maxFiles);
        IReadOnlyList<SolutionIndexSymbol> symbols = QuerySymbols(connection, normalizedScope, normalizedValue, maxSymbols);
        return new SolutionIndexQueryResult(normalizedScope, normalizedValue, false, files, symbols);
    }

    public SolutionIndexTree GetTree()
    {
        string dbPath = GetIndexDatabasePath();
        SolutionIndexStatus status = GetStatus();
        if (!File.Exists(dbPath))
        {
            return new SolutionIndexTree(status, []);
        }

        using SqliteConnection connection = OpenConnection(dbPath);
        Dictionary<string, List<string>> namespaceFiles = new(StringComparer.OrdinalIgnoreCase);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            select distinct s.namespace, f.relative_path
            from symbols s
            join files f on f.id = s.file_id
            order by s.namespace collate nocase, f.relative_path collate nocase;
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string namespaceName = reader.GetString(0);
            if (string.IsNullOrWhiteSpace(namespaceName))
            {
                namespaceName = "(global)";
            }

            if (!namespaceFiles.TryGetValue(namespaceName, out List<string>? files))
            {
                files = [];
                namespaceFiles[namespaceName] = files;
            }

            files.Add(reader.GetString(1));
        }

        IReadOnlyList<SolutionIndexNamespaceNode> namespaces = namespaceFiles
            .Select(pair => new SolutionIndexNamespaceNode(pair.Key, pair.Value.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray()))
            .OrderBy(node => node.Namespace, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new SolutionIndexTree(status, namespaces);
    }

    public IReadOnlyList<SolutionIndexSymbol> FindSymbols(string text, string? kind = null, string? namespaceName = null, int maxResults = 100)
    {
        string dbPath = GetIndexDatabasePath();
        if (!File.Exists(dbPath) || string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        maxResults = Math.Clamp(maxResults, 1, 1000);
        using SqliteConnection connection = OpenConnection(dbPath);
        using SqliteCommand command = connection.CreateCommand();
        StringBuilder sql = new(
            """
            select s.stable_key, f.relative_path, f.sha256, s.text_hash, s.namespace, s.containing_type, s.kind, s.name, s.signature, s.start_line, s.end_line, s.start_column, s.end_column, s.text_span_start, s.text_span_length, s.source_anchor, s.selector_json, s.declared_accessibility, s.is_generated, s.is_partial
            from symbols s
            join files f on f.id = s.file_id
            where s.name like $text escape '\'
            """);
        command.Parameters.AddWithValue("$text", "%" + EscapeLike(text.Trim()) + "%");
        if (!string.IsNullOrWhiteSpace(kind))
        {
            sql.AppendLine("and s.kind = $kind");
            command.Parameters.AddWithValue("$kind", kind.Trim());
        }

        if (!string.IsNullOrWhiteSpace(namespaceName))
        {
            sql.AppendLine("and s.namespace = $namespace");
            command.Parameters.AddWithValue("$namespace", NormalizeNamespaceValue(namespaceName));
        }

        sql.AppendLine("order by s.name collate nocase, f.relative_path collate nocase limit $limit;");
        command.Parameters.AddWithValue("$limit", maxResults);
        command.CommandText = sql.ToString();
        return ReadSymbols(command);
    }

    public SolutionIndexSymbol? GetSymbol(string stableSymbolKey)
    {
        stableSymbolKey = NormalizeStableSymbolKey(stableSymbolKey);
        string dbPath = GetIndexDatabasePath();
        if (!File.Exists(dbPath) || string.IsNullOrWhiteSpace(stableSymbolKey))
        {
            return null;
        }

        using SqliteConnection connection = OpenConnection(dbPath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            select s.stable_key, f.relative_path, f.sha256, s.text_hash, s.namespace, s.containing_type, s.kind, s.name, s.signature, s.start_line, s.end_line, s.start_column, s.end_column, s.text_span_start, s.text_span_length, s.source_anchor, s.selector_json, s.declared_accessibility, s.is_generated, s.is_partial
            from symbols s
            join files f on f.id = s.file_id
            where s.stable_key = $stableKey
            limit 1;
            """;
        command.Parameters.AddWithValue("$stableKey", stableSymbolKey);
        return ReadSymbols(command).FirstOrDefault();
    }

    public IReadOnlyList<SolutionIndexReference> FindReferences(string stableSymbolKey, int maxResults = 500)
    {
        return QueryReferenceRows(stableSymbolKey, onlyCallSites: false, maxResults);
    }

    public IReadOnlyList<SolutionIndexReference> FindCallers(string stableSymbolKey, int maxResults = 500)
    {
        return QueryReferenceRows(stableSymbolKey, onlyCallSites: true, maxResults);
    }

    private string GetObservedRoot()
    {
        string? directory = Path.GetDirectoryName(watchedSolutionPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Watched solution path does not have a containing folder.");
        }

        return TrimDirectorySeparator(Path.GetFullPath(directory));
    }

    private string GetIndexDatabasePath()
    {
        string observedRoot = GetObservedRoot();
        return Path.Combine(uiRoot, "Working", "Indexes", BuildObservedRootKey(observedRoot), "solution-index.sqlite");
    }

    private static SqliteConnection OpenConnection(string dbPath)
    {
        SqliteConnection connection = new($"Data Source={dbPath}");
        connection.Open();
        using SqliteCommand pragma = connection.CreateCommand();
        pragma.CommandText = "pragma journal_mode=wal; pragma foreign_keys=on;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private static void InitializeSchema(SqliteConnection connection)
    {
        ExecuteNonQuery(
            connection,
            null,
            """
            create table if not exists index_runs (
                id integer primary key autoincrement,
                observed_root text not null,
                observed_root_key text not null,
                watched_solution_path text not null,
                indexed_at_utc text not null
            );

            create table if not exists files (
                id integer primary key autoincrement,
                run_id integer not null references index_runs(id) on delete cascade,
                relative_path text not null,
                full_path text not null,
                sha256 text not null,
                length integer not null,
                last_write_time_utc text not null,
                parse_status text not null,
                diagnostic_count integer not null
            );

            create table if not exists symbols (
                id integer primary key autoincrement,
                file_id integer not null references files(id) on delete cascade,
                stable_key text not null,
                namespace text not null,
                containing_type text,
                kind text not null,
                name text not null,
                signature text not null,
                start_line integer not null,
                end_line integer not null,
                text_hash text not null default '',
                start_column integer not null default 1,
                end_column integer not null default 1,
                text_span_start integer not null default 0,
                text_span_length integer not null default 0,
                source_anchor text not null default '',
                selector_json text not null default '',
                declared_accessibility text not null default '',
                is_generated integer not null default 0,
                is_partial integer not null default 0
            );

            create table if not exists diagnostics (
                id integer primary key autoincrement,
                file_id integer not null references files(id) on delete cascade,
                severity text not null,
                diagnostic_id text not null,
                message text not null,
                start_line integer not null,
                end_line integer not null
            );

            create table if not exists symbol_references (
                id integer primary key autoincrement,
                target_symbol_id integer not null references symbols(id) on delete cascade,
                file_id integer not null references files(id) on delete cascade,
                caller_symbol_id integer references symbols(id) on delete set null,
                reference_kind text not null,
                line integer not null,
                column integer not null,
                snippet text not null
            );

            create table if not exists call_sites (
                id integer primary key autoincrement,
                callee_symbol_id integer not null references symbols(id) on delete cascade,
                file_id integer not null references files(id) on delete cascade,
                caller_symbol_id integer references symbols(id) on delete set null,
                line integer not null,
                column integer not null,
                snippet text not null
            );

            create index if not exists ix_files_relative_path on files(relative_path);
            create index if not exists ix_symbols_stable_key on symbols(stable_key);
            create index if not exists ix_symbols_name on symbols(name);
            create index if not exists ix_symbols_namespace on symbols(namespace);
            create index if not exists ix_symbols_kind on symbols(kind);
            create index if not exists ix_symbol_references_target on symbol_references(target_symbol_id);
            create index if not exists ix_symbol_references_file on symbol_references(file_id);
            create index if not exists ix_call_sites_callee on call_sites(callee_symbol_id);
            create index if not exists ix_call_sites_caller on call_sites(caller_symbol_id);
            """);
        EnsureColumn(connection, "symbols", "text_hash", "text not null default ''");
        EnsureColumn(connection, "symbols", "start_column", "integer not null default 1");
        EnsureColumn(connection, "symbols", "end_column", "integer not null default 1");
        EnsureColumn(connection, "symbols", "text_span_start", "integer not null default 0");
        EnsureColumn(connection, "symbols", "text_span_length", "integer not null default 0");
        EnsureColumn(connection, "symbols", "source_anchor", "text not null default ''");
        EnsureColumn(connection, "symbols", "selector_json", "text not null default ''");
        EnsureColumn(connection, "symbols", "declared_accessibility", "text not null default ''");
        EnsureColumn(connection, "symbols", "is_generated", "integer not null default 0");
        EnsureColumn(connection, "symbols", "is_partial", "integer not null default 0");
    }

    private static long InsertIndexRun(SqliteConnection connection, SqliteTransaction transaction, string observedRoot, string observedRootKey, string watchedSolutionPath, DateTimeOffset indexedAt)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            insert into index_runs(observed_root, observed_root_key, watched_solution_path, indexed_at_utc)
            values ($observedRoot, $observedRootKey, $watchedSolutionPath, $indexedAt);
            select last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$observedRoot", observedRoot);
        command.Parameters.AddWithValue("$observedRootKey", observedRootKey);
        command.Parameters.AddWithValue("$watchedSolutionPath", watchedSolutionPath);
        command.Parameters.AddWithValue("$indexedAt", indexedAt.UtcDateTime.ToString("O"));
        return (long)command.ExecuteScalar()!;
    }

    private long InsertFile(SqliteConnection connection, SqliteTransaction transaction, long runId, IndexedFileBuild file)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            insert into files(run_id, relative_path, full_path, sha256, length, last_write_time_utc, parse_status, diagnostic_count)
            values ($runId, $relativePath, $fullPath, $sha256, $length, $lastWriteTimeUtc, $parseStatus, $diagnosticCount);
            select last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$runId", runId);
        command.Parameters.AddWithValue("$relativePath", file.RelativePath);
        command.Parameters.AddWithValue("$fullPath", file.FullPath);
        command.Parameters.AddWithValue("$sha256", file.Sha256);
        command.Parameters.AddWithValue("$length", file.Length);
        command.Parameters.AddWithValue("$lastWriteTimeUtc", file.LastWriteTimeUtc.ToString("O"));
        command.Parameters.AddWithValue("$parseStatus", file.ParseStatus);
        command.Parameters.AddWithValue("$diagnosticCount", file.Diagnostics.Count);
        return (long)command.ExecuteScalar()!;
    }

    private static long InsertSymbol(SqliteConnection connection, SqliteTransaction transaction, long fileId, IndexedSymbolBuild symbol)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            insert into symbols(file_id, stable_key, namespace, containing_type, kind, name, signature, start_line, end_line, text_hash, start_column, end_column, text_span_start, text_span_length, source_anchor, selector_json, declared_accessibility, is_generated, is_partial)
            values ($fileId, $stableKey, $namespace, $containingType, $kind, $name, $signature, $startLine, $endLine, $textHash, $startColumn, $endColumn, $textSpanStart, $textSpanLength, $sourceAnchor, $selectorJson, $declaredAccessibility, $isGenerated, $isPartial);
            select last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$fileId", fileId);
        command.Parameters.AddWithValue("$stableKey", symbol.StableKey);
        command.Parameters.AddWithValue("$namespace", symbol.Namespace);
        command.Parameters.AddWithValue("$containingType", (object?)symbol.ContainingType ?? DBNull.Value);
        command.Parameters.AddWithValue("$kind", symbol.Kind);
        command.Parameters.AddWithValue("$name", symbol.Name);
        command.Parameters.AddWithValue("$signature", symbol.Signature);
        command.Parameters.AddWithValue("$startLine", symbol.StartLine);
        command.Parameters.AddWithValue("$endLine", symbol.EndLine);
        command.Parameters.AddWithValue("$textHash", symbol.TextHash);
        command.Parameters.AddWithValue("$startColumn", symbol.StartColumn);
        command.Parameters.AddWithValue("$endColumn", symbol.EndColumn);
        command.Parameters.AddWithValue("$textSpanStart", symbol.TextSpanStart);
        command.Parameters.AddWithValue("$textSpanLength", symbol.TextSpanLength);
        command.Parameters.AddWithValue("$sourceAnchor", symbol.SourceAnchor);
        command.Parameters.AddWithValue("$selectorJson", symbol.SelectorJson);
        command.Parameters.AddWithValue("$declaredAccessibility", symbol.DeclaredAccessibility);
        command.Parameters.AddWithValue("$isGenerated", symbol.IsGenerated ? 1 : 0);
        command.Parameters.AddWithValue("$isPartial", symbol.IsPartial ? 1 : 0);
        return (long)command.ExecuteScalar()!;
    }

    private static void InsertDiagnostic(SqliteConnection connection, SqliteTransaction transaction, long fileId, IndexedDiagnosticBuild diagnostic)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            insert into diagnostics(file_id, severity, diagnostic_id, message, start_line, end_line)
            values ($fileId, $severity, $diagnosticId, $message, $startLine, $endLine);
            """;
        command.Parameters.AddWithValue("$fileId", fileId);
        command.Parameters.AddWithValue("$severity", diagnostic.Severity);
        command.Parameters.AddWithValue("$diagnosticId", diagnostic.DiagnosticId);
        command.Parameters.AddWithValue("$message", diagnostic.Message);
        command.Parameters.AddWithValue("$startLine", diagnostic.StartLine);
        command.Parameters.AddWithValue("$endLine", diagnostic.EndLine);
        command.ExecuteNonQuery();
    }

    private static void InsertSymbolReference(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long fileId,
        long targetSymbolId,
        long? callerSymbolId,
        IndexedReferenceBuild reference)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            insert into symbol_references(target_symbol_id, file_id, caller_symbol_id, reference_kind, line, column, snippet)
            values ($targetSymbolId, $fileId, $callerSymbolId, $referenceKind, $line, $column, $snippet);
            """;
        command.Parameters.AddWithValue("$targetSymbolId", targetSymbolId);
        command.Parameters.AddWithValue("$fileId", fileId);
        command.Parameters.AddWithValue("$callerSymbolId", (object?)callerSymbolId ?? DBNull.Value);
        command.Parameters.AddWithValue("$referenceKind", reference.ReferenceKind);
        command.Parameters.AddWithValue("$line", reference.Line);
        command.Parameters.AddWithValue("$column", reference.Column);
        command.Parameters.AddWithValue("$snippet", reference.Snippet);
        command.ExecuteNonQuery();
    }

    private static void InsertCallSite(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long fileId,
        long calleeSymbolId,
        long? callerSymbolId,
        IndexedReferenceBuild reference)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            insert into call_sites(callee_symbol_id, file_id, caller_symbol_id, line, column, snippet)
            values ($calleeSymbolId, $fileId, $callerSymbolId, $line, $column, $snippet);
            """;
        command.Parameters.AddWithValue("$calleeSymbolId", calleeSymbolId);
        command.Parameters.AddWithValue("$fileId", fileId);
        command.Parameters.AddWithValue("$callerSymbolId", (object?)callerSymbolId ?? DBNull.Value);
        command.Parameters.AddWithValue("$line", reference.Line);
        command.Parameters.AddWithValue("$column", reference.Column);
        command.Parameters.AddWithValue("$snippet", reference.Snippet);
        command.ExecuteNonQuery();
    }

    private static IndexedFileBuild BuildFileIndex(string observedRoot, string sourceFilePath)
    {
        string text = File.ReadAllText(sourceFilePath);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(text, path: sourceFilePath);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
        string relativePath = Path.GetRelativePath(observedRoot, sourceFilePath);
        FileInfo info = new(sourceFilePath);
        IReadOnlyList<IndexedDiagnosticBuild> diagnostics = tree.GetDiagnostics()
            .Select(diagnostic => BuildDiagnostic(tree, diagnostic))
            .ToArray();
        bool isGenerated = IsGeneratedFile(root);
        IReadOnlyList<IndexedSymbolBuild> symbols = root.DescendantNodes()
            .SelectMany(node => BuildSymbolsForNode(tree, relativePath, node, isGenerated))
            .OrderBy(symbol => symbol.StartLine)
            .ThenBy(symbol => symbol.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new IndexedFileBuild(
            sourceFilePath,
            relativePath,
            tree,
            root,
            ComputeSha256(sourceFilePath),
            info.Length,
            info.LastWriteTimeUtc,
            diagnostics.Any(diagnostic => diagnostic.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase)) ? "error" : "ok",
            symbols,
            diagnostics);
    }

    private static IReadOnlyList<IndexedReferenceBuild> BuildReferenceIndex(string observedRoot, IReadOnlyList<IndexedFileBuild> files)
    {
        if (files.Count == 0)
        {
            return [];
        }

        Dictionary<string, IndexedFileBuild> filesByRelativePath = files
            .ToDictionary(file => NormalizeIndexPath(file.RelativePath), StringComparer.OrdinalIgnoreCase);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "MonitorBaseClaudeSolutionIndex",
            files.Select(file => file.SyntaxTree),
            GetMetadataReferences(observedRoot),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        List<IndexedReferenceBuild> references = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (IndexedFileBuild file in files)
        {
            SemanticModel model = compilation.GetSemanticModel(file.SyntaxTree, ignoreAccessibility: true);
            foreach (SimpleNameSyntax name in file.Root.DescendantNodes().OfType<SimpleNameSyntax>())
            {
                ISymbol? symbol = GetBestSymbol(model.GetSymbolInfo(name));
                string? callTargetStableKey = GetCallTargetStableKey(model, name, filesByRelativePath, out string? callKind);
                string? targetStableKey = callTargetStableKey ?? GetStableKeyForSymbol(symbol, filesByRelativePath);
                if (string.IsNullOrWhiteSpace(targetStableKey))
                {
                    continue;
                }

                FileLinePositionSpan span = file.SyntaxTree.GetLineSpan(name.Span);
                int line = span.StartLinePosition.Line + 1;
                int column = span.StartLinePosition.Character + 1;
                bool isCallSite = callTargetStableKey is not null;
                string referenceKind = callKind ?? ClassifyReferenceKind(name);
                string? callerStableKey = GetCallerStableKey(file.SyntaxTree, file.RelativePath, name);
                string snippet = GetLineSnippet(file.SyntaxTree, name.Span);
                string seenKey = $"{targetStableKey}|{NormalizeIndexPath(file.RelativePath)}|{line}|{column}|{referenceKind}";
                if (!seen.Add(seenKey))
                {
                    continue;
                }

                references.Add(new IndexedReferenceBuild(
                    targetStableKey,
                    callerStableKey,
                    file.RelativePath,
                    referenceKind,
                    isCallSite,
                    line,
                    column,
                    snippet));
            }

            foreach (ImplicitObjectCreationExpressionSyntax creation in file.Root.DescendantNodes().OfType<ImplicitObjectCreationExpressionSyntax>())
            {
                ISymbol? symbol = GetBestSymbol(model.GetSymbolInfo(creation));
                string? targetStableKey = GetStableKeyForSymbol(symbol, filesByRelativePath);
                if (string.IsNullOrWhiteSpace(targetStableKey))
                {
                    continue;
                }

                FileLinePositionSpan span = file.SyntaxTree.GetLineSpan(creation.NewKeyword.Span);
                int line = span.StartLinePosition.Line + 1;
                int column = span.StartLinePosition.Character + 1;
                string referenceKind = "construction";
                string? callerStableKey = GetCallerStableKey(file.SyntaxTree, file.RelativePath, creation);
                string snippet = GetLineSnippet(file.SyntaxTree, creation.Span);
                string seenKey = $"{targetStableKey}|{NormalizeIndexPath(file.RelativePath)}|{line}|{column}|{referenceKind}";
                if (!seen.Add(seenKey))
                {
                    continue;
                }

                references.Add(new IndexedReferenceBuild(
                    targetStableKey,
                    callerStableKey,
                    file.RelativePath,
                    referenceKind,
                    true,
                    line,
                    column,
                    snippet));
            }

            foreach (AttributeSyntax attribute in file.Root.DescendantNodes().OfType<AttributeSyntax>())
            {
                ISymbol? symbol = GetAttributeTypeSymbol(model, attribute);
                string? targetStableKey = GetStableKeyForSymbol(symbol, filesByRelativePath);
                if (string.IsNullOrWhiteSpace(targetStableKey))
                {
                    continue;
                }

                FileLinePositionSpan span = file.SyntaxTree.GetLineSpan(attribute.Name.Span);
                int line = span.StartLinePosition.Line + 1;
                int column = span.StartLinePosition.Character + 1;
                string referenceKind = "attribute";
                string? callerStableKey = GetCallerStableKey(file.SyntaxTree, file.RelativePath, attribute);
                string snippet = GetLineSnippet(file.SyntaxTree, attribute.Name.Span);
                string seenKey = $"{targetStableKey}|{NormalizeIndexPath(file.RelativePath)}|{line}|{column}|{referenceKind}";
                if (!seen.Add(seenKey))
                {
                    continue;
                }

                references.Add(new IndexedReferenceBuild(
                    targetStableKey,
                    callerStableKey,
                    file.RelativePath,
                    referenceKind,
                    false,
                    line,
                    column,
                    snippet));
            }

            foreach (ElementAccessExpressionSyntax elementAccess in file.Root.DescendantNodes().OfType<ElementAccessExpressionSyntax>())
            {
                AddExpressionReference(
                    references,
                    seen,
                    model,
                    filesByRelativePath,
                    file,
                    elementAccess,
                    elementAccess.Expression.Span,
                    "indexer",
                    isCallSite: true);
            }

            foreach (BinaryExpressionSyntax binary in file.Root.DescendantNodes().OfType<BinaryExpressionSyntax>())
            {
                AddExpressionReference(
                    references,
                    seen,
                    model,
                    filesByRelativePath,
                    file,
                    binary,
                    binary.OperatorToken.Span,
                    "operator",
                    isCallSite: true);
            }

            foreach (CastExpressionSyntax cast in file.Root.DescendantNodes().OfType<CastExpressionSyntax>())
            {
                AddExpressionReference(
                    references,
                    seen,
                    model,
                    filesByRelativePath,
                    file,
                    cast,
                    cast.Type.Span,
                    "conversion",
                    isCallSite: true);
            }

            foreach (ConstructorInitializerSyntax initializer in file.Root.DescendantNodes().OfType<ConstructorInitializerSyntax>())
            {
                AddExpressionReference(
                    references,
                    seen,
                    model,
                    filesByRelativePath,
                    file,
                    initializer,
                    initializer.ThisOrBaseKeyword.Span,
                    "construction",
                    isCallSite: true);
            }

            foreach (ExpressionSyntax expression in file.Root.DescendantNodes().OfType<ExpressionSyntax>())
            {
                IMethodSymbol? conversionMethod = model.GetConversion(expression).MethodSymbol;
                if (conversionMethod is not { MethodKind: MethodKind.Conversion })
                {
                    continue;
                }

                AddSymbolReference(
                    references,
                    seen,
                    filesByRelativePath,
                    file,
                    expression,
                    expression.Span,
                    conversionMethod,
                    "conversion",
                    isCallSite: true);
            }

            foreach (UsingStatementSyntax usingStatement in file.Root.DescendantNodes().OfType<UsingStatementSyntax>())
            {
                if (!usingStatement.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword))
                {
                    continue;
                }

                foreach (ISymbol disposeSymbol in GetAwaitUsingDisposeSymbols(model, usingStatement))
                {
                    AddSymbolReference(
                        references,
                        seen,
                        filesByRelativePath,
                        file,
                        usingStatement,
                        usingStatement.AwaitKeyword.Span,
                        disposeSymbol,
                        "await_using_dispose",
                        isCallSite: true);
                }
            }
        }

        return references;
    }

    private static void AddExpressionReference(
        List<IndexedReferenceBuild> references,
        HashSet<string> seen,
        SemanticModel model,
        IReadOnlyDictionary<string, IndexedFileBuild> filesByRelativePath,
        IndexedFileBuild file,
        SyntaxNode expression,
        TextSpan anchorSpan,
        string referenceKind,
        bool isCallSite)
    {
        ISymbol? symbol = GetBestSymbol(model.GetSymbolInfo(expression));
        AddSymbolReference(
            references,
            seen,
            filesByRelativePath,
            file,
            expression,
            anchorSpan,
            symbol,
            referenceKind,
            isCallSite);
    }

    private static void AddSymbolReference(
        List<IndexedReferenceBuild> references,
        HashSet<string> seen,
        IReadOnlyDictionary<string, IndexedFileBuild> filesByRelativePath,
        IndexedFileBuild file,
        SyntaxNode node,
        TextSpan anchorSpan,
        ISymbol? symbol,
        string referenceKind,
        bool isCallSite)
    {
        string? targetStableKey = GetStableKeyForSymbol(symbol, filesByRelativePath);
        if (string.IsNullOrWhiteSpace(targetStableKey))
        {
            return;
        }

        FileLinePositionSpan span = file.SyntaxTree.GetLineSpan(anchorSpan);
        int line = span.StartLinePosition.Line + 1;
        int column = span.StartLinePosition.Character + 1;
        string? callerStableKey = GetCallerStableKey(file.SyntaxTree, file.RelativePath, node);
        string snippet = GetLineSnippet(file.SyntaxTree, node.Span);
        string seenKey = $"{targetStableKey}|{NormalizeIndexPath(file.RelativePath)}|{line}|{column}|{referenceKind}";
        if (!seen.Add(seenKey))
        {
            return;
        }

        references.Add(new IndexedReferenceBuild(
            targetStableKey,
            callerStableKey,
            file.RelativePath,
            referenceKind,
            isCallSite,
            line,
            column,
            snippet));
    }

    private static IEnumerable<ISymbol> GetAwaitUsingDisposeSymbols(SemanticModel model, UsingStatementSyntax usingStatement)
    {
        if (usingStatement.Expression is not null)
        {
            foreach (ISymbol symbol in GetDisposeAsyncSymbols(model.GetTypeInfo(usingStatement.Expression).Type))
            {
                yield return symbol;
            }
        }

        if (usingStatement.Declaration is null)
        {
            yield break;
        }

        foreach (VariableDeclaratorSyntax variable in usingStatement.Declaration.Variables)
        {
            ITypeSymbol? type = variable.Initializer is null
                ? model.GetTypeInfo(usingStatement.Declaration.Type).Type
                : model.GetTypeInfo(variable.Initializer.Value).Type;
            foreach (ISymbol symbol in GetDisposeAsyncSymbols(type))
            {
                yield return symbol;
            }
        }
    }

    private static IEnumerable<ISymbol> GetDisposeAsyncSymbols(ITypeSymbol? type)
    {
        if (type is null)
        {
            yield break;
        }

        foreach (ISymbol member in type.GetMembers("DisposeAsync"))
        {
            if (member is IMethodSymbol { Parameters.Length: 0 })
            {
                yield return member;
            }
        }
    }

    private static IEnumerable<IndexedSymbolBuild> BuildSymbolsForNode(SyntaxTree tree, string relativePath, SyntaxNode node, bool isGenerated)
    {
        return node switch
        {
            EnumMemberDeclarationSyntax enumMember => BuildSymbolForSyntax(tree, relativePath, enumMember, "enum_member", enumMember.Identifier.ValueText, enumMember.Identifier.ValueText, BuildEnumMemberSignature(enumMember), string.Empty, isGenerated),
            MemberDeclarationSyntax member => BuildSymbolsForMember(tree, relativePath, member, isGenerated),
            LocalFunctionStatementSyntax localFunction => BuildSymbolForSyntax(tree, relativePath, localFunction, "local_function", localFunction.Identifier.ValueText, localFunction.Identifier.ValueText, BuildLocalFunctionSignature(localFunction), BuildParameterSuffix(localFunction.ParameterList), isGenerated),
            ParenthesizedLambdaExpressionSyntax lambda => BuildLambdaSymbol(tree, relativePath, lambda, isGenerated),
            SimpleLambdaExpressionSyntax lambda => BuildLambdaSymbol(tree, relativePath, lambda, isGenerated),
            _ => []
        };
    }

    private static IEnumerable<IndexedSymbolBuild> BuildSymbolsForMember(SyntaxTree tree, string relativePath, MemberDeclarationSyntax member, bool isGenerated)
    {
        string @namespace = GetContainingNamespace(member);
        string? containingType = GetContainingType(member);
        string relativeKeyPath = relativePath.Replace('\\', '/');
        string textHash = ComputeSha256Text(member.NormalizeWhitespace().ToFullString());
        foreach ((string Kind, string Name, string KeyName, string Signature, string ParameterSuffix) item in GetMemberSymbolParts(member))
        {
            FileLinePositionSpan span = tree.GetLineSpan(member.Span);
            string stableKey = $"{relativeKeyPath}::{@namespace}::{containingType ?? string.Empty}::{item.Kind}::{item.KeyName}{item.ParameterSuffix}";
            int startLine = span.StartLinePosition.Line + 1;
            int endLine = span.EndLinePosition.Line + 1;
            int startColumn = span.StartLinePosition.Character + 1;
            int endColumn = span.EndLinePosition.Character + 1;
            string sourceAnchor = $"{relativeKeyPath}:{startLine}:{startColumn}-{endLine}:{endColumn}";
            yield return new IndexedSymbolBuild(
                stableKey,
                @namespace,
                containingType,
                item.Kind,
                item.Name,
                item.Signature,
                textHash,
                startLine,
                endLine,
                startColumn,
                endColumn,
                member.SpanStart,
                member.Span.Length,
                sourceAnchor,
                BuildSelectorJson(stableKey, relativeKeyPath, @namespace, containingType, item.Kind, item.Name, GetParameterTypes(member)),
                GetDeclaredAccessibility(member),
                isGenerated,
                IsPartial(member));
        }
    }

    private static IEnumerable<IndexedSymbolBuild> BuildSymbolForSyntax(
        SyntaxTree tree,
        string relativePath,
        SyntaxNode node,
        string kind,
        string name,
        string keyName,
        string signature,
        string parameterSuffix,
        bool isGenerated)
    {
        string @namespace = GetContainingNamespace(node);
        string? containingType = GetContainingType(node);
        string relativeKeyPath = relativePath.Replace('\\', '/');
        string textHash = ComputeSha256Text(node.NormalizeWhitespace().ToFullString());
        FileLinePositionSpan span = tree.GetLineSpan(node.Span);
        int startLine = span.StartLinePosition.Line + 1;
        int endLine = span.EndLinePosition.Line + 1;
        int startColumn = span.StartLinePosition.Character + 1;
        int endColumn = span.EndLinePosition.Character + 1;
        string stableKey = $"{relativeKeyPath}::{@namespace}::{containingType ?? string.Empty}::{kind}::{keyName}{parameterSuffix}";
        string sourceAnchor = $"{relativeKeyPath}:{startLine}:{startColumn}-{endLine}:{endColumn}";
        yield return new IndexedSymbolBuild(
            stableKey,
            @namespace,
            containingType,
            kind,
            name,
            signature,
            textHash,
            startLine,
            endLine,
            startColumn,
            endColumn,
            node.SpanStart,
            node.Span.Length,
            sourceAnchor,
            BuildSelectorJson(stableKey, relativeKeyPath, @namespace, containingType, kind, name, GetParameterTypes(node)),
            GetDeclaredAccessibility(node),
            isGenerated,
            IsPartial(node));
    }

    private static IEnumerable<IndexedSymbolBuild> BuildLambdaSymbol(SyntaxTree tree, string relativePath, LambdaExpressionSyntax lambda, bool isGenerated)
    {
        FileLinePositionSpan span = tree.GetLineSpan(lambda.Span);
        string name = $"lambda@{span.StartLinePosition.Line + 1}:{span.StartLinePosition.Character + 1}";
        return BuildSymbolForSyntax(tree, relativePath, lambda, "lambda", name, name, "lambda", string.Empty, isGenerated);
    }

    private static IEnumerable<(string Kind, string Name, string KeyName, string Signature, string ParameterSuffix)> GetMemberSymbolParts(MemberDeclarationSyntax member)
    {
        switch (member)
        {
            case ClassDeclarationSyntax node:
                yield return ("class", node.Identifier.ValueText, node.Identifier.ValueText, BuildSignature(node), string.Empty);
                break;
            case StructDeclarationSyntax node:
                yield return ("struct", node.Identifier.ValueText, node.Identifier.ValueText, BuildSignature(node), string.Empty);
                break;
            case InterfaceDeclarationSyntax node:
                yield return ("interface", node.Identifier.ValueText, node.Identifier.ValueText, BuildSignature(node), string.Empty);
                break;
            case RecordDeclarationSyntax node:
                yield return ("record", node.Identifier.ValueText, node.Identifier.ValueText, BuildSignature(node), BuildParameterSuffix(node.ParameterList));
                break;
            case EnumDeclarationSyntax node:
                yield return ("enum", node.Identifier.ValueText, node.Identifier.ValueText, BuildSignature(node), string.Empty);
                break;
            case DelegateDeclarationSyntax node:
                yield return ("delegate", node.Identifier.ValueText, node.Identifier.ValueText, BuildSignature(node), BuildParameterSuffix(node.ParameterList));
                break;
            case MethodDeclarationSyntax node:
                yield return ("method", node.Identifier.ValueText, node.Identifier.ValueText, BuildSignature(node), BuildParameterSuffix(node.ParameterList));
                break;
            case ConstructorDeclarationSyntax node:
                yield return ("constructor", node.Identifier.ValueText, node.Identifier.ValueText, BuildSignature(node), BuildParameterSuffix(node.ParameterList));
                break;
            case IndexerDeclarationSyntax node:
                yield return ("indexer", "this[]", "this", BuildSignature(node), BuildBracketedParameterSuffix(node.ParameterList));
                break;
            case OperatorDeclarationSyntax node:
                yield return ("operator", $"operator {node.OperatorToken.ValueText}", node.OperatorToken.ValueText, BuildSignature(node), BuildParameterSuffix(node.ParameterList));
                break;
            case ConversionOperatorDeclarationSyntax node:
                string conversionName = $"{node.ImplicitOrExplicitKeyword.ValueText} operator {node.Type}";
                yield return ("conversion", conversionName, node.Type.ToString(), BuildSignature(node), BuildParameterSuffix(node.ParameterList));
                break;
            case PropertyDeclarationSyntax node:
                yield return ("property", node.Identifier.ValueText, node.Identifier.ValueText, BuildSignature(node), string.Empty);
                break;
            case EventDeclarationSyntax node:
                yield return ("event", node.Identifier.ValueText, node.Identifier.ValueText, BuildSignature(node), string.Empty);
                break;
            case EventFieldDeclarationSyntax node:
                yield return ("event", BuildVariableNameList(node.Declaration.Variables, includeSpaces: true), BuildVariableNameList(node.Declaration.Variables, includeSpaces: false), BuildSignature(node), string.Empty);
                break;
            case FieldDeclarationSyntax node:
                yield return ("field", BuildVariableNameList(node.Declaration.Variables, includeSpaces: true), BuildVariableNameList(node.Declaration.Variables, includeSpaces: false), BuildSignature(node), string.Empty);
                break;
        }
    }

    private static string BuildSignature(MemberDeclarationSyntax member)
    {
        return member.WithoutLeadingTrivia() switch
        {
            MethodDeclarationSyntax method => BuildMethodSignature(method),
            ConstructorDeclarationSyntax constructor => BuildConstructorSignature(constructor),
            PropertyDeclarationSyntax property => BuildPropertySignature(property),
            FieldDeclarationSyntax field => BuildFieldSignature(field),
            EventFieldDeclarationSyntax eventField => BuildEventFieldSignature(eventField),
            EventDeclarationSyntax evt => BuildEventSignature(evt),
            DelegateDeclarationSyntax del => BuildDelegateSignature(del),
            IndexerDeclarationSyntax indexer => BuildIndexerSignature(indexer),
            OperatorDeclarationSyntax op => BuildOperatorSignature(op),
            ConversionOperatorDeclarationSyntax conversion => BuildConversionSignature(conversion),
            BaseTypeDeclarationSyntax type => BuildTypeSignature(type),
            _ => member.WithoutLeadingTrivia().NormalizeWhitespace().ToFullString().Replace(Environment.NewLine, " ", StringComparison.Ordinal).Trim()
        };
    }

    private static string BuildMethodSignature(MethodDeclarationSyntax method)
    {
        string prefix = BuildModifierPrefix(method.Modifiers);
        return $"{prefix}{method.ReturnType} {method.Identifier.ValueText}({BuildParameterList(method.ParameterList.Parameters)})";
    }

    private static string BuildConstructorSignature(ConstructorDeclarationSyntax constructor)
    {
        string prefix = BuildModifierPrefix(constructor.Modifiers);
        return $"{prefix}{constructor.Identifier.ValueText}({BuildParameterList(constructor.ParameterList.Parameters)})";
    }

    private static string BuildPropertySignature(PropertyDeclarationSyntax property)
    {
        string prefix = BuildModifierPrefix(property.Modifiers);
        string accessorText = property.AccessorList is null
            ? "{ get; }"
            : "{ " + string.Join(" ", property.AccessorList.Accessors.Select(BuildAccessorSignature)) + " }";
        string initializer = property.Initializer is null ? string.Empty : $" {property.Initializer}";
        string terminator = property.Initializer is null ? string.Empty : ";";
        return $"{prefix}{property.Type} {property.Identifier.ValueText} {accessorText}{initializer}{terminator}";
    }

    private static string BuildFieldSignature(FieldDeclarationSyntax field)
    {
        string prefix = BuildModifierPrefix(field.Modifiers);
        return $"{prefix}{field.Declaration.Type} {BuildVariableList(field.Declaration.Variables)}";
    }

    private static string BuildEventFieldSignature(EventFieldDeclarationSyntax eventField)
    {
        string prefix = BuildModifierPrefix(eventField.Modifiers);
        return $"{prefix}event {eventField.Declaration.Type} {BuildVariableList(eventField.Declaration.Variables)}";
    }

    private static string BuildEventSignature(EventDeclarationSyntax evt)
    {
        string prefix = BuildModifierPrefix(evt.Modifiers);
        return $"{prefix}event {evt.Type} {evt.Identifier.ValueText}";
    }

    private static string BuildDelegateSignature(DelegateDeclarationSyntax del)
    {
        string prefix = BuildModifierPrefix(del.Modifiers);
        return $"{prefix}delegate {del.ReturnType} {del.Identifier.ValueText}({BuildParameterList(del.ParameterList.Parameters)})";
    }

    private static string BuildIndexerSignature(IndexerDeclarationSyntax indexer)
    {
        string prefix = BuildModifierPrefix(indexer.Modifiers);
        return $"{prefix}{indexer.Type} this[{BuildParameterList(indexer.ParameterList.Parameters)}]";
    }

    private static string BuildOperatorSignature(OperatorDeclarationSyntax op)
    {
        string prefix = BuildModifierPrefix(op.Modifiers);
        return $"{prefix}{op.ReturnType} operator {op.OperatorToken.ValueText}({BuildParameterList(op.ParameterList.Parameters)})";
    }

    private static string BuildConversionSignature(ConversionOperatorDeclarationSyntax conversion)
    {
        string prefix = BuildModifierPrefix(conversion.Modifiers);
        return $"{prefix}{conversion.ImplicitOrExplicitKeyword.ValueText} operator {conversion.Type}({BuildParameterList(conversion.ParameterList.Parameters)})";
    }

    private static string BuildEnumMemberSignature(EnumMemberDeclarationSyntax enumMember)
    {
        string value = enumMember.EqualsValue is null ? string.Empty : $" {enumMember.EqualsValue}";
        return $"{enumMember.Identifier.ValueText}{value}";
    }

    private static string BuildLocalFunctionSignature(LocalFunctionStatementSyntax localFunction)
    {
        string prefix = BuildModifierPrefix(localFunction.Modifiers);
        return $"{prefix}{localFunction.ReturnType} {localFunction.Identifier.ValueText}({BuildParameterList(localFunction.ParameterList.Parameters)})";
    }

    private static string BuildTypeSignature(BaseTypeDeclarationSyntax type)
    {
        string prefix = BuildModifierPrefix(type.Modifiers);
        return $"{prefix}{GetTypeDeclarationKeyword(type)} {type.Identifier.ValueText}{BuildBaseListSuffix(type)}";
    }

    private static string BuildParameterList(SeparatedSyntaxList<ParameterSyntax> parameters)
    {
        return string.Join(", ", parameters.Select(parameter =>
        {
            string prefix = BuildModifierPrefix(parameter.Modifiers);
            string defaultValue = parameter.Default is null ? string.Empty : $" = {parameter.Default.Value}";
            return $"{prefix}{parameter.Type} {parameter.Identifier.ValueText}{defaultValue}";
        }));
    }

    private static string BuildVariableList(SeparatedSyntaxList<VariableDeclaratorSyntax> variables)
    {
        return string.Join(", ", variables.Select(variable => variable.ToString()));
    }

    private static string BuildAccessorSignature(AccessorDeclarationSyntax accessor)
    {
        string prefix = BuildModifierPrefix(accessor.Modifiers);
        return $"{prefix}{accessor.Keyword.ValueText};";
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

    private static string GetDeclaredAccessibility(SyntaxNode node)
    {
        SyntaxTokenList modifiers = node switch
        {
            MemberDeclarationSyntax member => member.Modifiers,
            LocalFunctionStatementSyntax localFunction => localFunction.Modifiers,
            _ => default
        };
        if (modifiers.Any(SyntaxKind.PublicKeyword))
        {
            return "public";
        }

        if (modifiers.Any(SyntaxKind.PrivateKeyword))
        {
            return "private";
        }

        if (modifiers.Any(SyntaxKind.ProtectedKeyword) && modifiers.Any(SyntaxKind.InternalKeyword))
        {
            return "protected internal";
        }

        if (modifiers.Any(SyntaxKind.ProtectedKeyword))
        {
            return "protected";
        }

        if (modifiers.Any(SyntaxKind.InternalKeyword))
        {
            return "internal";
        }

        return string.Empty;
    }

    private static bool IsPartial(SyntaxNode node)
    {
        return node is MemberDeclarationSyntax member
            && member.Modifiers.Any(SyntaxKind.PartialKeyword);
    }

    private static bool IsGeneratedFile(CompilationUnitSyntax root)
    {
        return root.GetLeadingTrivia().Any(trivia =>
            trivia.ToFullString().Contains("<auto-generated", StringComparison.OrdinalIgnoreCase)
            || trivia.ToFullString().Contains("<autogenerated", StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildModifierPrefix(SyntaxTokenList modifiers)
    {
        string text = modifiers.ToFullString().Trim();
        return string.IsNullOrWhiteSpace(text) ? string.Empty : $"{text} ";
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

    private static string BuildBracketedParameterSuffix(BracketedParameterListSyntax? parameterList)
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

    private static IReadOnlyList<string> GetParameterTypes(MemberDeclarationSyntax member)
    {
        if (member is IndexerDeclarationSyntax indexer)
        {
            return indexer.ParameterList.Parameters.Select(GetStableKeyParameterType).ToArray();
        }

        ParameterListSyntax? parameterList = member switch
        {
            BaseMethodDeclarationSyntax method => method.ParameterList,
            DelegateDeclarationSyntax del => del.ParameterList,
            RecordDeclarationSyntax record => record.ParameterList,
            _ => null
        };
        return parameterList?.Parameters.Select(GetStableKeyParameterType).ToArray() ?? [];
    }

    private static IReadOnlyList<string> GetParameterTypes(SyntaxNode node)
    {
        if (node is IndexerDeclarationSyntax indexer)
        {
            return indexer.ParameterList.Parameters.Select(GetStableKeyParameterType).ToArray();
        }

        ParameterListSyntax? parameterList = node switch
        {
            BaseMethodDeclarationSyntax method => method.ParameterList,
            DelegateDeclarationSyntax del => del.ParameterList,
            RecordDeclarationSyntax record => record.ParameterList,
            LocalFunctionStatementSyntax localFunction => localFunction.ParameterList,
            _ => null
        };
        return parameterList?.Parameters.Select(GetStableKeyParameterType).ToArray() ?? [];
    }

    private static string BuildSelectorJson(
        string stableKey,
        string relativePath,
        string namespaceName,
        string? containingType,
        string kind,
        string name,
        IReadOnlyList<string> parameterTypes)
    {
        return JsonSerializer.Serialize(new
        {
            stableSymbolKey = stableKey,
            relativePath,
            memberKind = kind,
            name,
            containingNamespace = namespaceName,
            containingType,
            parameterTypes
        });
    }

    private static string? GetStableKeyForSymbol(ISymbol? symbol, IReadOnlyDictionary<string, IndexedFileBuild> filesByRelativePath)
    {
        if (symbol is null)
        {
            return null;
        }

        symbol = NormalizeSymbol(symbol);
        if (!IsIndexedDeclarationSymbol(symbol))
        {
            return null;
        }

        SyntaxReference? syntaxReference = symbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxReference is null)
        {
            return null;
        }

        SyntaxNode syntax = GetIndexedDeclarationSyntax(syntaxReference.GetSyntax());
        if (!IsIndexedDeclarationSyntax(syntax))
        {
            return null;
        }

        string relativePath = NormalizeIndexPath(Path.GetFileName(syntax.SyntaxTree.FilePath));
        foreach (IndexedFileBuild file in filesByRelativePath.Values)
        {
            if (Path.GetFullPath(file.SyntaxTree.FilePath).Equals(Path.GetFullPath(syntax.SyntaxTree.FilePath), StringComparison.OrdinalIgnoreCase))
            {
                relativePath = file.RelativePath;
                break;
            }
        }

        string? expectedKind = GetIndexedSymbolKind(symbol, syntax);
        string expectedName = GetIndexedSymbolName(symbol);
        bool isGenerated = IsGeneratedFile(syntax.SyntaxTree.GetCompilationUnitRoot());
        IndexedSymbolBuild[] candidates = BuildSymbolsForNode(syntax.SyntaxTree, relativePath, syntax, isGenerated).ToArray();
        IndexedSymbolBuild? exact = candidates.FirstOrDefault(candidate =>
            (expectedKind is null || candidate.Kind.Equals(expectedKind, StringComparison.OrdinalIgnoreCase))
            && SymbolNameMatches(candidate.Name, expectedName));
        return (exact ?? candidates.FirstOrDefault())?.StableKey;
    }

    private static bool IsIndexedDeclarationSymbol(ISymbol symbol)
    {
        return symbol is IMethodSymbol
            or INamedTypeSymbol
            or IPropertySymbol
            or IFieldSymbol
            or IEventSymbol;
    }

    private static SyntaxNode GetIndexedDeclarationSyntax(SyntaxNode syntax)
    {
        return syntax switch
        {
            AccessorDeclarationSyntax accessor when accessor.Parent?.Parent is IndexerDeclarationSyntax indexer => indexer,
            AccessorDeclarationSyntax accessor when accessor.Parent?.Parent is PropertyDeclarationSyntax property => property,
            AccessorDeclarationSyntax accessor when accessor.Parent?.Parent is EventDeclarationSyntax evt => evt,
            VariableDeclaratorSyntax variable when variable.Parent?.Parent is FieldDeclarationSyntax field => field,
            VariableDeclaratorSyntax variable when variable.Parent?.Parent is EventFieldDeclarationSyntax eventField => eventField,
            _ => syntax
        };
    }

    private static bool IsIndexedDeclarationSyntax(SyntaxNode syntax)
    {
        return syntax is MemberDeclarationSyntax
            or EnumMemberDeclarationSyntax
            or LocalFunctionStatementSyntax
            or ParenthesizedLambdaExpressionSyntax
            or SimpleLambdaExpressionSyntax;
    }

    private static ISymbol NormalizeSymbol(ISymbol symbol)
    {
        if (symbol is IMethodSymbol { ReducedFrom: not null } reduced)
        {
            symbol = reduced.ReducedFrom;
        }

        if (symbol is IMethodSymbol method)
        {
            return method.PartialDefinitionPart ?? method.OriginalDefinition;
        }

        if (symbol is INamedTypeSymbol namedType)
        {
            return namedType.OriginalDefinition;
        }

        return symbol.OriginalDefinition;
    }

    private static string? GetIndexedSymbolKind(ISymbol symbol, SyntaxNode declaration)
    {
        return symbol switch
        {
            IMethodSymbol { MethodKind: MethodKind.Constructor } => "constructor",
            IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } => "operator",
            IMethodSymbol { MethodKind: MethodKind.Conversion } => "conversion",
            IMethodSymbol => declaration is LocalFunctionStatementSyntax ? "local_function" : "method",
            IPropertySymbol => declaration is IndexerDeclarationSyntax ? "indexer" : "property",
            IFieldSymbol => declaration is EnumMemberDeclarationSyntax ? "enum_member" : "field",
            IEventSymbol => "event",
            INamedTypeSymbol => declaration switch
            {
                ClassDeclarationSyntax => "class",
                StructDeclarationSyntax => "struct",
                InterfaceDeclarationSyntax => "interface",
                RecordDeclarationSyntax => "record",
                EnumDeclarationSyntax => "enum",
                DelegateDeclarationSyntax => "delegate",
                _ => null
            },
            _ => null
        };
    }

    private static string GetIndexedSymbolName(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol { MethodKind: MethodKind.Constructor, ContainingType: not null } method => method.ContainingType.Name,
            IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } method => $"operator {GetOperatorTokenText(method.Name)}",
            IMethodSymbol { MethodKind: MethodKind.Conversion } method => $"{method.Name.Replace("op_", string.Empty, StringComparison.Ordinal).ToLowerInvariant()} operator {method.ReturnType}",
            IPropertySymbol { IsIndexer: true } => "this[]",
            _ => symbol.Name
        };
    }

    private static string GetOperatorTokenText(string metadataName)
    {
        return metadataName switch
        {
            "op_Addition" => "+",
            "op_Subtraction" => "-",
            "op_Multiply" => "*",
            "op_Division" => "/",
            "op_Modulus" => "%",
            "op_Equality" => "==",
            "op_Inequality" => "!=",
            "op_LessThan" => "<",
            "op_GreaterThan" => ">",
            "op_LessThanOrEqual" => "<=",
            "op_GreaterThanOrEqual" => ">=",
            "op_UnaryNegation" => "-",
            "op_UnaryPlus" => "+",
            "op_LogicalNot" => "!",
            _ => metadataName
        };
    }

    private static bool SymbolNameMatches(string indexedName, string symbolName)
    {
        return indexedName.Equals(symbolName, StringComparison.Ordinal)
            || indexedName.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Any(name => name.Equals(symbolName, StringComparison.Ordinal));
    }

    private static string? GetCallerStableKey(SyntaxTree tree, string relativePath, SyntaxNode node)
    {
        SyntaxNode? caller = node.Ancestors().FirstOrDefault(candidate =>
            candidate is ParenthesizedLambdaExpressionSyntax
                or SimpleLambdaExpressionSyntax
                or LocalFunctionStatementSyntax
                or MethodDeclarationSyntax
                or ConstructorDeclarationSyntax
                or PropertyDeclarationSyntax
                or EventDeclarationSyntax
                or DelegateDeclarationSyntax);
        if (caller is null)
        {
            return null;
        }

        bool isGenerated = IsGeneratedFile(tree.GetCompilationUnitRoot());
        return BuildSymbolsForNode(tree, relativePath, caller, isGenerated).FirstOrDefault()?.StableKey;
    }

    private static bool IsInvocationName(SimpleNameSyntax name, out InvocationExpressionSyntax? invocation)
    {
        invocation = name.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        return invocation?.Expression switch
        {
            SimpleNameSyntax simpleName => simpleName == name,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name == name,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name == name,
            _ => false
        };
    }

    private static string? GetCallTargetStableKey(
        SemanticModel model,
        SimpleNameSyntax name,
        IReadOnlyDictionary<string, IndexedFileBuild> filesByRelativePath,
        out string? callKind)
    {
        callKind = null;
        if (IsInvocationName(name, out InvocationExpressionSyntax? invocation) && invocation is not null)
        {
            callKind = "invocation";
            ISymbol? invocationSymbol = GetBestSymbol(model.GetSymbolInfo(invocation))
                ?? GetBestSymbol(model.GetSymbolInfo(name));
            return GetStableKeyForSymbol(invocationSymbol, filesByRelativePath);
        }

        ObjectCreationExpressionSyntax? objectCreation = name.FirstAncestorOrSelf<ObjectCreationExpressionSyntax>();
        if (objectCreation is not null && objectCreation.Type.Span.Contains(name.Span))
        {
            callKind = "construction";
            ISymbol? constructionSymbol = GetBestSymbol(model.GetSymbolInfo(objectCreation))
                ?? GetBestSymbol(model.GetSymbolInfo(name));
            return GetStableKeyForSymbol(constructionSymbol, filesByRelativePath);
        }

        return null;
    }

    private static ISymbol? GetBestSymbol(SymbolInfo symbolInfo)
    {
        return symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
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

    private static string ClassifyReferenceKind(SimpleNameSyntax name)
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

    private static string GetLineSnippet(SyntaxTree tree, TextSpan span)
    {
        SourceText text = tree.GetText();
        LinePosition linePosition = text.Lines.GetLinePosition(span.Start);
        TextLine line = text.Lines[linePosition.Line];
        return line.ToString().Trim();
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

        foreach (string path in EnumerateObservedBinaryReferences(observedRoot))
        {
            AddReference(references, path);
        }

        foreach (string path in EnumerateWindowsDesktopReferences())
        {
            AddReference(references, path);
        }

        if (references.Count == 0)
        {
            AddReference(references, typeof(object).Assembly.Location);
        }

        return references.Values.ToArray();
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
            // A bad binary reference should not prevent index rebuild for source files.
        }
    }

    private static string GetContainingNamespace(SyntaxNode node)
    {
        IEnumerable<string> names = node.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Reverse()
            .Select(ns => ns.Name.ToString());
        return string.Join(".", names);
    }

    private static string? GetContainingType(SyntaxNode node)
    {
        string[] names = node.Ancestors()
            .OfType<BaseTypeDeclarationSyntax>()
            .Reverse()
            .Select(type => type.Identifier.ValueText)
            .ToArray();
        return names.Length == 0 ? null : string.Join(".", names);
    }

    private static IndexedDiagnosticBuild BuildDiagnostic(SyntaxTree tree, Diagnostic diagnostic)
    {
        FileLinePositionSpan span = tree.GetLineSpan(diagnostic.Location.SourceSpan);
        return new IndexedDiagnosticBuild(
            diagnostic.Severity.ToString(),
            diagnostic.Id,
            diagnostic.GetMessage(),
            span.StartLinePosition.Line + 1,
            span.EndLinePosition.Line + 1);
    }

    private static IReadOnlyList<SolutionIndexFile> QueryFiles(SqliteConnection connection, string scope, string? value, int maxFiles)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            select relative_path, sha256, length, last_write_time_utc, parse_status, diagnostic_count
            from files
            where ($scope = 'solution')
                or ($scope = 'file' and relative_path = $value)
                or ($scope = 'folder' and replace(relative_path, '\', '/') like $valuePrefix escape '\')
                or ($scope = 'namespace' and exists (
                    select 1 from symbols s where s.file_id = files.id and s.namespace = $value
                ))
            order by relative_path collate nocase
            limit $limit;
            """;
        BindScopeParameters(command, scope, value, maxFiles);
        List<SolutionIndexFile> files = [];
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            files.Add(new SolutionIndexFile(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2),
                DateTimeOffset.Parse(reader.GetString(3)),
                reader.GetString(4),
                reader.GetInt32(5),
                false));
        }

        return files;
    }

    private static IReadOnlyList<SolutionIndexSymbol> QuerySymbols(SqliteConnection connection, string scope, string? value, int maxSymbols)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            select s.stable_key, f.relative_path, f.sha256, s.text_hash, s.namespace, s.containing_type, s.kind, s.name, s.signature, s.start_line, s.end_line, s.start_column, s.end_column, s.text_span_start, s.text_span_length, s.source_anchor, s.selector_json, s.declared_accessibility, s.is_generated, s.is_partial
            from symbols s
            join files f on f.id = s.file_id
            where ($scope = 'solution')
                or ($scope = 'file' and f.relative_path = $value)
                or ($scope = 'folder' and replace(f.relative_path, '\', '/') like $valuePrefix escape '\')
                or ($scope = 'namespace' and s.namespace = $value)
            order by f.relative_path collate nocase, s.start_line, s.name collate nocase
            limit $limit;
            """;
        BindScopeParameters(command, scope, value, maxSymbols);
        return ReadSymbols(command);
    }

    private static IReadOnlyList<SolutionIndexSymbol> ReadSymbols(SqliteCommand command)
    {
        List<SolutionIndexSymbol> symbols = [];
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            symbols.Add(new SolutionIndexSymbol(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetInt32(9),
                reader.GetInt32(10),
                reader.GetInt32(11),
                reader.GetInt32(12),
                reader.GetInt32(13),
                reader.GetInt32(14),
                reader.GetString(15),
                reader.GetString(16),
                reader.GetString(17),
                reader.GetInt32(18) != 0,
                reader.GetInt32(19) != 0));
        }

        return symbols;
    }

    private IReadOnlyList<SolutionIndexReference> QueryReferenceRows(string stableSymbolKey, bool onlyCallSites, int maxResults)
    {
        stableSymbolKey = NormalizeStableSymbolKey(stableSymbolKey);
        string dbPath = GetIndexDatabasePath();
        if (!File.Exists(dbPath) || string.IsNullOrWhiteSpace(stableSymbolKey))
        {
            return [];
        }

        maxResults = Math.Clamp(maxResults, 1, 5000);
        using SqliteConnection connection = OpenConnection(dbPath);
        if (!TableExists(connection, onlyCallSites ? "call_sites" : "symbol_references"))
        {
            return [];
        }

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = onlyCallSites
            ? """
              select target.stable_key, location.relative_path, location.sha256, caller.stable_key, caller.name, 'invocation', c.line, c.column, c.snippet
              from call_sites c
              join symbols target on target.id = c.callee_symbol_id
              join files location on location.id = c.file_id
              left join symbols caller on caller.id = c.caller_symbol_id
              where target.stable_key = $stableKey
              order by location.relative_path collate nocase, c.line, c.column
              limit $limit;
              """
            : """
              select target.stable_key, location.relative_path, location.sha256, caller.stable_key, caller.name, r.reference_kind, r.line, r.column, r.snippet
              from symbol_references r
              join symbols target on target.id = r.target_symbol_id
              join files location on location.id = r.file_id
              left join symbols caller on caller.id = r.caller_symbol_id
              where target.stable_key = $stableKey
              order by location.relative_path collate nocase, r.line, r.column
              limit $limit;
              """;
        command.Parameters.AddWithValue("$stableKey", stableSymbolKey);
        command.Parameters.AddWithValue("$limit", maxResults);
        List<SolutionIndexReference> references = [];
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            references.Add(new SolutionIndexReference(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetString(8)));
        }

        return references;
    }

    private static void BindScopeParameters(SqliteCommand command, string scope, string? value, int limit)
    {
        string normalizedValue = scope.Equals("namespace", StringComparison.OrdinalIgnoreCase)
            ? NormalizeNamespaceValue(value)
            : NormalizeRelativePath(value ?? string.Empty);
        command.Parameters.AddWithValue("$scope", scope);
        command.Parameters.AddWithValue("$value", normalizedValue);
        command.Parameters.AddWithValue("$valuePrefix", EscapeLike(NormalizeIndexPath(normalizedValue).TrimEnd('/')) + "/%");
        command.Parameters.AddWithValue("$limit", limit);
    }

    private static int CountStaleFiles(SqliteConnection connection, string observedRoot)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "select relative_path, length, last_write_time_utc from files;";
        int stale = 0;
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string fullPath = Path.Combine(observedRoot, reader.GetString(0));
            if (!File.Exists(fullPath))
            {
                stale++;
                continue;
            }

            FileInfo info = new(fullPath);
            DateTimeOffset indexedWrite = DateTimeOffset.Parse(reader.GetString(2));
            if (info.Length != reader.GetInt64(1) || info.LastWriteTimeUtc != indexedWrite.UtcDateTime)
            {
                stale++;
            }
        }

        return stale;
    }

    private static DateTimeOffset? ReadLastIndexedAt(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "select indexed_at_utc from index_runs order by id desc limit 1;";
        object? result = command.ExecuteScalar();
        return result is string value ? DateTimeOffset.Parse(value) : null;
    }

    private static int ExecuteScalarInt(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "select count(*) from sqlite_master where type = 'table' and name = $tableName;";
        command.Parameters.AddWithValue("$tableName", tableName);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static void ExecuteNonQuery(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static IEnumerable<string> EnumerateSourceFiles(string observedRoot)
    {
        EnumerationOptions options = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        return Directory.EnumerateFiles(observedRoot, "*.cs", options)
            .Where(path => !IsExcludedPath(observedRoot, path))
            .OrderBy(path => Path.GetRelativePath(observedRoot, path), StringComparer.OrdinalIgnoreCase);
    }

    private static string ResolveSourceFilePath(string observedRoot, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        string fullPath = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(observedRoot, path));
        string rootWithSeparator = observedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? observedRoot
            : observedRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            && !fullPath.Equals(observedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Path must be under the watched solution folder.");
        }

        if (!fullPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only C# files can be indexed by file refresh.");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Watched source file was not found.", fullPath);
        }

        return fullPath;
    }

    private static bool IsExcludedPath(string observedRoot, string path)
    {
        string relativePath = Path.GetRelativePath(observedRoot, path);
        string[] parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part => ExcludedDirectoryNames.Contains(part, StringComparer.OrdinalIgnoreCase));
    }

    private static string NormalizeScope(string scope)
    {
        return scope.Trim().ToLowerInvariant() switch
        {
            "file" => "file",
            "folder" => "folder",
            "namespace" => "namespace",
            _ => "solution"
        };
    }

    private static string NormalizeRelativePath(string path)
    {
        return path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }

    private static string NormalizeIndexPath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static string NormalizeStableSymbolKey(string stableSymbolKey)
    {
        int separatorIndex = stableSymbolKey.IndexOf("::", StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            return stableSymbolKey.Replace('\\', '/');
        }

        return stableSymbolKey[..separatorIndex].Replace('\\', '/') + stableSymbolKey[separatorIndex..];
    }

    private static string NormalizeNamespaceValue(string? value)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return normalized.Equals("(global)", StringComparison.OrdinalIgnoreCase) ? string.Empty : normalized;
    }

    private static string EscapeLike(string value)
    {
        return value.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string ComputeSha256Text(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private static string BuildVariableNameList(SeparatedSyntaxList<VariableDeclaratorSyntax> variables, bool includeSpaces)
    {
        string separator = includeSpaces ? ", " : ",";
        return string.Join(separator, variables.Select(variable => variable.Identifier.ValueText));
    }

    private static void EnsureColumn(SqliteConnection connection, string tableName, string columnName, string definition)
    {
        using SqliteCommand exists = connection.CreateCommand();
        exists.CommandText = $"pragma table_info({tableName});";
        using SqliteDataReader reader = exists.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetString(1).Equals(columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        using SqliteCommand alter = connection.CreateCommand();
        alter.CommandText = $"alter table {tableName} add column {columnName} {definition};";
        alter.ExecuteNonQuery();
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
        string hash = Convert.ToHexString(hashBytes, 0, 6).ToLowerInvariant();
        return $"{SanitizePathSegment(leafName)}_{hash}";
    }

    private static string SanitizePathSegment(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string sanitized = new(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "item" : sanitized;
    }

    private static string TrimDirectorySeparator(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private sealed record IndexedFileBuild(
        string FullPath,
        string RelativePath,
        SyntaxTree SyntaxTree,
        CompilationUnitSyntax Root,
        string Sha256,
        long Length,
        DateTime LastWriteTimeUtc,
        string ParseStatus,
        IReadOnlyList<IndexedSymbolBuild> Symbols,
        IReadOnlyList<IndexedDiagnosticBuild> Diagnostics);

    private sealed record IndexedSymbolBuild(
        string StableKey,
        string Namespace,
        string? ContainingType,
        string Kind,
        string Name,
        string Signature,
        string TextHash,
        int StartLine,
        int EndLine,
        int StartColumn,
        int EndColumn,
        int TextSpanStart,
        int TextSpanLength,
        string SourceAnchor,
        string SelectorJson,
        string DeclaredAccessibility,
        bool IsGenerated,
        bool IsPartial);

    private sealed record IndexedDiagnosticBuild(
        string Severity,
        string DiagnosticId,
        string Message,
        int StartLine,
        int EndLine);

    private sealed record IndexedReferenceBuild(
        string TargetStableKey,
        string? CallerStableKey,
        string RelativePath,
        string ReferenceKind,
        bool IsCallSite,
        int Line,
        int Column,
        string Snippet);
}

[Description("Status for the monitor-owned watched solution index.")]
public sealed record SolutionIndexStatus(
    string DatabasePath,
    string WatchedSolutionPath,
    string ObservedRoot,
    string ObservedRootKey,
    DateTimeOffset? LastIndexedAtUtc,
    int FileCount,
    int SymbolCount,
    int DiagnosticCount,
    int ReferenceCount,
    int CallSiteCount,
    int StaleFileCount,
    bool IsMissing);

[Description("Result from rebuilding the monitor-owned watched solution index.")]
public sealed record SolutionIndexBuildResult(
    SolutionIndexStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    double DurationMilliseconds,
    int IndexedFileCount,
    int IndexedSymbolCount,
    int IndexedDiagnosticCount,
    int IndexedReferenceCount,
    int IndexedCallSiteCount);

[Description("Result from refreshing one watched C# file in the monitor-owned solution index.")]
public sealed record SolutionIndexFileRefreshResult(
    SolutionIndexStatus Status,
    SolutionIndexFile? File,
    IReadOnlyList<SolutionIndexSymbol> Symbols,
    int DiagnosticCount);

[Description("Query result from the monitor-owned watched solution index.")]
public sealed record SolutionIndexQueryResult(
    string Scope,
    string? Value,
    bool IndexMissing,
    IReadOnlyList<SolutionIndexFile> Files,
    IReadOnlyList<SolutionIndexSymbol> Symbols);

[Description("Read-only tree data for the monitor-owned watched solution index.")]
public sealed record SolutionIndexTree(
    SolutionIndexStatus Status,
    IReadOnlyList<SolutionIndexNamespaceNode> Namespaces);

[Description("Namespace node in the watched solution index tree.")]
public sealed record SolutionIndexNamespaceNode(
    string Namespace,
    IReadOnlyList<string> Files);

[Description("Indexed C# file metadata from the watched solution index.")]
public sealed record SolutionIndexFile(
    string RelativePath,
    string Sha256,
    long Length,
    DateTimeOffset LastWriteTimeUtc,
    string ParseStatus,
    int DiagnosticCount,
    bool IsStale);

[Description("Indexed C# symbol metadata from the watched solution index.")]
public sealed record SolutionIndexSymbol(
    string StableSymbolKey,
    string RelativePath,
    string FileHash,
    string SymbolTextHash,
    string Namespace,
    string? ContainingType,
    string Kind,
    string Name,
    string Signature,
    int StartLine,
    int EndLine,
    int StartColumn,
    int EndColumn,
    int TextSpanStart,
    int TextSpanLength,
    string SourceAnchor,
    string SelectorJson,
    string DeclaredAccessibility,
    bool IsGenerated,
    bool IsPartial);

[Description("Indexed C# reference or call-site metadata from the watched solution index.")]
public sealed record SolutionIndexReference(
    string TargetStableSymbolKey,
    string RelativePath,
    string FileHash,
    string? CallerStableSymbolKey,
    string? CallerName,
    string ReferenceKind,
    int Line,
    int Column,
    string Snippet);
