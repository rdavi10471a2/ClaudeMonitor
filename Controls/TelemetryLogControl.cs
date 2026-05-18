using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using MonitorBaseClaude.AI;

namespace MonitorBaseClaude.Controls;

[DesignerCategory("Code")]
[AIFileContext("TelemetryLogControl.cs", "Code-only telemetry viewer for proxy JSONL request, response, error, and stderr logs.")]
[FileVersion("1.3")]
public sealed class TelemetryLogControl : UserControl
{
    private const int FriendlySplitterWidth = 12;

    private readonly string logRoot;
    private readonly string title;
    private readonly DataGridView requestsGrid = new();
    private readonly DataGridView callsGrid = new();
    private readonly DataGridView errorsGrid = new();
    private readonly RichTextBox stderrBox = new();
    private readonly Button refreshButton = new();
    private readonly Button openFolderButton = new();
    private readonly CheckBox autoRefreshCheckBox = new();

    public TelemetryLogControl()
        : this(ResolveRoslynLogRoot())
    {
    }

    public TelemetryLogControl(string logRoot)
        : this(logRoot, "MCP Traffic")
    {
    }

    public TelemetryLogControl(string logRoot, string title)
    {
        Dock = DockStyle.Fill;
        this.logRoot = logRoot;
        this.title = title;
        BuildLayout();
        McpProxyHubService.TelemetryRecorded += OnHubTelemetryRecorded;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            McpProxyHubService.TelemetryRecorded -= OnHubTelemetryRecorded;
        }

        base.Dispose(disposing);
    }

    public void OpenLogFolder()
    {
        Directory.CreateDirectory(logRoot);
        Process.Start(new ProcessStartInfo
        {
            FileName = logRoot,
            UseShellExecute = true
        });
    }

    public void RefreshLogs()
    {
        LoadRequests();
        LoadCalls();
        LoadErrors();
        LoadStderr();
    }

    private void BuildLayout()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        TableLayoutPanel buttons = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            Padding = new Padding(8, 5, 0, 0),
            MinimumSize = new Size(0, 38)
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 136));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttons.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        refreshButton.Text = "Refresh";
        refreshButton.Dock = DockStyle.Fill;
        refreshButton.Click += (_, _) => RefreshLogs();
        openFolderButton.Text = "Open Log Folder";
        openFolderButton.Dock = DockStyle.Fill;
        openFolderButton.Click += (_, _) => OpenLogFolder();
        autoRefreshCheckBox.Text = "Auto";
        autoRefreshCheckBox.AutoSize = true;
        autoRefreshCheckBox.Checked = true;
        autoRefreshCheckBox.Dock = DockStyle.Fill;
        autoRefreshCheckBox.Margin = new Padding(8, 3, 0, 0);
        buttons.Controls.Add(refreshButton, 0, 0);
        buttons.Controls.Add(openFolderButton, 1, 0);
        buttons.Controls.Add(autoRefreshCheckBox, 2, 0);
        buttons.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            Padding = new Padding(0, 0, 12, 0)
        }, 3, 0);

        TabControl tabs = new()
        {
            Dock = DockStyle.Fill
        };
        tabs.TabPages.Add(CreateTab("Traffic", CreateTrafficLayout()));
        tabs.TabPages.Add(CreateTab("Errors", errorsGrid));
        tabs.TabPages.Add(CreateTab("stderr", stderrBox));

        ConfigureTrafficGrid(requestsGrid, "Source", "Time", "Method", "Tool", "PID", "Process", "Arguments");
        ConfigureTrafficGrid(callsGrid, "Source", "Time", "Direction", "Method", "Tool", "ms", "Bytes", "Error");
        ConfigureGrid(errorsGrid, "Source", "Time", "Event", "Message");
        stderrBox.Dock = DockStyle.Fill;
        stderrBox.ReadOnly = true;
        stderrBox.BorderStyle = BorderStyle.None;
        stderrBox.Font = new Font("Consolas", 9.5F);
        stderrBox.WordWrap = false;

        root.Controls.Add(buttons, 0, 0);
        root.Controls.Add(tabs, 0, 1);
        Controls.Add(root);
    }

    private static TabPage CreateTab(string title, Control content)
    {
        TabPage tab = new(title);
        content.Dock = DockStyle.Fill;
        tab.Controls.Add(content);
        return tab;
    }

    private Control CreateTrafficLayout()
    {
        SplitContainer trafficSplit = new()
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = FriendlySplitterWidth,
            BackColor = SystemColors.ControlDark
        };
        trafficSplit.Panel1.BackColor = SystemColors.Control;
        trafficSplit.Panel2.BackColor = SystemColors.Control;

        trafficSplit.Panel1.Controls.Add(CreateLabeledPanel("Requests", requestsGrid));
        trafficSplit.Panel2.Controls.Add(CreateLabeledPanel("Responses", callsGrid));
        return trafficSplit;
    }

    private static Control CreateLabeledPanel(string title, Control content)
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Label label = new()
        {
            Text = title,
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 4, 0, 0),
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold)
        };

        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(content, 0, 1);
        return panel;
    }

    private static void ConfigureGrid(DataGridView grid, params string[] columns)
    {
        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.ReadOnly = true;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        foreach (string column in columns)
        {
            grid.Columns.Add(column, column);
        }
    }

    private static void ConfigureTrafficGrid(DataGridView grid, params string[] columns)
    {
        ConfigureGrid(grid, columns);
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        grid.ScrollBars = ScrollBars.Both;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        grid.AllowUserToResizeColumns = true;
        grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;

        SetColumnWidth(grid, "Source", 64);
        SetColumnWidth(grid, "Time", 96);
        SetColumnWidth(grid, "Direction", 78);
        SetColumnWidth(grid, "Method", 130);
        SetColumnWidth(grid, "Tool", 220);
        SetColumnWidth(grid, "PID", 72);
        SetColumnWidth(grid, "Process", 260);
        SetColumnWidth(grid, "ms", 64);
        SetColumnWidth(grid, "Bytes", 72);
        SetColumnWidth(grid, "Error", 70);
        SetColumnWidth(grid, "Arguments", 1400);
    }

    private static void SetColumnWidth(DataGridView grid, string name, int width)
    {
        if (grid.Columns[name] is { } column)
        {
            column.Width = width;
            column.MinimumWidth = Math.Min(width, 80);
        }
    }

    private void LoadRequests()
    {
        requestsGrid.Rows.Clear();
        foreach (JsonObject entry in ReadJsonLines("requests.jsonl").TakeLast(200))
        {
            requestsGrid.Rows.Add(
                "history",
                ShortTime(entry["timestampUtc"]?.GetValue<string>()),
                entry["method"]?.GetValue<string>() ?? string.Empty,
                entry["tool"]?.GetValue<string>() ?? string.Empty,
                entry["processId"]?.ToString() ?? string.Empty,
                ShortProcessPath(entry["processPath"]?.GetValue<string>()),
                CompactJson(entry["arguments"]));
        }

        ScrollToLastRow(requestsGrid);
    }

    private void LoadCalls()
    {
        callsGrid.Rows.Clear();
        foreach (JsonObject entry in ReadJsonLines("responses.jsonl").TakeLast(200))
        {
            callsGrid.Rows.Add(
                "history",
                ShortTime(entry["timestampUtc"]?.GetValue<string>()),
                entry["direction"]?.GetValue<string>() ?? string.Empty,
                entry["method"]?.GetValue<string>() ?? string.Empty,
                entry["tool"]?.GetValue<string>() ?? string.Empty,
                entry["elapsedMs"]?.ToString() ?? string.Empty,
                entry["messageBytes"]?.ToString() ?? string.Empty,
                entry["isError"]?.ToString() ?? string.Empty);
        }

        ScrollToLastRow(callsGrid);
    }

    private void LoadErrors()
    {
        errorsGrid.Rows.Clear();
        foreach (JsonObject entry in ReadJsonLines("errors.jsonl").TakeLast(200))
        {
            errorsGrid.Rows.Add(
                "history",
                ShortTime(entry["timestampUtc"]?.GetValue<string>()),
                entry["event"]?.GetValue<string>() ?? string.Empty,
                entry["message"]?.GetValue<string>() ?? string.Empty);
        }

        ScrollToLastRow(errorsGrid);
    }

    private void LoadStderr()
    {
        stderrBox.Clear();
        foreach (JsonObject entry in ReadJsonLines("stderr.jsonl").TakeLast(300))
        {
            stderrBox.AppendText($"[{ShortTime(entry["timestampUtc"]?.GetValue<string>())}] {entry["message"]?.GetValue<string>()}{Environment.NewLine}");
        }

        stderrBox.SelectionStart = stderrBox.TextLength;
        stderrBox.ScrollToCaret();
    }

    private void OnHubTelemetryRecorded(object? sender, McpHubTelemetryRecord record)
    {
        if (!autoRefreshCheckBox.Checked)
        {
            return;
        }

        if (!string.Equals(Path.GetFullPath(record.LogRoot), Path.GetFullPath(logRoot), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => AddLiveTelemetry(record));
            return;
        }

        AddLiveTelemetry(record);
    }

    private void AddLiveTelemetry(McpHubTelemetryRecord record)
    {
        JsonObject entry = record.Entry;
        switch (record.FileName)
        {
            case "requests.jsonl":
                requestsGrid.Rows.Add(
                    "live",
                    ShortTime(entry["timestampUtc"]?.GetValue<string>()),
                    entry["method"]?.GetValue<string>() ?? string.Empty,
                    entry["tool"]?.GetValue<string>() ?? string.Empty,
                    entry["processId"]?.ToString() ?? entry["hubProcessId"]?.ToString() ?? string.Empty,
                    ShortProcessPath(entry["processPath"]?.GetValue<string>() ?? entry["hubProcessPath"]?.GetValue<string>()),
                    CompactJson(entry["arguments"]));
                TrimGrid(requestsGrid, 250);
                ScrollToLastRow(requestsGrid);
                break;
            case "responses.jsonl":
                callsGrid.Rows.Add(
                    "live",
                    ShortTime(entry["timestampUtc"]?.GetValue<string>()),
                    entry["direction"]?.GetValue<string>() ?? string.Empty,
                    entry["method"]?.GetValue<string>() ?? string.Empty,
                    entry["tool"]?.GetValue<string>() ?? string.Empty,
                    entry["elapsedMs"]?.ToString() ?? string.Empty,
                    entry["messageBytes"]?.ToString() ?? string.Empty,
                    entry["isError"]?.ToString() ?? string.Empty);
                TrimGrid(callsGrid, 250);
                ScrollToLastRow(callsGrid);
                break;
            case "errors.jsonl":
                errorsGrid.Rows.Add(
                    "live",
                    ShortTime(entry["timestampUtc"]?.GetValue<string>()),
                    entry["event"]?.GetValue<string>() ?? string.Empty,
                    entry["message"]?.GetValue<string>() ?? string.Empty);
                TrimGrid(errorsGrid, 250);
                ScrollToLastRow(errorsGrid);
                break;
            case "stderr.jsonl":
                stderrBox.AppendText($"[{ShortTime(entry["timestampUtc"]?.GetValue<string>())}] {entry["message"]?.GetValue<string>()}{Environment.NewLine}");
                stderrBox.SelectionStart = stderrBox.TextLength;
                stderrBox.ScrollToCaret();
                break;
        }
    }

    private static void ScrollToLastRow(DataGridView grid)
    {
        if (grid.Rows.Count == 0 || !grid.IsHandleCreated || grid.DisplayedRowCount(includePartialRow: true) == 0)
        {
            return;
        }

        int lastIndex = grid.Rows.Count - 1;
        grid.ClearSelection();
        grid.Rows[lastIndex].Selected = true;
        try
        {
            grid.FirstDisplayedScrollingRowIndex = lastIndex;
        }
        catch (InvalidOperationException)
        {
            // The grid may still be measuring inside a newly-created split panel.
        }
    }

    private static void TrimGrid(DataGridView grid, int maxRows)
    {
        while (grid.Rows.Count > maxRows)
        {
            grid.Rows.RemoveAt(0);
        }
    }

    private IEnumerable<JsonObject> ReadJsonLines(string fileName)
    {
        foreach (JsonObject entry in ReadJsonLinesUnordered(fileName)
            .OrderBy(entry => entry["timestampUtc"]?.GetValue<string>() ?? string.Empty, StringComparer.Ordinal))
        {
            yield return entry;
        }
    }

    private IEnumerable<JsonObject> ReadJsonLinesUnordered(string fileName)
    {
        foreach (string path in GetTelemetryFiles(fileName))
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using StreamReader reader = new(stream);
            while (reader.ReadLine() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                JsonNode? node;
                try
                {
                    node = JsonNode.Parse(line);
                }
                catch
                {
                    continue;
                }

                if (node is JsonObject obj)
                {
                    yield return obj;
                }
            }
        }
    }

    private IEnumerable<string> GetTelemetryFiles(string fileName)
    {
        if (!Directory.Exists(logRoot))
        {
            yield break;
        }

        string exactPath = Path.Combine(logRoot, fileName);
        if (File.Exists(exactPath))
        {
            yield return exactPath;
        }

        string extension = Path.GetExtension(fileName);
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string pattern = string.IsNullOrWhiteSpace(extension)
            ? $"{stem}.*"
            : $"{stem}.*{extension}";

        foreach (string path in Directory.EnumerateFiles(logRoot, pattern)
            .Where(path => !string.Equals(path, exactPath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            yield return path;
        }
    }

    private static string ShortTime(string? timestamp)
    {
        return DateTimeOffset.TryParse(timestamp, out DateTimeOffset parsed)
            ? parsed.ToLocalTime().ToString("HH:mm:ss.fff")
            : string.Empty;
    }

    private static string CompactJson(JsonNode? node)
    {
        if (node is null)
        {
            return string.Empty;
        }

        string text = node.ToJsonString();
        return text.Length <= 700 ? text : string.Concat(text.AsSpan(0, 700), "...");
    }

    private static string ShortProcessPath(string? processPath)
    {
        return string.IsNullOrWhiteSpace(processPath)
            ? string.Empty
            : Path.GetFileName(processPath);
    }

    public static string ResolveMonitorMcpLogRoot(MonitorClientSettings settings)
    {
        return Path.Combine(settings.UiRoot, "Working", "History", "McpTelemetry", "MonitorBaseClaude");
    }

    public static string ResolveRoslynCodeLensLogRoot(MonitorClientSettings settings)
    {
        return Path.Combine(settings.UiRoot, "Working", "History", "McpTelemetry", "RoslynCodeLens");
    }

    private static string ResolveRoslynLogRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && current is not null; i++)
        {
            string candidate = Path.Combine(current.FullName, "ClaudeMonitor", "Monitor", "Working", "History", "McpTelemetry", "RoslynCodeLens");
            if (Directory.Exists(Path.Combine(current.FullName, "ClaudeMonitor")))
            {
                return candidate;
            }

            string monitorCandidate = Path.Combine(current.FullName, "Monitor", "Working", "History", "McpTelemetry", "RoslynCodeLens");
            if (File.Exists(Path.Combine(current.FullName, "Monitor", "AGENTS.md")))
            {
                return monitorCandidate;
            }

            current = current.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "McpTelemetry", "RoslynCodeLens");
    }
}
