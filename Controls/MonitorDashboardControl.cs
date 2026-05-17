using System.ComponentModel;
using MonitorBaseClaude.AI;

namespace MonitorBaseClaude.Controls;

[DesignerCategory("Code")]
[AIFileContext("MonitorDashboardControl.cs", "Code-only monitor shell using deterministic split containers for tool navigation, MCP testing, and telemetry panes.")]
[FileVersion("2.4")]
public sealed class MonitorDashboardControl : UserControl
{
    private readonly MonitorClientSettings settings;
    private readonly CommandBarControl commandBar;
    private readonly SplitContainer verticalSplit;
    private readonly SplitContainer workspaceSplit;
    private readonly TabControl mainTabs;
    private readonly RoslynCodeLensMcpClientService mcpClientService = new();
    private readonly MonitorMcpClientService monitorMcpClientService;
    private readonly OllamaToolExplorerService ollamaToolExplorerService;
    private readonly LocalMcpDiscoveryService localMcpDiscoveryService;

    private readonly MonitorHomeControl monitorHomeControl;
    private readonly OllamaToolExplorerControl ollamaToolExplorerControl;
    private readonly ToolNavigatorControl toolNavigatorControl;
    private readonly McpTestBenchControl testBenchControl;
    private readonly TelemetryLogControl telemetryLogControl;
    private readonly TelemetryLogControl monitorMcpTelemetryLogControl;
    private SessionInspectorControl? sessionInspectorControl;
    private Form? sessionInspectorWindow;
    private bool splitterLayoutSized;

    public MonitorDashboardControl()
    {
        Dock = DockStyle.Fill;
        settings = MonitorClientSettings.Load();

        commandBar = new CommandBarControl();
        mainTabs = new TabControl
        {
            Dock = DockStyle.Fill
        };
        verticalSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            FixedPanel = FixedPanel.Panel2,
            SplitterWidth = 6
        };
        workspaceSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel1,
            SplitterWidth = 6
        };

        monitorMcpClientService = new MonitorMcpClientService(settings);
        ollamaToolExplorerService = new OllamaToolExplorerService(settings);
        localMcpDiscoveryService = new LocalMcpDiscoveryService(settings);
        monitorHomeControl = new MonitorHomeControl(settings, monitorMcpClientService) { Dock = DockStyle.Fill };
        ollamaToolExplorerControl = new OllamaToolExplorerControl(settings, monitorMcpClientService, ollamaToolExplorerService, localMcpDiscoveryService) { Dock = DockStyle.Fill };
        toolNavigatorControl = new ToolNavigatorControl { Dock = DockStyle.Fill, MinimumSize = new Size(220, 200) };
        testBenchControl = new McpTestBenchControl(mcpClientService, settings) { Dock = DockStyle.Fill, MinimumSize = new Size(850, 360) };
        telemetryLogControl = new TelemetryLogControl(TelemetryLogControl.ResolveRoslynCodeLensLogRoot(settings))
        {
            Dock = DockStyle.Fill,
            MinimumSize = new Size(900, 260)
        };
        monitorMcpTelemetryLogControl = new TelemetryLogControl(TelemetryLogControl.ResolveMonitorMcpLogRoot(settings))
        {
            Dock = DockStyle.Fill,
            MinimumSize = new Size(850, 360)
        };

        workspaceSplit.Panel1.Controls.Add(toolNavigatorControl);
        workspaceSplit.Panel2.Controls.Add(testBenchControl);
        TabPage monitorPage = new("Monitor");
        TabPage localAiPage = new("Local AI Tool Explorer");
        TabPage codeLensPage = new("CodeLens Test Bench");
        TabPage monitorMcpTelemetryPage = new("Monitor MCP Calls");
        monitorPage.Controls.Add(monitorHomeControl);
        localAiPage.Controls.Add(ollamaToolExplorerControl);
        codeLensPage.Controls.Add(workspaceSplit);
        monitorMcpTelemetryPage.Controls.Add(monitorMcpTelemetryLogControl);
        mainTabs.TabPages.Add(monitorPage);
        mainTabs.TabPages.Add(localAiPage);
        mainTabs.TabPages.Add(codeLensPage);
        mainTabs.TabPages.Add(monitorMcpTelemetryPage);
        mainTabs.SelectedIndex = 0;
        verticalSplit.Panel1.Controls.Add(mainTabs);
        verticalSplit.Panel2.Controls.Add(telemetryLogControl);
        Controls.Add(verticalSplit);
        Controls.Add(commandBar);

        WireEvents();
        RegisterMenu();
        Load += (_, _) => BeginInvoke(ApplyInitialSplitterLayout);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            mcpClientService.Dispose();
            monitorMcpClientService.Dispose();
            ollamaToolExplorerService.Dispose();
            sessionInspectorWindow?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void WireEvents()
    {
        toolNavigatorControl.ToolSelected += tool =>
        {
            testBenchControl.SelectTool(tool);
        };

        testBenchControl.StatusChanged += status => sessionInspectorControl?.SetStatus(status);
        testBenchControl.ToolInvocationCompleted += telemetryLogControl.RefreshLogs;
        testBenchControl.SolutionLoaded += session =>
        {
            toolNavigatorControl.LoadTools(session.Tools);
            sessionInspectorControl?.LoadSession(session);
            telemetryLogControl.RefreshLogs();
            monitorMcpTelemetryLogControl.RefreshLogs();
        };
        testBenchControl.ProjectSelected += project => sessionInspectorControl?.ShowProject(project);
    }

    private void RegisterMenu()
    {
        commandBar.AddMenuItem("File", "Exit", (_, _) => FindForm()?.Close());
        commandBar.AddMenuItem("Proxy", "Refresh Telemetry", (_, _) => RefreshAllTelemetry());
        commandBar.AddMenuItem("View", "Monitor", (_, _) => mainTabs.SelectedIndex = 0);
        commandBar.AddMenuItem("View", "Local AI Tool Explorer", (_, _) => mainTabs.SelectedIndex = 1);
        commandBar.AddMenuItem("View", "CodeLens Test Bench", (_, _) => mainTabs.SelectedIndex = 2);
        commandBar.AddMenuItem("View", "Monitor MCP Calls", (_, _) => mainTabs.SelectedIndex = 3);
        commandBar.AddMenuItem("View", "Tool Navigator", (_, _) => TogglePanel(workspaceSplit.Panel1));
        commandBar.AddMenuItem("View", "Telemetry", (_, _) => TogglePanel(verticalSplit.Panel2));
        commandBar.AddMenuItem("View", "Current Session", (_, _) => ShowSessionInspectorWindow());
        commandBar.AddMenuItem("Tools", "Open CodeLens Telemetry Folder", (_, _) => telemetryLogControl.OpenLogFolder());
        commandBar.AddMenuItem("Tools", "Open Monitor MCP Telemetry Folder", (_, _) => monitorMcpTelemetryLogControl.OpenLogFolder());
    }

    private void RefreshAllTelemetry()
    {
        telemetryLogControl.RefreshLogs();
        monitorMcpTelemetryLogControl.RefreshLogs();
    }

    private void ApplyInitialSplitterLayout()
    {
        if (splitterLayoutSized)
        {
            return;
        }

        if (workspaceSplit.Width < 900 || verticalSplit.Height < 650)
        {
            BeginInvoke(ApplyInitialSplitterLayout);
            return;
        }

        splitterLayoutSized = true;
        int maxWorkspaceDistance = Math.Max(25, workspaceSplit.Width - workspaceSplit.Panel2MinSize - workspaceSplit.SplitterWidth);
        workspaceSplit.SplitterDistance = Math.Clamp((int)(workspaceSplit.Width * 0.14), 160, maxWorkspaceDistance);

        int maxVerticalDistance = Math.Max(25, verticalSplit.Height - verticalSplit.Panel2MinSize - verticalSplit.SplitterWidth);
        verticalSplit.SplitterDistance = Math.Clamp(verticalSplit.Height - 320, verticalSplit.Panel1MinSize, maxVerticalDistance);
    }

    private void TogglePanel(SplitterPanel panel)
    {
        panelCollapsed(panel, !panelCollapsed(panel));
    }

    private static bool panelCollapsed(SplitterPanel panel)
    {
        SplitContainer? split = panel.Parent as SplitContainer;
        if (split is null)
        {
            return false;
        }

        return ReferenceEquals(panel, split.Panel1) ? split.Panel1Collapsed : split.Panel2Collapsed;
    }

    private static void panelCollapsed(SplitterPanel panel, bool collapsed)
    {
        SplitContainer? split = panel.Parent as SplitContainer;
        if (split is null)
        {
            return;
        }

        if (ReferenceEquals(panel, split.Panel1))
        {
            split.Panel1Collapsed = collapsed;
        }
        else
        {
            split.Panel2Collapsed = collapsed;
        }
    }

    private void ShowSessionInspectorWindow()
    {
        if (sessionInspectorWindow is { IsDisposed: false })
        {
            sessionInspectorWindow.Show();
            sessionInspectorWindow.Activate();
            return;
        }

        sessionInspectorControl = new SessionInspectorControl { Dock = DockStyle.Fill };
        sessionInspectorWindow = new Form
        {
            Text = "Current Session",
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(460, 360),
            MinimumSize = new Size(420, 300)
        };
        sessionInspectorWindow.Controls.Add(sessionInspectorControl);
        sessionInspectorWindow.Show(FindForm());
    }
}
