using System.ComponentModel;
using MonitorBaseClaude.AI;

namespace MonitorBaseClaude.Controls;

[DesignerCategory("Code")]
[AIFileContext("SessionInspectorControl.cs", "Code-only docked session inspector for proxy, solution, project, and status details.")]
[FileVersion("1.1")]
public sealed class SessionInspectorControl : UserControl
{
    private readonly Label proxyValue = CreateValueLabel();
    private readonly Label serverValue = CreateValueLabel();
    private readonly Label solutionValue = CreateValueLabel();
    private readonly Label sessionStatusValue = CreateValueLabel();
    private readonly Label projectValue = CreateValueLabel();
    private readonly Label frameworkValue = CreateValueLabel();
    private readonly Label sourceFilesValue = CreateValueLabel();
    private readonly Label packagesValue = CreateValueLabel();
    private readonly Label dependenciesValue = CreateValueLabel();

    public SessionInspectorControl()
    {
        Dock = DockStyle.Fill;
        MinimumSize = new Size(360, 240);
        BuildLayout();
        proxyValue.Text = "Telemetry proxy";
        serverValue.Text = "roslyn-codelens-mcp";
        sessionStatusValue.Text = "Not loaded";
    }

    public void SetStatus(string status)
    {
        sessionStatusValue.Text = status;
    }

    public void LoadSession(RoslynCodeLensMcpClientService.LoadedSolutionSession session)
    {
        solutionValue.Text = session.SolutionName;
        sessionStatusValue.Text = session.Status;
    }

    public void ShowProject(RoslynCodeLensMcpClientService.McpProjectSummary project)
    {
        projectValue.Text = project.Name;
        frameworkValue.Text = project.TargetFramework;
        sourceFilesValue.Text = project.SourceFileCount.ToString();
        packagesValue.Text = project.Packages.Count.ToString();
        dependenciesValue.Text = project.DependencySummary.Count.ToString();
    }

    private void BuildLayout()
    {
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 10,
            Padding = new Padding(10)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 9; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        }

        AddRow(layout, 0, "Proxy", proxyValue);
        AddRow(layout, 1, "Server", serverValue);
        AddRow(layout, 2, "Solution", solutionValue);
        AddRow(layout, 3, "Status", sessionStatusValue);
        AddRow(layout, 4, "Project", projectValue);
        AddRow(layout, 5, "Framework", frameworkValue);
        AddRow(layout, 6, "Files", sourceFilesValue);
        AddRow(layout, 7, "Packages", packagesValue);
        AddRow(layout, 8, "Dependencies", dependenciesValue);
        Controls.Add(layout);
    }

    private static void AddRow(TableLayoutPanel layout, int row, string label, Label value)
    {
        layout.Controls.Add(CreateHeaderLabel(label), 0, row);
        layout.Controls.Add(value, 1, row);
    }

    private static Label CreateHeaderLabel(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Text = text,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private static Label CreateValueLabel()
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            MinimumSize = new Size(180, 0),
            AutoEllipsis = true,
            Text = "-",
            TextAlign = ContentAlignment.MiddleLeft
        };
    }
}
