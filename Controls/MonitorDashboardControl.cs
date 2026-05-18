using System.ComponentModel;
using MonitorBaseClaude.AI;

namespace MonitorBaseClaude.Controls;

[DesignerCategory("Code")]
[AIFileContext("MonitorDashboardControl.cs", "Code-only monitor shell using deterministic split containers for tool navigation, MCP testing, and telemetry panes.")]
[FileVersion("2.5")]
public sealed class MonitorDashboardControl : UserControl
{
    private const int FriendlySplitterWidth = 12;

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
            SplitterWidth = FriendlySplitterWidth,
            BackColor = SystemColors.ControlDark
        };
        workspaceSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel1,
            SplitterWidth = FriendlySplitterWidth,
            BackColor = SystemColors.ControlDark
        };
        verticalSplit.Panel1.BackColor = SystemColors.Control;
        verticalSplit.Panel2.BackColor = SystemColors.Control;
        workspaceSplit.Panel1.BackColor = SystemColors.Control;
        workspaceSplit.Panel2.BackColor = SystemColors.Control;

        monitorMcpClientService = new MonitorMcpClientService(settings);
        ollamaToolExplorerService = new OllamaToolExplorerService(settings);
        localMcpDiscoveryService = new LocalMcpDiscoveryService(settings);
        monitorHomeControl = new MonitorHomeControl(settings, monitorMcpClientService) { Dock = DockStyle.Fill };
        ollamaToolExplorerControl = new OllamaToolExplorerControl(settings, monitorMcpClientService, ollamaToolExplorerService, localMcpDiscoveryService) { Dock = DockStyle.Fill };
        toolNavigatorControl = new ToolNavigatorControl { Dock = DockStyle.Fill, MinimumSize = new Size(220, 200) };
        testBenchControl = new McpTestBenchControl(mcpClientService, settings) { Dock = DockStyle.Fill, MinimumSize = new Size(850, 360) };
        telemetryLogControl = new TelemetryLogControl(TelemetryLogControl.ResolveRoslynCodeLensLogRoot(settings), "Roslyn Tooling")
        {
            Dock = DockStyle.Fill,
            MinimumSize = new Size(900, 260)
        };
        monitorMcpTelemetryLogControl = new TelemetryLogControl(TelemetryLogControl.ResolveMonitorMcpLogRoot(settings), "System Monitor")
        {
            Dock = DockStyle.Fill,
            MinimumSize = new Size(850, 360)
        };

        workspaceSplit.Panel1.Controls.Add(toolNavigatorControl);
        workspaceSplit.Panel2.Controls.Add(testBenchControl);
        TabPage systemMonitorPage = new("System Monitor");
        TabPage roslynToolingPage = new("Roslyn Tooling");
        systemMonitorPage.Controls.Add(monitorMcpTelemetryLogControl);
        roslynToolingPage.Controls.Add(telemetryLogControl);
        mainTabs.TabPages.Add(systemMonitorPage);
        mainTabs.TabPages.Add(roslynToolingPage);
        mainTabs.SelectedIndex = 0;
        verticalSplit.Panel1.Controls.Add(mainTabs);
        verticalSplit.Panel2Collapsed = true;
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
        testBenchControl.SolutionLoaded += session =>
        {
            toolNavigatorControl.LoadTools(session.Tools);
            sessionInspectorControl?.LoadSession(session);
        };
        testBenchControl.ProjectSelected += project => sessionInspectorControl?.ShowProject(project);
    }

    private void RegisterMenu()
    {
        commandBar.AddMenuItem("File", "Exit", (_, _) => FindForm()?.Close());
        commandBar.AddMenuItem("Proxy", "Refresh Telemetry", (_, _) => RefreshAllTelemetry());
        commandBar.AddMenuItem("View", "System Monitor", (_, _) => mainTabs.SelectedIndex = 0);
        commandBar.AddMenuItem("View", "Roslyn Tooling", (_, _) => mainTabs.SelectedIndex = 1);
        commandBar.AddMenuItem("View", "Current Session", (_, _) => ShowSessionInspectorWindow());
        commandBar.AddMenuItem("Tools", "Open CodeLens Telemetry Folder", (_, _) => telemetryLogControl.OpenLogFolder());
        commandBar.AddMenuItem("Tools", "Open System Monitor Telemetry Folder", (_, _) => monitorMcpTelemetryLogControl.OpenLogFolder());
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

        if (verticalSplit.Height < 650)
        {
            BeginInvoke(ApplyInitialSplitterLayout);
            return;
        }

        splitterLayoutSized = true;

        if (!verticalSplit.Panel2Collapsed)
        {
            int maxVerticalDistance = Math.Max(25, verticalSplit.Height - verticalSplit.Panel2MinSize - verticalSplit.SplitterWidth);
            verticalSplit.SplitterDistance = Math.Clamp(verticalSplit.Height - 320, verticalSplit.Panel1MinSize, maxVerticalDistance);
        }
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
