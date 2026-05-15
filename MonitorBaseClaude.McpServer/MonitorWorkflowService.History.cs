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

        if (Directory.Exists(historyRoot))
        {
            DateTime cutoffUtc = DateTime.UtcNow - TimeSpan.FromDays(Math.Clamp(retentionDays, 1, 3650));
            foreach (string file in Directory.EnumerateFiles(historyRoot, "*.*", SearchOption.AllDirectories))
            {
                if (IsInIgnoredHistoryPruneDirectory(file)
                    || file.EndsWith("_runs.json", StringComparison.OrdinalIgnoreCase)
                    || file.EndsWith("_telemetry.json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (File.GetLastWriteTimeUtc(file) >= cutoffUtc)
                {
                    continue;
                }

                string relative = Path.GetRelativePath(historyRoot, file);
                string archivePath = Path.Combine(archiveRoot, $"{DateTime.UtcNow:yyyyMMdd}_history.zip");
                Directory.CreateDirectory(archiveRoot);
                using System.IO.Compression.ZipArchive archive = System.IO.Compression.ZipFile.Open(archivePath, System.IO.Compression.ZipArchiveMode.Update);
                string entryName = EnsureUniqueArchiveEntryName(archive, relative.Replace(Path.DirectorySeparatorChar, '/'));
                archive.CreateEntryFromFile(file, entryName, System.IO.Compression.CompressionLevel.Optimal);
                File.Delete(file);
                archivedSnapshots++;
            }
        }

        string ledgerRoot = GetLedgerRoot();
        if (Directory.Exists(ledgerRoot))
        {
            DateTime cutoff = DateTime.Now - TimeSpan.FromDays(Math.Clamp(retentionDays, 1, 3650));
            foreach (string ledger in Directory.EnumerateFiles(ledgerRoot, "*.md", SearchOption.AllDirectories))
            {
                if (File.GetLastWriteTime(ledger) >= cutoff)
                {
                    continue;
                }

                File.Delete(ledger);
                prunedLedgers++;
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
        string safeName = SanitizeForFileName(Path.Combine(context.ObservedRootKey, context.RelativeSourcePath)
            .Replace(Path.DirectorySeparatorChar, '_')
            .Replace(Path.AltDirectorySeparatorChar, '_'));
        return Path.Combine(GetLedgerRoot(), $"{safeName}.md");
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

    private static bool IsInIgnoredHistoryPruneDirectory(string path)
    {
        string normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        string separator = Path.DirectorySeparatorChar.ToString();
        return normalized.Contains($"{separator}Archive{separator}", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains($"{separator}Ledgers{separator}", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains($"{separator}ToolSmokeTests{separator}", StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureUniqueArchiveEntryName(System.IO.Compression.ZipArchive archive, string entryName)
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
