namespace MonitorBaseClaude.McpServer;

public sealed partial class MonitorWorkflowService
{
    public MonitorSelfCheckResult GetSelfCheck()
    {
        string watchedProjectFolder = Path.GetDirectoryName(settings.WatchedSolutionPath) ?? string.Empty;
        string workingRoot = Path.Combine(settings.UiRoot, "Working");
        string historyRoot = Path.Combine(workingRoot, "History");
        MonitorGuardrailDecision[] guardrails =
        [
            BuildGuardrailDecision("ui-root-as-watched-root", settings.UiRoot),
            BuildGuardrailDecision("working-root-as-watched-root", workingRoot),
            BuildGuardrailDecision("configured-watched-project", watchedProjectFolder)
        ];

        return new MonitorSelfCheckResult(
            settings.UiRoot,
            settings.McpServerRoot,
            settings.LegacyMonitorRoot,
            settings.WatchedSolutionPath,
            watchedProjectFolder,
            workingRoot,
            historyRoot,
            ResolveWinMergePath(),
            File.Exists(settings.WatchedSolutionPath),
            Directory.Exists(watchedProjectFolder),
            guardrails);
    }

    private MonitorGuardrailDecision BuildGuardrailDecision(string label, string candidateWatchedRoot)
    {
        string candidate = TrimDirectorySeparator(Path.GetFullPath(candidateWatchedRoot));
        string uiRoot = TrimDirectorySeparator(Path.GetFullPath(settings.UiRoot));
        string workingRoot = TrimDirectorySeparator(Path.Combine(uiRoot, "Working"));

        if (string.Equals(candidate, uiRoot, StringComparison.OrdinalIgnoreCase))
        {
            return new MonitorGuardrailDecision(label, candidate, false, "watched root cannot be the Host project folder");
        }

        if (IsSameOrChildPath(candidate, workingRoot))
        {
            return new MonitorGuardrailDecision(label, candidate, false, "watched root cannot be inside monitor-owned Working state");
        }

        return new MonitorGuardrailDecision(label, candidate, true, string.Empty);
    }

    private static bool IsSameOrChildPath(string path, string possibleParent)
    {
        string fullPath = TrimDirectorySeparator(Path.GetFullPath(path));
        string fullParent = TrimDirectorySeparator(Path.GetFullPath(possibleParent));
        return string.Equals(fullPath, fullParent, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(fullParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record MonitorSelfCheckResult(
    string UiRoot,
    string McpServerRoot,
    string SourceImplementationRoot,
    string WatchedSolutionPath,
    string WatchedProjectFolder,
    string WorkingRoot,
    string HistoryRoot,
    string? WinMergePath,
    bool WatchedSolutionExists,
    bool WatchedProjectFolderExists,
    IReadOnlyList<MonitorGuardrailDecision> Guardrails);

public sealed record MonitorGuardrailDecision(
    string Label,
    string Path,
    bool Allowed,
    string Reason);
