using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using MonitorBaseClaude.AI;

namespace MonitorBaseClaude.Controls;

[DesignerCategory("Code")]
[AIFileContext("OllamaToolExplorerControl.cs", "Code-only WinForms Host pane that asks a local LLM to choose actions against the Monitor MCP Server.")]
[FileVersion("2.0")]
public sealed class OllamaToolExplorerControl : UserControl
{
    private readonly MonitorClientSettings settings;
    private readonly MonitorMcpClientService monitorMcpClientService;
    private readonly OllamaToolExplorerService ollamaToolExplorerService;
    private readonly LocalMcpDiscoveryService localMcpDiscoveryService;
    private readonly Label statusLabel = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly TextBox questionBox = new() { Dock = DockStyle.Fill };
    private readonly ListBox scenarioListBox = new() { Dock = DockStyle.Fill };
    private readonly ComboBox modelComboBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
    private readonly RichTextBox responseBox = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Segoe UI", 9.5F)
    };
    private readonly RichTextBox promptBox = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Consolas", 9F)
    };

    public OllamaToolExplorerControl(
        MonitorClientSettings settings,
        MonitorMcpClientService monitorMcpClientService,
        OllamaToolExplorerService ollamaToolExplorerService,
        LocalMcpDiscoveryService localMcpDiscoveryService)
    {
        this.settings = settings;
        this.monitorMcpClientService = monitorMcpClientService;
        this.ollamaToolExplorerService = ollamaToolExplorerService;
        this.localMcpDiscoveryService = localMcpDiscoveryService;
        Dock = DockStyle.Fill;
        BuildLayout(settings);
        Load += async (_, _) => await LoadModelsAsync(settings.OllamaModel);
    }

    private void BuildLayout(MonitorClientSettings settings)
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 5
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Label title = new()
        {
            Dock = DockStyle.Fill,
            Text = $"Local LLM Tool Explorer - Host driving MCP Server tools - configured {settings.OllamaModel}",
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };

        LoadScenarios();
        scenarioListBox.SelectedIndexChanged += (_, _) =>
        {
            if (scenarioListBox.SelectedItem is PromptScenario scenario)
            {
                questionBox.Text = scenario.Prompt;
            }
        };

        questionBox.Multiline = true;

        Button askButton = new()
        {
            Text = "Ask LLM",
            AutoSize = true,
            Height = 32
        };
        askButton.Click += async (_, _) => await AskAsync();

        Button routeOnlyButton = new()
        {
            Text = "Route Only",
            AutoSize = true,
            Height = 32
        };
        routeOnlyButton.Click += async (_, _) => await RouteOnlyAsync();

        Button routerDrillButton = new()
        {
            Text = "Router Drill",
            AutoSize = true,
            Height = 32
        };
        routerDrillButton.Click += async (_, _) => await RouterDrillAsync();

        Button compareButton = new()
        {
            Text = "Compare Tiny Models",
            AutoSize = true,
            Height = 32
        };
        compareButton.Click += async (_, _) => await CompareTinyModelsAsync();

        FlowLayoutPanel actionRail = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight
        };
        actionRail.Controls.Add(modelComboBox);
        actionRail.Controls.Add(askButton);
        actionRail.Controls.Add(routeOnlyButton);
        actionRail.Controls.Add(routerDrillButton);
        actionRail.Controls.Add(compareButton);
        actionRail.Controls.Add(statusLabel);

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(scenarioListBox, 0, 1);
        root.Controls.Add(questionBox, 0, 2);
        root.Controls.Add(actionRail, 0, 3);
        TabControl outputTabs = new()
        {
            Dock = DockStyle.Fill
        };
        TabPage responsePage = new("LLM / Server Output");
        TabPage promptPage = new("Host Trace");
        responsePage.Controls.Add(responseBox);
        promptPage.Controls.Add(promptBox);
        outputTabs.TabPages.Add(responsePage);
        outputTabs.TabPages.Add(promptPage);

        root.Controls.Add(outputTabs, 0, 4);
        Controls.Add(root);
    }

    private void LoadScenarios()
    {
        PromptScenario[] scenarios =
        [
            new("Discover API Surface", "What MCP Server tools are available in this Host session? Summarize the Server names and tool purposes."),
            new("Route: Source Structure", "Show me the structure of EditorSurface\\ExplorerControl.cs.", "monitor-base-claude", "get_source_map"),
            new("Route: Exact Symbol Body", "Show me the body of LoadTable in EditorSurface\\EditorSurfaceControl.cs.", "monitor-base-claude", "get_symbol"),
            new("Route: File Search", "Find file Program.cs in the watched project.", "monitor-base-claude", "find_file"),
            new("Route: File Read", "Read Program.cs from the watched project and summarize what application starts.", "monitor-base-claude", "get_file"),
            new("Route: Workflow Status", "Inspect the current monitor workflow status and tell me whether WinMerge is available.", "monitor-base-claude", "get_workflow_status"),
            new("Route: Refresh File", "Refresh Program.cs into the monitor Working folder.", "monitor-base-claude", "refresh_file"),
            new("Route: Compare File", "Compare Program.cs using the configured monitor diff workflow.", "monitor-base-claude", "compare_file"),
            new("Route: Start Session", "Start a Monitor Server session for investigating Program.cs and record that the user asked to inspect startup flow.", "monitor-base-claude", "start_monitor_session"),
            new("Route: List Sessions", "List Monitor Server sessions and identify the most recent session.", "monitor-base-claude", "list_monitor_sessions"),
            new("Drill: Null Guard No Map", "User wants to add a null guard to LoadTable in EditorSurface\\EditorSurfaceControl.cs, but no source map has been read yet.", ExpectedRouterAction: "SOURCE_MAP"),
            new("Drill: Null Guard Has Map", "Source map for EditorSurface\\EditorSurfaceControl.cs already shows LoadTable(BaseTableDefinition table), but the method body has not been read.", ExpectedRouterAction: "GET_SYMBOL"),
            new("Drill: Candidate Ready", "The model has the symbol body and proposes a complete updated file candidate.", ExpectedRouterAction: "STAGE_FILE"),
            new("Drill: Accepted Hash Match", "Operator reports accepted and watched hash equals staged candidate hash.", ExpectedRouterAction: "RECORD_DECISION"),
            new("Drill: Direct Source Write", "User asks the model to write the changed file directly into watched source.", ExpectedRouterAction: "REFUSE_UNSAFE"),
            new("Drill: Partial WinMerge Repair", "Operator wants to fix the few bad lines manually in WinMerge and save the result.", ExpectedRouterAction: "REFUSE_UNSAFE"),
            new("Drill: Unknown Target", "User wants to fix table loading, but no file or symbol name is known.", ExpectedRouterAction: "ASK_NARROWING_QUESTION"),
            new("Feature Context", "I want to edit the startup flow. What files should you read first? Use tools if needed."),
            new("No Tool Needed", "Explain in one sentence what a Monitor Server session handle is.")
        ];

        scenarioListBox.Items.Clear();
        foreach (PromptScenario scenario in scenarios)
        {
            scenarioListBox.Items.Add(scenario);
        }

        scenarioListBox.SelectedIndex = 1;
    }

    private async Task AskAsync()
    {
        statusLabel.Text = "Asking Ollama for next MCP action...";
        responseBox.Text = string.Empty;
        try
        {
            IReadOnlyList<LocalMcpServerSurface> localServers = await localMcpDiscoveryService.DiscoverAsync();
            string model = modelComboBox.SelectedItem?.ToString() ?? "llama3.2:1b";
            OllamaActionDecision decision = await ollamaToolExplorerService.DecideNextActionAsync(localServers, questionBox.Text, model);
            object trace = new
            {
                mode = "tool-action-loop",
                model,
                userRequest = questionBox.Text,
                discoveredServers = localServers,
                decision
            };
            promptBox.Text = ToJson(trace);

            if (decision.Action.Equals("call_tool", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(decision.Tool))
            {
                RouteValidation validation = ValidateRoute(localServers, decision, null);
                if (!validation.IsExecutable)
                {
                    string failureLogPath = await WriteHostTraceAsync(new
                    {
                        mode = "tool-action-loop",
                        model,
                        userRequest = questionBox.Text,
                        discoveredServers = localServers,
                        decision,
                        routeValidation = validation
                    });
                    promptBox.Text = ToJson(new
                    {
                        mode = "tool-action-loop",
                        model,
                        userRequest = questionBox.Text,
                        discoveredServers = localServers,
                        decision,
                        routeValidation = validation,
                        logPath = failureLogPath
                    });
                    responseBox.Text = "Routing failed before tool call\r\n================================\r\n"
                        + ToJson(validation)
                        + "\r\n\r\nTool decision\r\n=============\r\n"
                        + ToJson(decision)
                        + "\r\n\r\nHost trace log\r\n==============\r\n"
                        + failureLogPath;
                    statusLabel.Text = "Route failed";
                    return;
                }

                string serverName = validation.ResolvedServerName!;
                LocalMcpToolCallResult toolResult = await localMcpDiscoveryService.CallToolAsync(serverName, decision.Tool, decision.Arguments);
                string answer = await ollamaToolExplorerService.AnswerWithToolResultAsync(questionBox.Text, decision.Tool, toolResult.ResponseJson, model);
                string logPath = await WriteHostTraceAsync(new
                {
                    mode = "tool-action-loop",
                    model,
                    userRequest = questionBox.Text,
                    discoveredServers = localServers,
                    decision,
                    toolCall = new
                    {
                        name = decision.Tool,
                        arguments = decision.Arguments
                    },
                    toolResult,
                    finalAnswer = answer
                });
                promptBox.Text = ToJson(new
                {
                    mode = "tool-action-loop",
                    model,
                    userRequest = questionBox.Text,
                    discoveredServers = localServers,
                    decision,
                    toolCall = new
                    {
                        name = decision.Tool,
                        arguments = decision.Arguments
                    },
                    toolResult,
                    finalAnswer = answer,
                    logPath
                });
                responseBox.Text = "Tool decision\r\n=============\r\n"
                    + ToJson(decision)
                    + "\r\n\r\nTool result\r\n===========\r\n"
                    + FormatPayloadForDisplay(toolResult.ResponseJson)
                    + "\r\n\r\nModel answer\r\n============\r\n"
                    + NormalizeNewlines(answer)
                    + "\r\n\r\nHost trace log\r\n==============\r\n"
                    + logPath;
            }
            else
            {
                string logPath = await WriteHostTraceAsync(new
                {
                    mode = "tool-action-loop",
                    model,
                    userRequest = questionBox.Text,
                    discoveredServers = localServers,
                    decision
                });
                promptBox.Text = ToJson(new
                {
                    mode = "tool-action-loop",
                    model,
                    userRequest = questionBox.Text,
                    discoveredServers = localServers,
                    decision,
                    logPath
                });
                responseBox.Text = NormalizeNewlines(decision.Answer ?? ToJson(decision))
                    + "\r\n\r\nHost trace log\r\n==============\r\n"
                    + logPath;
            }

            statusLabel.Text = "Ready";
        }
        catch (Exception ex)
        {
            responseBox.Text = ex.ToString();
            statusLabel.Text = "Error";
        }
    }

    private async Task RouteOnlyAsync()
    {
        statusLabel.Text = "Asking Ollama to route without executing...";
        responseBox.Text = string.Empty;
        try
        {
            IReadOnlyList<LocalMcpServerSurface> localServers = await localMcpDiscoveryService.DiscoverAsync();
            string model = modelComboBox.SelectedItem?.ToString() ?? "llama3.2:1b";
            PromptScenario? scenario = CurrentPromptScenario();
            OllamaActionDecision decision = await ollamaToolExplorerService.DecideNextActionAsync(localServers, questionBox.Text, model);
            RouteValidation validation = ValidateRoute(localServers, decision, scenario);
            string logPath = await WriteHostTraceAsync(new
            {
                mode = "route-only",
                model,
                userRequest = questionBox.Text,
                expected = scenario is null
                    ? null
                    : new
                    {
                        scenario.ExpectedServer,
                        scenario.ExpectedTool
                    },
                discoveredServers = localServers,
                decision,
                routeValidation = validation
            });

            promptBox.Text = ToJson(new
            {
                mode = "route-only",
                model,
                userRequest = questionBox.Text,
                expected = scenario is null
                    ? null
                    : new
                    {
                        scenario.ExpectedServer,
                        scenario.ExpectedTool
                    },
                discoveredServers = localServers,
                decision,
                routeValidation = validation,
                logPath
            });

            responseBox.Text = "Route-only result\r\n=================\r\n"
                + $"Expected server: {scenario?.ExpectedServer ?? "(none)"}\r\n"
                + $"Expected tool:   {scenario?.ExpectedTool ?? "(none)"}\r\n"
                + $"Actual server:   {decision.Server ?? "(none)"}\r\n"
                + $"Resolved server: {validation.ResolvedServerName ?? "(none)"}\r\n"
                + $"Actual tool:     {decision.Tool ?? "(none)"}\r\n"
                + $"Pass:            {validation.IsExpectedRoute}\r\n"
                + $"Executable:      {validation.IsExecutable}\r\n"
                + $"Issue:           {validation.Message ?? "(none)"}\r\n"
                + "\r\nTool decision\r\n=============\r\n"
                + ToJson(decision)
                + "\r\n\r\nHost trace log\r\n==============\r\n"
                + logPath;

            statusLabel.Text = validation.IsExpectedRoute ? "Route passed" : "Route mismatch";
        }
        catch (Exception ex)
        {
            responseBox.Text = ex.ToString();
            statusLabel.Text = "Error";
        }
    }

    private async Task RouterDrillAsync()
    {
        statusLabel.Text = "Asking Ollama for fake-router workflow classification...";
        responseBox.Text = string.Empty;
        try
        {
            string model = modelComboBox.SelectedItem?.ToString() ?? "llama3.2:1b";
            PromptScenario? scenario = CurrentPromptScenario();
            OllamaRouterDecision decision = await ollamaToolExplorerService.DecideRouterActionAsync(questionBox.Text, model);
            string expected = scenario?.ExpectedRouterAction ?? "(none)";
            bool passed = !string.IsNullOrWhiteSpace(scenario?.ExpectedRouterAction)
                && decision.Action.Equals(scenario.ExpectedRouterAction, StringComparison.OrdinalIgnoreCase);
            string logPath = await WriteHostTraceAsync(new
            {
                mode = "router-drill",
                model,
                userRequest = questionBox.Text,
                expectedRouterAction = scenario?.ExpectedRouterAction,
                decision,
                passed
            });

            promptBox.Text = ToJson(new
            {
                mode = "router-drill",
                model,
                userRequest = questionBox.Text,
                expectedRouterAction = scenario?.ExpectedRouterAction,
                decision,
                passed,
                logPath
            });

            responseBox.Text = "Router drill result\r\n===================\r\n"
                + $"Expected action: {expected}\r\n"
                + $"Actual action:   {decision.Action}\r\n"
                + $"Pass:            {passed}\r\n"
                + $"Reason:          {decision.Reason ?? "(none)"}\r\n"
                + "\r\nDecision JSON\r\n=============\r\n"
                + ToJson(decision)
                + "\r\n\r\nHost trace log\r\n==============\r\n"
                + logPath;

            statusLabel.Text = passed ? "Router drill passed" : "Router drill mismatch";
        }
        catch (Exception ex)
        {
            responseBox.Text = ex.ToString();
            statusLabel.Text = "Error";
        }
    }

    private async Task CompareTinyModelsAsync()
    {
        statusLabel.Text = "Comparing tiny local models...";
        responseBox.Text = string.Empty;
        try
        {
            IReadOnlyList<LocalMcpServerSurface> localServers = await localMcpDiscoveryService.DiscoverAsync();
            IReadOnlyList<string> installedModels = await ollamaToolExplorerService.GetInstalledModelsAsync();
            string[] candidates =
            [
                "qwen2:0.5b",
                "tinyllama:latest",
                "llama3.2:1b"
            ];
            List<string> responses = [];
            foreach (string model in candidates.Where(model => installedModels.Contains(model, StringComparer.OrdinalIgnoreCase)))
            {
                OllamaActionDecision decision = await ollamaToolExplorerService.DecideNextActionAsync(localServers, questionBox.Text, model);
                RouteValidation validation = ValidateRoute(localServers, decision, CurrentPromptScenario());
                responses.Add($"Model: {model}\r\nExpected: {CurrentPromptScenario()?.ExpectedServer ?? "(none)"} / {CurrentPromptScenario()?.ExpectedTool ?? "(none)"}\r\nPass: {validation.IsExpectedRoute}\r\nExecutable: {validation.IsExecutable}\r\nIssue: {validation.Message ?? "(none)"}\r\n\r\n{ToJson(decision)}");
                responses.Add(new string('-', 80));
            }

            responseBox.Text = responses.Count == 0
                ? "No tiny comparison models are installed."
                : NormalizeNewlines(string.Join(Environment.NewLine + Environment.NewLine, responses));
            statusLabel.Text = "Ready";
        }
        catch (Exception ex)
        {
            responseBox.Text = ex.ToString();
            statusLabel.Text = "Error";
        }
    }

    private async Task LoadModelsAsync(string preferredModel)
    {
        IReadOnlyList<string> models = await ollamaToolExplorerService.GetInstalledModelsAsync();
        modelComboBox.Items.Clear();
        foreach (string model in models)
        {
            modelComboBox.Items.Add(model);
        }

        int preferredIndex = modelComboBox.Items.IndexOf(preferredModel);
        modelComboBox.SelectedIndex = preferredIndex >= 0
            ? preferredIndex
            : modelComboBox.Items.Count > 0 ? 0 : -1;
    }

    private sealed record PromptScenario(
        string Name,
        string Prompt,
        string? ExpectedServer = null,
        string? ExpectedTool = null,
        string? ExpectedRouterAction = null)
    {
        public override string ToString()
        {
            return Name;
        }
    }

    private sealed record RouteValidation(
        bool IsExecutable,
        bool IsExpectedRoute,
        string? ResolvedServerName,
        string? Message);

    private async Task<string> WriteHostTraceAsync(object trace)
    {
        string logRoot = Path.Combine(settings.UiRoot, "Working", "History", "HostRuns", DateTime.Now.ToString("yyyyMMdd"));
        Directory.CreateDirectory(logRoot);
        string logPath = Path.Combine(logRoot, DateTime.Now.ToString("HHmmss_fff") + ".json");
        await File.WriteAllTextAsync(logPath, ToJson(trace));
        return logPath;
    }

    private static string ToJson(object value)
    {
        return JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
    }

    private PromptScenario? CurrentPromptScenario()
    {
        return scenarioListBox.SelectedItem is PromptScenario scenario
            && questionBox.Text.Equals(scenario.Prompt, StringComparison.Ordinal)
            ? scenario
            : null;
    }

    private static RouteValidation ValidateRoute(
        IReadOnlyList<LocalMcpServerSurface> localServers,
        OllamaActionDecision decision,
        PromptScenario? scenario)
    {
        if (!decision.Action.Equals("call_tool", StringComparison.OrdinalIgnoreCase))
        {
            bool expectedAnswer = string.IsNullOrWhiteSpace(scenario?.ExpectedTool);
            return new RouteValidation(expectedAnswer, expectedAnswer, null, expectedAnswer ? null : "Model answered instead of choosing a tool.");
        }

        if (string.IsNullOrWhiteSpace(decision.Tool))
        {
            return new RouteValidation(false, false, null, "Model requested a tool call without a tool name.");
        }

        LocalMcpServerSurface? resolvedServer = null;
        if (!string.IsNullOrWhiteSpace(decision.Server))
        {
            resolvedServer = localServers.FirstOrDefault(server => server.Name.Equals(decision.Server, StringComparison.OrdinalIgnoreCase));
            if (resolvedServer is null)
            {
                return new RouteValidation(false, false, null, $"Model named unknown MCP Server '{decision.Server}'.");
            }
        }
        else
        {
            LocalMcpServerSurface[] matchingServers = localServers
                .Where(server => server.Tools.Any(tool => tool.Name.Equals(decision.Tool, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (matchingServers.Length == 1)
            {
                resolvedServer = matchingServers[0];
            }
            else if (matchingServers.Length == 0)
            {
                return new RouteValidation(false, false, null, $"No discovered MCP Server exposes tool '{decision.Tool}'.");
            }
            else
            {
                return new RouteValidation(false, false, null, $"Tool '{decision.Tool}' is exposed by multiple MCP Servers; the model must name a server.");
            }
        }

        bool serverHasTool = resolvedServer.Tools.Any(tool => tool.Name.Equals(decision.Tool, StringComparison.OrdinalIgnoreCase));
        if (!serverHasTool)
        {
            return new RouteValidation(false, false, resolvedServer.Name, $"Server '{resolvedServer.Name}' does not expose tool '{decision.Tool}'.");
        }

        bool expectedToolMatches = string.IsNullOrWhiteSpace(scenario?.ExpectedTool)
            || decision.Tool.Equals(scenario.ExpectedTool, StringComparison.OrdinalIgnoreCase);
        bool expectedServerMatches = string.IsNullOrWhiteSpace(scenario?.ExpectedServer)
            || resolvedServer.Name.Equals(scenario.ExpectedServer, StringComparison.OrdinalIgnoreCase);
        bool expectedRoute = expectedToolMatches && expectedServerMatches;
        string? message = expectedRoute
            ? null
            : $"Expected {scenario?.ExpectedServer ?? "(any server)"}/{scenario?.ExpectedTool ?? "(any tool)"}.";

        return new RouteValidation(true, expectedRoute, resolvedServer.Name, message);
    }

    private static string FormatPayloadForDisplay(string payload)
    {
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
}
