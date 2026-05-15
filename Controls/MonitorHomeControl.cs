using System.ComponentModel;
using System.Diagnostics;
using MonitorBaseClaude.AI;

namespace MonitorBaseClaude.Controls;

[DesignerCategory("Code")]
[AIFileContext("MonitorHomeControl.cs", "Default code-only dashboard page for the monitor workflow, MCP server destination, source implementation folder, and watched solution.")]
[FileVersion("1.6")]
public sealed class MonitorHomeControl : UserControl
{
    private readonly MonitorClientSettings settings;
    private readonly MonitorMcpClientService monitorMcpClientService;
    private readonly Label uiRootValue = CreateValueLabel();
    private readonly Label monitorMcpServerRootValue = CreateValueLabel();
    private readonly Label legacyMonitorRootValue = CreateValueLabel();
    private readonly Label watchedSolutionValue = CreateValueLabel();
    private readonly Label watchedProjectFolderValue = CreateValueLabel();
    private readonly Label codeLensSolutionValue = CreateValueLabel();
    private readonly Label ollamaValue = CreateValueLabel();
    private readonly Label monitorStatusValue = CreateValueLabel();
    private readonly RichTextBox notes = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Segoe UI", 9.5F)
    };

    public MonitorHomeControl(MonitorClientSettings settings, MonitorMcpClientService monitorMcpClientService)
    {
        this.settings = settings;
        this.monitorMcpClientService = monitorMcpClientService;
        Dock = DockStyle.Fill;
        BuildLayout();
        LoadSettings();
        Load += async (_, _) => await RefreshMonitorMcpAsync();
    }

    private void BuildLayout()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 220));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        TableLayoutPanel facts = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 8
        };
        facts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        facts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 8; i++)
        {
            facts.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        }

        AddFact(facts, 0, "WinForms UI root", uiRootValue);
        AddFact(facts, 1, "New MCP server root", monitorMcpServerRootValue);
        AddFact(facts, 2, "Source implementation root", legacyMonitorRootValue);
        AddFact(facts, 3, "Watched solution", watchedSolutionValue);
        AddFact(facts, 4, "Watched project folder", watchedProjectFolderValue);
        AddFact(facts, 5, "CodeLens solution", codeLensSolutionValue);
        AddFact(facts, 6, "Local Ollama", ollamaValue);
        AddFact(facts, 7, "Monitor MCP", monitorStatusValue);

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 8, 0, 0)
        };
        actions.Controls.Add(CreateActionButton("Open UI Root", () => OpenFolder(settings.UiRoot)));
        actions.Controls.Add(CreateActionButton("Open New MCP Server", () => OpenFolder(settings.MonitorMcpServerRoot)));
        actions.Controls.Add(CreateActionButton("Open Source Implementation", () => OpenFolder(settings.LegacyMonitorRoot)));
        actions.Controls.Add(CreateActionButton("Open Watched Project", () => OpenFolder(Path.GetDirectoryName(settings.WatchedSolutionPath) ?? settings.UiRoot)));
        actions.Controls.Add(CreateActionButton("Open Telemetry Folder", () => OpenFolder(Path.Combine(settings.UiRoot, "Working", "History", "McpTelemetry"))));
        actions.Controls.Add(CreateActionButton("Refresh Monitor MCP", async () => await RefreshMonitorMcpAsync()));

        root.Controls.Add(facts, 0, 0);
        root.Controls.Add(actions, 0, 1);
        root.Controls.Add(notes, 0, 2);
        Controls.Add(root);
    }

    private void LoadSettings()
    {
        uiRootValue.Text = settings.UiRoot;
        monitorMcpServerRootValue.Text = settings.MonitorMcpServerRoot;
        legacyMonitorRootValue.Text = settings.LegacyMonitorRoot;
        watchedSolutionValue.Text = settings.WatchedSolutionPath;
        watchedProjectFolderValue.Text = Path.GetDirectoryName(settings.WatchedSolutionPath) ?? "(folder not found)";
        codeLensSolutionValue.Text = settings.CodeLensSolutionPath;
        ollamaValue.Text = $"{settings.OllamaModel} at {settings.OllamaEndpoint}";
        monitorStatusValue.Text = Directory.Exists(settings.MonitorMcpServerRoot) ? "Scaffolded, not connected" : "Not created";
        notes.Text = "Monitor dashboard\r\n=================\r\n\r\nWorking model:\r\n- WinForms UI lives here and drives MCP servers.\r\n- Monitor MCP Server is the workflow API for files, sessions, refresh, compare, and history.\r\n- Source implementation root is reference code while the Server tool surface is completed.\r\n- CodeLens MCP Server is the Roslyn API for symbols, diagnostics, dependencies, and analysis.\r\n- Watched input is a solution file, currently DBV2.\r\n- Local Ollama is the baby-Claude Host simulation for tool routing and answer shaping.\r\n\r\nConnecting to MonitorBaseClaude.McpServer...\r\n";
    }

    private async Task RefreshMonitorMcpAsync()
    {
        monitorStatusValue.Text = "Connecting...";
        try
        {
            MonitorMcpDashboardSnapshot snapshot = await monitorMcpClientService.LoadDashboardSnapshotAsync();
            monitorStatusValue.Text = snapshot.HasErrors
                ? $"Connected with tool error ({snapshot.ToolNames.Count} tools)"
                : $"Connected ({snapshot.ToolNames.Count} tools)";
            notes.Text = "Monitor MCP status\r\n==================\r\n\r\n"
                + snapshot.StatusJson
                + "\r\n\r\nTool manifest\r\n=============\r\n\r\n"
                + snapshot.ToolManifest;
        }
        catch (Exception ex)
        {
            monitorStatusValue.Text = "Connection failed";
            notes.Text = "Monitor MCP connection failed\r\n=============================\r\n\r\n"
                + ex.Message
                + "\r\n\r\nBuild the MCP server first, then refresh this page.\r\n";
        }
    }

    private static void AddFact(TableLayoutPanel facts, int row, string label, Label value)
    {
        facts.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = label,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        }, 0, row);
        facts.Controls.Add(value, 1, row);
    }

    private static Label CreateValueLabel()
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private static Button CreateActionButton(string text, Action action)
    {
        Button button = new()
        {
            Text = text,
            AutoSize = true,
            Height = 32,
            Margin = new Padding(0, 0, 8, 0)
        };
        button.Click += (_, _) => action();
        return button;
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }
}
