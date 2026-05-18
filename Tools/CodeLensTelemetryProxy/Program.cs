using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodeLensTelemetryProxy;

internal static class Program
{
    private const string ServerDisplayName = "roslyn-codelens";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private static async Task<int> Main(string[] args)
    {
        ProxyOptions options = ProxyOptions.Parse(args);
        ProxySettings settings = ProxySettings.Load(AppContext.BaseDirectory);

        if (options.ShowHelp)
        {
            PrintUsage();
            return 0;
        }

        string serverCommand = ToolResolver.ResolveServerCommand(options.ServerCommand, settings.ServerCommand);
        string logRoot = ResolveLogRoot(options.LogRoot, settings.LogRoot);

        if (options.SelfCheck)
        {
            return RunSelfCheck(serverCommand, logRoot);
        }

        if (string.IsNullOrWhiteSpace(options.SolutionPath))
        {
            Console.Error.WriteLine("Missing required solution path.");
            PrintUsage();
            return 10;
        }

        string solutionPath = Path.GetFullPath(options.SolutionPath);
        if (!File.Exists(solutionPath))
        {
            Console.Error.WriteLine($"Solution file not found: {solutionPath}");
            return 20;
        }

        TelemetryWriter telemetry = new(logRoot, settings.CapturePayloadPreviewChars);
        string sessionId = $"{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}-{Environment.ProcessId}";
        await telemetry.WriteSessionAsync(new Dictionary<string, object?>
        {
            ["event"] = "session_start",
            ["sessionId"] = sessionId,
            ["server"] = ServerDisplayName,
            ["proxyProcessId"] = Environment.ProcessId,
            ["serverCommand"] = serverCommand,
            ["solutionPath"] = solutionPath,
            ["workingDirectory"] = Path.GetDirectoryName(solutionPath) ?? Environment.CurrentDirectory
        });

        Process server = StartServer(serverCommand, solutionPath);
        ConcurrentDictionary<string, PendingRequest> pendingRequests = new(StringComparer.Ordinal);

        try
        {
            Task clientToServer = PumpClientToServerAsync(server, telemetry, sessionId, pendingRequests, settings.CapturePayloadPreviewChars);
            Task serverToClient = PumpServerToClientAsync(
                server,
                telemetry,
                sessionId,
                pendingRequests,
                settings.CapturePayloadPreviewChars,
                settings.TrimFrameworkMetadataInterfaces != false);
            Task stderrPump = PumpServerStderrAsync(server, telemetry, sessionId);
            Task serverExit = server.WaitForExitAsync();

            await Task.WhenAny(clientToServer, serverToClient, serverExit);

            if (!server.HasExited)
            {
                await TryStopServerAsync(server);
            }

            await Task.WhenAll(Swallow(clientToServer), Swallow(serverToClient), Swallow(stderrPump));
            return server.HasExited ? server.ExitCode : 0;
        }
        catch (Exception ex)
        {
            await telemetry.WriteErrorAsync(sessionId, "proxy_failure", ex.Message);
            Console.Error.WriteLine($"Proxy failure: {ex.Message}");
            return 1;
        }
        finally
        {
            await telemetry.WriteSessionAsync(new Dictionary<string, object?>
            {
                ["event"] = "session_end",
                ["sessionId"] = sessionId,
                ["server"] = ServerDisplayName,
                ["serverExitCode"] = server.HasExited ? server.ExitCode : null
            });

            server.Dispose();
        }
    }

    private static Process StartServer(string serverCommand, string solutionPath)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = serverCommand,
            WorkingDirectory = Path.GetDirectoryName(solutionPath) ?? Environment.CurrentDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(solutionPath);

        Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start server command: {serverCommand}");
        return process;
    }

    private static async Task PumpClientToServerAsync(
        Process server,
        TelemetryWriter telemetry,
        string sessionId,
        ConcurrentDictionary<string, PendingRequest> pendingRequests,
        int previewChars)
    {
        using StreamReader input = new(Console.OpenStandardInput(), Encoding.UTF8);
        while (await input.ReadLineAsync() is { } line)
        {
            MessageMetadata metadata = MessageMetadata.FromJsonLine(line);
            string? id = metadata.Id;
            if (!string.IsNullOrWhiteSpace(id))
            {
                pendingRequests[id] = new PendingRequest(DateTimeOffset.UtcNow, metadata.Method, metadata.Tool, metadata.Arguments, Encoding.UTF8.GetByteCount(line));
            }

            await telemetry.WriteMessageAsync("requests", sessionId, "request", line, metadata, elapsedMs: null, previewChars);
            await server.StandardInput.WriteLineAsync(line);
            await server.StandardInput.FlushAsync();
        }
    }

    private static async Task PumpServerToClientAsync(
        Process server,
        TelemetryWriter telemetry,
        string sessionId,
        ConcurrentDictionary<string, PendingRequest> pendingRequests,
        int previewChars,
        bool trimFrameworkMetadataInterfaces)
    {
        while (await server.StandardOutput.ReadLineAsync() is { } line)
        {
            MessageMetadata metadata = MessageMetadata.FromJsonLine(line);
            long? elapsedMs = null;
            if (!string.IsNullOrWhiteSpace(metadata.Id)
                && pendingRequests.TryRemove(metadata.Id, out PendingRequest? pending))
            {
                elapsedMs = (long)(DateTimeOffset.UtcNow - pending.StartedUtc).TotalMilliseconds;
                metadata = metadata with
                {
                    Method = metadata.Method ?? pending.Method,
                    Tool = metadata.Tool ?? pending.Tool,
                    Arguments = metadata.Arguments ?? pending.Arguments,
                    RequestBytes = pending.RequestBytes
                };
            }

            line = TrimResponseForClaude(line, metadata.Tool, trimFrameworkMetadataInterfaces);
            await telemetry.WriteMessageAsync("responses", sessionId, "response", line, metadata, elapsedMs, previewChars);
            await Console.Out.WriteLineAsync(line);
            await Console.Out.FlushAsync();
        }
    }

    private static string TrimResponseForClaude(string line, string? toolName, bool trimFrameworkMetadataInterfaces)
    {
        if (!trimFrameworkMetadataInterfaces
            || !string.Equals(toolName, "get_type_overview", StringComparison.Ordinal))
        {
            return line;
        }

        try
        {
            JsonNode? node = JsonNode.Parse(line);
            if (node is not JsonObject obj)
            {
                return line;
            }

            int removed = TrimTypeOverviewNode(obj);
            return removed == 0 ? line : obj.ToJsonString(JsonOptions);
        }
        catch
        {
            return line;
        }
    }

    private static int TrimTypeOverviewNode(JsonNode? node)
    {
        int removed = 0;
        if (node is JsonObject obj)
        {
            removed += TrimInterfacesArray(obj);
            foreach (KeyValuePair<string, JsonNode?> property in obj.ToArray())
            {
                if (string.Equals(property.Key, "text", StringComparison.Ordinal)
                    && property.Value is JsonValue textValue
                    && textValue.TryGetValue(out string? text)
                    && TryTrimJsonText(text, out string? trimmedText, out int textRemoved))
                {
                    obj[property.Key] = trimmedText;
                    removed += textRemoved;
                    continue;
                }

                removed += TrimTypeOverviewNode(property.Value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? item in array)
            {
                removed += TrimTypeOverviewNode(item);
            }
        }

        return removed;
    }

    private static bool TryTrimJsonText(string text, out string? trimmedText, out int removed)
    {
        trimmedText = null;
        removed = 0;
        string candidate = text.Trim();
        if (!candidate.StartsWith('{') && !candidate.StartsWith('['))
        {
            return false;
        }

        try
        {
            JsonNode? parsed = JsonNode.Parse(candidate);
            removed = TrimTypeOverviewNode(parsed);
            if (removed == 0 || parsed is null)
            {
                return false;
            }

            trimmedText = parsed.ToJsonString(JsonOptions);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int TrimInterfacesArray(JsonObject obj)
    {
        if (obj["interfaces"] is not JsonArray interfaces)
        {
            return 0;
        }

        int originalCount = interfaces.Count;
        List<JsonNode?> kept = [];
        foreach (JsonNode? item in interfaces)
        {
            if (!IsFrameworkMetadataInterface(item))
            {
                kept.Add(item?.DeepClone());
            }
        }

        if (kept.Count == originalCount)
        {
            return 0;
        }

        JsonArray replacement = [];
        foreach (JsonNode? item in kept)
        {
            replacement.Add(item);
        }

        obj["interfaces"] = replacement;
        obj["interfacesTrimmedByProxy"] = originalCount - kept.Count;
        obj["interfacesTrimReason"] = "framework-metadata interfaces are hidden by default by CodeLensTelemetryProxy. Set TrimFrameworkMetadataInterfaces=false in codelens-proxy.settings.json if inherited framework interfaces are needed.";
        return originalCount - kept.Count;
    }

    private static bool IsFrameworkMetadataInterface(JsonNode? item)
    {
        string name = GetInterfaceProperty(item, "name", "fullName", "fullyQualifiedName", "typeName");
        string assembly = GetInterfaceProperty(item, "assembly", "assemblyName", "containingAssembly");
        string candidate = string.IsNullOrWhiteSpace(name) ? item?.ToJsonString(JsonOptions) ?? string.Empty : name;

        return IsFrameworkAssemblyName(assembly)
            || IsFrameworkInterfaceName(candidate);
    }

    private static string GetInterfaceProperty(JsonNode? item, params string[] names)
    {
        if (item is not JsonObject obj)
        {
            return string.Empty;
        }

        foreach (string name in names)
        {
            if (obj.TryGetPropertyValue(name, out JsonNode? value)
                && value is JsonValue jsonValue
                && jsonValue.TryGetValue(out string? text)
                && !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return string.Empty;
    }

    private static bool IsFrameworkAssemblyName(string assembly)
    {
        return assembly.StartsWith("System", StringComparison.Ordinal)
            || assembly.StartsWith("Microsoft.AspNetCore.Components", StringComparison.Ordinal)
            || assembly.StartsWith("Microsoft.Extensions", StringComparison.Ordinal)
            || assembly.StartsWith("Windows", StringComparison.Ordinal)
            || assembly.StartsWith("Presentation", StringComparison.Ordinal);
    }

    private static bool IsFrameworkInterfaceName(string name)
    {
        return name.StartsWith("System.", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.AspNetCore.Components.", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal)
            || name.StartsWith("Windows.", StringComparison.Ordinal)
            || name.Contains(".IOle", StringComparison.Ordinal)
            || name.Contains(".IPersist", StringComparison.Ordinal)
            || name.EndsWith(".IHandleEvent", StringComparison.Ordinal)
            || name.EndsWith(".IHandleAfterRender", StringComparison.Ordinal)
            || name.Equals("IHandleEvent", StringComparison.Ordinal)
            || name.Equals("IHandleAfterRender", StringComparison.Ordinal);
    }

    private static async Task PumpServerStderrAsync(Process server, TelemetryWriter telemetry, string sessionId)
    {
        while (await server.StandardError.ReadLineAsync() is { } line)
        {
            await telemetry.WriteStderrAsync(sessionId, line);
        }
    }

    private static async Task TryStopServerAsync(Process server)
    {
        try
        {
            server.StandardInput.Close();
            if (await WaitForExitAsync(server, TimeSpan.FromMilliseconds(750)))
            {
                return;
            }

            server.Kill(entireProcessTree: true);
        }
        catch
        {
            // The client may already be tearing down. Telemetry records the final state.
        }
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        Task exitTask = process.WaitForExitAsync();
        Task completed = await Task.WhenAny(exitTask, Task.Delay(timeout));
        return ReferenceEquals(completed, exitTask);
    }

    private static async Task Swallow(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
            // The paired pipe often closes first during shutdown.
        }
    }

    private static int RunSelfCheck(string serverCommand, string logRoot)
    {
        Console.WriteLine("CodeLens Telemetry Proxy self-check");
        Console.WriteLine();
        Console.WriteLine($"Resolved server command: {serverCommand}");
        Console.WriteLine($"Server exists: {File.Exists(serverCommand)}");
        Console.WriteLine($"Default log root: {logRoot}");
        Console.WriteLine($"Log root exists: {Directory.Exists(logRoot)}");
        Console.WriteLine($"Settings path: {Path.Combine(AppContext.BaseDirectory, "codelens-proxy.settings.json")}");
        Console.WriteLine($"PATH contains dotnet tools: {ToolResolver.PathContainsDotnetTools()}");
        Console.WriteLine();
        Console.WriteLine("Global tools:");
        foreach (string line in ToolResolver.GetDotnetToolListLines())
        {
            Console.WriteLine($"  {line}");
        }

        return File.Exists(serverCommand) || string.Equals(serverCommand, "roslyn-codelens-mcp", StringComparison.OrdinalIgnoreCase)
            ? 0
            : 20;
    }

    private static string ResolveLogRoot(string? optionLogRoot, string? configuredLogRoot)
    {
        string? candidate = FirstNonWhiteSpace(optionLogRoot, configuredLogRoot);
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            return Path.GetFullPath(candidate);
        }

        DirectoryInfo? current = new(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && current is not null; i++)
        {
            string monitorRoot = Path.Combine(current.FullName, "Monitor");
            if (File.Exists(Path.Combine(monitorRoot, "AGENTS.md")))
            {
                return Path.Combine(monitorRoot, "Working", "History", "McpTelemetry", "RoslynCodeLens");
            }

            current = current.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "McpTelemetry", "RoslynCodeLens");
    }

    private static string? FirstNonWhiteSpace(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  CodeLensTelemetryProxy.exe <solutionPath> [--server-command <path-or-command>] [--log-root <path>]");
        Console.WriteLine("  CodeLensTelemetryProxy.exe --self-check");
    }
}

internal sealed record PendingRequest(DateTimeOffset StartedUtc, string? Method, string? Tool, JsonNode? Arguments, int RequestBytes);

internal sealed record MessageMetadata(
    string? Id,
    string? Method,
    string? Tool,
    JsonNode? Arguments,
    bool IsError,
    int? RequestBytes)
{
    public static MessageMetadata FromJsonLine(string line)
    {
        try
        {
            JsonNode? node = JsonNode.Parse(line);
            if (node is not JsonObject obj)
            {
                return new(null, null, null, null, false, null);
            }

            string? method = obj["method"]?.GetValue<string>();
            string? id = obj["id"]?.ToJsonString(JsonSerializerOptions.Default);
            string? tool = null;
            JsonNode? arguments = null;
            if (string.Equals(method, "tools/call", StringComparison.Ordinal)
                && obj["params"] is JsonObject parameters)
            {
                tool = parameters["name"]?.GetValue<string>();
                arguments = parameters["arguments"]?.DeepClone();
            }

            bool isError = obj["error"] is not null
                || obj["result"]?["isError"]?.GetValue<bool?>() == true;
            return new(id, method, tool, arguments, isError, null);
        }
        catch
        {
            return new(null, null, null, null, false, null);
        }
    }
}

internal sealed class TelemetryWriter
{
    private readonly string _logRoot;
    private readonly string _instanceSuffix;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly int _configuredPreviewChars;

    public TelemetryWriter(string logRoot, int configuredPreviewChars)
    {
        _logRoot = logRoot;
        _instanceSuffix = Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _configuredPreviewChars = Math.Max(0, configuredPreviewChars);
        Directory.CreateDirectory(_logRoot);
    }

    public Task WriteSessionAsync(Dictionary<string, object?> payload)
    {
        return WriteJsonLineAsync("sessions.jsonl", payload);
    }

    public Task WriteMessageAsync(
        string fileName,
        string sessionId,
        string direction,
        string line,
        MessageMetadata metadata,
        long? elapsedMs,
        int previewChars)
    {
        int effectivePreview = Math.Max(_configuredPreviewChars, previewChars);
        Dictionary<string, object?> payload = BasePayload(sessionId);
        payload["direction"] = direction;
        payload["jsonRpcId"] = metadata.Id;
        payload["method"] = metadata.Method;
        payload["tool"] = metadata.Tool;
        payload["arguments"] = metadata.Arguments;
        payload["elapsedMs"] = elapsedMs;
        payload["requestBytes"] = metadata.RequestBytes;
        payload["messageBytes"] = Encoding.UTF8.GetByteCount(line);
        payload["isError"] = metadata.IsError;
        if (effectivePreview > 0)
        {
            payload["payloadPreview"] = line.Length <= effectivePreview ? line : line[..effectivePreview];
        }

        return WriteJsonLineAsync($"{fileName}.jsonl", payload);
    }

    public Task WriteStderrAsync(string sessionId, string line)
    {
        Dictionary<string, object?> payload = BasePayload(sessionId);
        payload["stream"] = "stderr";
        payload["message"] = line;
        return WriteJsonLineAsync("stderr.jsonl", payload);
    }

    public Task WriteErrorAsync(string sessionId, string eventName, string message)
    {
        Dictionary<string, object?> payload = BasePayload(sessionId);
        payload["event"] = eventName;
        payload["message"] = message;
        return WriteJsonLineAsync("errors.jsonl", payload);
    }

    private Dictionary<string, object?> BasePayload(string sessionId)
    {
        return new Dictionary<string, object?>
        {
            ["timestampUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["sessionId"] = sessionId,
            ["server"] = "roslyn-codelens",
            ["processId"] = Environment.ProcessId,
            ["processPath"] = Environment.ProcessPath ?? string.Empty
        };
    }

    private async Task WriteJsonLineAsync(string fileName, Dictionary<string, object?> payload)
    {
        string path = Path.Combine(_logRoot, GetInstanceFileName(fileName));
        string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });
        await _writeGate.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(path, json + Environment.NewLine, new UTF8Encoding(false));
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private string GetInstanceFileName(string fileName)
    {
        string extension = Path.GetExtension(fileName);
        string stem = Path.GetFileNameWithoutExtension(fileName);
        return string.IsNullOrWhiteSpace(extension)
            ? $"{stem}.{_instanceSuffix}"
            : $"{stem}.{_instanceSuffix}{extension}";
    }
}

internal sealed class ProxySettings
{
    public string? ServerCommand { get; init; }
    public string? LogRoot { get; init; }
    public int CapturePayloadPreviewChars { get; init; }
    public bool? TrimFrameworkMetadataInterfaces { get; init; }

    public static ProxySettings Load(string baseDirectory)
    {
        string templatePath = Path.Combine(baseDirectory, "codelens-proxy.settings.template.json");
        string localPath = Path.Combine(baseDirectory, "codelens-proxy.settings.json");
        ProxySettings template = ReadSettings(templatePath);
        ProxySettings local = ReadSettings(localPath);

        return new ProxySettings
        {
            ServerCommand = FirstNonWhiteSpace(local.ServerCommand, template.ServerCommand),
            LogRoot = FirstNonWhiteSpace(local.LogRoot, template.LogRoot),
            CapturePayloadPreviewChars = local.CapturePayloadPreviewChars > 0
                ? local.CapturePayloadPreviewChars
                : template.CapturePayloadPreviewChars,
            TrimFrameworkMetadataInterfaces = local.TrimFrameworkMetadataInterfaces
                ?? template.TrimFrameworkMetadataInterfaces
                ?? true
        };
    }

    private static ProxySettings ReadSettings(string path)
    {
        if (!File.Exists(path))
        {
            return new ProxySettings();
        }

        try
        {
            return JsonSerializer.Deserialize<ProxySettings>(File.ReadAllText(path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new ProxySettings();
        }
        catch
        {
            return new ProxySettings();
        }
    }

    private static string? FirstNonWhiteSpace(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}

internal sealed class ProxyOptions
{
    public string? SolutionPath { get; private init; }
    public string? ServerCommand { get; private init; }
    public string? LogRoot { get; private init; }
    public bool SelfCheck { get; private init; }
    public bool ShowHelp { get; private init; }

    public static ProxyOptions Parse(string[] args)
    {
        string? solutionPath = null;
        string? serverCommand = null;
        string? logRoot = null;
        bool selfCheck = false;
        bool showHelp = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is "--help" or "-h" or "/?")
            {
                showHelp = true;
                continue;
            }

            if (string.Equals(arg, "--self-check", StringComparison.OrdinalIgnoreCase))
            {
                selfCheck = true;
                continue;
            }

            if (string.Equals(arg, "--server-command", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                serverCommand = args[++i];
                continue;
            }

            if (arg.StartsWith("--server-command=", StringComparison.OrdinalIgnoreCase))
            {
                serverCommand = arg["--server-command=".Length..];
                continue;
            }

            if (string.Equals(arg, "--log-root", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                logRoot = args[++i];
                continue;
            }

            if (arg.StartsWith("--log-root=", StringComparison.OrdinalIgnoreCase))
            {
                logRoot = arg["--log-root=".Length..];
                continue;
            }

            solutionPath ??= arg;
        }

        return new ProxyOptions
        {
            SolutionPath = solutionPath,
            ServerCommand = serverCommand,
            LogRoot = logRoot,
            SelfCheck = selfCheck,
            ShowHelp = showHelp
        };
    }
}

internal static class ToolResolver
{
    public static string ResolveServerCommand(string? optionCommand, string? configuredCommand)
    {
        string? configured = FirstNonWhiteSpace(optionCommand, configuredCommand);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        foreach (string localCandidate in GetLocalRoslynCodeLensCandidates())
        {
            if (File.Exists(localCandidate))
            {
                return localCandidate;
            }
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string globalToolPath = Path.Combine(userProfile, ".dotnet", "tools", "roslyn-codelens-mcp.exe");
        if (File.Exists(globalToolPath))
        {
            return globalToolPath;
        }

        string? pathCandidate = FindOnPath("roslyn-codelens-mcp.exe");
        return pathCandidate ?? "roslyn-codelens-mcp";
    }

    private static IEnumerable<string> GetLocalRoslynCodeLensCandidates()
    {
        const string toolExe = "roslyn-codelens-mcp.exe";
        const string localToolPath = @"Tools\RoslynCodeLens\roslyn-codelens-mcp.exe";

        yield return Path.Combine(AppContext.BaseDirectory, toolExe);
        yield return Path.Combine(AppContext.BaseDirectory, "RoslynCodeLens", toolExe);
        yield return Path.Combine(AppContext.BaseDirectory, localToolPath);

        DirectoryInfo? current = new(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && current is not null; i++)
        {
            yield return Path.Combine(current.FullName, localToolPath);
            yield return Path.Combine(current.FullName, "MonitorBaseClaude", localToolPath);
            current = current.Parent;
        }
    }

    public static bool PathContainsDotnetTools()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string globalToolDir = Path.Combine(userProfile, ".dotnet", "tools");
        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator)
            .Any(path => string.Equals(path.Trim(), globalToolDir, StringComparison.OrdinalIgnoreCase));
    }

    public static IEnumerable<string> GetDotnetToolListLines()
    {
        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = "dotnet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("tool");
            startInfo.ArgumentList.Add("list");
            startInfo.ArgumentList.Add("-g");

            using Process process = Process.Start(startInfo)!;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            return output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains("roslyn", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("Package Id", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("---", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
        catch (Exception ex)
        {
            return [$"(unable to run dotnet tool list -g: {ex.Message})"];
        }
    }

    private static string? FindOnPath(string fileName)
    {
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            string candidate = Path.Combine(directory.Trim(), fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? FirstNonWhiteSpace(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
