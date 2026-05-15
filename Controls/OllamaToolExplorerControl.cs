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
            new("Find File", "Find file Program.cs in the watched project."),
            new("Read File", "Read Program.cs from the watched project and summarize what application starts."),
            new("Inspect Workflow", "Inspect the current monitor workflow status and tell me whether WinMerge is available."),
            new("Refresh File", "Refresh Program.cs into the monitor Working folder."),
            new("Compare File", "Compare Program.cs using the configured monitor diff workflow."),
            new("Start Server Session", "Start a Monitor Server session for investigating Program.cs and record that the user asked to inspect startup flow."),
            new("Resume Server Session", "List Monitor Server sessions and identify the most recent session."),
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
                string serverName = ResolveServerName(localServers, decision);
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
                responses.Add($"Model: {model}\r\n\r\n{ToJson(decision)}");
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

    private sealed record PromptScenario(string Name, string Prompt)
    {
        public override string ToString()
        {
            return Name;
        }
    }

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
