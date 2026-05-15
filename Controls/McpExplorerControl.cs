using System.ComponentModel;
using MonitorBaseClaude.AI;

namespace MonitorBaseClaude.Controls;

[DesignerCategory("Code")]
[AIFileContext("McpExplorerControl.cs", "Code-only docked explorer showing loaded solution, project, and top-level project folders.")]
[FileVersion("1.0")]
public sealed class McpExplorerControl : UserControl
{
    private readonly TreeView treeView = new()
    {
        Dock = DockStyle.Fill,
        HideSelection = false
    };

    public event Action<RoslynCodeLensMcpClientService.McpProjectSummary>? ProjectSelected;

    public McpExplorerControl()
    {
        Dock = DockStyle.Fill;
        treeView.AfterSelect += TreeView_AfterSelect;
        Controls.Add(treeView);
    }

    public void LoadSession(RoslynCodeLensMcpClientService.LoadedSolutionSession session)
    {
        treeView.Nodes.Clear();
        TreeNode solutionNode = new(session.SolutionName)
        {
            Tag = session
        };
        treeView.Nodes.Add(solutionNode);

        foreach (RoslynCodeLensMcpClientService.McpProjectSummary project in session.Projects)
        {
            TreeNode projectNode = new($"{project.Name} ({project.TargetFramework})")
            {
                Tag = project
            };
            solutionNode.Nodes.Add(projectNode);
            AddProjectFolderNodes(projectNode, project.ProjectPath);
        }

        solutionNode.Expand();
        if (solutionNode.Nodes.Count > 0)
        {
            treeView.SelectedNode = solutionNode.Nodes[0];
        }
    }

    public void SelectProject(RoslynCodeLensMcpClientService.McpProjectSummary project)
    {
        foreach (TreeNode node in treeView.Nodes)
        {
            TreeNode? match = FindProjectNode(node, project);
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
        if (e.Node?.Tag is RoslynCodeLensMcpClientService.McpProjectSummary project)
        {
            ProjectSelected?.Invoke(project);
        }
    }

    private static void AddProjectFolderNodes(TreeNode projectNode, string projectPath)
    {
        string? projectDirectory = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory))
        {
            return;
        }

        foreach (string directory in Directory.GetDirectories(projectDirectory)
            .Where(path => !IsBuildOrIdeFolder(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            projectNode.Nodes.Add(new TreeNode(Path.GetFileName(directory)));
        }
    }

    private static bool IsBuildOrIdeFolder(string path)
    {
        string folderName = Path.GetFileName(path);
        return folderName.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || folderName.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || folderName.Equals(".vs", StringComparison.OrdinalIgnoreCase);
    }

    private static TreeNode? FindProjectNode(TreeNode node, RoslynCodeLensMcpClientService.McpProjectSummary project)
    {
        if (ReferenceEquals(node.Tag, project))
        {
            return node;
        }

        foreach (TreeNode child in node.Nodes)
        {
            TreeNode? match = FindProjectNode(child, project);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }
}
