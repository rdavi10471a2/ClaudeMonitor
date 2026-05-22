using System.ComponentModel;
using System.Text.Json;
using MonitorBaseClaude.AI;
using MonitorBaseClaude.Services;

namespace MonitorBaseClaude.Controls;

[DesignerCategory("Code")]
[AIFileContext("SolutionIndexControl.cs", "Read-only WinForms explorer for the monitor-owned watched solution SQLite index.")]
[FileVersion("1.3")]
public sealed class SolutionIndexControl : UserControl
{
    private readonly SolutionIndexService indexService;
    private readonly TreeView indexTree;
    private readonly ComboBox scopeBox;
    private readonly TextBox valueBox;
    private readonly NumericUpDown maxSymbolsBox;
    private readonly Button rebuildButton;
    private readonly Button refreshFileButton;
    private readonly Button refreshButton;
    private readonly Button queryButton;
    private readonly Label statusLabel;
    private readonly TextBox databasePathBox;
    private readonly DataGridView filesGrid;
    private readonly DataGridView symbolsGrid;
    private readonly DataGridView referencesGrid;
    private readonly DataGridView callersGrid;
    private readonly DataGridView relationshipsGrid;
    private readonly DataGridView diagnosticsGrid;
    private readonly TextBox rawTextBox;
    private readonly Button copyStableKeyButton;
    private readonly Button copySourceAnchorButton;
    private readonly Button copyRawButton;
    private SplitContainer? mainSplit;

    public SolutionIndexControl(MonitorClientSettings settings)
    {
        Dock = DockStyle.Fill;
        indexService = new SolutionIndexService(settings.UiRoot, settings.WatchedSolutionPath);

        indexTree = new TreeView
        {
            Dock = DockStyle.Fill,
            HideSelection = false
        };
        scopeBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 120
        };
        scopeBox.Items.AddRange(["solution", "namespace", "folder", "file"]);
        scopeBox.SelectedIndex = 0;

        valueBox = new TextBox
        {
            Width = 360,
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        maxSymbolsBox = new NumericUpDown
        {
            Minimum = 25,
            Maximum = 5000,
            Increment = 25,
            Value = 500,
            Width = 90
        };
        rebuildButton = new Button { Text = "Rebuild Index", AutoSize = true };
        refreshFileButton = new Button { Text = "Refresh File", AutoSize = true };
        refreshButton = new Button { Text = "Refresh Status", AutoSize = true };
        queryButton = new Button { Text = "Query", AutoSize = true };
        statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        databasePathBox = new TextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true
        };
        filesGrid = CreateGrid();
        symbolsGrid = CreateGrid();
        referencesGrid = CreateGrid();
        callersGrid = CreateGrid();
        relationshipsGrid = CreateGrid();
        diagnosticsGrid = CreateGrid();
        rawTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 9)
        };
        copyStableKeyButton = new Button { Text = "Copy Stable Key", AutoSize = true };
        copySourceAnchorButton = new Button { Text = "Copy Source Anchor", AutoSize = true };
        copyRawButton = new Button { Text = "Copy Raw", AutoSize = true };

        Controls.Add(BuildLayout());
        WireEvents();
        Load += (_, _) =>
        {
            ApplyInitialSplitterLayout();
            RefreshStatus();
        };
    }

    private Control BuildLayout()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));

        TableLayoutPanel toolbar = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 10
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.Controls.Add(new Label { Text = "Scope", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Anchor = AnchorStyles.Left }, 0, 0);
        toolbar.Controls.Add(scopeBox, 1, 0);
        toolbar.Controls.Add(new Label { Text = "Value", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Anchor = AnchorStyles.Left, Margin = new Padding(12, 3, 3, 3) }, 2, 0);
        toolbar.Controls.Add(valueBox, 3, 0);
        toolbar.Controls.Add(new Label { Text = "Max Symbols", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Anchor = AnchorStyles.Left, Margin = new Padding(12, 3, 3, 3) }, 4, 0);
        toolbar.Controls.Add(maxSymbolsBox, 5, 0);
        toolbar.Controls.Add(queryButton, 6, 0);
        toolbar.Controls.Add(refreshFileButton, 7, 0);
        toolbar.Controls.Add(refreshButton, 8, 0);
        toolbar.Controls.Add(rebuildButton, 9, 0);

        TableLayoutPanel statusRow = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2
        };
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        statusRow.Controls.Add(statusLabel, 0, 0);
        statusRow.Controls.Add(databasePathBox, 1, 0);

        mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel1,
            SplitterWidth = 8
        };
        GroupBox treeGroup = new()
        {
            Text = "Index",
            Dock = DockStyle.Fill
        };
        treeGroup.Controls.Add(indexTree);

        SplitContainer detailSplit = new()
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 8
        };

        SplitContainer lowerDetailSplit = new()
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 8
        };

        GroupBox filesGroup = new()
        {
            Text = "Files",
            Dock = DockStyle.Fill
        };
        filesGroup.Controls.Add(filesGrid);

        GroupBox symbolsGroup = new()
        {
            Text = "Symbols",
            Dock = DockStyle.Fill
        };
        symbolsGroup.Controls.Add(symbolsGrid);

        TabControl inspectorTabs = new()
        {
            Dock = DockStyle.Fill
        };
        inspectorTabs.TabPages.Add(BuildGridTab("References", referencesGrid));
        inspectorTabs.TabPages.Add(BuildGridTab("Callers", callersGrid));
        inspectorTabs.TabPages.Add(BuildGridTab("Relationships", relationshipsGrid));
        inspectorTabs.TabPages.Add(BuildGridTab("Diagnostics", diagnosticsGrid));
        inspectorTabs.TabPages.Add(BuildRawTab());
        detailSplit.Panel1.Controls.Add(filesGroup);
        lowerDetailSplit.Panel1.Controls.Add(symbolsGroup);
        lowerDetailSplit.Panel2.Controls.Add(inspectorTabs);
        detailSplit.Panel2.Controls.Add(lowerDetailSplit);
        mainSplit.Panel1.Controls.Add(treeGroup);
        mainSplit.Panel2.Controls.Add(detailSplit);

        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(statusRow, 0, 1);
        root.Controls.Add(mainSplit, 0, 2);
        return root;
    }

    private static TabPage BuildGridTab(string title, DataGridView grid)
    {
        TabPage page = new(title);
        page.Controls.Add(grid);
        return page;
    }

    private TabPage BuildRawTab()
    {
        TabPage page = new("Raw");
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        buttons.Controls.Add(copyStableKeyButton);
        buttons.Controls.Add(copySourceAnchorButton);
        buttons.Controls.Add(copyRawButton);

        layout.Controls.Add(buttons, 0, 0);
        layout.Controls.Add(rawTextBox, 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private void WireEvents()
    {
        refreshButton.Click += (_, _) => RefreshStatus();
        refreshFileButton.Click += (_, _) => RefreshSelectedFile();
        queryButton.Click += (_, _) => QueryIndex();
        copyStableKeyButton.Click += (_, _) => CopySelectedSymbolValue(symbol => symbol.StableSymbolKey);
        copySourceAnchorButton.Click += (_, _) => CopySelectedSymbolValue(symbol => symbol.SourceAnchor);
        copyRawButton.Click += (_, _) => CopyText(rawTextBox.Text);
        rebuildButton.Click += async (_, _) => await RebuildIndexAsync();
        indexTree.AfterSelect += (_, args) => QueryTreeNode(args.Node);
        filesGrid.SelectionChanged += (_, _) => RefreshDiagnosticsInspector();
        symbolsGrid.SelectionChanged += (_, _) => RefreshSymbolInspector(showSelectionMessage: false);
        scopeBox.SelectedIndexChanged += (_, _) =>
        {
            valueBox.Enabled = !string.Equals(scopeBox.Text, "solution", StringComparison.OrdinalIgnoreCase);
        };
    }

    private async Task RebuildIndexAsync()
    {
        SetBusy(true);
        try
        {
            SolutionIndexBuildResult result = await Task.Run(indexService.Rebuild);
            ApplyStatus(result.Status);
            LoadTree();
            QueryIndex();
        }
        catch (Exception ex)
        {
            statusLabel.Text = ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void RefreshStatus()
    {
        try
        {
            ApplyStatus(indexService.GetStatus());
            LoadTree();
        }
        catch (Exception ex)
        {
            statusLabel.Text = ex.Message;
        }
    }

    private void QueryIndex()
    {
        try
        {
            SolutionIndexQueryResult result = indexService.Query(scopeBox.Text, valueBox.Text, 500, (int)maxSymbolsBox.Value);
            filesGrid.DataSource = result.Files.ToList();
            symbolsGrid.DataSource = result.Symbols.ToList();
            FormatSymbolsGrid();
            ClearInspector();
            RefreshDiagnosticsInspector();
            ApplyStatus(indexService.GetStatus());
        }
        catch (Exception ex)
        {
            statusLabel.Text = ex.Message;
        }
    }

    private void RefreshSelectedFile()
    {
        try
        {
            string? path = ResolveSelectedFilePath();
            if (string.IsNullOrWhiteSpace(path))
            {
                statusLabel.Text = "Select a file node, or set Scope=file and Value to a C# path.";
                return;
            }

            SolutionIndexFileRefreshResult result = indexService.RefreshFile(path);
            ApplyStatus(result.Status);
            LoadTree();
            scopeBox.SelectedItem = "file";
            valueBox.Text = path;
            QueryIndex();
        }
        catch (Exception ex)
        {
            statusLabel.Text = ex.Message;
        }
    }

    private void RefreshSymbolInspector(bool showSelectionMessage)
    {
        try
        {
            SolutionIndexSymbol? symbol = GetSelectedSymbol();
            string? stableKey = symbol?.StableSymbolKey;
            if (symbol is null || string.IsNullOrWhiteSpace(stableKey))
            {
                referencesGrid.DataSource = null;
                callersGrid.DataSource = null;
                relationshipsGrid.DataSource = null;
                rawTextBox.Clear();
                if (showSelectionMessage)
                {
                    statusLabel.Text = "Select a symbol row first.";
                }

                return;
            }

            IReadOnlyList<SolutionIndexReference> references = indexService.FindReferences(stableKey);
            IReadOnlyList<SolutionIndexReference> callers = indexService.FindCallers(stableKey);
            IReadOnlyList<SolutionIndexRelationship> relationships = indexService.FindRelationships(stableKey);
            referencesGrid.DataSource = references.ToList();
            callersGrid.DataSource = callers.ToList();
            relationshipsGrid.DataSource = relationships.ToList();
            FormatReferencesGrid();
            FormatCallersGrid();
            FormatRelationshipsGrid();
            RefreshDiagnosticsInspector();
            rawTextBox.Text = FormatRawSelection(symbol, references, callers, relationships);
            SolutionIndexStatus status = indexService.GetStatus();
            ApplyStatus(status);
            statusLabel.Text += $" | Refs: {references.Count} | Callers: {callers.Count} | Relationships: {relationships.Count}";
        }
        catch (Exception ex)
        {
            statusLabel.Text = ex.Message;
        }
    }

    private void RefreshDiagnosticsInspector()
    {
        try
        {
            string? filePath = GetSelectedFile()?.RelativePath ?? GetSelectedSymbol()?.RelativePath;
            IReadOnlyList<SolutionIndexDiagnostic> diagnostics = indexService.FindDiagnostics(filePath, maxResults: 500);
            diagnosticsGrid.DataSource = diagnostics.ToList();
            FormatDiagnosticsGrid();
        }
        catch (Exception ex)
        {
            statusLabel.Text = ex.Message;
        }
    }

    private void ClearInspector()
    {
        referencesGrid.DataSource = null;
        callersGrid.DataSource = null;
        relationshipsGrid.DataSource = null;
        diagnosticsGrid.DataSource = null;
        rawTextBox.Clear();
    }

    private SolutionIndexSymbol? GetSelectedSymbol()
    {
        if (symbolsGrid.CurrentRow?.DataBoundItem is SolutionIndexSymbol symbol)
        {
            return symbol;
        }

        if (symbolsGrid.SelectedRows.Count > 0
            && symbolsGrid.SelectedRows[0].DataBoundItem is SolutionIndexSymbol selected)
        {
            return selected;
        }

        return null;
    }

    private SolutionIndexFile? GetSelectedFile()
    {
        if (filesGrid.CurrentRow?.DataBoundItem is SolutionIndexFile file)
        {
            return file;
        }

        if (filesGrid.SelectedRows.Count > 0
            && filesGrid.SelectedRows[0].DataBoundItem is SolutionIndexFile selected)
        {
            return selected;
        }

        return null;
    }

    private string? ResolveSelectedFilePath()
    {
        if (indexTree.SelectedNode?.Tag is IndexTreeTag { Scope: "file" } tag)
        {
            return tag.Value;
        }

        return string.Equals(scopeBox.Text, "file", StringComparison.OrdinalIgnoreCase)
            ? valueBox.Text
            : null;
    }

    private void LoadTree()
    {
        IndexTreeTag? selectedTag = indexTree.SelectedNode?.Tag as IndexTreeTag;
        SolutionIndexTree tree = indexService.GetTree();
        indexTree.BeginUpdate();
        try
        {
            indexTree.Nodes.Clear();
            TreeNode root = new(Path.GetFileNameWithoutExtension(tree.Status.WatchedSolutionPath))
            {
                Tag = new IndexTreeTag("solution", null)
            };
            foreach (SolutionIndexNamespaceNode namespaceNode in tree.Namespaces)
            {
                TreeNode nsNode = new(namespaceNode.Namespace)
                {
                    Tag = new IndexTreeTag("namespace", namespaceNode.Namespace)
                };
                foreach (string file in namespaceNode.Files)
                {
                    nsNode.Nodes.Add(new TreeNode(file)
                    {
                        Tag = new IndexTreeTag("file", file)
                    });
                }

                root.Nodes.Add(nsNode);
            }

            indexTree.Nodes.Add(root);
            root.Expand();
            RestoreSelectedNode(root, selectedTag);
        }
        finally
        {
            indexTree.EndUpdate();
        }
    }

    private void QueryTreeNode(TreeNode? node)
    {
        if (node?.Tag is not IndexTreeTag tag)
        {
            return;
        }

        scopeBox.SelectedItem = tag.Scope;
        valueBox.Text = tag.Value ?? string.Empty;
        QueryIndex();
    }

    private void RestoreSelectedNode(TreeNode root, IndexTreeTag? selectedTag)
    {
        if (selectedTag is null)
        {
            return;
        }

        TreeNode? match = FindNode(root, selectedTag);
        if (match is not null)
        {
            indexTree.SelectedNode = match;
            match.EnsureVisible();
        }
    }

    private static TreeNode? FindNode(TreeNode node, IndexTreeTag tag)
    {
        if (node.Tag is IndexTreeTag candidate
            && candidate.Scope.Equals(tag.Scope, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.Value, tag.Value, StringComparison.OrdinalIgnoreCase))
        {
            return node;
        }

        foreach (TreeNode child in node.Nodes)
        {
            TreeNode? match = FindNode(child, tag);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private void ApplyStatus(SolutionIndexStatus status)
    {
        string indexedText = status.LastIndexedAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "not built";
        statusLabel.Text = $"Indexed: {indexedText} | Files: {status.FileCount} | Symbols: {status.SymbolCount} | References: {status.ReferenceCount} | Calls: {status.CallSiteCount} | Relationships: {status.RelationshipCount} | Diagnostics: {status.DiagnosticCount} | Stale: {status.StaleFileCount}";
        databasePathBox.Text = status.DatabasePath;
    }

    private void SetBusy(bool busy)
    {
        rebuildButton.Enabled = !busy;
        refreshFileButton.Enabled = !busy;
        refreshButton.Enabled = !busy;
        queryButton.Enabled = !busy;
        copyStableKeyButton.Enabled = !busy;
        copySourceAnchorButton.Enabled = !busy;
        copyRawButton.Enabled = !busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        statusLabel.Text = busy ? "Rebuilding solution index..." : statusLabel.Text;
    }

    private void ApplyInitialSplitterLayout()
    {
        if (mainSplit is null || mainSplit.Width <= 0)
        {
            return;
        }

        mainSplit.Panel1MinSize = Math.Min(220, Math.Max(25, mainSplit.Width / 3));
        mainSplit.Panel2MinSize = Math.Min(420, Math.Max(25, mainSplit.Width / 2));
        int maxDistance = Math.Max(mainSplit.Panel1MinSize, mainSplit.Width - mainSplit.Panel2MinSize - mainSplit.SplitterWidth);
        mainSplit.SplitterDistance = Math.Clamp(300, mainSplit.Panel1MinSize, maxDistance);
    }

    private static DataGridView CreateGrid()
    {
        return new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoGenerateColumns = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle
        };
    }

    private void FormatSymbolsGrid()
    {
        SetDisplayIndex(symbolsGrid, nameof(SolutionIndexSymbol.RelativePath), 0);
        SetDisplayIndex(symbolsGrid, nameof(SolutionIndexSymbol.Name), 1);
        SetDisplayIndex(symbolsGrid, nameof(SolutionIndexSymbol.Kind), 2);
        SetDisplayIndex(symbolsGrid, nameof(SolutionIndexSymbol.ContainingType), 3);
        SetDisplayIndex(symbolsGrid, nameof(SolutionIndexSymbol.Signature), 4);
        SetDisplayIndex(symbolsGrid, nameof(SolutionIndexSymbol.SourceAnchor), 5);
        SetDisplayIndex(symbolsGrid, nameof(SolutionIndexSymbol.StableSymbolKey), 6);
        SetDisplayIndex(symbolsGrid, nameof(SolutionIndexSymbol.SelectorJson), 7);
        SetDisplayIndex(symbolsGrid, nameof(SolutionIndexSymbol.FileHash), 8);
        SetDisplayIndex(symbolsGrid, nameof(SolutionIndexSymbol.SymbolTextHash), 9);
    }

    private void FormatReferencesGrid()
    {
        SetDisplayIndex(referencesGrid, nameof(SolutionIndexReference.RelativePath), 0);
        SetDisplayIndex(referencesGrid, nameof(SolutionIndexReference.Line), 1);
        SetDisplayIndex(referencesGrid, nameof(SolutionIndexReference.Column), 2);
        SetDisplayIndex(referencesGrid, nameof(SolutionIndexReference.ReferenceKind), 3);
        SetDisplayIndex(referencesGrid, nameof(SolutionIndexReference.CallerName), 4);
        SetDisplayIndex(referencesGrid, nameof(SolutionIndexReference.Snippet), 5);
        SetDisplayIndex(referencesGrid, nameof(SolutionIndexReference.CallerStableSymbolKey), 6);
        SetDisplayIndex(referencesGrid, nameof(SolutionIndexReference.TargetStableSymbolKey), 7);
    }

    private void FormatCallersGrid()
    {
        SetDisplayIndex(callersGrid, nameof(SolutionIndexReference.RelativePath), 0);
        SetDisplayIndex(callersGrid, nameof(SolutionIndexReference.Line), 1);
        SetDisplayIndex(callersGrid, nameof(SolutionIndexReference.Column), 2);
        SetDisplayIndex(callersGrid, nameof(SolutionIndexReference.CallerName), 3);
        SetDisplayIndex(callersGrid, nameof(SolutionIndexReference.Snippet), 4);
        SetDisplayIndex(callersGrid, nameof(SolutionIndexReference.CallerStableSymbolKey), 5);
        SetDisplayIndex(callersGrid, nameof(SolutionIndexReference.TargetStableSymbolKey), 6);
    }

    private void FormatRelationshipsGrid()
    {
        SetDisplayIndex(relationshipsGrid, nameof(SolutionIndexRelationship.RelationshipKind), 0);
        SetDisplayIndex(relationshipsGrid, nameof(SolutionIndexRelationship.SourceName), 1);
        SetDisplayIndex(relationshipsGrid, nameof(SolutionIndexRelationship.SourceKind), 2);
        SetDisplayIndex(relationshipsGrid, nameof(SolutionIndexRelationship.TargetName), 3);
        SetDisplayIndex(relationshipsGrid, nameof(SolutionIndexRelationship.TargetKind), 4);
        SetDisplayIndex(relationshipsGrid, nameof(SolutionIndexRelationship.RelativePath), 5);
        SetDisplayIndex(relationshipsGrid, nameof(SolutionIndexRelationship.Line), 6);
        SetDisplayIndex(relationshipsGrid, nameof(SolutionIndexRelationship.Column), 7);
        SetDisplayIndex(relationshipsGrid, nameof(SolutionIndexRelationship.Snippet), 8);
        SetDisplayIndex(relationshipsGrid, nameof(SolutionIndexRelationship.MetadataJson), 9);
    }

    private void FormatDiagnosticsGrid()
    {
        SetDisplayIndex(diagnosticsGrid, nameof(SolutionIndexDiagnostic.RelativePath), 0);
        SetDisplayIndex(diagnosticsGrid, nameof(SolutionIndexDiagnostic.Severity), 1);
        SetDisplayIndex(diagnosticsGrid, nameof(SolutionIndexDiagnostic.DiagnosticId), 2);
        SetDisplayIndex(diagnosticsGrid, nameof(SolutionIndexDiagnostic.StartLine), 3);
        SetDisplayIndex(diagnosticsGrid, nameof(SolutionIndexDiagnostic.EndLine), 4);
        SetDisplayIndex(diagnosticsGrid, nameof(SolutionIndexDiagnostic.Message), 5);
        SetDisplayIndex(diagnosticsGrid, nameof(SolutionIndexDiagnostic.FileHash), 6);
    }

    private static string FormatRawSelection(
        SolutionIndexSymbol symbol,
        IReadOnlyList<SolutionIndexReference> references,
        IReadOnlyList<SolutionIndexReference> callers,
        IReadOnlyList<SolutionIndexRelationship> relationships)
    {
        object raw = new
        {
            Symbol = symbol,
            Counts = new
            {
                References = references.Count,
                Callers = callers.Count,
                Relationships = relationships.Count
            },
            References = references,
            Callers = callers,
            Relationships = relationships
        };
        return JsonSerializer.Serialize(raw, new JsonSerializerOptions { WriteIndented = true });
    }

    private void CopySelectedSymbolValue(Func<SolutionIndexSymbol, string> selector)
    {
        SolutionIndexSymbol? symbol = GetSelectedSymbol();
        if (symbol is null)
        {
            statusLabel.Text = "Select a symbol row first.";
            return;
        }

        CopyText(selector(symbol));
    }

    private void CopyText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            statusLabel.Text = "Nothing to copy.";
            return;
        }

        Clipboard.SetText(text);
        statusLabel.Text = "Copied to clipboard.";
    }

    private static void SetDisplayIndex(DataGridView grid, string columnName, int displayIndex)
    {
        if (grid.Columns[columnName] is DataGridViewColumn column)
        {
            column.DisplayIndex = Math.Min(displayIndex, grid.Columns.Count - 1);
        }
    }

    private sealed record IndexTreeTag(string Scope, string? Value);
}
