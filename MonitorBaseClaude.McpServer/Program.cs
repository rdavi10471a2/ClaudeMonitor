using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace MonitorBaseClaude.McpServer;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(MonitorServerSettings.Load());
        builder.Services.AddSingleton<MonitorWorkflowService>();
        builder.Services.AddSingleton<MonitorSessionService>();
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<MonitorTools>();

        await builder.Build().RunAsync();
    }
}

[McpServerToolType]
public sealed class MonitorTools
{
    private readonly MonitorServerSettings settings;
    private readonly MonitorWorkflowService workflowService;
    private readonly MonitorSessionService sessionService;

    public MonitorTools(MonitorServerSettings settings, MonitorWorkflowService workflowService, MonitorSessionService sessionService)
    {
        this.settings = settings;
        this.workflowService = workflowService;
        this.sessionService = sessionService;
    }

    [McpServerTool]
    [Description("Return paths and high-level status for the new Monitor MCP server.")]
    public MonitorStatus GetMonitorStatus()
    {
        return new MonitorStatus(
            settings.UiRoot,
            settings.McpServerRoot,
            settings.LegacyMonitorRoot,
            settings.WatchedSolutionPath,
            Path.GetDirectoryName(settings.WatchedSolutionPath) ?? string.Empty,
            Directory.Exists(settings.McpServerRoot),
            Directory.Exists(settings.LegacyMonitorRoot),
            File.Exists(settings.WatchedSolutionPath));
    }

    [McpServerTool]
    [Description("Return the monitor workflow status, including watched solution, Working folder, and WinMerge resolution.")]
    public MonitorWorkflowStatus GetWorkflowStatus()
    {
        return workflowService.GetWorkflowStatus();
    }

    [McpServerTool]
    [Description("Return a self-check snapshot for configured roots, working folders, diff tool availability, and safety guardrails.")]
    public MonitorSelfCheckResult GetSelfCheck()
    {
        return workflowService.GetSelfCheck();
    }

    [McpServerTool]
    [Description("Create a durable monitor session handle. Handles are explicit state references that clients should pass through later calls.")]
    public MonitorSessionState StartMonitorSession(
        [Description("Short purpose for this monitor session, such as 'local Ollama tool exploration' or 'Claude feature edit'.")] string purpose = "monitor workflow")
    {
        return sessionService.StartSession(purpose);
    }

    [McpServerTool]
    [Description("List durable monitor session handles known to this MCP server.")]
    public IReadOnlyList<MonitorSessionSummary> ListMonitorSessions()
    {
        return sessionService.ListSessions();
    }

    [McpServerTool]
    [Description("Return a durable monitor session by explicit sessionId handle.")]
    public MonitorSessionState GetMonitorSession(
        [Description("Session handle returned by start_monitor_session.")] string sessionId)
    {
        return sessionService.GetSession(sessionId);
    }

    [McpServerTool]
    [Description("Append an event to a durable monitor session. Use this to record tool decisions, tool results, and final answers.")]
    public MonitorSessionState RecordMonitorSessionEvent(
        [Description("Session handle returned by start_monitor_session.")] string sessionId,
        [Description("Short event type, such as user-message, tool-call, tool-result, final-answer, or error.")] string eventType,
        [Description("Human-readable event summary.")] string summary,
        [Description("Optional JSON payload for the event.")] string? payloadJson = null)
    {
        return sessionService.RecordEvent(sessionId, eventType, summary, payloadJson);
    }

    [McpServerTool]
    [Description("Refresh a watched source file into the monitor-owned Working folder. The path may be absolute or relative to the watched solution folder.")]
    public MonitorFileRefreshResult RefreshFile(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string sourceFilePath)
    {
        return workflowService.RefreshFile(sourceFilePath);
    }

    [McpServerTool]
    [Description("Read a watched source file through the Monitor MCP server. The path may be absolute or relative to the watched solution folder.")]
    public MonitorFileReadResult GetFile(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string sourceFilePath,
        [Description("Optional session handle. When supplied, the server records the file hash as fetched in that durable session.")] string? sessionId = null)
    {
        MonitorFileReadResult result = workflowService.GetFile(sourceFilePath);
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            sessionService.RecordFileFetch(sessionId, workflowService.GetFileHashInfo(sourceFilePath), "get_file");
        }

        return result;
    }

    [McpServerTool]
    [Description("Check whether a watched source file has changed since it was last fetched in a durable monitor session. Returns hashes and metadata, not file contents.")]
    public MonitorSessionFileCheckResult CheckFileHash(
        [Description("Session handle returned by start_monitor_session.")] string sessionId,
        [Description("Source file path, absolute or relative to the watched solution folder.")] string sourceFilePath)
    {
        return sessionService.CheckFileHash(sessionId, workflowService.GetFileHashInfo(sourceFilePath));
    }

    [McpServerTool]
    [Description("Find files under the watched project folder by filename or wildcard pattern.")]
    public IReadOnlyList<MonitorFileMatch> FindFile(
        [Description("Filename or wildcard pattern, such as Program.cs or *.razor.")] string fileNameOrPattern,
        [Description("Maximum number of matches to return.")] int maxResults = 25)
    {
        return workflowService.FindFile(fileNameOrPattern, maxResults);
    }

    [McpServerTool]
    [Description("Return a C# file outline with symbol names, signatures, and line spans without method bodies.")]
    public MonitorFileOutlineResult GetFileOutline(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path)
    {
        return workflowService.GetFileOutline(path);
    }

    [McpServerTool]
    [Description("Return one C# symbol body from a watched source file.")]
    public MonitorSymbolReadResult GetSymbol(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path,
        [Description("Class, method, property, field, event, delegate, or constructor name.")] string symbolName)
    {
        return workflowService.GetSymbol(path, symbolName);
    }

    [McpServerTool]
    [Description("Stage a full-file replacement under monitor-owned Working\\Staged and optionally launch WinMerge. Does not overwrite the watched source file.")]
    public MonitorFileSubmitResult SubmitFile(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path,
        [Description("Complete replacement file content.")] string content,
        [Description("Optional durable session handle to link this staged edit to a monitor workflow session.")] string? sessionId = null,
        [Description("Optional JSON manifest expressing Model intent. The Tool Server records it but verifies using Roslyn-derived metadata.")] string? manifestJson = null,
        [Description("Launch WinMerge against the staged file and watched source file.")] bool launchDiff = false)
    {
        return workflowService.SubmitFile(path, content, sessionId, manifestJson, launchDiff);
    }

    [McpServerTool]
    [Description("Compare a monitor Working file against the watched source file using WinMerge. The path may be absolute or relative to the watched solution folder.")]
    public MonitorFileCompareResult CompareFile(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string sourceFilePath,
        [Description("Optional compact ledger summary to append to the monitor-owned ledger.")] string? ledgerSummary = null,
        [Description("Refresh from source first if the Working copy is missing.")] bool refreshIfMissing = true)
    {
        return workflowService.CompareFile(sourceFilePath, ledgerSummary, refreshIfMissing);
    }

    [McpServerTool]
    [Description("List monitor run/history entries recorded under monitor-owned Working\\History.")]
    public IReadOnlyList<MonitorRunEntry> ListMonitorRuns(
        [Description("Maximum entries to return.")] int maxEntries = 100)
    {
        return workflowService.ListMonitorRuns(maxEntries);
    }

    [McpServerTool]
    [Description("Return all recorded entries for one monitor run id.")]
    public MonitorRunDetail GetMonitorRun(
        [Description("Run id from list_monitor_runs.")] string runId)
    {
        return workflowService.GetMonitorRun(runId);
    }

    [McpServerTool]
    [Description("List monitor-owned per-file ledgers.")]
    public IReadOnlyList<MonitorLedgerInfo> ListLedgers(
        [Description("Maximum ledgers to return.")] int maxEntries = 100)
    {
        return workflowService.ListLedgers(maxEntries);
    }

    [McpServerTool]
    [Description("Read one monitor-owned per-file ledger by source file or ledger path.")]
    public MonitorLedgerReadResult GetLedger(
        [Description("Optional source file path, absolute or relative to the watched solution folder.")] string? sourceFilePath = null,
        [Description("Optional absolute ledger path under monitor Working\\History\\Ledgers.")] string? ledgerPath = null)
    {
        return workflowService.GetLedger(sourceFilePath, ledgerPath);
    }

    [McpServerTool]
    [Description("Archive old monitor-owned history snapshots and prune old ledgers.")]
    public MonitorPruneResult PruneMonitorHistory(
        [Description("Retention window in days.")] int retentionDays = 7)
    {
        return workflowService.PruneMonitorHistory(retentionDays);
    }

    [McpServerTool]
    [Description("Return the Markdown tool manifest for the Monitor MCP Server tool surface.")]
    public string GetToolManifest()
    {
        string path = Path.Combine(settings.McpServerRoot, "MONITOR_MCP_TOOL_MANIFEST.md");
        return File.Exists(path)
            ? File.ReadAllText(path)
            : "Monitor MCP tool manifest is missing.";
    }

    [McpServerTool]
    [Description("List watched project folders under the configured watched-projects root.")]
    public IReadOnlyList<WatchedProjectInfo> ListWatchedProjects()
    {
        if (!Directory.Exists(settings.WatchedProjectsRoot))
        {
            return [];
        }

        return Directory
            .GetDirectories(settings.WatchedProjectsRoot)
            .Where(path => !IsHiddenOrBuildFolder(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new WatchedProjectInfo(
                Path.GetFileName(path),
                path,
                Directory.GetFiles(path, "*.sln", SearchOption.TopDirectoryOnly)))
            .ToArray();
    }

    private static bool IsHiddenOrBuildFolder(string path)
    {
        string name = Path.GetFileName(path);
        return name.StartsWith(".", StringComparison.Ordinal)
            || name.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || name.Equals("obj", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record MonitorStatus(
    string UiRoot,
    string McpServerRoot,
    string LegacyMonitorRoot,
    string WatchedSolutionPath,
    string WatchedProjectFolder,
    bool McpServerRootExists,
    bool LegacyMonitorRootExists,
    bool WatchedSolutionExists);

public sealed record WatchedProjectInfo(
    string Name,
    string Path,
    IReadOnlyList<string> SolutionFiles);

public sealed record MonitorServerSettings(
    string UiRoot,
    string McpServerRoot,
    string LegacyMonitorRoot,
    string WatchedProjectsRoot,
    string WatchedSolutionPath)
{
    public static MonitorServerSettings Load()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string uiRoot = FindAncestorWithFile(baseDirectory, "MonitorBaseClaude.csproj")
            ?? @"C:\VSCodeProjects\MonitorBaseClaude";
        string mcpRoot = Path.Combine(uiRoot, "MonitorBaseClaude.McpServer");
        string legacyRoot = @"C:\VSCodeProjects\ClaudeMonitor\Monitor";
        string watchedRoot = @"C:\VSCodeProjects";
        string watchedSolutionPath = @"C:\Schema Studio - DBV2\Schema Studio.sln";

        string settingsPath = Path.Combine(uiRoot, "appsettings.json");
        if (File.Exists(settingsPath))
        {
            using FileStream stream = File.OpenRead(settingsPath);
            JsonDocument document = JsonDocument.Parse(stream);
            if (document.RootElement.TryGetProperty("MonitorClient", out JsonElement client))
            {
                mcpRoot = GetString(client, "MonitorMcpServerRoot") ?? mcpRoot;
                legacyRoot = GetString(client, "LegacyMonitorRoot") ?? legacyRoot;
                watchedRoot = GetString(client, "WatchedProjectsRoot") ?? watchedRoot;
                watchedSolutionPath = GetString(client, "WatchedSolutionPath") ?? watchedSolutionPath;
            }
        }

        return new MonitorServerSettings(uiRoot, mcpRoot, legacyRoot, watchedRoot, watchedSolutionPath);
    }

    private static string? FindAncestorWithFile(string startPath, string fileName)
    {
        DirectoryInfo? current = new(startPath);
        for (int i = 0; i < 8 && current is not null; i++)
        {
            if (File.Exists(Path.Combine(current.FullName, fileName)))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
