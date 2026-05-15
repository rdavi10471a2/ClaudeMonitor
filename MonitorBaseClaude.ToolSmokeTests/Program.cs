using System.Text.Json;
using System.Text.Json.Nodes;
using MonitorBaseClaude;

namespace MonitorBaseClaude.ToolSmokeTests;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static async Task<int> Main(string[] args)
    {
        bool pause = args.Contains("--pause", StringComparer.OrdinalIgnoreCase);
        bool scriptedMode = args.Contains("--scripted", StringComparer.OrdinalIgnoreCase);
        string? modelOverride = ReadOption(args, "--model");

        MonitorClientSettings settings = MonitorClientSettings.Load();
        using MonitorMcpClientService monitorClient = new(settings);
        using OllamaToolExplorerService ollama = new(settings);
        LocalMcpDiscoveryService discovery = new(settings);

        string model = modelOverride ?? settings.OllamaModel;
        string runRoot = Path.Combine(settings.UiRoot, "Working", "History", "ToolSmokeTests", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(runRoot);

        if (scriptedMode)
        {
            return await RunScriptedSmokeAsync(settings, monitorClient, runRoot, pause);
        }

        SmokeQuestion[] questions =
        [
            new("Discover tools", "What MCP Server tools are available in this Host session?"),
            new("Find Program.cs", "Find file Program.cs in the watched project."),
            new("Read Program.cs", "Read Program.cs from the watched project and summarize what application starts."),
            new("Workflow status", "Inspect the current monitor workflow status and tell me whether WinMerge is available."),
            new("Start session", "Start a Monitor Server session for investigating Program.cs."),
            new("List sessions", "List Monitor Server sessions and identify the most recent session."),
            new("No tool needed", "Explain in one sentence what a Monitor Server session handle is.")
        ];

        Console.WriteLine($"MonitorBaseClaude tool smoke test");
        Console.WriteLine("Mode: llm");
        Console.WriteLine($"Model: {model}");
        Console.WriteLine($"Log root: {runRoot}");
        Console.WriteLine();

        List<SmokeResult> results = [];
        IReadOnlyList<LocalMcpServerSurface> localServers = await discovery.DiscoverAsync();

        for (int i = 0; i < questions.Length; i++)
        {
            SmokeQuestion question = questions[i];
            Console.WriteLine($"[{i + 1}/{questions.Length}] {question.Name}");
            Console.WriteLine(question.Text);

            SmokeResult result = await RunQuestionAsync(question, model, localServers, monitorClient, ollama);
            results.Add(result);

            string fileName = $"{i + 1:00}_{SanitizeFileName(question.Name)}.json";
            string logPath = Path.Combine(runRoot, fileName);
            await File.WriteAllTextAsync(logPath, JsonSerializer.Serialize(result, JsonOptions));

            Console.WriteLine($"Decision: {result.Decision.Action} {result.Decision.Server} {result.Decision.Tool}");
            Console.WriteLine($"Tool error: {result.ToolResult?.IsError.ToString() ?? "(no tool)"}");
            Console.WriteLine("Answer:");
            Console.WriteLine(Indent(NormalizeNewlines(result.FinalAnswer ?? result.Decision.Answer ?? "(no answer)"), "  "));
            Console.WriteLine($"Trace: {logPath}");
            Console.WriteLine(new string('-', 80));

            if (pause && i < questions.Length - 1)
            {
                Console.WriteLine("Press Enter for next smoke question...");
                Console.ReadLine();
            }
        }

        string reportPath = Path.Combine(runRoot, "summary.md");
        await File.WriteAllTextAsync(reportPath, BuildMarkdownReport(results));
        Console.WriteLine($"Summary report: {reportPath}");
        return results.Any(result => result.Decision.Action.Equals("error", StringComparison.OrdinalIgnoreCase)
            || result.ToolResult?.IsError == true)
            ? 1
            : 0;
    }

    private static async Task<int> RunScriptedSmokeAsync(
        MonitorClientSettings settings,
        MonitorMcpClientService monitorClient,
        string runRoot,
        bool pause)
    {
        Console.WriteLine("MonitorBaseClaude tool smoke test");
        Console.WriteLine("Mode: scripted MCP Server calls");
        Console.WriteLine($"Log root: {runRoot}");
        Console.WriteLine();

        List<ScriptedSmokeResult> results = [];
        string? sessionId = null;

        ScriptedSmokeStep[] staticSteps =
        [
            new("Status", "get_monitor_status", null, "Verify configured roots and watched solution."),
            new("Self Check", "get_self_check", null, "Verify guardrails, Working paths, and diff tool discovery."),
            new("Workflow", "get_workflow_status", null, "Verify Working folder and WinMerge discovery."),
            new("List Monitor Runs", "list_monitor_runs", new Dictionary<string, object?> { ["maxEntries"] = 10 }, "List monitor-owned run history entries."),
            new("List Ledgers", "list_ledgers", new Dictionary<string, object?> { ["maxEntries"] = 10 }, "List monitor-owned per-file ledgers."),
            new("Find Program.cs", "find_file", new Dictionary<string, object?> { ["fileNameOrPattern"] = "Program.cs", ["maxResults"] = 10 }, "Find Program.cs under watched project."),
            new("Read Program.cs", "get_file", new Dictionary<string, object?> { ["sourceFilePath"] = "Program.cs" }, "Read full Program.cs through the Server."),
            new("Outline Program.cs", "get_file_outline", new Dictionary<string, object?> { ["path"] = "Program.cs" }, "Read symbol outline for Program.cs."),
            new("Get Main Symbol", "get_symbol", new Dictionary<string, object?> { ["path"] = "Program.cs", ["symbolName"] = "Main" }, "Read only Program.Main from Program.cs."),
            new("Get Program Ledger", "get_ledger", new Dictionary<string, object?> { ["sourceFilePath"] = "Program.cs" }, "Read Program.cs ledger if present."),
            new("Stage Program.cs", "submit_file", new Dictionary<string, object?> { ["path"] = "Program.cs", ["content"] = File.ReadAllText(Path.Combine(Path.GetDirectoryName(settings.WatchedSolutionPath) ?? string.Empty, "Program.cs")), ["launchDiff"] = false }, "Stage a full-file replacement identical to Program.cs without touching source."),
            new("Start Session", "start_monitor_session", new Dictionary<string, object?> { ["purpose"] = "scripted smoke test for Program.cs" }, "Create durable session handle.")
        ];

        int index = 0;
        foreach (ScriptedSmokeStep step in staticSteps)
        {
            index++;
            ScriptedSmokeResult result = await RunScriptedStepAsync(index, step, monitorClient, runRoot);
            results.Add(result);
            sessionId ??= ExtractSessionId(result.ToolResult.ResponseJson);
            WriteScriptedSummary(result);
            MaybePause(pause);
        }

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            ScriptedSmokeStep[] sessionSteps =
            [
                new("Read Program.cs With Session", "get_file", new Dictionary<string, object?> { ["sourceFilePath"] = "Program.cs", ["sessionId"] = sessionId }, "Read Program.cs and record hash in session."),
                new("Check Program.cs Hash", "check_file_hash", new Dictionary<string, object?> { ["sourceFilePath"] = "Program.cs", ["sessionId"] = sessionId }, "Verify whether Program.cs changed since session fetch."),
                new("List Sessions", "list_monitor_sessions", null, "Verify the created session appears in durable session list.")
            ];

            foreach (ScriptedSmokeStep step in sessionSteps)
            {
                index++;
                ScriptedSmokeResult result = await RunScriptedStepAsync(index, step, monitorClient, runRoot);
                results.Add(result);
                WriteScriptedSummary(result);
                MaybePause(pause);
            }
        }

        string reportPath = Path.Combine(runRoot, "scripted-summary.md");
        await File.WriteAllTextAsync(reportPath, BuildScriptedMarkdownReport(results));
        Console.WriteLine($"Summary report: {reportPath}");
        return results.Any(result => result.ToolResult.IsError) ? 1 : 0;
    }

    private static async Task<ScriptedSmokeResult> RunScriptedStepAsync(
        int index,
        ScriptedSmokeStep step,
        MonitorMcpClientService monitorClient,
        string runRoot)
    {
        DateTimeOffset startedAt = DateTimeOffset.Now;
        MonitorMcpToolCallResult toolResult = await monitorClient.CallToolAsync(step.ToolName, step.Arguments);
        ScriptedSmokeResult result = new(
            step,
            startedAt,
            DateTimeOffset.Now,
            toolResult,
            FormatPayloadForDisplay(toolResult.ResponseJson));

        string logPath = Path.Combine(runRoot, $"{index:00}_{SanitizeFileName(step.Name)}.json");
        await File.WriteAllTextAsync(logPath, JsonSerializer.Serialize(result, JsonOptions));
        return result;
    }

    private static void WriteScriptedSummary(ScriptedSmokeResult result)
    {
        Console.WriteLine($"{result.Step.Name} -> {result.Step.ToolName}");
        Console.WriteLine(result.Step.Question);
        Console.WriteLine($"Error: {result.ToolResult.IsError}");
        Console.WriteLine("Response:");
        Console.WriteLine(Indent(TruncateForConsole(result.ToolResultDisplay, 2400), "  "));
        Console.WriteLine(new string('-', 80));
    }

    private static string BuildScriptedMarkdownReport(IReadOnlyList<ScriptedSmokeResult> results)
    {
        List<string> lines =
        [
            "# Scripted Tool Smoke Test Summary",
            "",
            $"Generated: {DateTimeOffset.Now:O}",
            ""
        ];

        foreach (ScriptedSmokeResult result in results)
        {
            lines.Add($"## {result.Step.Name}");
            lines.Add("");
            lines.Add($"Tool: `{result.Step.ToolName}`");
            lines.Add($"Question: {result.Step.Question}");
            lines.Add($"Error: `{result.ToolResult.IsError}`");
            lines.Add("");
            lines.Add("```text");
            lines.Add(NormalizeNewlines(result.ToolResultDisplay));
            lines.Add("```");
            lines.Add("");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static async Task<SmokeResult> RunQuestionAsync(
        SmokeQuestion question,
        string model,
        IReadOnlyList<LocalMcpServerSurface> localServers,
        MonitorMcpClientService monitorClient,
        OllamaToolExplorerService ollama)
    {
        DateTimeOffset startedAt = DateTimeOffset.Now;
        OllamaActionDecision decision = await ollama.DecideNextActionAsync(localServers, question.Text, model);
        LocalMcpToolCallResult? toolResult = null;
        string? finalAnswer;

        if (decision.Action.Equals("call_tool", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(decision.Tool))
        {
            string serverName = ResolveServerName(localServers, decision);
            LocalMcpDiscoveryService discovery = new(MonitorClientSettings.Load());
            toolResult = await discovery.CallToolAsync(serverName, decision.Tool, decision.Arguments);
            finalAnswer = await ollama.AnswerWithToolResultAsync(question.Text, decision.Tool, toolResult.ResponseJson, model);
        }
        else
        {
            finalAnswer = decision.Answer;
        }

        return new SmokeResult(
            question,
            model,
            startedAt,
            DateTimeOffset.Now,
            localServers,
            decision,
            toolResult,
            FormatPayloadForDisplay(toolResult?.ResponseJson),
            NormalizeNewlines(finalAnswer ?? string.Empty));
    }

    private static string BuildMarkdownReport(IReadOnlyList<SmokeResult> results)
    {
        List<string> lines =
        [
            "# Tool Smoke Test Summary",
            "",
            $"Generated: {DateTimeOffset.Now:O}",
            ""
        ];

        foreach (SmokeResult result in results)
        {
            lines.Add($"## {result.Question.Name}");
            lines.Add("");
            lines.Add($"Question: {result.Question.Text}");
            lines.Add("");
            lines.Add($"Decision: `{result.Decision.Action}` `{result.Decision.Server ?? string.Empty}` `{result.Decision.Tool ?? string.Empty}`");
            lines.Add($"Tool error: `{result.ToolResult?.IsError.ToString() ?? "no tool"}`");
            lines.Add("");
            lines.Add("Answer:");
            lines.Add("");
            lines.Add("```text");
            lines.Add(NormalizeNewlines(result.FinalAnswer));
            lines.Add("```");
            lines.Add("");
        }

        return string.Join(Environment.NewLine, lines);
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

    private static string ResolveServerName(IReadOnlyList<LocalMcpServerSurface> localServers, OllamaActionDecision decision)
    {
        if (!string.IsNullOrWhiteSpace(decision.Server))
        {
            return decision.Server;
        }

        LocalMcpServerSurface? matchingServer = localServers.FirstOrDefault(server =>
            server.Tools.Any(tool => tool.Name.Equals(decision.Tool, StringComparison.OrdinalIgnoreCase)));
        return matchingServer?.Name
            ?? throw new InvalidOperationException($"Could not resolve MCP Server for tool '{decision.Tool}'.");
    }

    private static string FormatPayloadForDisplay(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return string.Empty;
        }

        try
        {
            JsonNode? node = JsonNode.Parse(payload);
            string? text = FindTextValue(node);
            if (!string.IsNullOrEmpty(text))
            {
                return NormalizeNewlines(text);
            }
        }
        catch (JsonException)
        {
        }

        return NormalizeNewlines(payload);
    }

    private static string? FindTextValue(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (string key in new[] { "Text", "text" })
            {
                if (obj.TryGetPropertyValue(key, out JsonNode? value) && value is not null)
                {
                    return value.ToString();
                }
            }

            foreach (KeyValuePair<string, JsonNode?> property in obj)
            {
                string? nested = FindTextValue(property.Value);
                if (!string.IsNullOrEmpty(nested))
                {
                    return nested;
                }
            }
        }

        if (node is JsonArray array)
        {
            foreach (JsonNode? child in array)
            {
                string? nested = FindTextValue(child);
                if (!string.IsNullOrEmpty(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string NormalizeNewlines(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", Environment.NewLine, StringComparison.Ordinal);
    }

    private static string Indent(string text, string prefix)
    {
        return prefix + text.Replace(Environment.NewLine, Environment.NewLine + prefix, StringComparison.Ordinal);
    }

    private static string SanitizeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(c => invalid.Contains(c) ? '_' : c));
    }

    private static string TruncateForConsole(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..maxLength] + Environment.NewLine + "...(truncated for console)";
    }

    private static void MaybePause(bool pause)
    {
        if (!pause)
        {
            return;
        }

        Console.WriteLine("Press Enter for next smoke step...");
        Console.ReadLine();
    }

    private static string? ExtractSessionId(string responseJson)
    {
        try
        {
            JsonNode? node = JsonNode.Parse(responseJson);
            return FindPropertyValue(node, "SessionId") ?? FindPropertyValue(node, "sessionId");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FindPropertyValue(JsonNode? node, string propertyName)
    {
        if (node is JsonObject obj)
        {
            foreach (KeyValuePair<string, JsonNode?> property in obj)
            {
                if (property.Key.Equals(propertyName, StringComparison.OrdinalIgnoreCase) && property.Value is not null)
                {
                    return property.Value.ToString();
                }

                string? nested = FindPropertyValue(property.Value, propertyName);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        if (node is JsonArray array)
        {
            foreach (JsonNode? child in array)
            {
                string? nested = FindPropertyValue(child, propertyName);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }
}

internal sealed record ScriptedSmokeStep(
    string Name,
    string ToolName,
    IReadOnlyDictionary<string, object?>? Arguments,
    string Question);

internal sealed record ScriptedSmokeResult(
    ScriptedSmokeStep Step,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    MonitorMcpToolCallResult ToolResult,
    string ToolResultDisplay);

internal sealed record SmokeQuestion(
    string Name,
    string Text);

internal sealed record SmokeResult(
    SmokeQuestion Question,
    string Model,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    IReadOnlyList<LocalMcpServerSurface> DiscoveredServers,
    OllamaActionDecision Decision,
    LocalMcpToolCallResult? ToolResult,
    string? ToolResultDisplay,
    string FinalAnswer);
