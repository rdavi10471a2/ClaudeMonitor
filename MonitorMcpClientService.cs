using System.Text.Json;
using System.Text.Json.Nodes;
using MonitorBaseClaude.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MonitorBaseClaude;

[AIFileContext("MonitorMcpClientService.cs", "Starts and talks to the new MonitorBaseClaude MCP server over stdio for the WinForms operator dashboard.")]
[FileVersion("1.1")]
public sealed class MonitorMcpClientService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly MonitorClientSettings settings;
    private readonly string? serverSettingsPath;
    private readonly SemaphoreSlim clientGate = new(1, 1);
    private McpClient? client;

    public MonitorMcpClientService(MonitorClientSettings settings, string? serverSettingsPath = null)
    {
        this.settings = settings;
        this.serverSettingsPath = serverSettingsPath;
    }

    public async Task<MonitorMcpDashboardSnapshot> LoadDashboardSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await clientGate.WaitAsync(cancellationToken);
        try
        {
            McpClient activeClient = await GetClientAsync(cancellationToken);
            IList<McpClientTool> tools = await activeClient.ListToolsAsync(cancellationToken: cancellationToken);
            string statusTool = ResolveToolName(tools, "get_monitor_status");
            string manifestTool = ResolveToolName(tools, "get_tool_manifest");

            CallToolResult statusResult = await activeClient.CallToolAsync(statusTool, cancellationToken: cancellationToken);
            CallToolResult manifestResult = await activeClient.CallToolAsync(manifestTool, cancellationToken: cancellationToken);

            return new MonitorMcpDashboardSnapshot(
                tools.Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray(),
                ToJsonNode(statusResult)?.ToJsonString(JsonOptions) ?? JsonSerializer.Serialize(statusResult, JsonOptions),
                ExtractText(manifestResult) ?? ToJsonNode(manifestResult)?.ToJsonString(JsonOptions) ?? JsonSerializer.Serialize(manifestResult, JsonOptions),
                statusResult.IsError == true || manifestResult.IsError == true);
        }
        finally
        {
            clientGate.Release();
        }
    }

    public async Task<MonitorMcpToolCallResult> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        await clientGate.WaitAsync(cancellationToken);
        try
        {
            McpClient activeClient = await GetClientAsync(cancellationToken);
            CallToolResult result = await activeClient.CallToolAsync(
                toolName,
                arguments?.ToDictionary(pair => pair.Key, pair => pair.Value),
                cancellationToken: cancellationToken);
            JsonNode? resultJson = ToJsonNode(result);
            return new MonitorMcpToolCallResult(
                toolName,
                result.IsError == true,
                resultJson?.ToJsonString(JsonOptions) ?? JsonSerializer.Serialize(result, JsonOptions));
        }
        finally
        {
            clientGate.Release();
        }
    }

    public void Dispose()
    {
        clientGate.Wait();
        try
        {
            client?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            client = null;
        }
        finally
        {
            clientGate.Release();
            clientGate.Dispose();
        }
    }

    private async Task<McpClient> GetClientAsync(CancellationToken cancellationToken)
    {
        if (client is not null)
        {
            return client;
        }

        string command = ResolveServerCommand();
        StdioClientTransportOptions transportOptions = new()
        {
            Name = "monitor-base-claude",
            Command = command,
            WorkingDirectory = settings.MonitorMcpServerRoot
        };
        if (!string.IsNullOrWhiteSpace(serverSettingsPath))
        {
            transportOptions.Arguments = ["--settings", serverSettingsPath];
        }

        client = await McpClient.CreateAsync(new StdioClientTransport(transportOptions), cancellationToken: cancellationToken);
        return client;
    }

    private string ResolveServerCommand()
    {
        string debugExe = Path.Combine(settings.MonitorMcpServerRoot, "bin", "Debug", "net10.0", "MonitorBaseClaude.McpServer.exe");
        if (File.Exists(debugExe))
        {
            return debugExe;
        }

        string releaseExe = Path.Combine(settings.MonitorMcpServerRoot, "bin", "Release", "net10.0", "MonitorBaseClaude.McpServer.exe");
        if (File.Exists(releaseExe))
        {
            return releaseExe;
        }

        throw new FileNotFoundException("Build MonitorBaseClaude.McpServer before connecting from WinForms.", debugExe);
    }

    private static string ResolveToolName(IList<McpClientTool> tools, string expectedName)
    {
        McpClientTool? exact = tools.FirstOrDefault(tool => tool.Name.Equals(expectedName, StringComparison.Ordinal));
        if (exact is not null)
        {
            return exact.Name;
        }

        McpClientTool? relaxed = tools.FirstOrDefault(tool =>
            tool.Name.Replace("-", "_", StringComparison.Ordinal).Equals(expectedName, StringComparison.OrdinalIgnoreCase)
            || tool.Name.Contains(expectedName, StringComparison.OrdinalIgnoreCase));
        if (relaxed is not null)
        {
            return relaxed.Name;
        }

        throw new InvalidOperationException($"Monitor MCP tool '{expectedName}' is not available.");
    }

    private static JsonNode? ToJsonNode(CallToolResult result)
    {
        if (result.StructuredContent is not null)
        {
            return JsonSerializer.SerializeToNode(result.StructuredContent, JsonOptions);
        }

        JsonNode? resultJson = JsonSerializer.SerializeToNode(result, JsonOptions);
        string? text = ExtractText(result);
        if (!string.IsNullOrWhiteSpace(text))
        {
            try
            {
                return JsonNode.Parse(text);
            }
            catch (JsonException)
            {
                return resultJson;
            }
        }

        return resultJson;
    }

    private static string? ExtractText(CallToolResult result)
    {
        JsonNode? resultJson = JsonSerializer.SerializeToNode(result, JsonOptions);
        JsonArray? content = FindArrayByName(resultJson, "content");
        if (content is null)
        {
            return null;
        }

        foreach (JsonNode? item in content)
        {
            if (item is JsonObject obj
                && obj.TryGetPropertyValue("text", out JsonNode? textNode)
                && textNode is not null)
            {
                return textNode.ToString();
            }
        }

        return null;
    }

    private static JsonArray? FindArrayByName(JsonNode? node, string propertyName)
    {
        if (node is JsonObject obj)
        {
            foreach (KeyValuePair<string, JsonNode?> property in obj)
            {
                if (property.Key.Equals(propertyName, StringComparison.OrdinalIgnoreCase)
                    && property.Value is JsonArray array)
                {
                    return array;
                }

                JsonArray? nested = FindArrayByName(property.Value, propertyName);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        if (node is JsonArray nodes)
        {
            foreach (JsonNode? child in nodes)
            {
                JsonArray? nested = FindArrayByName(child, propertyName);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }
}

public sealed record MonitorMcpDashboardSnapshot(
    IReadOnlyList<string> ToolNames,
    string StatusJson,
    string ToolManifest,
    bool HasErrors);

public sealed record MonitorMcpToolCallResult(
    string ToolName,
    bool IsError,
    string ResponseJson);
