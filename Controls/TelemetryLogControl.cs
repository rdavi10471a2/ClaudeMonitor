using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using MonitorBaseClaude.AI;

namespace MonitorBaseClaude.Controls;

[DesignerCategory("Code")]
[AIFileContext("TelemetryLogControl.cs", "Code-only telemetry viewer for proxy JSONL request, response, error, and stderr logs.")]
[FileVersion("1.2")]
public sealed class TelemetryLogControl : UserControl
{
    private readonly string logRoot;
    private readonly DataGridView requestsGrid = new();
    private readonly DataGridView callsGrid = new();
    private readonly DataGridView errorsGrid = new();
    private readonly RichTextBox stderrBox = new();
    private readonly Button refreshButton = new();
    private readonly Button openFolderButton = new();
    private readonly CheckBox autoRefreshCheckBox = new();
    private readonly System.Windows.Forms.Timer refreshTimer = new();

    public TelemetryLogControl()
        : this(ResolveRoslynLogRoot())
    {
    }

    public TelemetryLogControl(string logRoot)
    {
        Dock = DockStyle.Fill;
        this.logRoot = logRoot;
        BuildLayout();
        refreshTimer.Interval = 3000;
        refreshTimer.Tick += (_, _) => RefreshLogs();
        refreshTimer.Start();
        RefreshLogs();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            refreshTimer.Dispose();
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
            ColumnCount = 4,
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
        autoRefreshCheckBox.Text = "Auto 3s";
        autoRefreshCheckBox.AutoSize = true;
        autoRefreshCheckBox.Checked = true;
        autoRefreshCheckBox.Dock = DockStyle.Fill;
        autoRefreshCheckBox.Margin = new Padding(8, 3, 0, 0);
        autoRefreshCheckBox.CheckedChanged += (_, _) =>
        {
            refreshTimer.Enabled = autoRefreshCheckBox.Checked;
            if (autoRefreshCheckBox.Checked)
            {
                RefreshLogs();
            }
        };
        buttons.Controls.Add(refreshButton, 0, 0);
        buttons.Controls.Add(openFolderButton, 1, 0);
        buttons.Controls.Add(autoRefreshCheckBox, 2, 0);

        TabControl tabs = new()
        {
            Dock = DockStyle.Fill
        };
        tabs.TabPages.Add(CreateTab("Requests", requestsGrid));
        tabs.TabPages.Add(CreateTab("Responses", callsGrid));
        tabs.TabPages.Add(CreateTab("Errors", errorsGrid));
        tabs.TabPages.Add(CreateTab("stderr", stderrBox));

        ConfigureGrid(requestsGrid, "Time", "Method", "Tool", "PID", "Process", "Arguments");
        ConfigureGrid(callsGrid, "Time", "Direction", "Method", "Tool", "PID", "ms", "Bytes", "Error");
        ConfigureGrid(errorsGrid, "Time", "Event", "Message");
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

    private void LoadRequests()
    {
        requestsGrid.Rows.Clear();
        foreach (JsonObject entry in ReadJsonLines("requests.jsonl").TakeLast(200))
        {
            requestsGrid.Rows.Add(
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
                ShortTime(entry["timestampUtc"]?.GetValue<string>()),
                entry["direction"]?.GetValue<string>() ?? string.Empty,
                entry["method"]?.GetValue<string>() ?? string.Empty,
                entry["tool"]?.GetValue<string>() ?? string.Empty,
                entry["processId"]?.ToString() ?? string.Empty,
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

    private static void ScrollToLastRow(DataGridView grid)
    {
        if (grid.Rows.Count == 0)
        {
            return;
        }

        int lastIndex = grid.Rows.Count - 1;
        grid.ClearSelection();
        grid.Rows[lastIndex].Selected = true;
        grid.FirstDisplayedScrollingRowIndex = lastIndex;
    }

    private IEnumerable<JsonObject> ReadJsonLines(string fileName)
    {
        string path = Path.Combine(logRoot, fileName);
        if (!File.Exists(path))
        {
            yield break;
        }

        foreach (string line in File.ReadLines(path))
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
