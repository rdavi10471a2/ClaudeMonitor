using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using MonitorBaseClaude.AI;

namespace MonitorBaseClaude;

[AIFileContext("McpProxyHubService.cs", "WinForms-owned MCP stdio relay hub that keeps external MCP traffic observable through the dashboard process.")]
[FileVersion("1.0")]
public sealed class McpProxyHubService : IDisposable
{
    public const string PipeName = "MonitorBaseClaude.McpProxyHub";
    public static event EventHandler<McpHubTelemetryRecord>? TelemetryRecorded;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly MonitorClientSettings settings;
    private readonly Control? uiOwner;
    private readonly CancellationTokenSource shutdown = new();
    private readonly Task acceptLoop;

    public McpProxyHubService(MonitorClientSettings settings, Control? uiOwner = null)
    {
        this.settings = settings;
        this.uiOwner = uiOwner;
        acceptLoop = Task.Run(() => AcceptLoopAsync(shutdown.Token));
    }

    public void Dispose()
    {
        shutdown.Cancel();
        try
        {
            acceptLoop.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Shutdown is best-effort; active MCP clients may already be closing stdio.
        }

        shutdown.Dispose();
    }

    internal static void PublishTelemetry(object sender, McpHubTelemetryRecord record)
    {
        TelemetryRecorded?.Invoke(sender, record);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream pipe = new(
                PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken);
                _ = Task.Run(() => HandleClientAsync(pipe, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync();
                break;
            }
            catch
            {
                await pipe.DisposeAsync();
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        await using (pipe)
        {
        using StreamReader clientReader = new(pipe, Encoding.UTF8, leaveOpen: true);
        await using StreamWriter clientWriter = new(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

        string? handshakeLine = await clientReader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(handshakeLine))
        {
            return;
        }

        if (IsHostRequest(handshakeLine))
        {
            await HandleHostRequestAsync(handshakeLine, clientWriter, cancellationToken);
            return;
        }

        HubHandshake handshake = HubHandshake.Parse(handshakeLine);
        McpServerLaunch launch = ResolveLaunch(handshake);
        HubTelemetryWriter telemetry = new(ResolveLogRoot(launch.ServerKey), launch.ServerDisplayName, Environment.ProcessId, Environment.ProcessPath ?? string.Empty);
        string sessionId = $"{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}-{Environment.ProcessId}-{Guid.NewGuid():N}";

        telemetry.WriteSession(new Dictionary<string, object?>
        {
            ["event"] = "session_start",
            ["sessionId"] = sessionId,
            ["server"] = launch.ServerDisplayName,
            ["hubProcessId"] = Environment.ProcessId,
            ["hubProcessPath"] = Environment.ProcessPath ?? string.Empty,
            ["childCommand"] = launch.FileName,
            ["childArguments"] = launch.Arguments,
            ["workingDirectory"] = launch.WorkingDirectory
        });

        using Process child = StartChild(launch);
        ConcurrentDictionary<string, PendingHubRequest> pending = new(StringComparer.Ordinal);

        telemetry.WriteRequest(new Dictionary<string, object?>
        {
            ["direction"] = "request",
            ["method"] = "server/start",
            ["tool"] = launch.ServerDisplayName,
            ["processId"] = child.Id,
            ["processPath"] = launch.FileName,
            ["arguments"] = new
            {
                launch.WorkingDirectory,
                launch.Arguments
            }
        });

        try
        {
            Task clientToChild = PumpClientToChildAsync(clientReader, child, telemetry, pending, cancellationToken);
            Task childToClient = PumpChildToClientAsync(child, clientWriter, telemetry, pending, launch.ServerKey, cancellationToken);
            Task stderr = PumpChildStderrAsync(child, telemetry, cancellationToken);
            Task exit = child.WaitForExitAsync(cancellationToken);

            await Task.WhenAny(clientToChild, childToClient, exit);
            if (!child.HasExited)
            {
                child.StandardInput.Close();
                Task exitAfterClose = child.WaitForExitAsync(cancellationToken);
                Task completed = await Task.WhenAny(exitAfterClose, Task.Delay(750, cancellationToken));
                if (!ReferenceEquals(completed, exitAfterClose))
                {
                    child.Kill(entireProcessTree: true);
                }
            }

            await Task.WhenAll(Swallow(clientToChild), Swallow(childToClient), Swallow(stderr));
        }
        catch (Exception ex)
        {
            telemetry.WriteError("hub-failure", ex.Message);
            throw;
        }
        finally
        {
            telemetry.WriteSession(new Dictionary<string, object?>
            {
                ["event"] = "session_end",
                ["sessionId"] = sessionId,
                ["server"] = launch.ServerDisplayName,
                ["childExitCode"] = child.HasExited ? child.ExitCode : null
            });
        }
        }
    }

    private static bool IsHostRequest(string line)
    {
        try
        {
            JsonObject obj = JsonNode.Parse(line)?.AsObject() ?? [];
            return string.Equals(obj["kind"]?.GetValue<string>(), "hostRequest", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private async Task HandleHostRequestAsync(string requestLine, StreamWriter clientWriter, CancellationToken cancellationToken)
    {
        JsonObject request = JsonNode.Parse(requestLine)?.AsObject()
            ?? throw new InvalidOperationException("Invalid host request.");
        string requestType = request["requestType"]?.GetValue<string>() ?? string.Empty;

        JsonObject response = requestType.Equals("overlayValidationReview", StringComparison.OrdinalIgnoreCase)
            ? ShowOverlayValidationReviewDialog(request)
            : new JsonObject
            {
                ["status"] = "unsupported-request",
                ["decision"] = "cancel_for_fix",
                ["message"] = $"Unsupported host request: {requestType}"
            };

        await clientWriter.WriteLineAsync(response.ToJsonString(JsonOptions));
    }

    private JsonObject ShowOverlayValidationReviewDialog(JsonObject request)
    {
        string recordId = request["stagedRecordId"]?.GetValue<string>() ?? "(unknown staged record)";
        string relativePath = request["relativeSourcePath"]?.GetValue<string>() ?? "(unknown source)";
        int diagnosticCount = request["diagnosticCount"]?.GetValue<int?>() ?? 0;
        string diagnostics = request["diagnostics"]?.GetValue<string>() ?? string.Empty;
        string message =
            $"Overlay compile validation reported {diagnosticCount} error(s) for:\r\n\r\n" +
            $"{relativePath}\r\n\r\n" +
            $"{diagnostics}\r\n\r\n" +
            "Open WinMerge anyway?\r\n\r\n" +
            "Choose Yes only when you intentionally want Operator review of a compile-failed candidate. Choose No to return the diagnostics to the agent for a fix.";

        DialogResult result = ShowOverlayValidationDialogOnUiThread(
            message,
            $"Overlay validation failed: {recordId}");

        bool forceReview = result == DialogResult.OK;
        return new JsonObject
        {
            ["status"] = "completed",
            ["decision"] = forceReview ? "force_review" : "cancel_for_fix",
            ["message"] = forceReview
                ? "Operator chose to force WinMerge review despite overlay compile errors."
                : "Operator cancelled review so the agent can fix overlay compile errors first."
        };
    }

    private DialogResult ShowOverlayValidationDialogOnUiThread(string message, string caption)
    {
        if (uiOwner is not null && !uiOwner.IsDisposed)
        {
            if (uiOwner.InvokeRequired)
            {
                return (DialogResult)uiOwner.Invoke(() => ShowOverlayValidationDialog(uiOwner, message, caption));
            }

            return ShowOverlayValidationDialog(uiOwner, message, caption);
        }

        return ShowOverlayValidationDialog(null, message, caption);
    }

    private static DialogResult ShowOverlayValidationDialog(IWin32Window? owner, string message, string caption)
    {
        using Form dialog = new()
        {
            Text = caption,
            StartPosition = owner is null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = owner is null,
            ClientSize = new Size(620, 360)
        };

        Label icon = new()
        {
            AutoSize = false,
            Location = new Point(18, 22),
            Size = new Size(36, 36),
            Text = "!",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(FontFamily.GenericSansSerif, 18, FontStyle.Bold),
            ForeColor = Color.DarkGoldenrod
        };

        TextBox textBox = new()
        {
            BorderStyle = BorderStyle.None,
            Location = new Point(68, 22),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Size = new Size(530, 255),
            Text = message,
            BackColor = SystemColors.Control,
            Font = SystemFonts.MessageBoxFont
        };

        Button cancelButton = new()
        {
            Text = "Cancel Review",
            DialogResult = DialogResult.Cancel,
            Size = new Size(140, 32),
            Location = new Point(308, 306)
        };

        Button forceButton = new()
        {
            Text = "Force WinMerge Review",
            DialogResult = DialogResult.OK,
            Size = new Size(170, 32),
            Location = new Point(458, 306)
        };

        dialog.Controls.Add(icon);
        dialog.Controls.Add(textBox);
        dialog.Controls.Add(cancelButton);
        dialog.Controls.Add(forceButton);
        dialog.AcceptButton = cancelButton;
        dialog.CancelButton = cancelButton;

        return owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
    }

    private static async Task PumpClientToChildAsync(
        StreamReader clientReader,
        Process child,
        HubTelemetryWriter telemetry,
        ConcurrentDictionary<string, PendingHubRequest> pending,
        CancellationToken cancellationToken)
    {
        while (await clientReader.ReadLineAsync(cancellationToken) is { } line)
        {
            HubMessageMetadata metadata = HubMessageMetadata.FromJsonLine(line);
            if (!string.IsNullOrWhiteSpace(metadata.Id))
            {
                pending[metadata.Id] = new PendingHubRequest(DateTimeOffset.UtcNow, metadata.Method, metadata.Tool, metadata.Arguments, Encoding.UTF8.GetByteCount(line));
            }

            telemetry.WriteRequest(new Dictionary<string, object?>
            {
                ["direction"] = "request",
                ["jsonRpcId"] = metadata.Id,
                ["method"] = metadata.Method,
                ["tool"] = metadata.Tool,
                ["arguments"] = metadata.Arguments,
                ["messageBytes"] = Encoding.UTF8.GetByteCount(line),
                ["isError"] = metadata.IsError
            });
            await child.StandardInput.WriteLineAsync(line);
            await child.StandardInput.FlushAsync(cancellationToken);
        }
    }

    private static async Task PumpChildToClientAsync(
        Process child,
        StreamWriter clientWriter,
        HubTelemetryWriter telemetry,
        ConcurrentDictionary<string, PendingHubRequest> pending,
        string serverKey,
        CancellationToken cancellationToken)
    {
        while (await child.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
        {
            HubMessageMetadata metadata = HubMessageMetadata.FromJsonLine(line);
            long? elapsedMs = null;
            JsonNode? arguments = metadata.Arguments;
            long? requestBytes = null;
            if (!string.IsNullOrWhiteSpace(metadata.Id)
                && pending.TryRemove(metadata.Id, out PendingHubRequest? request))
            {
                elapsedMs = (long)(DateTimeOffset.UtcNow - request.StartedUtc).TotalMilliseconds;
                metadata = metadata with
                {
                    Method = metadata.Method ?? request.Method,
                    Tool = metadata.Tool ?? request.Tool
                };
                arguments ??= request.Arguments;
                requestBytes = request.RequestBytes;
            }

            string outgoingLine = EnrichResponseForClient(serverKey, metadata, line);

            telemetry.WriteResponse(new Dictionary<string, object?>
            {
                ["direction"] = "response",
                ["jsonRpcId"] = metadata.Id,
                ["method"] = metadata.Method,
                ["tool"] = metadata.Tool,
                ["arguments"] = arguments,
                ["elapsedMs"] = elapsedMs,
                ["requestBytes"] = requestBytes,
                ["messageBytes"] = Encoding.UTF8.GetByteCount(outgoingLine),
                ["isError"] = metadata.IsError
            });
            await clientWriter.WriteLineAsync(outgoingLine);
        }
    }

    private static string EnrichResponseForClient(string serverKey, HubMessageMetadata metadata, string line)
    {
        if (!serverKey.Equals("roslyn", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(metadata.Method, "tools/list", StringComparison.Ordinal))
        {
            return line;
        }

        try
        {
            JsonObject root = JsonNode.Parse(line)?.AsObject() ?? [];
            if (root["result"]?["tools"] is not JsonArray tools)
            {
                return line;
            }

            foreach (JsonNode? node in tools)
            {
                if (node is JsonObject tool)
                {
                    EnrichRoslynToolDescription(tool);
                }
            }

            return root.ToJsonString(JsonOptions);
        }
        catch
        {
            return line;
        }
    }

    private static void EnrichRoslynToolDescription(JsonObject tool)
    {
        string? name = tool["name"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        string? hint = GetRoslynToolHint(name);
        if (string.IsNullOrWhiteSpace(hint))
        {
            return;
        }

        string existing = tool["description"]?.GetValue<string>() ?? string.Empty;
        if (existing.Contains("MonitorBaseClaude workflow hint:", StringComparison.Ordinal))
        {
            return;
        }

        tool["description"] = string.IsNullOrWhiteSpace(existing)
            ? hint
            : $"{existing.TrimEnd()} MonitorBaseClaude workflow hint: {hint}";
    }

    private static string? GetRoslynToolHint(string toolName)
    {
        return toolName switch
        {
            "search_symbols" => "Entry point for C# symbol discovery; prefer over text/grep search. Argument is query. Next: get_type_overview, then callers/references/impact.",
            "get_type_overview" => "Confirms namespace, members, file, and diagnostics before deeper calls; disambiguates search results. Argument is typeName.",
            "find_references" => "Compiler-backed usages for a type/member. Argument is symbol, e.g. DatabaseRepository.GetAll; not symbolName or query. Do not edit until references are confirmed; edits go through System Monitor only.",
            "find_callers" => "Compiler-backed call sites for a callable symbol. Argument is symbol, usually Type.Method. For async propagation, recurse up the caller chain because callers may also need conversion.",
            "find_implementations" => "Find implementations and derived types. Argument is symbol. Run before changing any method that may be declared on an interface, even from a concrete type.",
            "analyze_change_impact" => "Run before any signature/API change. Argument is symbol. Results must drive System Monitor staging; do not edit watched source directly.",
            "get_call_graph" => "Use when caller/callee depth matters. Argument is symbol; tune direction/maxDepth/maxNodes to keep output small.",
            "get_diagnostics" => "Returns compiler/analyzer errors and warnings. Run after staged edits compile to confirm zero new errors before operator review.",
            "get_code_fixes" => "Structured fix options for a diagnostic. Fixes are advisory; convert watched-source changes into System Monitor staged candidates and do not apply directly.",
            _ => null
        };
    }

    private static async Task PumpChildStderrAsync(Process child, HubTelemetryWriter telemetry, CancellationToken cancellationToken)
    {
        while (await child.StandardError.ReadLineAsync(cancellationToken) is { } line)
        {
            telemetry.WriteStderr(line);
        }
    }

    private static Process StartChild(McpServerLaunch launch)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = launch.FileName,
            WorkingDirectory = launch.WorkingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (string argument in launch.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (launch.ServerKey.Equals("monitor", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.Environment["MONITORBASECLAUDE_DISABLE_SERVER_TELEMETRY"] = "1";
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start MCP server: {launch.FileName}");
    }

    private McpServerLaunch ResolveLaunch(HubHandshake handshake)
    {
        if (handshake.Server.Equals("monitor", StringComparison.OrdinalIgnoreCase))
        {
            string exe = Path.Combine(settings.MonitorMcpServerRoot, "bin", "Debug", "net10.0", "MonitorBaseClaude.McpServer.exe");
            return new McpServerLaunch("monitor", "MonitorBaseClaude", exe, settings.MonitorMcpServerRoot, []);
        }

        if (handshake.Server.Equals("roslyn", StringComparison.OrdinalIgnoreCase)
            || handshake.Server.Equals("roslyn-codelens", StringComparison.OrdinalIgnoreCase))
        {
            string solutionPath = FirstNonWhiteSpace(handshake.SolutionPath, settings.CodeLensSolutionPath)
                ?? throw new InvalidOperationException("Roslyn hub launch requires a solution path.");
            string? resolvedSolution = RoslynCodeLensMcpClientService.ResolveSolutionPath(solutionPath)
                ?? throw new FileNotFoundException("Roslyn solution path not found.", solutionPath);
            string command = FirstNonWhiteSpace(handshake.ServerCommand, FindRoslynCodeLensCommand()) ?? "roslyn-codelens-mcp";
            return new McpServerLaunch("roslyn", "roslyn-codelens", command, Path.GetDirectoryName(resolvedSolution) ?? Environment.CurrentDirectory, [resolvedSolution]);
        }

        throw new InvalidOperationException($"Unknown MCP hub server: {handshake.Server}");
    }

    private string ResolveLogRoot(string serverKey)
    {
        string folder = serverKey.Equals("monitor", StringComparison.OrdinalIgnoreCase)
            ? "MonitorBaseClaude"
            : "RoslynCodeLens";
        return Path.Combine(settings.UiRoot, "Working", "History", "McpTelemetry", folder);
    }

    private static string? FindRoslynCodeLensCommand()
    {
        foreach (string localCandidate in GetLocalRoslynCodeLensCandidates())
        {
            if (File.Exists(localCandidate))
            {
                return localCandidate;
            }
        }

        string? userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            string globalTool = Path.Combine(userProfile, ".dotnet", "tools", "roslyn-codelens-mcp.exe");
            if (File.Exists(globalTool))
            {
                return globalTool;
            }
        }

        return "roslyn-codelens-mcp";
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

    private static async Task Swallow(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
            // Paired stdio streams commonly close in either order.
        }
    }

    private static string? FirstNonWhiteSpace(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}

public sealed record McpHubTelemetryRecord(string LogRoot, string FileName, JsonObject Entry);

internal sealed record HubHandshake(string Server, string? SolutionPath, string? ServerCommand)
{
    public static HubHandshake Parse(string line)
    {
        JsonObject obj = JsonNode.Parse(line)?.AsObject()
            ?? throw new InvalidOperationException("Invalid MCP hub handshake.");
        string server = obj["server"]?.GetValue<string>() ?? throw new InvalidOperationException("MCP hub handshake is missing server.");
        return new HubHandshake(server, obj["solutionPath"]?.GetValue<string>(), obj["serverCommand"]?.GetValue<string>());
    }
}

internal sealed record McpServerLaunch(
    string ServerKey,
    string ServerDisplayName,
    string FileName,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments);

internal sealed record PendingHubRequest(
    DateTimeOffset StartedUtc,
    string? Method,
    string? Tool,
    JsonNode? Arguments,
    long RequestBytes);

internal sealed record HubMessageMetadata(
    string? Id,
    string? Method,
    string? Tool,
    JsonNode? Arguments,
    bool IsError)
{
    public static HubMessageMetadata FromJsonLine(string line)
    {
        try
        {
            JsonObject obj = JsonNode.Parse(line)?.AsObject() ?? [];
            string? id = obj["id"]?.ToString();
            string? method = obj["method"]?.GetValue<string>();
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
            return new HubMessageMetadata(id, method, tool, arguments, isError);
        }
        catch
        {
            return new HubMessageMetadata(null, null, null, null, false);
        }
    }
}

internal sealed class HubTelemetryWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly string logRoot;
    private readonly string server;
    private readonly int hubProcessId;
    private readonly string hubProcessPath;

    public HubTelemetryWriter(string logRoot, string server, int hubProcessId, string hubProcessPath)
    {
        this.logRoot = logRoot;
        this.server = server;
        this.hubProcessId = hubProcessId;
        this.hubProcessPath = hubProcessPath;
        Directory.CreateDirectory(logRoot);
    }

    public void WriteSession(Dictionary<string, object?> payload) => WriteJsonLine("sessions.jsonl", payload);
    public void WriteRequest(Dictionary<string, object?> payload) => WriteJsonLine("requests.jsonl", payload);
    public void WriteResponse(Dictionary<string, object?> payload) => WriteJsonLine("responses.jsonl", payload);
    public void WriteError(string eventName, string message) => WriteJsonLine("errors.jsonl", BasePayload(new Dictionary<string, object?>
    {
        ["event"] = eventName,
        ["message"] = message
    }));
    public void WriteStderr(string message) => WriteJsonLine("stderr.jsonl", BasePayload(new Dictionary<string, object?>
    {
        ["stream"] = "stderr",
        ["message"] = message
    }));

    private void WriteJsonLine(string fileName, Dictionary<string, object?> payload)
    {
        string path = Path.Combine(logRoot, fileName);
        Dictionary<string, object?> entry = BasePayload(payload);
        File.AppendAllText(path, JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine, new UTF8Encoding(false));
        if (JsonSerializer.SerializeToNode(entry, JsonOptions) is JsonObject obj)
        {
            McpProxyHubService.PublishTelemetry(this, new McpHubTelemetryRecord(logRoot, fileName, obj));
        }
    }

    private Dictionary<string, object?> BasePayload(Dictionary<string, object?> payload)
    {
        payload.TryAdd("timestampUtc", DateTimeOffset.UtcNow.ToString("O"));
        payload.TryAdd("server", server);
        payload.TryAdd("hubProcessId", hubProcessId);
        payload.TryAdd("hubProcessPath", hubProcessPath);
        return payload;
    }
}
