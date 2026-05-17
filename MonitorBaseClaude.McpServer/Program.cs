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
        builder.Services.AddSingleton(MonitorServerSettings.Load(args));
        builder.Services.AddSingleton<MonitorWorkflowService>();
        builder.Services.AddSingleton<MonitorSessionService>();
        builder.Services.AddSingleton<MonitorMcpTelemetryService>();
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
    private readonly MonitorMcpTelemetryService telemetryService;

    public MonitorTools(
        MonitorServerSettings settings,
        MonitorWorkflowService workflowService,
        MonitorSessionService sessionService,
        MonitorMcpTelemetryService telemetryService)
    {
        this.settings = settings;
        this.workflowService = workflowService;
        this.sessionService = sessionService;
        this.telemetryService = telemetryService;
    }

    [McpServerTool]
    [Description("Return paths and high-level status for the new Monitor MCP server.")]
    public MonitorStatus GetMonitorStatus()
    {
        return Track(nameof(GetMonitorStatus), null, () => new MonitorStatus(
            settings.UiRoot,
            settings.McpServerRoot,
            settings.LegacyMonitorRoot,
            settings.WatchedSolutionPath,
            Path.GetDirectoryName(settings.WatchedSolutionPath) ?? string.Empty,
            Directory.Exists(settings.McpServerRoot),
            Directory.Exists(settings.LegacyMonitorRoot),
            File.Exists(settings.WatchedSolutionPath)));
    }

    [McpServerTool]
    [Description("Return the monitor workflow status, including watched solution, Working folder, and WinMerge resolution.")]
    public MonitorWorkflowStatus GetWorkflowStatus()
    {
        return Track(nameof(GetWorkflowStatus), null, workflowService.GetWorkflowStatus);
    }

    [McpServerTool]
    [Description("Return a self-check snapshot for configured roots, working folders, diff tool availability, and safety guardrails.")]
    public MonitorSelfCheckResult GetSelfCheck()
    {
        return Track(nameof(GetSelfCheck), null, workflowService.GetSelfCheck);
    }

    [McpServerTool]
    [Description("Create a durable monitor session handle. Handles are explicit state references that clients should pass through later calls.")]
    public MonitorSessionState StartMonitorSession(
        [Description("Short purpose for this monitor session, such as 'local Ollama tool exploration' or 'Claude feature edit'.")] string purpose = "monitor workflow")
    {
        return Track(nameof(StartMonitorSession), new { purpose }, () => sessionService.StartSession(purpose));
    }

    [McpServerTool]
    [Description("List durable monitor session handles known to this MCP server.")]
    public IReadOnlyList<MonitorSessionSummary> ListMonitorSessions()
    {
        return Track(nameof(ListMonitorSessions), null, sessionService.ListSessions);
    }

    [McpServerTool]
    [Description("Return a durable monitor session by explicit sessionId handle.")]
    public MonitorSessionState GetMonitorSession(
        [Description("Session handle returned by start_monitor_session.")] string sessionId)
    {
        return Track(nameof(GetMonitorSession), new { sessionId }, () => sessionService.GetSession(sessionId));
    }

    [McpServerTool]
    [Description("Append an event to a durable monitor session. Use this to record tool decisions, tool results, and final answers.")]
    public MonitorSessionState RecordMonitorSessionEvent(
        [Description("Session handle returned by start_monitor_session.")] string sessionId,
        [Description("Short event type, such as user-message, tool-call, tool-result, final-answer, or error.")] string eventType,
        [Description("Human-readable event summary.")] string summary,
        [Description("Optional JSON payload for the event.")] string? payloadJson = null)
    {
        return Track(nameof(RecordMonitorSessionEvent), new { sessionId, eventType, summary, payloadLength = payloadJson?.Length ?? 0 }, () => sessionService.RecordEvent(sessionId, eventType, summary, payloadJson));
    }

    [McpServerTool]
    [Description("Refresh a watched source file into the monitor-owned Working folder. The path may be absolute or relative to the watched solution folder.")]
    public MonitorFileRefreshResult RefreshFile(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string sourceFilePath)
    {
        return Track(nameof(RefreshFile), new { sourceFilePath }, () => workflowService.RefreshFile(sourceFilePath));
    }

    [McpServerTool]
    [Description("Read a watched source file through the Monitor MCP server. The path may be absolute or relative to the watched solution folder.")]
    public MonitorFileReadResult GetFile(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string sourceFilePath,
        [Description("Optional session handle. When supplied, the server records the file hash as fetched in that durable session.")] string? sessionId = null)
    {
        return Track(nameof(GetFile), new { sourceFilePath, sessionId }, () =>
        {
            MonitorFileReadResult result = workflowService.GetFile(sourceFilePath);
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                sessionService.RecordFileFetch(sessionId, workflowService.GetFileHashInfo(sourceFilePath), "get_file");
            }

            return result;
        });
    }

    [McpServerTool]
    [Description("Check whether a watched source file has changed since it was last fetched in a durable monitor session. Returns hashes and metadata, not file contents.")]
    public MonitorSessionFileCheckResult CheckFileHash(
        [Description("Session handle returned by start_monitor_session.")] string sessionId,
        [Description("Source file path, absolute or relative to the watched solution folder.")] string sourceFilePath)
    {
        return Track(nameof(CheckFileHash), new { sessionId, sourceFilePath }, () => sessionService.CheckFileHash(sessionId, workflowService.GetFileHashInfo(sourceFilePath)));
    }

    [McpServerTool]
    [Description("Find source or related files under the watched project folder by filename or wildcard pattern. Use this before edits to locate neighboring context.")]
    public IReadOnlyList<MonitorFileMatch> FindFile(
        [Description("Filename or wildcard pattern, such as Program.cs or *.razor.")] string fileNameOrPattern,
        [Description("Maximum number of matches to return.")] int maxResults = 25)
    {
        return Track(nameof(FindFile), new { fileNameOrPattern, maxResults }, () => workflowService.FindFile(fileNameOrPattern, maxResults));
    }

    [McpServerTool]
    [Description("Return a C# file outline with symbol names, signatures, and line spans without method bodies.")]
    public MonitorFileOutlineResult GetFileOutline(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path)
    {
        return Track(nameof(GetFileOutline), new { path }, () => workflowService.GetFileOutline(path));
    }

    [McpServerTool]
    [Description("Return a Roslyn-derived source map for a C# file, folder, or watched project. Use before C# edits. Modes: navigation for broad orientation, selector for stable get_symbol/submit_symbol selectors, detail for contract detail, full for audit/debug.")]
    public MonitorSourceMapResult GetSourceMap(
        [Description("Optional source file or folder path, absolute or relative to the watched solution folder. Omit for the watched project.")] string? path = null,
        [Description("Source map scope: auto, file, folder, or project.")] string scope = "auto",
        [Description("Source map density: auto, navigation, selector, detail, or full. auto means selector for file scope and navigation for folder/project scope.")] string mode = "auto")
    {
        return Track(nameof(GetSourceMap), new { path, scope, mode }, () => workflowService.GetSourceMap(path, scope, mode));
    }

    [McpServerTool]
    [Description("Return one C# symbol body from a watched source file. Prefer symbolSelectorJson from get_source_map stableSymbolKey; symbolName is a compatibility shortcut for unambiguous files.")]
    public MonitorSymbolReadResult GetSymbol(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path,
        [Description("Compatibility shortcut: class, method, property, field, event, delegate, or constructor name. Must be unambiguous.")] string? symbolName = null,
        [Description("Structured selector JSON from get_source_map, including stableSymbolKey, memberKind, containingNamespace, containingType, parameterTypes, and arity.")] string? symbolSelectorJson = null)
    {
        return Track(nameof(GetSymbol), new { path, symbolName, hasSelector = !string.IsNullOrWhiteSpace(symbolSelectorJson) }, () => workflowService.GetSymbol(path, symbolName, symbolSelectorJson));
    }

    [McpServerTool]
    [Description("Stage a full-file replacement under monitor-owned Working\\Staged. Does not overwrite the watched source file. Host-like clients should launch GUI diff tools using the returned paths.")]
    public MonitorFileSubmitResult SubmitFile(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path,
        [Description("Complete replacement file content.")] string content,
        [Description("Optional durable session handle to link this staged edit to a monitor workflow session.")] string? sessionId = null,
        [Description("Optional JSON manifest expressing Model intent. The Tool Server records it but verifies using Roslyn-derived metadata.")] string? manifestJson = null,
        [Description("Deprecated compatibility flag. GUI diff launch from the stdio Tool Server is unreliable; prefer false and let the Host or sidecar launch WinMerge using returned paths.")] bool launchDiff = false)
    {
        return Track(nameof(SubmitFile), new { path, contentLength = content.Length, sessionId, manifestLength = manifestJson?.Length ?? 0, launchDiff }, () => workflowService.SubmitFile(path, content, sessionId, manifestJson, launchDiff));
    }

    [McpServerTool]
    [Description("Stage replacement of one C# symbol selected by structured JSON. Produces a full staged candidate; does not overwrite watched source.")]
    public MonitorFileSubmitResult SubmitSymbol(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path,
        [Description("Structured symbol selector JSON with name/memberKind/containingType/parameterTypes/stableSymbolKey.")] string symbolSelectorJson,
        [Description("Complete replacement C# member declaration.")] string code,
        [Description("Optional durable session handle to link this staged edit to a monitor workflow session.")] string? sessionId = null,
        [Description("Optional JSON manifest expressing Model intent.")] string? manifestJson = null)
    {
        return Track(nameof(SubmitSymbol), new { path, selectorLength = symbolSelectorJson.Length, codeLength = code.Length, sessionId, manifestLength = manifestJson?.Length ?? 0 }, () => workflowService.SubmitSymbol(path, symbolSelectorJson, code, sessionId, manifestJson));
    }

    [McpServerTool]
    [Description("Stage adding a using directive to a C# source file. Produces a full staged candidate; does not overwrite watched source.")]
    public MonitorFileSubmitResult AddUsing(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path,
        [Description("Namespace to add as a using directive.")] string @namespace,
        [Description("Optional durable session handle to link this staged edit to a monitor workflow session.")] string? sessionId = null,
        [Description("Optional JSON manifest expressing Model intent.")] string? manifestJson = null)
    {
        return Track(nameof(AddUsing), new { path, @namespace, sessionId, manifestLength = manifestJson?.Length ?? 0 }, () => workflowService.AddUsing(path, @namespace, sessionId, manifestJson));
    }

    [McpServerTool]
    [Description("Stage removing a using directive from a C# source file. Produces a full staged candidate; does not overwrite watched source.")]
    public MonitorFileSubmitResult RemoveUsing(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path,
        [Description("Namespace to remove from using directives.")] string @namespace,
        [Description("Optional durable session handle to link this staged edit to a monitor workflow session.")] string? sessionId = null,
        [Description("Optional JSON manifest expressing Model intent.")] string? manifestJson = null)
    {
        return Track(nameof(RemoveUsing), new { path, @namespace, sessionId, manifestLength = manifestJson?.Length ?? 0 }, () => workflowService.RemoveUsing(path, @namespace, sessionId, manifestJson));
    }

    [McpServerTool]
    [Description("Stage adding or removing the partial modifier on one C# type declaration. Use when a refactor needs a partial companion file. Produces a full staged candidate; does not overwrite watched source.")]
    public MonitorFileSubmitResult SetTypePartial(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path,
        [Description("Containing type name.")] string containingType,
        [Description("True to require partial; false to remove partial.")] bool isPartial = true,
        [Description("Optional durable session handle to link this staged edit to a monitor workflow session.")] string? sessionId = null,
        [Description("Optional JSON manifest expressing Model intent.")] string? manifestJson = null)
    {
        return Track(nameof(SetTypePartial), new { path, containingType, isPartial, sessionId, manifestLength = manifestJson?.Length ?? 0 }, () => workflowService.SetTypePartial(path, containingType, isPartial, sessionId, manifestJson));
    }

    [McpServerTool]
    [Description("Stage adding one C# member to a containing type. Produces a full staged candidate; does not overwrite watched source.")]
    public MonitorFileSubmitResult AddSymbol(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path,
        [Description("Containing type name.")] string containingType,
        [Description("Expected symbol kind, such as method, property, field, event, constructor, or class.")] string symbolType,
        [Description("Complete C# member declaration to add.")] string code,
        [Description("Optional existing member name after which to insert the new member.")] string? afterSymbol = null,
        [Description("Optional durable session handle to link this staged edit to a monitor workflow session.")] string? sessionId = null,
        [Description("Optional JSON manifest expressing Model intent.")] string? manifestJson = null)
    {
        return Track(nameof(AddSymbol), new { path, containingType, symbolType, codeLength = code.Length, afterSymbol, sessionId, manifestLength = manifestJson?.Length ?? 0 }, () => workflowService.AddSymbol(path, containingType, symbolType, code, afterSymbol, sessionId, manifestJson));
    }

    [McpServerTool]
    [Description("Stage adding one C# field to a containing type. Prefer this over generic add_symbol for field insertion. Produces a full staged candidate; does not overwrite watched source.")]
    public MonitorFileSubmitResult AddField(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path,
        [Description("Containing type name.")] string containingType,
        [Description("Complete C# field declaration, such as private readonly IClock _clock;.")] string declaration,
        [Description("Optional existing member name after which to insert the field, such as _logger.")] string? afterSymbol = null,
        [Description("Optional durable session handle to link this staged edit to a monitor workflow session.")] string? sessionId = null,
        [Description("Optional JSON manifest expressing Model intent.")] string? manifestJson = null)
    {
        return Track(nameof(AddField), new { path, containingType, declarationLength = declaration.Length, afterSymbol, sessionId, manifestLength = manifestJson?.Length ?? 0 }, () => workflowService.AddField(path, containingType, declaration, afterSymbol, sessionId, manifestJson));
    }

    [McpServerTool]
    [Description("Stage adding one C# property to a containing type. Prefer this over generic add_symbol for property insertion. Produces a full staged candidate; does not overwrite watched source.")]
    public MonitorFileSubmitResult AddProperty(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path,
        [Description("Containing type name.")] string containingType,
        [Description("Complete C# property declaration.")] string declaration,
        [Description("Optional existing member name after which to insert the property.")] string? afterSymbol = null,
        [Description("Optional durable session handle to link this staged edit to a monitor workflow session.")] string? sessionId = null,
        [Description("Optional JSON manifest expressing Model intent.")] string? manifestJson = null)
    {
        return Track(nameof(AddProperty), new { path, containingType, declarationLength = declaration.Length, afterSymbol, sessionId, manifestLength = manifestJson?.Length ?? 0 }, () => workflowService.AddProperty(path, containingType, declaration, afterSymbol, sessionId, manifestJson));
    }

    [McpServerTool]
    [Description("Stage adding one C# method to a containing type. Prefer this over generic add_symbol for method insertion. Produces a full staged candidate; does not overwrite watched source.")]
    public MonitorFileSubmitResult AddMethod(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path,
        [Description("Containing type name.")] string containingType,
        [Description("Complete C# method declaration.")] string declaration,
        [Description("Optional existing member name after which to insert the method.")] string? afterSymbol = null,
        [Description("Optional durable session handle to link this staged edit to a monitor workflow session.")] string? sessionId = null,
        [Description("Optional JSON manifest expressing Model intent.")] string? manifestJson = null)
    {
        return Track(nameof(AddMethod), new { path, containingType, declarationLength = declaration.Length, afterSymbol, sessionId, manifestLength = manifestJson?.Length ?? 0 }, () => workflowService.AddMethod(path, containingType, declaration, afterSymbol, sessionId, manifestJson));
    }

    [McpServerTool]
    [Description("Stage adding one C# constructor to a containing type. Prefer this over generic add_symbol for constructor overload insertion. Produces a full staged candidate; does not overwrite watched source.")]
    public MonitorFileSubmitResult AddConstructor(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path,
        [Description("Containing type name.")] string containingType,
        [Description("Complete C# constructor declaration.")] string declaration,
        [Description("Optional existing constructor/member name after which to insert the constructor.")] string? afterSymbol = null,
        [Description("Optional durable session handle to link this staged edit to a monitor workflow session.")] string? sessionId = null,
        [Description("Optional JSON manifest expressing Model intent.")] string? manifestJson = null)
    {
        return Track(nameof(AddConstructor), new { path, containingType, declarationLength = declaration.Length, afterSymbol, sessionId, manifestLength = manifestJson?.Length ?? 0 }, () => workflowService.AddConstructor(path, containingType, declaration, afterSymbol, sessionId, manifestJson));
    }

    [McpServerTool]
    [Description("Stage adding one nested C# type to a containing type. The declaration must be class, struct, interface, record, or enum. Produces a full staged candidate; does not overwrite watched source.")]
    public MonitorFileSubmitResult AddNestedType(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path,
        [Description("Containing type name.")] string containingType,
        [Description("Complete nested C# type declaration.")] string declaration,
        [Description("Optional existing member name after which to insert the nested type.")] string? afterSymbol = null,
        [Description("Optional durable session handle to link this staged edit to a monitor workflow session.")] string? sessionId = null,
        [Description("Optional JSON manifest expressing Model intent.")] string? manifestJson = null)
    {
        return Track(nameof(AddNestedType), new { path, containingType, declarationLength = declaration.Length, afterSymbol, sessionId, manifestLength = manifestJson?.Length ?? 0 }, () => workflowService.AddNestedType(path, containingType, declaration, afterSymbol, sessionId, manifestJson));
    }

    [McpServerTool]
    [Description("Stage removing one C# symbol selected by structured JSON. Produces a full staged candidate; does not overwrite watched source.")]
    public MonitorFileSubmitResult RemoveSymbol(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path,
        [Description("Structured symbol selector JSON with name/memberKind/containingType/parameterTypes/stableSymbolKey.")] string symbolSelectorJson,
        [Description("Optional durable session handle to link this staged edit to a monitor workflow session.")] string? sessionId = null,
        [Description("Optional JSON manifest expressing Model intent.")] string? manifestJson = null)
    {
        return Track(nameof(RemoveSymbol), new { path, selectorLength = symbolSelectorJson.Length, sessionId, manifestLength = manifestJson?.Length ?? 0 }, () => workflowService.RemoveSymbol(path, symbolSelectorJson, sessionId, manifestJson));
    }

    [McpServerTool]
    [Description("Classify a completed WinMerge review for a staged edit. The decision argument is the Operator-reported outcome; accepted requires reported accepted plus watched==staged, or accepted-normalized when only BOM/EOL shape differs; rejected requires reported rejected plus watched==original; other mismatches are dirty-unexpected.")]
    public MonitorDiffDecisionResult RecordDiffDecision(
        [Description("Staged edit record id returned by submit_file, submit_symbol, set_type_partial, add_symbol, remove_symbol, add_using, or remove_using.")] string stagedRecordId,
        [Description("Operator-reported outcome: accepted if WinMerge saved the full candidate, or rejected if it was not saved. Hash comparison is authoritative.")] string decision,
        [Description("Optional Operator note.")] string? note = null,
        [Description("Optional session handle. Defaults to the staged record session when present.")] string? sessionId = null)
    {
        return Track(nameof(RecordDiffDecision), new { stagedRecordId, decision, hasNote = !string.IsNullOrWhiteSpace(note), sessionId }, () =>
        {
            MonitorDiffDecisionResult result = workflowService.RecordDiffDecision(stagedRecordId, decision, note, sessionId);
            if (!string.IsNullOrWhiteSpace(result.SessionId))
            {
                sessionService.RecordEvent(
                    result.SessionId,
                    "diff-decision",
                    $"{result.RelativeSourcePath} classified as {result.Classification}.",
                    JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                sessionService.RecordFileFetch(
                    result.SessionId,
                    workflowService.GetFileHashInfo(result.SourceFilePath),
                    "record_diff_decision");
            }

            return result;
        });
    }

    [McpServerTool]
    [Description("Launch WinMerge for a staged edit record and return review paths. If overlay compile validation has errors, this asks the WinForms Host for an explicit force-review decision before launching. This does not classify or accept the edit; after review call record_diff_decision.")]
    public MonitorStagedDiffLaunchResult LaunchStagedDiff(
        [Description("Staged edit record id returned by submit_file, submit_symbol, add_symbol, remove_symbol, add_using, or remove_using.")] string stagedRecordId,
        [Description("Bypass the WinForms overlay-error dialog and force review. Use only when the Operator explicitly requested review of a compile-failed staged candidate.")] bool forceReviewOnOverlayErrors = false)
    {
        return Track(nameof(LaunchStagedDiff), new { stagedRecordId, forceReviewOnOverlayErrors }, () => workflowService.LaunchStagedDiff(stagedRecordId, forceReviewOnOverlayErrors));
    }

    [McpServerTool]
    [Description("Create a proposed compare snapshot for a monitor Working file and return paths for Host-launched review. The path may be absolute or relative to the watched solution folder.")]
    public MonitorFileCompareResult CompareFile(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string sourceFilePath,
        [Description("Optional compact ledger summary to append to the monitor-owned ledger.")] string? ledgerSummary = null,
        [Description("Refresh from source first if the Working copy is missing.")] bool refreshIfMissing = true)
    {
        return Track(nameof(CompareFile), new { sourceFilePath, hasLedgerSummary = !string.IsNullOrWhiteSpace(ledgerSummary), refreshIfMissing }, () => workflowService.CompareFile(sourceFilePath, ledgerSummary, refreshIfMissing));
    }

    [McpServerTool]
    [Description("List monitor run/history entries recorded under monitor-owned Working\\History.")]
    public IReadOnlyList<MonitorRunEntry> ListMonitorRuns(
        [Description("Maximum entries to return.")] int maxEntries = 100)
    {
        return Track(nameof(ListMonitorRuns), new { maxEntries }, () => workflowService.ListMonitorRuns(maxEntries));
    }

    [McpServerTool]
    [Description("Return all recorded entries for one monitor run id.")]
    public MonitorRunDetail GetMonitorRun(
        [Description("Run id from list_monitor_runs.")] string runId)
    {
        return Track(nameof(GetMonitorRun), new { runId }, () => workflowService.GetMonitorRun(runId));
    }

    [McpServerTool]
    [Description("List monitor-owned per-file ledgers.")]
    public IReadOnlyList<MonitorLedgerInfo> ListLedgers(
        [Description("Maximum ledgers to return.")] int maxEntries = 100)
    {
        return Track(nameof(ListLedgers), new { maxEntries }, () => workflowService.ListLedgers(maxEntries));
    }

    [McpServerTool]
    [Description("Read one monitor-owned per-file ledger by source file or ledger path.")]
    public MonitorLedgerReadResult GetLedger(
        [Description("Optional source file path, absolute or relative to the watched solution folder.")] string? sourceFilePath = null,
        [Description("Optional absolute ledger path under monitor Working\\History\\Ledgers.")] string? ledgerPath = null)
    {
        return Track(nameof(GetLedger), new { sourceFilePath, ledgerPath }, () => workflowService.GetLedger(sourceFilePath, ledgerPath));
    }

    [McpServerTool]
    [Description("Archive old monitor-owned history snapshots and prune old ledgers.")]
    public MonitorPruneResult PruneMonitorHistory(
        [Description("Retention window in days.")] int retentionDays = 7)
    {
        return Track(nameof(PruneMonitorHistory), new { retentionDays }, () => workflowService.PruneMonitorHistory(retentionDays));
    }

    [McpServerTool]
    [Description("Return the Markdown tool manifest for the Monitor MCP Server tool surface.")]
    public string GetToolManifest()
    {
        return Track(nameof(GetToolManifest), null, () =>
        {
            string path = Path.Combine(settings.McpServerRoot, "MONITOR_MCP_TOOL_MANIFEST.md");
            return File.Exists(path)
                ? File.ReadAllText(path)
                : "Monitor MCP tool manifest is missing.";
        });
    }

    [McpServerTool]
    [Description("Return the normal Claude staging guide, including session-overlay rules. This is review-facing guidance, not debug smoke coverage.")]
    public string GetStagingGuide()
    {
        return Track(nameof(GetStagingGuide), null, () =>
        {
            string stagingPath = Path.Combine(settings.UiRoot, "Docs", "Skills", "SystemMonitorStaging.md");
            string overlayPath = Path.Combine(settings.UiRoot, "Docs", "Skills", "SessionOverlayValidation.md");

            List<string> sections = [];
            sections.Add(ReadMarkdownOrMissing(stagingPath, "System Monitor staging guide"));
            sections.Add(ReadMarkdownOrMissing(overlayPath, "Session overlay validation guide"));

            return string.Join($"{Environment.NewLine}{Environment.NewLine}---{Environment.NewLine}{Environment.NewLine}", sections);
        });
    }

    [McpServerTool]
    [Description("Return the smoke-test catalog for MonitorBaseClaude, including runnable modes, coverage areas, outputs, and known gaps.")]
    public string GetSmokeTestCatalog()
    {
        return Track(nameof(GetSmokeTestCatalog), null, () =>
        {
            string path = Path.Combine(settings.UiRoot, "Docs", "SmokeTestCatalog.md");
            return File.Exists(path)
                ? File.ReadAllText(path)
                : "Smoke test catalog file was not found.";
        });
    }

    [McpServerTool]
    [Description("List watched project folders under the configured watched-projects root.")]
    public IReadOnlyList<WatchedProjectInfo> ListWatchedProjects()
    {
        return Track(nameof(ListWatchedProjects), null, () =>
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
        });
    }

    private T Track<T>(string toolName, object? arguments, Func<T> action)
    {
        return telemetryService.Track(toolName, arguments, action);
    }

    private static string ReadMarkdownOrMissing(string path, string description)
    {
        return File.Exists(path)
            ? File.ReadAllText(path)
            : $"{description} was not found at {path}.";
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
    public static MonitorServerSettings Load(string[]? args = null)
    {
        string baseDirectory = AppContext.BaseDirectory;
        string uiRoot = FindAncestorWithFile(baseDirectory, "MonitorBaseClaude.csproj")
            ?? FindAncestorWithFile(Directory.GetCurrentDirectory(), "MonitorBaseClaude.csproj")
            ?? Directory.GetCurrentDirectory();
        string siblingRoot = Directory.GetParent(uiRoot)?.FullName ?? uiRoot;
        string mcpRoot = Path.Combine(uiRoot, "MonitorBaseClaude.McpServer");
        string legacyRoot = Path.Combine(siblingRoot, "ClaudeMonitor", "Monitor");
        string watchedRoot = siblingRoot;
        string watchedSolutionPath = FindFirstSolution(Path.Combine(Path.GetPathRoot(uiRoot) ?? "C:\\", "Schema Studio - DBV2")) ?? string.Empty;

        string settingsPath = ResolvePath(ReadOption(args ?? [], "--settings"), Directory.GetCurrentDirectory())
            ?? Path.Combine(uiRoot, "appsettings.json");
        string settingsDirectory = Path.GetDirectoryName(Path.GetFullPath(settingsPath)) ?? uiRoot;
        if (File.Exists(settingsPath))
        {
            using FileStream stream = File.OpenRead(settingsPath);
            JsonDocument document = JsonDocument.Parse(stream);
            if (document.RootElement.TryGetProperty("MonitorClient", out JsonElement client))
            {
                mcpRoot = ResolvePath(GetString(client, "MonitorMcpServerRoot"), settingsDirectory) ?? mcpRoot;
                legacyRoot = ResolvePath(GetString(client, "LegacyMonitorRoot"), settingsDirectory) ?? legacyRoot;
                watchedRoot = ResolvePath(GetString(client, "WatchedProjectsRoot"), settingsDirectory) ?? watchedRoot;
                watchedSolutionPath = ResolvePath(GetString(client, "WatchedSolutionPath"), settingsDirectory) ?? watchedSolutionPath;
            }

            if (document.RootElement.TryGetProperty("WorkflowSettings", out JsonElement workflow))
            {
                string? observedRoot = ResolvePath(GetString(workflow, "ObservedRoot"), settingsDirectory);
                if (!string.IsNullOrWhiteSpace(observedRoot) && Directory.Exists(observedRoot))
                {
                    watchedSolutionPath = Directory.GetFiles(observedRoot, "*.sln", SearchOption.TopDirectoryOnly)
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault() ?? watchedSolutionPath;
                }
            }
        }

        return new MonitorServerSettings(uiRoot, mcpRoot, legacyRoot, watchedRoot, watchedSolutionPath);
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static string? ResolvePath(string? path, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            string trimmed = path.Trim();
            return Path.IsPathRooted(trimmed)
                ? Path.GetFullPath(trimmed)
                : Path.GetFullPath(Path.Combine(baseDirectory, trimmed));
        }
        catch
        {
            return path;
        }
    }

    private static string? FindFirstSolution(string folder)
    {
        return Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.sln", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault()
            : null;
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
