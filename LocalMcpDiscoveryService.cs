using System.Text.Json;
using System.Text.Json.Nodes;
using MonitorBaseClaude.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MonitorBaseClaude;

[AIFileContext("LocalMcpDiscoveryService.cs", "Discovers local MCP servers from the monitor workspace config and lists their available tools.")]
[FileVersion("1.2")]
public sealed class LocalMcpDiscoveryService
{
    private readonly MonitorClientSettings settings;

    public LocalMcpDiscoveryService(MonitorClientSettings settings)
    {
        this.settings = settings;
    }

    public async Task<IReadOnlyList<LocalMcpServerSurface>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        List<LocalMcpServerDefinition> definitions = LoadConfiguredServers()
            .GroupBy(server => server.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(server => server.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<LocalMcpServerSurface> surfaces = [];
        foreach (LocalMcpServerDefinition definition in definitions)
        {
            surfaces.Add(await ProbeServerAsync(definition, cancellationToken));
        }

        return surfaces;
    }

    public async Task<LocalMcpToolCallResult> CallToolAsync(
        string serverName,
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        LocalMcpServerDefinition definition = LoadConfiguredServers()
            .FirstOrDefault(server => server.Name.Equals(serverName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Local MCP Server '{serverName}' is not configured.");

        StdioClientTransportOptions options = new()
        {
            Name = definition.Name,
            Command = definition.Command,
            Arguments = definition.Arguments.ToArray(),
            WorkingDirectory = ResolveWorkingDirectory(definition)
        };

        await using McpClient client = await McpClient.CreateAsync(new StdioClientTransport(options), cancellationToken: cancellationToken);
        CallToolResult result = await client.CallToolAsync(
            toolName,
            arguments?.ToDictionary(pair => pair.Key, pair => pair.Value),
            cancellationToken: cancellationToken);

        return new LocalMcpToolCallResult(
            definition.Name,
            toolName,
            result.IsError == true,
            ToJsonNode(result)?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task<LocalMcpServerSurface> ProbeServerAsync(LocalMcpServerDefinition definition, CancellationToken cancellationToken)
    {
        try
        {
            StdioClientTransportOptions options = new()
            {
                Name = definition.Name,
                Command = definition.Command,
                Arguments = definition.Arguments.ToArray(),
                WorkingDirectory = ResolveWorkingDirectory(definition)
            };

            await using McpClient client = await McpClient.CreateAsync(new StdioClientTransport(options), cancellationToken: cancellationToken);
            IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
            return new LocalMcpServerSurface(
                definition.Name,
                definition.SourceConfigPath,
                definition.Command,
                definition.Arguments,
                true,
                null,
                GetServerRole(definition.Name),
                tools.Select(tool => new LocalMcpToolSurface(tool.Name, tool.Description ?? string.Empty)).OrderBy(tool => tool.Name, StringComparer.Ordinal).ToArray());
        }
        catch (Exception ex)
        {
            return new LocalMcpServerSurface(
                definition.Name,
                definition.SourceConfigPath,
                definition.Command,
                definition.Arguments,
                false,
                ex.Message,
                GetServerRole(definition.Name),
                []);
        }
    }

    private IEnumerable<LocalMcpServerDefinition> LoadConfiguredServers()
    {
        string projectConfig = Path.Combine(settings.UiRoot, ".mcp.json");
        if (File.Exists(projectConfig))
        {
            foreach (LocalMcpServerDefinition server in LoadServersFromFile(projectConfig))
            {
                yield return server;
            }
        }

    }

    private static IEnumerable<LocalMcpServerDefinition> LoadServersFromFile(string configPath)
    {
        JsonNode? root = JsonNode.Parse(File.ReadAllText(configPath));
        if (root?["mcpServers"] is not JsonObject servers)
        {
            yield break;
        }

        foreach (KeyValuePair<string, JsonNode?> entry in servers)
        {
            if (entry.Value is not JsonObject server)
            {
                continue;
            }

            string? command = server["command"]?.ToString();
            if (string.IsNullOrWhiteSpace(command))
            {
                continue;
            }

            string[] args = server["args"] is JsonArray argsArray
                ? argsArray.Select(arg => arg?.ToString()).Where(arg => !string.IsNullOrWhiteSpace(arg)).Cast<string>().ToArray()
                : [];

            yield return new LocalMcpServerDefinition(entry.Key, configPath, command, args);
        }
    }

    private static string? ResolveWorkingDirectory(LocalMcpServerDefinition definition)
    {
        if (Path.IsPathRooted(definition.Command))
        {
            return Path.GetDirectoryName(definition.Command);
        }

        return null;
    }

    private static string GetServerRole(string serverName)
    {
        if (serverName.Contains("codelens", StringComparison.OrdinalIgnoreCase)
            || serverName.Contains("roslyn", StringComparison.OrdinalIgnoreCase))
        {
            return "Use for Roslyn code intelligence: symbols, references, diagnostics, project dependencies, NuGet dependencies, call graphs, type hierarchy, and code analysis.";
        }

        if (serverName.Contains("monitor", StringComparison.OrdinalIgnoreCase))
        {
            return "Use for monitor workflow: watched project files, durable sessions, file hashes, refresh/compare staging, WinMerge review, and monitor-owned history.";
        }

        return "Use according to its tool descriptions.";
    }

    private static JsonNode? ToJsonNode(CallToolResult result)
    {
        if (result.StructuredContent is not null)
        {
            return JsonSerializer.SerializeToNode(result.StructuredContent);
        }

        JsonNode? resultJson = JsonSerializer.SerializeToNode(result);
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
        JsonNode? resultJson = JsonSerializer.SerializeToNode(result);
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

public sealed record LocalMcpServerDefinition(
    string Name,
    string SourceConfigPath,
    string Command,
    IReadOnlyList<string> Arguments);

public sealed record LocalMcpServerSurface(
    string Name,
    string SourceConfigPath,
    string Command,
    IReadOnlyList<string> Arguments,
    bool IsAvailable,
    string? Error,
    string Role,
    IReadOnlyList<LocalMcpToolSurface> Tools);

public sealed record LocalMcpToolSurface(
    string Name,
    string Description);

public sealed record LocalMcpToolCallResult(
    string ServerName,
    string ToolName,
    bool IsError,
    string ResponseJson);
