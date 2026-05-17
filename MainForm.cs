using System.ComponentModel;
using MonitorBaseClaude.AI;
using MonitorBaseClaude.Controls;

namespace MonitorBaseClaude;

[DesignerCategory("Code")]
[AIFileContext("MainForm.cs", "Code-only shell form that hosts the MCP monitor dashboard without designer-generated layout files.")]
[FileVersion("2.0")]
public sealed class MainForm : Form
{
    private readonly MonitorDashboardControl dashboard;
    private readonly McpProxyHubService proxyHub;

    public MainForm()
    {
        Text = "MonitorBaseClaude MCP Client";
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1100, 720);

        MonitorClientSettings settings = MonitorClientSettings.Load();
        proxyHub = new McpProxyHubService(settings, this);
        dashboard = new MonitorDashboardControl
        {
            Dock = DockStyle.Fill
        };

        Controls.Add(dashboard);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            proxyHub.Dispose();
        }

        base.Dispose(disposing);
    }
}
