using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MonitorBaseClaude.McpServer;

public sealed partial class MonitorWorkflowService
{
    private const int DefaultHistoryRetentionDays = 7;
    private const int DefaultRunLogEntryRetentionLimit = 500;

    public IReadOnlyList<MonitorRunEntry> ListMonitorRuns(int maxEntries = 100)
    {
        string runLogPath = GetRunLogPath();
        if (!File.Exists(runLogPath))
        {
            return [];
        }

        return ReadJsonArray(runLogPath)
            .Select(node => ToRunEntry(node, runLogPath))
            .OrderByDescending(entry => entry.TimestampLocal)
            .Take(Math.Clamp(maxEntries, 1, 1000))
            .ToArray();
    }

    public MonitorRunDetail GetMonitorRun(string runId)
    {
        string runLogPath = GetRunLogPath();
        MonitorRunEntry[] entries = File.Exists(runLogPath)
            ? ReadJsonArray(runLogPath)
                .Select(node => ToRunEntry(node, runLogPath))
                .Where(entry => string.Equals(entry.RunId, runId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(entry => entry.Sequence)
                .ToArray()
            : [];

        return new MonitorRunDetail(runId, runLogPath, entries);
    }

    public IReadOnlyList<MonitorLedgerInfo> ListLedgers(int maxEntries = 100)
    {
        string ledgerRoot = GetLedgerRoot();
        if (!Directory.Exists(ledgerRoot))
        {
            return [];
        }

        return Directory.EnumerateFiles(ledgerRoot, "*.md", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(Math.Clamp(maxEntries, 1, 1000))
            .Select(path => new MonitorLedgerInfo(
                path,
                Path.GetRelativePath(ledgerRoot, path),
                new FileInfo(path).Length,
                File.GetLastWriteTimeUtc(path)))
            .ToArray();
    }

    public MonitorLedgerReadResult GetLedger(string? sourceFilePath = null, string? ledgerPath = null)
    {
        string resolvedPath;
        if (!string.IsNullOrWhiteSpace(ledgerPath))
        {
            resolvedPath = Path.GetFullPath(ledgerPath);
            string ledgerRoot = TrimDirectorySeparator(Path.GetFullPath(GetLedgerRoot()));
            if (!IsSameOrChildPath(resolvedPath, ledgerRoot))
            {
                throw new InvalidOperationException($"Ledger path is outside monitor ledger root: {resolvedPath}");
            }
        }
        else if (!string.IsNullOrWhiteSpace(sourceFilePath))
        {
            MonitorFileContext context = ResolveFileContext(sourceFilePath);
            resolvedPath = GetFileLedgerPath(context);
        }
        else
        {
            throw new ArgumentException("Provide sourceFilePath or ledgerPath.");
        }

        if (!File.Exists(resolvedPath))
        {
            return new MonitorLedgerReadResult(resolvedPath, false, string.Empty, 0);
        }

        string text = File.ReadAllText(resolvedPath);
        return new MonitorLedgerReadResult(resolvedPath, true, text, text.Length);
    }

    public MonitorPruneResult PruneMonitorHistory(int retentionDays = DefaultHistoryRetentionDays)
    {
        string historyRoot = GetHistoryRoot();
        string archiveRoot = Path.Combine(historyRoot, "Archive");
        int archivedSnapshots = 0;
        int prunedLedgers = 0;

        string watchedProjectFolder = Path.GetDirectoryName(settings.WatchedSolutionPath) ?? string.Empty;
        if (Directory.Exists(watchedProjectFolder))
        {
            foreach (string sourcePath in Directory.EnumerateFiles(watchedProjectFolder, "*.*", SearchOption.AllDirectories)
                .Where(path => !IsInIgnoredDirectory(path)))
            {
                string extension = Path.GetExtension(sourcePath);
                if (!extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
                    && !extension.Equals(".razor", StringComparison.OrdinalIgnoreCase)
                    && !extension.Equals(".sql", StringComparison.OrdinalIgnoreCase)
                    && !extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
                    && !extension.Equals(".md", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                MonitorFileContext context = ResolveFileContext(sourcePath);
                archivedSnapshots += PruneHistoryForSource(context, archiveRoot, retentionDays);
                prunedLedgers += PruneFileLedger(context, retentionDays);
            }
        }

        return new MonitorPruneResult(historyRoot, archiveRoot, retentionDays, archivedSnapshots, prunedLedgers);
    }

    private string GetHistoryRoot()
    {
        return Path.Combine(settings.UiRoot, "Working", "History");
    }

    private string GetRunLogPath()
    {
        return Path.Combine(GetHistoryRoot(), "_runs.json");
    }

    private string GetLedgerRoot()
    {
        return Path.Combine(GetHistoryRoot(), "Ledgers");
    }

    private string GetFileLedgerPath(MonitorFileContext context)
    {
        string workingRelativePath = Path.Combine(context.ObservedRootKey, context.RelativeSourcePath);
        string safeName = SanitizeForFileName(workingRelativePath.Replace(Path.DirectorySeparatorChar, '_'));
        return Path.Combine(GetLedgerRoot(), $"{safeName}.md");
    }

    private void AppendFileLedgerEntry(MonitorFileContext context, string proposedFilePath, string? summary)
    {
        string ledgerPath = GetFileLedgerPath(context);
        Directory.CreateDirectory(Path.GetDirectoryName(ledgerPath)!);

        string workingRelativePath = Path.Combine(context.ObservedRootKey, context.RelativeSourcePath);
        List<string> entry =
        [
            $"## {DateTime.Now:yyyy-MM-dd HH:mm}",
            string.Empty,
            $"File: `{context.RelativeSourcePath}`",
            $"Original: `{context.SourceFilePath}`",
            $"Working: `{context.WorkingFilePath}`",
            $"Snapshot: `{proposedFilePath}`",
            "ArchiveZip: `not archived`",
            "ArchiveEntry: `not archived`",
            string.Empty,
            string.IsNullOrWhiteSpace(summary)
                ? "Compare snapshot created. Add a concise summary with `--ledger-summary` when Codex has useful context."
                : summary.Trim(),
            string.Empty
        ];

        _ = workingRelativePath;
        File.AppendAllText(ledgerPath, string.Join(Environment.NewLine, entry));
    }

    private int PruneHistoryForSource(MonitorFileContext context, string archiveRoot, int retentionDays, string? keepHistoryFilePath = null)
    {
        string workingRelativePath = Path.Combine(context.ObservedRootKey, context.RelativeSourcePath);
        string sourceDirRelative = Path.GetDirectoryName(workingRelativePath) ?? string.Empty;
        string baseName = Path.GetFileNameWithoutExtension(context.SourceFilePath);
        string extension = Path.GetExtension(context.SourceFilePath);
        string historySourceDir = Path.Combine(GetHistoryRoot(), sourceDirRelative);
        if (!Directory.Exists(historySourceDir))
        {
            return 0;
        }

        DateTime cutoffUtc = DateTime.UtcNow - TimeSpan.FromDays(Math.Clamp(retentionDays, 1, 3650));
        string searchPattern = $"{baseName}_*{extension}";
        List<string> files = Directory.EnumerateFiles(historySourceDir, searchPattern, SearchOption.TopDirectoryOnly)
            .Where(path => File.GetLastWriteTimeUtc(path) < cutoffUtc)
            .Where(path => keepHistoryFilePath is null || !string.Equals(path, keepHistoryFilePath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (files.Count == 0)
        {
            return 0;
        }

        Directory.CreateDirectory(archiveRoot);
        int archived = 0;
        foreach (string file in files)
        {
            string archivePath = GetSnapshotArchivePath(archiveRoot, File.GetLastWriteTime(file));
            string entryRelative = Path.Combine(sourceDirRelative, Path.GetFileName(file))
                .Replace(Path.DirectorySeparatorChar, '/');
            string archiveEntry = AddFileToArchive(archivePath, file, entryRelative);
            UpdateLedgerForArchivedSnapshot(context, file, archivePath, archiveEntry);
            File.Delete(file);
            archived++;
        }

        return archived;
    }

    private int PruneFileLedger(MonitorFileContext context, int retentionDays)
    {
        string ledgerPath = GetFileLedgerPath(context);
        if (!File.Exists(ledgerPath))
        {
            return 0;
        }

        List<List<string>> entries = SplitLedgerEntries(File.ReadAllLines(ledgerPath).ToList());
        DateTime cutoff = DateTime.Now - TimeSpan.FromDays(Math.Clamp(retentionDays, 1, 3650));
        List<List<string>> keptEntries = [];
        int pruned = 0;

        foreach (List<string> entry in entries)
        {
            if (!TryParseLedgerEntryDate(entry, out DateTime entryDate) || entryDate >= cutoff)
            {
                keptEntries.Add(entry);
                continue;
            }

            pruned++;
        }

        if (pruned == 0)
        {
            return 0;
        }

        if (keptEntries.Count == 0)
        {
            File.Delete(ledgerPath);
            return pruned;
        }

        File.WriteAllLines(ledgerPath, keptEntries.SelectMany(entry => entry));
        return pruned;
    }

    private void UpdateLedgerForArchivedSnapshot(MonitorFileContext context, string snapshotPath, string archivePath, string archiveEntry)
    {
        string ledgerPath = GetFileLedgerPath(context);
        if (!File.Exists(ledgerPath))
        {
            return;
        }

        List<string> lines = File.ReadAllLines(ledgerPath).ToList();
        bool changed = false;
        for (int i = 0; i < lines.Count; i++)
        {
            if (!string.Equals(lines[i], $"Snapshot: `{snapshotPath}`", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            EnsureArchiveLine(lines, i + 1, "ArchiveZip", archivePath);
            EnsureArchiveLine(lines, i + 2, "ArchiveEntry", archiveEntry);
            changed = true;
        }

        if (changed)
        {
            File.WriteAllLines(ledgerPath, lines);
        }
    }

    private static void EnsureArchiveLine(List<string> lines, int index, string label, string value)
    {
        string line = $"{label}: `{value}`";
        if (index < lines.Count && lines[index].StartsWith($"{label}: ", StringComparison.Ordinal))
        {
            lines[index] = line;
            return;
        }

        lines.Insert(Math.Min(index, lines.Count), line);
    }

    private static List<List<string>> SplitLedgerEntries(List<string> lines)
    {
        List<List<string>> entries = [];
        List<string>? current = null;

        foreach (string line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (current is { Count: > 0 })
                {
                    entries.Add(current);
                }

                current = [line];
                continue;
            }

            current ??= [];
            current.Add(line);
        }

        if (current is { Count: > 0 })
        {
            entries.Add(current);
        }

        return entries;
    }

    private static bool TryParseLedgerEntryDate(List<string> entry, out DateTime date)
    {
        date = default;
        if (entry.Count == 0 || !entry[0].StartsWith("## ", StringComparison.Ordinal))
        {
            return false;
        }

        return DateTime.TryParse(entry[0]["## ".Length..], out date);
    }

    private static IReadOnlyList<JsonNode?> ReadJsonArray(string path)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(path))?.AsArray().ToArray() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static MonitorRunEntry ToRunEntry(JsonNode? node, string logPath)
    {
        JsonObject? obj = node?.AsObject();
        return new MonitorRunEntry(
            obj?["run_id"]?.GetValue<string>() ?? string.Empty,
            obj?["entry_type"]?.GetValue<string>() ?? string.Empty,
            obj?["stage"]?.GetValue<string>(),
            obj?["operation"]?.GetValue<string>(),
            obj?["source_file"]?.GetValue<string>(),
            obj?["working_file"]?.GetValue<string>(),
            obj?["proposed_file"]?.GetValue<string>(),
            obj?["timestamp_local"]?.GetValue<string>() ?? string.Empty,
            obj?["seq"]?.GetValue<int>() ?? 0,
            logPath,
            obj?.ToJsonString(JsonOptions) ?? "{}");
    }

    private static string GetSnapshotArchivePath(string archiveRoot, DateTime snapshotTime)
    {
        string archiveName = $"history_{snapshotTime:yyyyMMdd}.zip";
        return Path.Combine(archiveRoot, archiveName);
    }

    private static string AddFileToArchive(ZipArchive archive, string sourcePath, string entryRelativePath)
    {
        string entryName = EnsureUniqueArchiveEntryName(archive, entryRelativePath);
        archive.CreateEntryFromFile(sourcePath, entryName, CompressionLevel.Optimal);
        return entryName;
    }

    private static string AddFileToArchive(string archivePath, string sourcePath, string entryRelativePath)
    {
        using ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Update);
        return AddFileToArchive(archive, sourcePath, entryRelativePath);
    }

    private static string EnsureUniqueArchiveEntryName(ZipArchive archive, string entryName)
    {
        if (archive.GetEntry(entryName) is null)
        {
            return entryName;
        }

        string dir = Path.GetDirectoryName(entryName)?.Replace(Path.DirectorySeparatorChar, '/') ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(entryName);
        string ext = Path.GetExtension(entryName);
        string suffix = DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff");
        string uniqueName = $"{name}_archived_{suffix}{ext}";
        return string.IsNullOrWhiteSpace(dir) ? uniqueName : $"{dir}/{uniqueName}";
    }
}

public sealed record MonitorRunEntry(
    string RunId,
    string EntryType,
    string? Stage,
    string? Operation,
    string? SourceFile,
    string? WorkingFile,
    string? ProposedFile,
    string TimestampLocal,
    int Sequence,
    string LogPath,
    string RawJson);

public sealed record MonitorRunDetail(
    string RunId,
    string LogPath,
    IReadOnlyList<MonitorRunEntry> Entries);

public sealed record MonitorLedgerInfo(
    string LedgerPath,
    string RelativePath,
    long Length,
    DateTime LastWriteUtc);

public sealed record MonitorLedgerReadResult(
    string LedgerPath,
    bool Exists,
    string Text,
    int TextLength);

public sealed record MonitorPruneResult(
    string HistoryRoot,
    string ArchiveRoot,
    int RetentionDays,
    int ArchivedSnapshots,
    int PrunedLedgers);
