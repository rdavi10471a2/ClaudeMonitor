using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using MonitorBaseClaude.AI;

namespace MonitorBaseClaude.Controls;

[DesignerCategory("Code")]
[AIFileContext("McpTestBenchControl.cs", "Code-only MCP test surface for loading a solution through the telemetry proxy and proving project-scoped calls.")]
[FileVersion("1.4")]
public sealed class McpTestBenchControl : UserControl
{
    private readonly RoslynCodeLensMcpClientService mcpClientService;
    private readonly MonitorClientSettings settings;
    private readonly TextBox solutionPathTextBox = new();
    private readonly Button browseButton = new();
    private readonly Button loadSolutionButton = new();
    private readonly ComboBox toolComboBox = new();
    private readonly RichTextBox argumentsBox = new();
    private readonly RichTextBox schemaBox = new();
    private readonly Button runToolButton = new();
    private readonly ComboBox projectComboBox = new();
    private readonly Label statusLabel = new();
    private readonly SplitContainer payloadSplit = new();
    private readonly RichTextBox requestBox = new();
    private readonly RichTextBox responseBox = new();

    private RoslynCodeLensMcpClientService.LoadedSolutionSession? loadedSession;
    private bool isUpdatingSolutionPath;
    private bool payloadSplitterSized;

    public event Action<RoslynCodeLensMcpClientService.LoadedSolutionSession>? SolutionLoaded;
    public event Action<RoslynCodeLensMcpClientService.McpProjectSummary>? ProjectSelected;
    public event Action<string>? StatusChanged;
    public event Action? ToolInvocationCompleted;

    public McpTestBenchControl(RoslynCodeLensMcpClientService mcpClientService, MonitorClientSettings settings)
    {
        this.mcpClientService = mcpClientService;
        this.settings = settings;
        Dock = DockStyle.Fill;
        BuildLayout();
        Load += (_, _) => BeginInvoke(ApplyInitialPayloadSplitterLayout);
        Load += async (_, _) => await LoadConfiguredSolutionAsync();
    }

    public void SelectProject(RoslynCodeLensMcpClientService.McpProjectSummary project)
    {
        projectComboBox.SelectedItem = project;
    }

    public void SelectTool(RoslynCodeLensMcpClientService.McpToolSummary tool)
    {
        for (int i = 0; i < toolComboBox.Items.Count; i++)
        {
            if (toolComboBox.Items[i] is RoslynCodeLensMcpClientService.McpToolSummary candidate
                && candidate.Name.Equals(tool.Name, StringComparison.Ordinal))
            {
                toolComboBox.SelectedIndex = i;
                return;
            }
        }
    }

    private void BuildLayout()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Panel pickerHost = new()
        {
            Dock = DockStyle.Fill,
            MinimumSize = new Size(0, 104),
            Padding = new Padding(10, 10, 10, 8)
        };

        TableLayoutPanel pickers = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            MinimumSize = new Size(0, 84),
            Padding = new Padding(0)
        };
        pickers.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        pickers.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pickers.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        pickers.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 138));
        pickers.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        pickers.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        Label solutionLabel = CreateHeaderLabel("Solution");
        solutionPathTextBox.Dock = DockStyle.Fill;
        solutionPathTextBox.MinimumSize = new Size(300, 0);
        solutionPathTextBox.Margin = new Padding(0, 7, 10, 7);
        solutionPathTextBox.ReadOnly = true;
        solutionPathTextBox.Text = settings.CodeLensSolutionPath;
        solutionPathTextBox.TextChanged += SolutionPathTextBox_TextChanged;
        browseButton.Visible = false;
        loadSolutionButton.Text = "Reload";
        loadSolutionButton.Dock = DockStyle.Fill;
        loadSolutionButton.Margin = new Padding(0, 5, 0, 7);
        loadSolutionButton.Click += LoadSolutionButton_Click;

        Label projectLabel = CreateHeaderLabel("Project");
        projectComboBox.Dock = DockStyle.Fill;
        projectComboBox.MinimumSize = new Size(300, 0);
        projectComboBox.Margin = new Padding(0, 7, 0, 7);
        projectComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        projectComboBox.DisplayMember = "Name";
        projectComboBox.Enabled = false;
        projectComboBox.SelectedIndexChanged += ProjectComboBox_SelectedIndexChanged;

        pickers.Controls.Add(solutionLabel, 0, 0);
        pickers.Controls.Add(solutionPathTextBox, 1, 0);
        pickers.Controls.Add(browseButton, 2, 0);
        pickers.Controls.Add(loadSolutionButton, 3, 0);
        pickers.Controls.Add(projectLabel, 0, 1);
        pickers.Controls.Add(projectComboBox, 1, 1);
        pickers.SetColumnSpan(projectComboBox, 3);
        pickerHost.Controls.Add(pickers);

        TableLayoutPanel toolPanel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 2,
            Padding = new Padding(0, 6, 0, 6),
            MinimumSize = new Size(0, 110)
        };
        toolPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        toolPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        toolPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        toolPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        toolPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        toolPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        toolComboBox.Dock = DockStyle.Fill;
        toolComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        toolComboBox.DisplayMember = "Name";
        toolComboBox.Enabled = false;
        toolComboBox.Margin = new Padding(0, 5, 10, 5);
        toolComboBox.SelectedIndexChanged += ToolComboBox_SelectedIndexChanged;
        runToolButton.Text = "Run Tool";
        runToolButton.Dock = DockStyle.Fill;
        runToolButton.Enabled = false;
        runToolButton.Margin = new Padding(0, 4, 8, 4);
        runToolButton.Click += async (_, _) => await RunSelectedToolAsync();

        statusLabel.Dock = DockStyle.Fill;
        statusLabel.AutoEllipsis = true;
        statusLabel.TextAlign = ContentAlignment.MiddleRight;
        argumentsBox.Dock = DockStyle.Fill;
        argumentsBox.Font = new Font("Consolas", 9.5F);
        argumentsBox.WordWrap = false;
        schemaBox.Dock = DockStyle.Fill;
        schemaBox.ReadOnly = true;
        schemaBox.Font = new Font("Consolas", 9.5F);
        schemaBox.WordWrap = false;

        toolPanel.Controls.Add(CreateHeaderLabel("Tool"), 0, 0);
        toolPanel.Controls.Add(toolComboBox, 1, 0);
        toolPanel.Controls.Add(runToolButton, 2, 0);
        toolPanel.Controls.Add(statusLabel, 3, 0);
        toolPanel.Controls.Add(argumentsBox, 1, 1);
        toolPanel.SetColumnSpan(argumentsBox, 2);
        toolPanel.Controls.Add(schemaBox, 3, 1);

        ConfigurePayloadViewer();

        root.Controls.Add(pickerHost, 0, 0);
        root.Controls.Add(toolPanel, 0, 1);
        root.Controls.Add(payloadSplit, 0, 2);
        Controls.Add(root);
    }

    private void ConfigurePayloadViewer()
    {
        payloadSplit.Dock = DockStyle.Fill;
        payloadSplit.Orientation = Orientation.Vertical;
        payloadSplit.SplitterWidth = 6;
        payloadSplit.Panel1.Controls.Add(CreatePayloadPanel("Request", requestBox));
        payloadSplit.Panel2.Controls.Add(CreatePayloadPanel("Response", responseBox));

        ConfigurePayloadBox(requestBox);
        ConfigurePayloadBox(responseBox);
    }

    private static Panel CreatePayloadPanel(string title, RichTextBox content)
    {
        Panel panel = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0)
        };
        Label label = new()
        {
            Dock = DockStyle.Top,
            Height = 24,
            Text = title,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };
        content.Dock = DockStyle.Fill;
        panel.Controls.Add(content);
        panel.Controls.Add(label);
        return panel;
    }

    private static void ConfigurePayloadBox(RichTextBox box)
    {
        box.ReadOnly = true;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Font = new Font("Consolas", 9.5F);
        box.WordWrap = false;
    }

    private void ApplyInitialPayloadSplitterLayout()
    {
        if (payloadSplitterSized)
        {
            return;
        }

        if (payloadSplit.Width < 600)
        {
            BeginInvoke(ApplyInitialPayloadSplitterLayout);
            return;
        }

        payloadSplitterSized = true;
        payloadSplit.SplitterDistance = payloadSplit.Width / 2;
    }

    private void BrowseButton_Click(object? sender, EventArgs e)
    {
        using OpenFileDialog dialog = new()
        {
            Title = "Select a Visual Studio solution",
            Filter = "Visual Studio Solution (*.sln)|*.sln|All files (*.*)|*.*",
            CheckFileExists = true,
            InitialDirectory = Directory.Exists(Path.GetDirectoryName(settings.CodeLensSolutionPath)) ? Path.GetDirectoryName(settings.CodeLensSolutionPath) : Environment.CurrentDirectory,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            solutionPathTextBox.Text = dialog.FileName;
        }
    }

    private void SolutionPathTextBox_TextChanged(object? sender, EventArgs e)
    {
        if (isUpdatingSolutionPath)
        {
            return;
        }

        string? resolvedSolutionPath = RoslynCodeLensMcpClientService.ResolveSolutionPath(solutionPathTextBox.Text.Trim());
        if (loadedSession is null
            || string.Equals(loadedSession.SolutionPath, resolvedSolutionPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        loadedSession = null;
        projectComboBox.Items.Clear();
        projectComboBox.Enabled = false;
        SetLoadingState(false);
        SetStatus("Solution path changed. Load Solution to refresh.");
    }

    private async void LoadSolutionButton_Click(object? sender, EventArgs e)
    {
        await LoadConfiguredSolutionAsync();
    }

    private async Task LoadConfiguredSolutionAsync()
    {
        string? resolvedSolutionPath = RoslynCodeLensMcpClientService.ResolveSolutionPath(solutionPathTextBox.Text.Trim());
        if (resolvedSolutionPath is null)
        {
            SetStatus("Solution file not found.");
            return;
        }

        SetLoadingState(true);
        requestBox.Clear();
        responseBox.Clear();
        projectComboBox.Items.Clear();
        projectComboBox.Enabled = false;
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            isUpdatingSolutionPath = true;
            solutionPathTextBox.Text = resolvedSolutionPath;
            isUpdatingSolutionPath = false;
            Progress<string> progress = new(SetStatus);
            loadedSession = await mcpClientService.LoadSolutionSessionAsync(resolvedSolutionPath, progress);
            stopwatch.Stop();

            foreach (RoslynCodeLensMcpClientService.McpProjectSummary project in loadedSession.Projects)
            {
                projectComboBox.Items.Add(project);
            }

            toolComboBox.Items.Clear();
            foreach (RoslynCodeLensMcpClientService.McpToolSummary tool in loadedSession.Tools)
            {
                toolComboBox.Items.Add(tool);
            }

            projectComboBox.Enabled = loadedSession.Projects.Count > 0;
            toolComboBox.Enabled = loadedSession.Tools.Count > 0;
            if (projectComboBox.Items.Count > 0)
            {
                projectComboBox.SelectedIndex = 0;
            }
            if (toolComboBox.Items.Count > 0)
            {
                SelectDefaultTool("list_solutions");
            }

            SetStatus($"Ready - {loadedSession.Projects.Count} project(s) loaded");
            SolutionLoaded?.Invoke(loadedSession);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            SetStatus($"Load failed: {ex.Message}");
            responseBox.Text = ex.ToString();
        }
        finally
        {
            SetLoadingState(false);
        }
    }

    private void ProjectComboBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (projectComboBox.SelectedItem is RoslynCodeLensMcpClientService.McpProjectSummary project)
        {
            ProjectSelected?.Invoke(project);
            if (toolComboBox.SelectedItem is RoslynCodeLensMcpClientService.McpToolSummary tool)
            {
                argumentsBox.Text = BuildDefaultArguments(tool);
            }
        }
    }

    private void ToolComboBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (toolComboBox.SelectedItem is not RoslynCodeLensMcpClientService.McpToolSummary tool)
        {
            return;
        }

        argumentsBox.Text = BuildDefaultArguments(tool);
        schemaBox.Text = $"{tool.Category} / {tool.Name}{Environment.NewLine}{Environment.NewLine}{tool.Description}{Environment.NewLine}{Environment.NewLine}{tool.JsonSchema}";
    }

    private void SelectDefaultTool(string toolName)
    {
        for (int i = 0; i < toolComboBox.Items.Count; i++)
        {
            if (toolComboBox.Items[i] is RoslynCodeLensMcpClientService.McpToolSummary tool
                && tool.Name.Equals(toolName, StringComparison.Ordinal))
            {
                toolComboBox.SelectedIndex = i;
                return;
            }
        }

        toolComboBox.SelectedIndex = 0;
    }

    private async Task RunSelectedToolAsync()
    {
        if (toolComboBox.SelectedItem is not RoslynCodeLensMcpClientService.McpToolSummary tool)
        {
            SetStatus("Select a tool before running.");
            return;
        }

        IReadOnlyDictionary<string, object?> arguments = ParseArguments(argumentsBox.Text);
        await InvokeToolButtonAsync(tool.Name, tool.Name, arguments);
    }

    private async Task InvokeToolButtonAsync(
        string step,
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments = null)
    {
        if (loadedSession is null)
        {
            SetStatus("Load a solution before invoking MCP tools.");
            return;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        SetLoadingState(true);
        SetStatus($"Calling {toolName}...");

        try
        {
            Progress<string> progress = new(SetStatus);
            RoslynCodeLensMcpClientService.McpToolInvocation invocation = await mcpClientService.InvokeToolAsync(
                loadedSession.SolutionPath,
                toolName,
                arguments,
                progress);
            stopwatch.Stop();
            ShowInvocationPayload(invocation);
            SetStatus($"{toolName}: {(invocation.IsError ? "error" : "ready")} ({stopwatch.ElapsedMilliseconds} ms)");
            ToolInvocationCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            SetStatus($"{toolName} failed: {ex.Message}");
            responseBox.Text = ex.ToString();
            ToolInvocationCompleted?.Invoke();
        }
        finally
        {
            SetLoadingState(false);
        }
    }

    private void ShowInvocationPayload(RoslynCodeLensMcpClientService.McpToolInvocation invocation)
    {
        requestBox.Text = invocation.RequestJson;
        responseBox.Text = invocation.RawJson;
        requestBox.SelectionStart = 0;
        responseBox.SelectionStart = 0;
        requestBox.ScrollToCaret();
        responseBox.ScrollToCaret();
    }

    private void SetLoadingState(bool isLoading)
    {
        browseButton.Enabled = !isLoading;
        loadSolutionButton.Enabled = !isLoading;
        solutionPathTextBox.Enabled = !isLoading;
        runToolButton.Enabled = !isLoading && loadedSession is not null && toolComboBox.SelectedItem is not null;
        Cursor = isLoading ? Cursors.WaitCursor : Cursors.Default;
    }

    private IReadOnlyDictionary<string, object?> ParseArguments(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new Dictionary<string, object?>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(text)
                ?? new Dictionary<string, object?>();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid argument JSON: {ex.Message}", ex);
        }
    }

    private string BuildDefaultArguments(RoslynCodeLensMcpClientService.McpToolSummary tool)
    {
        Dictionary<string, object?> arguments = [];
        string projectName = (projectComboBox.SelectedItem as RoslynCodeLensMcpClientService.McpProjectSummary)?.Name ?? string.Empty;

        JsonNode? schema = TryParseJson(tool.JsonSchema);
        JsonArray? required = schema?["required"] as JsonArray;
        if (required is not null)
        {
            foreach (JsonNode? item in required)
            {
                string? name = item?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                arguments[name] = GuessArgumentValue(name, projectName);
            }
        }

        if (arguments.Count == 0 && tool.Name.Contains("project", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(projectName))
        {
            arguments["project"] = projectName;
        }

        return JsonSerializer.Serialize(arguments, new JsonSerializerOptions { WriteIndented = true });
    }

    private static object? GuessArgumentValue(string name, string projectName)
    {
        if (name.Contains("project", StringComparison.OrdinalIgnoreCase))
        {
            return projectName;
        }

        if (name.Contains("path", StringComparison.OrdinalIgnoreCase)
            || name.Contains("file", StringComparison.OrdinalIgnoreCase)
            || name.Contains("symbol", StringComparison.OrdinalIgnoreCase)
            || name.Contains("type", StringComparison.OrdinalIgnoreCase)
            || name.Contains("method", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return null;
    }

    private static JsonNode? TryParseJson(string text)
    {
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void SetStatus(string status)
    {
        statusLabel.Text = status;
        StatusChanged?.Invoke(status);
    }

    private static Label CreateHeaderLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };
    }
}
