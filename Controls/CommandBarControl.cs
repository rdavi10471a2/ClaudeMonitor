using System.ComponentModel;
using MonitorBaseClaude.AI;

namespace MonitorBaseClaude.Controls;

[DesignerCategory("Code")]
[AIFileContext("CommandBarControl.cs", "Code-only top menu bar used by the DockPanelSuite dashboard shell.")]
[FileVersion("1.0")]
public sealed class CommandBarControl : UserControl
{
    private readonly MenuStrip menuStrip = new() { Dock = DockStyle.Fill };

    public CommandBarControl()
    {
        Dock = DockStyle.Top;
        Height = 28;
        Controls.Add(menuStrip);
    }

    public void AddMenuItem(string menuName, string itemText, EventHandler handler)
    {
        ToolStripMenuItem? root = menuStrip.Items
            .OfType<ToolStripMenuItem>()
            .FirstOrDefault(item => string.Equals(item.Text, menuName, StringComparison.Ordinal));

        if (root is null)
        {
            root = new ToolStripMenuItem(menuName);
            menuStrip.Items.Add(root);
        }

        ToolStripMenuItem child = new(itemText);
        child.Click += handler;
        root!.DropDownItems.Add(child);
    }
}
