using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MonitorBaseClaude.AI;

namespace MonitorBaseClaude;

[AIFileContext("OllamaToolExplorerService.cs", "Uses the local Ollama LLM API to choose Host actions against the Monitor MCP Server.")]
[FileVersion("2.1")]
public sealed class OllamaToolExplorerService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly MonitorClientSettings settings;
    private readonly HttpClient httpClient = new();

    public OllamaToolExplorerService(MonitorClientSettings settings)
    {
        this.settings = settings;
    }

    public async Task<string> AskAboutMonitorToolsAsync(
        MonitorMcpDashboardSnapshot snapshot,
        IReadOnlyList<LocalMcpServerSurface> localServers,
        string question,
        CancellationToken cancellationToken = default)
    {
        string model = await ResolveUsableModelAsync(cancellationToken);
        return await AskAboutMonitorToolsAsync(snapshot, localServers, question, model, cancellationToken);
    }

    public async Task<string> AskAboutMonitorToolsAsync(
        MonitorMcpDashboardSnapshot snapshot,
        IReadOnlyList<LocalMcpServerSurface> localServers,
        string question,
        string model,
        CancellationToken cancellationToken = default)
    {
        string prompt = BuildPrompt(snapshot, localServers, question);
        Uri endpoint = new(new Uri(settings.OllamaEndpoint.TrimEnd('/') + "/"), "api/chat");
        OllamaChatRequest request = new(
            model,
            [
                new OllamaMessage("system", "You are a local MCP tool surface reviewer. You do not modify files. You suggest read-only tests, tool categories, and safe next MCP tool ports."),
                new OllamaMessage("user", prompt)
            ],
            Stream: false);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(endpoint, request, JsonOptions, cancellationToken);
        string responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return $"Ollama request failed using {model}: {(int)response.StatusCode} {response.ReasonPhrase}\r\n\r\n{responseText}";
        }

        OllamaChatResponse? chatResponse = JsonSerializer.Deserialize<OllamaChatResponse>(responseText, JsonOptions);
        return $"Model: {model}\r\n\r\n{chatResponse?.Message?.Content ?? responseText}";
    }

    public async Task<IReadOnlyList<string>> GetInstalledModelsAsync(CancellationToken cancellationToken = default)
    {
        OllamaTagsResponse? tags = await GetTagsAsync(cancellationToken);
        return tags?.Models
            .Select(model => model.Name)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? [];
    }

    public async Task<OllamaActionDecision> DecideNextActionAsync(
        IReadOnlyList<LocalMcpServerSurface> localServers,
        string question,
        string model,
        CancellationToken cancellationToken = default)
    {
        string prompt = BuildActionPrompt(localServers, question);
        Uri endpoint = new(new Uri(settings.OllamaEndpoint.TrimEnd('/') + "/"), "api/chat");
        OllamaChatRequest request = new(
            model,
            [
                new OllamaMessage("system", "You are the LLM. The WinForms Host can execute MCP Server tools for you. Choose the next Server tool action. Return only JSON. No Markdown. No explanation."),
                new OllamaMessage("user", prompt)
            ],
            Stream: false);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(endpoint, request, JsonOptions, cancellationToken);
        string responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new OllamaActionDecision("error", null, null, null, $"Ollama request failed: {(int)response.StatusCode} {response.ReasonPhrase}\r\n{responseText}");
        }

        OllamaChatResponse? chatResponse = JsonSerializer.Deserialize<OllamaChatResponse>(responseText, JsonOptions);
        string content = chatResponse?.Message?.Content ?? responseText;
        try
        {
            OllamaActionDecision? decision = JsonSerializer.Deserialize<OllamaActionDecision>(ExtractJsonObject(content), JsonOptions);
            return decision ?? new OllamaActionDecision("error", null, null, null, content);
        }
        catch (JsonException)
        {
            return new OllamaActionDecision("error", null, null, null, content);
        }
    }

    public async Task<string> AnswerWithToolResultAsync(
        string question,
        string toolName,
        string toolResultJson,
        string model,
        CancellationToken cancellationToken = default)
    {
        string prompt = $"""
        User request:
        {question}

        MCP tool executed:
        {toolName}

        MCP tool result JSON:
        {toolResultJson}

        Answer the user using the tool result. Do not suggest calling the tool again.
        """;
        Uri endpoint = new(new Uri(settings.OllamaEndpoint.TrimEnd('/') + "/"), "api/chat");
        OllamaChatRequest request = new(
            model,
            [
                new OllamaMessage("system", "Answer using the MCP tool result. Be concise."),
                new OllamaMessage("user", prompt)
            ],
            Stream: false);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(endpoint, request, JsonOptions, cancellationToken);
        string responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return $"Ollama request failed: {(int)response.StatusCode} {response.ReasonPhrase}\r\n{responseText}";
        }

        OllamaChatResponse? chatResponse = JsonSerializer.Deserialize<OllamaChatResponse>(responseText, JsonOptions);
        return chatResponse?.Message?.Content ?? responseText;
    }

    public string BuildPreviewPrompt(MonitorMcpDashboardSnapshot snapshot, IReadOnlyList<LocalMcpServerSurface> localServers, string question)
    {
        return BuildPrompt(snapshot, localServers, question);
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }

    private static string BuildPrompt(MonitorMcpDashboardSnapshot snapshot, IReadOnlyList<LocalMcpServerSurface> localServers, string question)
    {
        string toolList = string.Join("\n", snapshot.ToolNames.Take(20).Select(name => "- " + name));
        if (snapshot.ToolNames.Count > 20)
        {
            toolList += $"\n- ... {snapshot.ToolNames.Count - 20} more";
        }

        string manifestSummary = SummarizeManifest(snapshot.ToolManifest);
        string localServerSummary = FormatLocalServerSummary(localServers);
        return $"""
        Answer the human question using the MCP server context below.
        Do not assume the user wants a migration plan unless they ask for one.
        If the question asks for the API surface, summarize the available tools and arguments.

        Human question:
        {question}

        MCP server context:
        Local MCP services discovered from config:
        {localServerSummary}

        Active monitor server tools:
        Tools:
        {toolList}

        Status JSON:
        {Truncate(snapshot.StatusJson, 1200)}

        Manifest summary:
        {manifestSummary}
        """;
    }

    private static string BuildActionPrompt(IReadOnlyList<LocalMcpServerSurface> localServers, string question)
    {
        string toolSummary = FormatLocalServerSummary(localServers);
        return string.Join(Environment.NewLine, [
            "Available MCP Servers and tools:",
            toolSummary,
            "",
            "User request:",
            question,
            "",
            "Choose the next action.",
            "",
            "If a Server tool is needed, return exactly:",
            "{\"action\":\"call_tool\",\"server\":\"server_name\",\"tool\":\"tool_name\",\"arguments\":{}}",
            "",
            "If no tool is needed, return exactly:",
            "{\"action\":\"answer\",\"answer\":\"your answer\"}",
            "",
            "Examples:",
            "User asks \"find Program.cs\" -> {\"action\":\"call_tool\",\"server\":\"monitor-base-claude\",\"tool\":\"find_file\",\"arguments\":{\"fileNameOrPattern\":\"Program.cs\",\"maxResults\":10}}",
            "User asks \"read Program.cs\" -> first use find_file if the exact relative path is uncertain, otherwise use get_file.",
            "User asks about symbols, references, diagnostics, NuGet dependencies, or project dependencies -> use roslyn-codelens.",
            "User asks about monitor sessions, file hashes, refresh, compare, or WinMerge -> use monitor-base-claude."
        ]);
    }

    private static string ExtractJsonObject(string text)
    {
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        return start >= 0 && end >= start
            ? text[start..(end + 1)]
            : text;
    }

    private static string FormatLocalServerSummary(IReadOnlyList<LocalMcpServerSurface> localServers)
    {
        if (localServers.Count == 0)
        {
            return "- none discovered";
        }

        List<string> lines = [];
        foreach (LocalMcpServerSurface server in localServers)
        {
            lines.Add($"- {server.Name} ({(server.IsAvailable ? "available" : "unavailable")})");
            lines.Add($"  role: {server.Role}");
            lines.Add($"  command: {server.Command}");
            if (server.Arguments.Count > 0)
            {
                lines.Add($"  args: {string.Join(" ", server.Arguments)}");
            }

            if (!server.IsAvailable)
            {
                lines.Add($"  error: {server.Error}");
                continue;
            }

            foreach (LocalMcpToolSurface tool in server.Tools.Take(20))
            {
                string description = string.IsNullOrWhiteSpace(tool.Description) ? string.Empty : $" - {tool.Description}";
                lines.Add($"  tool: {tool.Name}{description}");
            }

            if (server.Tools.Count > 20)
            {
                lines.Add($"  ... {server.Tools.Count - 20} more tools");
            }
        }

        return string.Join("\n", lines);
    }

    private static string SummarizeManifest(string manifest)
    {
        string[] lines = manifest
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Where(line => line.StartsWith("#", StringComparison.Ordinal)
                || line.Contains("| `", StringComparison.Ordinal)
                || line.Contains("| planned", StringComparison.OrdinalIgnoreCase)
                || line.Contains("| scaffolded", StringComparison.OrdinalIgnoreCase)
                || line.Contains("refresh_file", StringComparison.Ordinal)
                || line.Contains("compare_file", StringComparison.Ordinal)
                || line.Contains("get_monitor_status", StringComparison.Ordinal))
            .Take(80)
            .ToArray();

        return lines.Length == 0
            ? Truncate(manifest, 2400)
            : Truncate(string.Join("\n", lines), 2400);
    }

    private static string Truncate(string text, int maxChars)
    {
        if (text.Length <= maxChars)
        {
            return text;
        }

        return text[..maxChars] + "\n...(truncated)";
    }

    private async Task<string> ResolveUsableModelAsync(CancellationToken cancellationToken)
    {
        OllamaTagsResponse? tags = await GetTagsAsync(cancellationToken);
        IReadOnlyList<string> installedModels = tags?.Models.Select(model => model.Name).ToArray() ?? [];

        if (installedModels.Contains(settings.OllamaModel, StringComparer.OrdinalIgnoreCase))
        {
            return settings.OllamaModel;
        }

        string[] preferredFallbacks =
        [
            "llama3.2:1b",
            "llama3.2:3b",
            "gemma3:27b",
            "qwen3:8b"
        ];

        foreach (string fallback in preferredFallbacks)
        {
            if (installedModels.Contains(fallback, StringComparer.OrdinalIgnoreCase))
            {
                return fallback;
            }
        }

        return installedModels.FirstOrDefault()
            ?? throw new InvalidOperationException("No local Ollama models are installed.");
    }

    private async Task<OllamaTagsResponse?> GetTagsAsync(CancellationToken cancellationToken)
    {
        Uri endpoint = new(new Uri(settings.OllamaEndpoint.TrimEnd('/') + "/"), "api/tags");
        using HttpResponseMessage response = await httpClient.GetAsync(endpoint, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<OllamaTagsResponse>(JsonOptions, cancellationToken);
    }

    private sealed record OllamaChatRequest(
        string Model,
        IReadOnlyList<OllamaMessage> Messages,
        bool Stream);

    private sealed record OllamaMessage(
        string Role,
        string Content);

    private sealed record OllamaChatResponse(
        OllamaMessage? Message);

    private sealed record OllamaTagsResponse(
        IReadOnlyList<OllamaModelInfo> Models);

    private sealed record OllamaModelInfo(
        string Name,
        [property: JsonPropertyName("model")] string ModelName);
}

public sealed record OllamaActionDecision(
    string Action,
    string? Server,
    string? Tool,
    Dictionary<string, object?>? Arguments,
    string? Answer);
