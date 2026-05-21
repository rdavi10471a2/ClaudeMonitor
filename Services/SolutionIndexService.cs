using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Data.Sqlite;
using MonitorBaseClaude.AI;

namespace MonitorBaseClaude.Services;

[AIFileContext("SolutionIndexService.cs", "Builds and queries the monitor-owned SQLite index for watched C# solution structure.")]
[FileVersion("1.0")]
public sealed class SolutionIndexService
{
    private static readonly string[] ExcludedDirectoryNames =
    [
        ".git",
        ".vs",
        "bin",
        "obj",
        "node_modules"
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
                true);
        }

        using SqliteConnection connection = OpenConnection(dbPath);
        DateTimeOffset? indexedAt = ReadLastIndexedAt(connection);
        int fileCount = ExecuteScalarInt(connection, "select count(*) from files;");
        int symbolCount = ExecuteScalarInt(connection, "select count(*) from symbols;");
        int diagnosticCount = ExecuteScalarInt(connection, "select count(*) from diagnostics;");
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
        ExecuteNonQuery(connection, transaction, "delete from diagnostics;");
        ExecuteNonQuery(connection, transaction, "delete from symbols;");
        ExecuteNonQuery(connection, transaction, "delete from files;");
        ExecuteNonQuery(connection, transaction, "delete from index_runs;");

        long runId = InsertIndexRun(connection, transaction, observedRoot, observedRootKey, watchedSolutionPath, startedAt);
        int indexedFiles = 0;
        int indexedSymbols = 0;
        int indexedDiagnostics = 0;

        foreach (string sourceFilePath in EnumerateSourceFiles(observedRoot))
        {
            IndexedFileBuild file = BuildFileIndex(observedRoot, sourceFilePath);
            long fileId = InsertFile(connection, transaction, runId, file);
            indexedFiles++;
            foreach (IndexedSymbolBuild symbol in file.Symbols)
            {
                InsertSymbol(connection, transaction, fileId, symbol);
                indexedSymbols++;
            }

            foreach (IndexedDiagnosticBuild diagnostic in file.Diagnostics)
            {
                InsertDiagnostic(connection, transaction, fileId, diagnostic);
                indexedDiagnostics++;
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
            indexedFiles,
            indexedSymbols,
            indexedDiagnostics);
    }

    public SolutionIndexFileRefreshResult RefreshFile(string path)
    {
        string observedRoot = GetObservedRoot();
        string sourceFilePath = ResolveSourceFilePath(observedRoot, path);
        string dbPath = GetIndexDatabasePath();
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        using SqliteConnection connection = OpenConnection(dbPath);
        InitializeSchema(connection);

        using SqliteTransaction transaction = connection.BeginTransaction();
        long runId = EnsureIndexRun(connection, transaction, observedRoot);
        string relativePath = Path.GetRelativePath(observedRoot, sourceFilePath);
        DeleteFileIndex(connection, transaction, relativePath);

        IndexedFileBuild file = BuildFileIndex(observedRoot, sourceFilePath);
        long fileId = InsertFile(connection, transaction, runId, file);
        foreach (IndexedSymbolBuild symbol in file.Symbols)
        {
            InsertSymbol(connection, transaction, fileId, symbol);
        }

        foreach (IndexedDiagnosticBuild diagnostic in file.Diagnostics)
        {
            InsertDiagnostic(connection, transaction, fileId, diagnostic);
        }

        transaction.Commit();
        SolutionIndexQueryResult fileQuery = Query("file", relativePath, maxFiles: 1, maxSymbols: 5000);
        return new SolutionIndexFileRefreshResult(
            GetStatus(),
            fileQuery.Files.FirstOrDefault(),
            fileQuery.Symbols,
            file.Diagnostics.Count);
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
        maxFiles = Math.Clamp(maxFiles, 1, 1000);
        maxSymbols = Math.Clamp(maxSymbols, 1, 5000);

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
            select s.stable_key, f.relative_path, s.namespace, s.containing_type, s.kind, s.name, s.signature, s.start_line, s.end_line
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
            command.Parameters.AddWithValue("$namespace", namespaceName.Trim());
        }

        sql.AppendLine("order by s.name collate nocase, f.relative_path collate nocase limit $limit;");
        command.Parameters.AddWithValue("$limit", maxResults);
        command.CommandText = sql.ToString();
        return ReadSymbols(command);
    }

    public SolutionIndexSymbol? GetSymbol(string stableSymbolKey)
    {
        string dbPath = GetIndexDatabasePath();
        if (!File.Exists(dbPath) || string.IsNullOrWhiteSpace(stableSymbolKey))
        {
            return null;
        }

        using SqliteConnection connection = OpenConnection(dbPath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            select s.stable_key, f.relative_path, s.namespace, s.containing_type, s.kind, s.name, s.signature, s.start_line, s.end_line
            from symbols s
            join files f on f.id = s.file_id
            where s.stable_key = $stableKey
            limit 1;
            """;
        command.Parameters.AddWithValue("$stableKey", stableSymbolKey);
        return ReadSymbols(command).FirstOrDefault();
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
                end_line integer not null
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

            create index if not exists ix_files_relative_path on files(relative_path);
            create index if not exists ix_symbols_stable_key on symbols(stable_key);
            create index if not exists ix_symbols_name on symbols(name);
            create index if not exists ix_symbols_namespace on symbols(namespace);
            create index if not exists ix_symbols_kind on symbols(kind);
            """);
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

    private long EnsureIndexRun(SqliteConnection connection, SqliteTransaction transaction, string observedRoot)
    {
        using SqliteCommand select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = "select id from index_runs order by id desc limit 1;";
        object? existing = select.ExecuteScalar();
        if (existing is not null && existing != DBNull.Value)
        {
            return Convert.ToInt64(existing);
        }

        return InsertIndexRun(connection, transaction, observedRoot, BuildObservedRootKey(observedRoot), watchedSolutionPath, DateTimeOffset.UtcNow);
    }

    private static void DeleteFileIndex(SqliteConnection connection, SqliteTransaction transaction, string relativePath)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "delete from files where relative_path = $relativePath;";
        command.Parameters.AddWithValue("$relativePath", relativePath);
        command.ExecuteNonQuery();
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

    private static void InsertSymbol(SqliteConnection connection, SqliteTransaction transaction, long fileId, IndexedSymbolBuild symbol)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            insert into symbols(file_id, stable_key, namespace, containing_type, kind, name, signature, start_line, end_line)
            values ($fileId, $stableKey, $namespace, $containingType, $kind, $name, $signature, $startLine, $endLine);
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
        command.ExecuteNonQuery();
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
        IReadOnlyList<IndexedSymbolBuild> symbols = root.DescendantNodes()
            .OfType<MemberDeclarationSyntax>()
            .SelectMany(member => BuildSymbolsForMember(tree, relativePath, member))
            .OrderBy(symbol => symbol.StartLine)
            .ThenBy(symbol => symbol.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new IndexedFileBuild(
            sourceFilePath,
            relativePath,
            ComputeSha256(sourceFilePath),
            info.Length,
            info.LastWriteTimeUtc,
            diagnostics.Any(diagnostic => diagnostic.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase)) ? "error" : "ok",
            symbols,
            diagnostics);
    }

    private static IEnumerable<IndexedSymbolBuild> BuildSymbolsForMember(SyntaxTree tree, string relativePath, MemberDeclarationSyntax member)
    {
        string @namespace = GetContainingNamespace(member);
        string? containingType = GetContainingType(member);
        string relativeKeyPath = relativePath.Replace('\\', '/');
        foreach ((string Kind, string Name, string Signature, string ParameterSuffix) item in GetMemberSymbolParts(member))
        {
            FileLinePositionSpan span = tree.GetLineSpan(member.Span);
            string stableKey = $"{relativeKeyPath}::{@namespace}::{containingType ?? string.Empty}::{item.Kind}::{item.Name}{item.ParameterSuffix}";
            yield return new IndexedSymbolBuild(
                stableKey,
                @namespace,
                containingType,
                item.Kind,
                item.Name,
                item.Signature,
                span.StartLinePosition.Line + 1,
                span.EndLinePosition.Line + 1);
        }
    }

    private static IEnumerable<(string Kind, string Name, string Signature, string ParameterSuffix)> GetMemberSymbolParts(MemberDeclarationSyntax member)
    {
        switch (member)
        {
            case ClassDeclarationSyntax node:
                yield return ("class", node.Identifier.ValueText, BuildSignature(node), string.Empty);
                break;
            case StructDeclarationSyntax node:
                yield return ("struct", node.Identifier.ValueText, BuildSignature(node), string.Empty);
                break;
            case InterfaceDeclarationSyntax node:
                yield return ("interface", node.Identifier.ValueText, BuildSignature(node), string.Empty);
                break;
            case RecordDeclarationSyntax node:
                yield return ("record", node.Identifier.ValueText, BuildSignature(node), BuildParameterSuffix(node.ParameterList));
                break;
            case EnumDeclarationSyntax node:
                yield return ("enum", node.Identifier.ValueText, BuildSignature(node), string.Empty);
                break;
            case DelegateDeclarationSyntax node:
                yield return ("delegate", node.Identifier.ValueText, BuildSignature(node), BuildParameterSuffix(node.ParameterList));
                break;
            case MethodDeclarationSyntax node:
                yield return ("method", node.Identifier.ValueText, BuildSignature(node), BuildParameterSuffix(node.ParameterList));
                break;
            case ConstructorDeclarationSyntax node:
                yield return ("constructor", node.Identifier.ValueText, BuildSignature(node), BuildParameterSuffix(node.ParameterList));
                break;
            case PropertyDeclarationSyntax node:
                yield return ("property", node.Identifier.ValueText, BuildSignature(node), string.Empty);
                break;
            case EventDeclarationSyntax node:
                yield return ("event", node.Identifier.ValueText, BuildSignature(node), string.Empty);
                break;
            case EventFieldDeclarationSyntax node:
                foreach (VariableDeclaratorSyntax variable in node.Declaration.Variables)
                {
                    yield return ("event", variable.Identifier.ValueText, BuildSignature(node), string.Empty);
                }

                break;
            case FieldDeclarationSyntax node:
                foreach (VariableDeclaratorSyntax variable in node.Declaration.Variables)
                {
                    yield return ("field", variable.Identifier.ValueText, BuildSignature(node), string.Empty);
                }

                break;
        }
    }

    private static string BuildSignature(MemberDeclarationSyntax member)
    {
        string text = member.NormalizeWhitespace(elasticTrivia: false).ToFullString().Trim();
        int bodyIndex = text.IndexOf('{', StringComparison.Ordinal);
        if (bodyIndex >= 0)
        {
            text = text[..bodyIndex].TrimEnd() + " { ... }";
        }

        int accessorIndex = text.IndexOf("=>", StringComparison.Ordinal);
        if (accessorIndex >= 0)
        {
            text = text[..accessorIndex].TrimEnd() + " => ...";
        }

        return text.Replace(Environment.NewLine, " ").Replace("\r", " ").Replace("\n", " ");
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
        SyntaxToken modifier = parameter.Modifiers.FirstOrDefault(token =>
            token.IsKind(SyntaxKind.RefKeyword)
            || token.IsKind(SyntaxKind.OutKeyword)
            || token.IsKind(SyntaxKind.InKeyword)
            || token.IsKind(SyntaxKind.ParamsKeyword));
        return modifier.RawKind == 0 ? type : $"{modifier.Text} {type}";
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
        BaseTypeDeclarationSyntax? type = node.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault();
        return type?.Identifier.ValueText;
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
                or ($scope = 'folder' and relative_path like $valuePrefix escape '\')
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
            select s.stable_key, f.relative_path, s.namespace, s.containing_type, s.kind, s.name, s.signature, s.start_line, s.end_line
            from symbols s
            join files f on f.id = s.file_id
            where ($scope = 'solution')
                or ($scope = 'file' and f.relative_path = $value)
                or ($scope = 'folder' and f.relative_path like $valuePrefix escape '\')
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
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetInt32(7),
                reader.GetInt32(8)));
        }

        return symbols;
    }

    private static void BindScopeParameters(SqliteCommand command, string scope, string? value, int limit)
    {
        string normalizedValue = NormalizeRelativePath(value ?? string.Empty);
        command.Parameters.AddWithValue("$scope", scope);
        command.Parameters.AddWithValue("$value", normalizedValue);
        command.Parameters.AddWithValue("$valuePrefix", EscapeLike(normalizedValue.TrimEnd('\\', '/')) + "\\%");
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
        int StartLine,
        int EndLine);

    private sealed record IndexedDiagnosticBuild(
        string Severity,
        string DiagnosticId,
        string Message,
        int StartLine,
        int EndLine);
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
    int IndexedDiagnosticCount);

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
    string Namespace,
    string? ContainingType,
    string Kind,
    string Name,
    string Signature,
    int StartLine,
    int EndLine);
