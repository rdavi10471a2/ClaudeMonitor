using System.Text.Json;
using MonitorBaseClaude.AI;

namespace MonitorBaseClaude;

[AIFileContext("MonitorClientSettings.cs", "Loads local WinForms monitor client settings such as UI root, new MCP server root, legacy monitor source, watched solution, CodeLens solution path, and local Ollama defaults.")]
[FileVersion("1.3")]
public sealed record MonitorClientSettings(
    string UiRoot,
    string MonitorMcpServerRoot,
    string LegacyMonitorRoot,
    string WatchedSolutionPath,
    string CodeLensSolutionPath,
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
            Path.Combine(baseDirectory, "MonitorClient.appsettings.json"),
            @"C:\VSCodeProjects\ClaudeMonitor\Monitor\appsettings.json"
        ];

        string? path = candidates.FirstOrDefault(File.Exists);
        string uiRoot = FindAncestorWithFile(baseDirectory, "MonitorBaseClaude.csproj")
            ?? @"C:\VSCodeProjects\MonitorBaseClaude";
        string monitorMcpServerRoot = Path.Combine(uiRoot, "MonitorBaseClaude.McpServer");
        string legacyMonitorRoot = @"C:\VSCodeProjects\ClaudeMonitor\Monitor";
        string watchedSolutionPath = @"C:\Schema Studio - DBV2\Schema Studio.sln";
        string codeLensSolutionPath = @"C:\Schema Studio - DBV2\Schema Studio.sln";
        string ollamaEndpoint = "http://127.0.0.1:11434";
        string ollamaModel = "qwen3-coder:30b";

        if (path is not null)
        {
            using FileStream stream = File.OpenRead(path);
            JsonDocument document = JsonDocument.Parse(stream);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("MonitorClient", out JsonElement client))
            {
                uiRoot = GetString(client, "UiRoot") ?? uiRoot;
                monitorMcpServerRoot = GetString(client, "MonitorMcpServerRoot") ?? monitorMcpServerRoot;
                legacyMonitorRoot = GetString(client, "LegacyMonitorRoot") ?? legacyMonitorRoot;
                watchedSolutionPath = GetString(client, "WatchedSolutionPath") ?? watchedSolutionPath;
                codeLensSolutionPath = GetString(client, "CodeLensSolutionPath") ?? codeLensSolutionPath;
                ollamaEndpoint = GetString(client, "OllamaEndpoint") ?? ollamaEndpoint;
                ollamaModel = GetString(client, "OllamaModel") ?? ollamaModel;
            }

            if (root.TryGetProperty("WorkflowSettings", out JsonElement workflow))
            {
                string? observedRoot = GetString(workflow, "ObservedRoot");
                if (!string.IsNullOrWhiteSpace(observedRoot))
                {
                    watchedSolutionPath = Directory.GetFiles(observedRoot, "*.sln", SearchOption.TopDirectoryOnly)
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault() ?? watchedSolutionPath;
                }
            }
        }

        return new MonitorClientSettings(uiRoot, monitorMcpServerRoot, legacyMonitorRoot, watchedSolutionPath, codeLensSolutionPath, ollamaEndpoint, ollamaModel);
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
