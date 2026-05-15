using System.ComponentModel;
using MonitorBaseClaude.AI;

namespace MonitorBaseClaude.Controls;

[DesignerCategory("Code")]
[AIFileContext("ToolNavigatorControl.cs", "Code-only left navigation control that groups MCP tools by testing category.")]
[FileVersion("1.0")]
public sealed class ToolNavigatorControl : UserControl
{
    private readonly TreeView treeView = new()
    {
        Dock = DockStyle.Fill,
        HideSelection = false
    };

    public event Action<RoslynCodeLensMcpClientService.McpToolSummary>? ToolSelected;

    public ToolNavigatorControl()
    {
        Dock = DockStyle.Fill;
        Controls.Add(treeView);
        treeView.AfterSelect += TreeView_AfterSelect;
    }

    public void LoadTools(IReadOnlyList<RoslynCodeLensMcpClientService.McpToolSummary> tools)
    {
        treeView.BeginUpdate();
        treeView.Nodes.Clear();

        foreach (IGrouping<string, RoslynCodeLensMcpClientService.McpToolSummary> category in tools
            .GroupBy(tool => tool.Category)
            .OrderBy(group => CategoryOrder(group.Key))
            .ThenBy(group => group.Key, StringComparer.Ordinal))
        {
            TreeNode categoryNode = new(category.Key);
            treeView.Nodes.Add(categoryNode);

            foreach (RoslynCodeLensMcpClientService.McpToolSummary tool in category.OrderBy(tool => tool.Name, StringComparer.Ordinal))
            {
                categoryNode.Nodes.Add(new TreeNode(tool.Name) { Tag = tool });
            }
        }

        foreach (TreeNode node in treeView.Nodes)
        {
            if (node.Text.Equals("Solution", StringComparison.Ordinal))
            {
                node.Expand();
            }
            else
            {
                node.Collapse();
            }
        }
        if (treeView.Nodes.Count > 0 && treeView.Nodes[0].Nodes.Count > 0)
        {
            treeView.SelectedNode = treeView.Nodes[0].Nodes[0];
        }

        treeView.EndUpdate();
    }

    public void SelectTool(RoslynCodeLensMcpClientService.McpToolSummary tool)
    {
        foreach (TreeNode node in treeView.Nodes)
        {
            TreeNode? match = FindToolNode(node, tool);
            if (match is not null)
            {
                treeView.SelectedNode = match;
                match.EnsureVisible();
                return;
            }
        }
    }

    private void TreeView_AfterSelect(object? sender, TreeViewEventArgs e)
    {
        if (e.Node?.Tag is RoslynCodeLensMcpClientService.McpToolSummary tool)
        {
            ToolSelected?.Invoke(tool);
        }
    }

    private static TreeNode? FindToolNode(TreeNode node, RoslynCodeLensMcpClientService.McpToolSummary tool)
    {
        if (node.Tag is RoslynCodeLensMcpClientService.McpToolSummary candidate
            && candidate.Name.Equals(tool.Name, StringComparison.Ordinal))
        {
            return node;
        }

        foreach (TreeNode child in node.Nodes)
        {
            TreeNode? match = FindToolNode(child, tool);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static int CategoryOrder(string category)
    {
        return category switch
        {
            "Solution" => 0,
            "Project" => 1,
            "File / Symbol" => 2,
            "References" => 3,
            "Diagnostics" => 4,
            "Tests" => 5,
            "Code Actions" => 6,
            _ => 99
        };
    }
}
