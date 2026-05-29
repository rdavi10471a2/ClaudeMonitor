using System.Text.Json;
using MonitorBaseClaude.AI;

namespace MonitorBaseClaude;

[AIFileContext("MonitorClientSettings.cs", "Loads local WinForms monitor client settings such as UI root, MCP server root, legacy monitor source, the single watched solution path shared by Monitor and Roslyn, and local Ollama defaults.")]
[FileVersion("1.7")]
public sealed record MonitorClientSettings(
    string UiRoot,
    string MonitorMcpServerRoot,
    string LegacyMonitorRoot,
    string WatchedSolutionPath,
    string OllamaEndpoint,
    string OllamaModel)
{
    public static MonitorClientSettings Load()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string currentDirectory = Directory.GetCurrentDirectory();
        string[] candidates =
        [
            Path.Combine(currentDirectory, "appsettings.json"),
            Path.Combine(baseDirectory, "appsettings.json"),
            Path.Combine(currentDirectory, "MonitorClient.appsettings.json"),
            Path.Combine(baseDirectory, "MonitorClient.appsettings.json")
        ];

        string? path = candidates.FirstOrDefault(File.Exists);
        string settingsDirectory = path is null
            ? currentDirectory
            : Path.GetDirectoryName(Path.GetFullPath(path)) ?? currentDirectory;
        string uiRoot = FindAncestorWithFile(baseDirectory, "MonitorBaseClaude.csproj")
            ?? FindAncestorWithFile(currentDirectory, "MonitorBaseClaude.csproj")
            ?? currentDirectory;
        string monitorMcpServerRoot = Path.Combine(uiRoot, "MonitorBaseClaude.McpServer");
        string legacyMonitorRoot = Path.Combine(uiRoot, "Monitor");
        string watchedSolutionPath = string.Empty;
        string ollamaEndpoint = "http://127.0.0.1:11434";
        string ollamaModel = "qwen3-coder:30b";

        if (path is not null)
        {
            using FileStream stream = File.OpenRead(path);
            JsonDocument document = JsonDocument.Parse(stream);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("MonitorClient", out JsonElement client))
            {
                uiRoot = ResolvePath(GetString(client, "UiRoot"), settingsDirectory) ?? uiRoot;
                monitorMcpServerRoot = ResolvePath(GetString(client, "MonitorMcpServerRoot"), settingsDirectory) ?? monitorMcpServerRoot;
                legacyMonitorRoot = ResolvePath(GetString(client, "LegacyMonitorRoot"), settingsDirectory) ?? legacyMonitorRoot;
                watchedSolutionPath = ResolvePath(GetString(client, "WatchedSolutionPath"), settingsDirectory) ?? watchedSolutionPath;
                ollamaEndpoint = GetString(client, "OllamaEndpoint") ?? ollamaEndpoint;
                ollamaModel = GetString(client, "OllamaModel") ?? ollamaModel;
            }

            if (root.TryGetProperty("WorkflowSettings", out JsonElement workflow))
            {
                string? observedRoot = ResolvePath(GetString(workflow, "ObservedRoot"), settingsDirectory);
                if (string.IsNullOrWhiteSpace(watchedSolutionPath)
                    && !string.IsNullOrWhiteSpace(observedRoot)
                    && Directory.Exists(observedRoot))
                {
                    watchedSolutionPath = Directory.GetFiles(observedRoot, "*.sln", SearchOption.TopDirectoryOnly)
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault() ?? watchedSolutionPath;
                }
            }
        }

        return new MonitorClientSettings(uiRoot, monitorMcpServerRoot, legacyMonitorRoot, watchedSolutionPath, ollamaEndpoint, ollamaModel);
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
