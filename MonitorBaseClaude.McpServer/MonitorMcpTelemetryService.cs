using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace MonitorBaseClaude.McpServer;

public sealed class MonitorMcpTelemetryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly string logRoot;
    private readonly int processId = Environment.ProcessId;
    private readonly string processPath = Environment.ProcessPath ?? string.Empty;

    public MonitorMcpTelemetryService(MonitorServerSettings settings)
    {
        logRoot = Path.Combine(settings.UiRoot, "Working", "History", "McpTelemetry", "MonitorBaseClaude");
        Directory.CreateDirectory(logRoot);
        WriteJsonLine("requests.jsonl", new
        {
            timestampUtc = DateTimeOffset.UtcNow,
            direction = "request",
            method = "server/start",
            tool = "MonitorBaseClaude.McpServer",
            processId,
            processPath,
            arguments = new
            {
                settings.UiRoot,
                settings.McpServerRoot,
                settings.WatchedSolutionPath
            }
        });
    }

    public T Track<T>(string toolName, object? arguments, Func<T> action)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        WriteJsonLine("requests.jsonl", new
        {
            timestampUtc = DateTimeOffset.UtcNow,
            direction = "request",
            method = "tools/call",
            tool = toolName,
            processId,
            processPath,
            arguments
        });

        try
        {
            T result = action();
            stopwatch.Stop();
            string responseJson = JsonSerializer.Serialize(result, JsonOptions);
            WriteJsonLine("responses.jsonl", new
            {
                timestampUtc = DateTimeOffset.UtcNow,
                direction = "response",
                method = "tools/call",
                tool = toolName,
                processId,
                processPath,
                elapsedMs = stopwatch.ElapsedMilliseconds,
                messageBytes = Encoding.UTF8.GetByteCount(responseJson),
                isError = false
            });

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            WriteJsonLine("responses.jsonl", new
            {
                timestampUtc = DateTimeOffset.UtcNow,
                direction = "response",
                method = "tools/call",
                tool = toolName,
                processId,
                processPath,
                elapsedMs = stopwatch.ElapsedMilliseconds,
                messageBytes = Encoding.UTF8.GetByteCount(ex.Message),
                isError = true
            });
            WriteJsonLine("errors.jsonl", new
            {
                timestampUtc = DateTimeOffset.UtcNow,
                eventName = "tool-error",
                eventType = "tool-error",
                @event = "tool-error",
                tool = toolName,
                processId,
                processPath,
                message = ex.Message,
                exceptionType = ex.GetType().FullName
            });
            throw;
        }
    }

    private void WriteJsonLine(string fileName, object entry)
    {
        string path = Path.Combine(logRoot, fileName);
        string line = JsonSerializer.Serialize(entry, JsonOptions);
        File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
    }
}
