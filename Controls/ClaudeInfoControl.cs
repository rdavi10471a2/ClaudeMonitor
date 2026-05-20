using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using MonitorBaseClaude.AI;

namespace MonitorBaseClaude.Controls;

[DesignerCategory("Code")]
[AIFileContext("ClaudeInfoControl.cs", "Code-only Claude Code session usage inspector backed by statusline snapshot JSON.")]
[FileVersion("1.0")]
public sealed class ClaudeInfoControl : UserControl
{
    private readonly string snapshotRoot;
    private readonly string snapshotPath;
    private readonly Button refreshButton = new();
    private readonly Button openFolderButton = new();
    private readonly Label statusValue = CreateValueLabel();
    private readonly Label updatedValue = CreateValueLabel();
    private readonly Label sessionValue = CreateValueLabel();
    private readonly Label modelValue = CreateValueLabel();
    private readonly Label effortValue = CreateValueLabel();
    private readonly Label thinkingValue = CreateValueLabel();
    private readonly Label contextValue = CreateValueLabel();
    private readonly Label inputTokensValue = CreateValueLabel();
    private readonly Label outputTokensValue = CreateValueLabel();
    private readonly Label cacheValue = CreateValueLabel();
    private readonly Label costValue = CreateValueLabel();
    private readonly Label rateLimitValue = CreateValueLabel();
    private readonly Label changesValue = CreateValueLabel();
    private readonly Label workspaceValue = CreateValueLabel();
    private readonly Label transcriptValue = CreateValueLabel();

    public ClaudeInfoControl(MonitorClientSettings settings)
    {
        Dock = DockStyle.Fill;
        MinimumSize = new Size(560, 460);
        snapshotRoot = Path.Combine(settings.UiRoot, "Working", "History", "ClaudeCode");
        snapshotPath = Path.Combine(snapshotRoot, "statusline-latest.json");
        BuildLayout();
        RefreshSnapshot();
    }

    public void RefreshSnapshot()
    {
        if (!File.Exists(snapshotPath))
        {
            statusValue.Text = "No Claude statusline snapshot found";
            updatedValue.Text = "-";
            sessionValue.Text = "-";
            modelValue.Text = "-";
            effortValue.Text = "-";
            thinkingValue.Text = "-";
            contextValue.Text = "-";
            inputTokensValue.Text = "-";
            outputTokensValue.Text = "-";
            cacheValue.Text = "-";
            costValue.Text = "-";
            rateLimitValue.Text = "-";
            changesValue.Text = "-";
            workspaceValue.Text = "-";
            transcriptValue.Text = "-";
            return;
        }

        JsonObject? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(snapshotPath))?.AsObject();
        }
        catch (Exception ex)
        {
            statusValue.Text = $"Snapshot parse failed: {ex.Message}";
            return;
        }

        if (root is null)
        {
            statusValue.Text = "Snapshot parse failed";
            return;
        }

        FileInfo snapshot = new(snapshotPath);
        JsonObject? cost = root["cost"]?.AsObject();
        JsonObject? context = root["context_window"]?.AsObject();
        JsonObject? usage = context?["current_usage"]?.AsObject();
        JsonObject? rateLimits = root["rate_limits"]?.AsObject();

        statusValue.Text = "Loaded from Claude Code statusline snapshot";
        updatedValue.Text = snapshot.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
        sessionValue.Text = root["session_name"]?.GetValue<string>() ?? root["session_id"]?.GetValue<string>() ?? "-";
        modelValue.Text = root["model"]?["display_name"]?.GetValue<string>() ?? root["model"]?["id"]?.GetValue<string>() ?? "-";
        effortValue.Text = root["effort"]?["level"]?.GetValue<string>() ?? "-";
        thinkingValue.Text = FormatBool(root["thinking"]?["enabled"]?.GetValue<bool?>());
        contextValue.Text = FormatContext(context);
        inputTokensValue.Text = FormatNumber(context?["total_input_tokens"]?.GetValue<long?>());
        outputTokensValue.Text = FormatNumber(context?["total_output_tokens"]?.GetValue<long?>());
        cacheValue.Text = FormatCache(usage);
        costValue.Text = FormatCost(cost);
        rateLimitValue.Text = FormatRateLimits(rateLimits);
        changesValue.Text = FormatChanges(cost);
        workspaceValue.Text = root["workspace"]?["current_dir"]?.GetValue<string>() ?? root["cwd"]?.GetValue<string>() ?? "-";
        transcriptValue.Text = root["transcript_path"]?.GetValue<string>() ?? "-";
    }

    private void BuildLayout()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        TableLayoutPanel toolbar = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(10, 8, 10, 0)
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        refreshButton.Text = "Refresh";
        refreshButton.Dock = DockStyle.Fill;
        refreshButton.Click += (_, _) => RefreshSnapshot();
        openFolderButton.Text = "Open Folder";
        openFolderButton.Dock = DockStyle.Fill;
        openFolderButton.Click += (_, _) => OpenSnapshotFolder();
        toolbar.Controls.Add(refreshButton, 0, 0);
        toolbar.Controls.Add(openFolderButton, 1, 0);

        TableLayoutPanel rows = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 15,
            Padding = new Padding(10)
        };
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 15; i++)
        {
            rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        }

        AddRow(rows, 0, "Status", statusValue);
        AddRow(rows, 1, "Updated", updatedValue);
        AddRow(rows, 2, "Session", sessionValue);
        AddRow(rows, 3, "Model", modelValue);
        AddRow(rows, 4, "Effort", effortValue);
        AddRow(rows, 5, "Thinking", thinkingValue);
        AddRow(rows, 6, "Context", contextValue);
        AddRow(rows, 7, "Input Tokens", inputTokensValue);
        AddRow(rows, 8, "Output Tokens", outputTokensValue);
        AddRow(rows, 9, "Cache", cacheValue);
        AddRow(rows, 10, "Cost", costValue);
        AddRow(rows, 11, "Rate Limits", rateLimitValue);
        AddRow(rows, 12, "Code Changes", changesValue);
        AddRow(rows, 13, "Workspace", workspaceValue);
        AddRow(rows, 14, "Transcript", transcriptValue);

        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(rows, 0, 1);
        Controls.Add(root);
    }

    private void OpenSnapshotFolder()
    {
        Directory.CreateDirectory(snapshotRoot);
        Process.Start(new ProcessStartInfo
        {
            FileName = snapshotRoot,
            UseShellExecute = true
        });
    }

    private static void AddRow(TableLayoutPanel layout, int row, string label, Label value)
    {
        layout.Controls.Add(CreateHeaderLabel(label), 0, row);
        layout.Controls.Add(value, 1, row);
    }

    private static string FormatContext(JsonObject? context)
    {
        if (context is null)
        {
            return "-";
        }

        string used = FormatPercent(context["used_percentage"]?.GetValue<double?>());
        string remaining = FormatPercent(context["remaining_percentage"]?.GetValue<double?>());
        string size = FormatNumber(context["context_window_size"]?.GetValue<long?>());
        return $"{used} used, {remaining} remaining of {size}";
    }

    private static string FormatCache(JsonObject? usage)
    {
        if (usage is null)
        {
            return "-";
        }

        string read = FormatNumber(usage["cache_read_input_tokens"]?.GetValue<long?>());
        string created = FormatNumber(usage["cache_creation_input_tokens"]?.GetValue<long?>());
        return $"read {read}, created {created}";
    }

    private static string FormatCost(JsonObject? cost)
    {
        if (cost is null)
        {
            return "-";
        }

        double? total = cost["total_cost_usd"]?.GetValue<double?>();
        string duration = FormatDuration(cost["total_duration_ms"]?.GetValue<long?>());
        string api = FormatDuration(cost["total_api_duration_ms"]?.GetValue<long?>());
        return total is null ? $"wall {duration}, API {api}" : $"{total.Value:C4}, wall {duration}, API {api}";
    }

    private static string FormatRateLimits(JsonObject? rateLimits)
    {
        if (rateLimits is null)
        {
            return "-";
        }

        string fiveHour = FormatPercent(rateLimits["five_hour"]?["used_percentage"]?.GetValue<double?>());
        string sevenDay = FormatPercent(rateLimits["seven_day"]?["used_percentage"]?.GetValue<double?>());
        return $"5h {fiveHour}, 7d {sevenDay}";
    }

    private static string FormatChanges(JsonObject? cost)
    {
        if (cost is null)
        {
            return "-";
        }

        string added = FormatNumber(cost["total_lines_added"]?.GetValue<long?>());
        string removed = FormatNumber(cost["total_lines_removed"]?.GetValue<long?>());
        return $"+{added}, -{removed}";
    }

    private static string FormatDuration(long? milliseconds)
    {
        if (milliseconds is null)
        {
            return "-";
        }

        TimeSpan span = TimeSpan.FromMilliseconds(milliseconds.Value);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h {span.Minutes}m"
            : span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes}m {span.Seconds}s" : $"{span.Seconds}s";
    }

    private static string FormatNumber(long? value)
    {
        return value?.ToString("N0") ?? "-";
    }

    private static string FormatPercent(double? value)
    {
        return value is null ? "-" : $"{value.Value:0.#}%";
    }

    private static string FormatBool(bool? value)
    {
        return value is null ? "-" : value.Value ? "Enabled" : "Disabled";
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
            MinimumSize = new Size(240, 0),
            AutoEllipsis = true,
            Text = "-",
            TextAlign = ContentAlignment.MiddleLeft
        };
    }
}
