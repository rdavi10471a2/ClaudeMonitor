using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MonitorBaseClaude;

namespace MonitorBaseClaude.ToolSmokeTests;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static async Task<int> Main(string[] args)
    {
        bool pause = args.Contains("--pause", StringComparer.OrdinalIgnoreCase);
        bool scriptedMode = args.Contains("--scripted", StringComparer.OrdinalIgnoreCase);
        bool stageCommentDiffMode = args.Contains("--stage-comment-diff", StringComparer.OrdinalIgnoreCase);
        bool recordDecisionMode = args.Contains("--record-decision", StringComparer.OrdinalIgnoreCase);
        bool fixtureAcceptSmokeMode = args.Contains("--fixture-accept-smoke", StringComparer.OrdinalIgnoreCase);
        bool fixtureDecisionGateSmokeMode = args.Contains("--fixture-decision-gate-smoke", StringComparer.OrdinalIgnoreCase);
        bool fixtureRoslynSurgerySmokeMode = args.Contains("--fixture-roslyn-surgery-smoke", StringComparer.OrdinalIgnoreCase);
        bool fixtureRazorSmokeMode = args.Contains("--fixture-razor-smoke", StringComparer.OrdinalIgnoreCase);
        bool sourceMapSmokeMode = args.Contains("--source-map-smoke", StringComparer.OrdinalIgnoreCase);
        bool sourceMapBudgetSmokeMode = args.Contains("--source-map-budget-smoke", StringComparer.OrdinalIgnoreCase);
        bool sourceMapCorpusSmokeMode = args.Contains("--source-map-corpus-smoke", StringComparer.OrdinalIgnoreCase);
        bool ollamaRouteSmokeMode = args.Contains("--ollama-route-smoke", StringComparer.OrdinalIgnoreCase);
        bool ollamaRouterDrillSmokeMode = args.Contains("--ollama-router-drill-smoke", StringComparer.OrdinalIgnoreCase);
        bool waitForOperator = args.Contains("--wait", StringComparer.OrdinalIgnoreCase);
        string? modelOverride = ReadOption(args, "--model");

        MonitorClientSettings settings = MonitorClientSettings.Load();
        string runRoot = Path.Combine(settings.UiRoot, "Working", "History", "ToolSmokeTests", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(runRoot);

        SmokeFixture? fixture = null;
        if (fixtureAcceptSmokeMode || fixtureDecisionGateSmokeMode || fixtureRoslynSurgerySmokeMode)
        {
            fixture = CreateDbv2ShapeFixture(settings, runRoot);
        }
        else if (fixtureRazorSmokeMode)
        {
            fixture = CreateRazorShapeFixture(settings, runRoot);
        }

        using MonitorMcpClientService monitorClient = new(settings, fixture?.ServerSettingsPath);
        using OllamaToolExplorerService ollama = new(settings);
        LocalMcpDiscoveryService discovery = new(settings);

        string model = modelOverride ?? settings.OllamaModel;

        if (fixtureAcceptSmokeMode)
        {
            return await RunFixtureAcceptSmokeAsync(fixture!, monitorClient, runRoot);
        }

        if (fixtureDecisionGateSmokeMode)
        {
            return await RunFixtureDecisionGateSmokeAsync(fixture!, monitorClient, runRoot);
        }

        if (fixtureRoslynSurgerySmokeMode)
        {
            return await RunFixtureRoslynSurgerySmokeAsync(fixture!, monitorClient, runRoot);
        }

        if (fixtureRazorSmokeMode)
        {
            return await RunFixtureRazorSmokeAsync(fixture!, monitorClient, runRoot);
        }

        if (sourceMapSmokeMode)
        {
            string path = ReadOptionalValueAfter(args, "--source-map-smoke") ?? "Program.cs";
            string scope = ReadOption(args, "--scope") ?? "auto";
            string mode = ReadOption(args, "--mode") ?? "auto";
            return await RunSourceMapSmokeAsync(monitorClient, runRoot, path, scope, mode);
        }

        if (sourceMapBudgetSmokeMode)
        {
            string path = ReadOptionalValueAfter(args, "--source-map-budget-smoke") ?? string.Empty;
            string scope = ReadOption(args, "--scope") ?? "project";
            string mode = ReadOption(args, "--mode") ?? "full";
            return await RunSourceMapSmokeAsync(monitorClient, runRoot, path, scope, mode, expectTruncated: true);
        }

        if (sourceMapCorpusSmokeMode)
        {
            string? path = ReadOptionalValueAfter(args, "--source-map-corpus-smoke");
            return await RunSourceMapCorpusSmokeAsync(settings, monitorClient, runRoot, path);
        }

        if (ollamaRouteSmokeMode)
        {
            return await RunOllamaRouteSmokeAsync(discovery, ollama, model, runRoot, pause);
        }

        if (ollamaRouterDrillSmokeMode)
        {
            return await RunOllamaRouterDrillSmokeAsync(ollama, model, runRoot, pause);
        }

        if (stageCommentDiffMode)
        {
            return await RunStageCommentDiffAsync(settings, monitorClient, runRoot, waitForOperator);
        }

        if (recordDecisionMode)
        {
            return await RunRecordDecisionAsync(args, monitorClient, runRoot);
        }

        if (scriptedMode)
        {
            return await RunScriptedSmokeAsync(settings, monitorClient, runRoot, pause);
        }

        SmokeQuestion[] questions = BuildDefaultQuestions();

        Console.WriteLine($"MonitorBaseClaude tool smoke test");
        Console.WriteLine("Mode: llm");
        Console.WriteLine($"Model: {model}");
        Console.WriteLine($"Log root: {runRoot}");
        Console.WriteLine();

        List<SmokeResult> results = [];
        IReadOnlyList<LocalMcpServerSurface> localServers = await discovery.DiscoverAsync();

        for (int i = 0; i < questions.Length; i++)
        {
            SmokeQuestion question = questions[i];
            Console.WriteLine($"[{i + 1}/{questions.Length}] {question.Name}");
            Console.WriteLine(question.Text);

            SmokeResult result = await RunQuestionAsync(question, model, localServers, monitorClient, ollama);
            results.Add(result);

            string fileName = $"{i + 1:00}_{SanitizeFileName(question.Name)}.json";
            string logPath = Path.Combine(runRoot, fileName);
            await File.WriteAllTextAsync(logPath, JsonSerializer.Serialize(result, JsonOptions));

            Console.WriteLine($"Decision: {result.Decision.Action} {result.Decision.Server} {result.Decision.Tool}");
            Console.WriteLine($"Tool error: {result.ToolResult?.IsError.ToString() ?? "(no tool)"}");
            Console.WriteLine("Answer:");
            Console.WriteLine(Indent(NormalizeNewlines(result.FinalAnswer ?? result.Decision.Answer ?? "(no answer)"), "  "));
            Console.WriteLine($"Trace: {logPath}");
            Console.WriteLine(new string('-', 80));

            if (pause && i < questions.Length - 1)
            {
                Console.WriteLine("Press Enter for next smoke question...");
                Console.ReadLine();
            }
        }

        string reportPath = Path.Combine(runRoot, "summary.md");
        await File.WriteAllTextAsync(reportPath, BuildMarkdownReport(results));
        Console.WriteLine($"Summary report: {reportPath}");
        return results.Any(result => result.Decision.Action.Equals("error", StringComparison.OrdinalIgnoreCase)
            || result.ToolResult?.IsError == true)
            ? 1
            : 0;
    }

    private static async Task<int> RunScriptedSmokeAsync(
        MonitorClientSettings settings,
        MonitorMcpClientService monitorClient,
        string runRoot,
        bool pause)
    {
        Console.WriteLine("MonitorBaseClaude tool smoke test");
        Console.WriteLine("Mode: scripted MCP Server calls");
        Console.WriteLine($"Log root: {runRoot}");
        Console.WriteLine();

        List<ScriptedSmokeResult> results = [];
        string? sessionId = null;
        string? stagedRecordId = null;

        ScriptedSmokeStep[] staticSteps =
        [
            new("Status", "get_monitor_status", null, "Verify configured roots and watched solution."),
            new("Self Check", "get_self_check", null, "Verify guardrails, Working paths, and diff tool discovery."),
            new("Workflow", "get_workflow_status", null, "Verify Working folder and WinMerge discovery."),
            new("List Monitor Runs", "list_monitor_runs", new Dictionary<string, object?> { ["maxEntries"] = 10 }, "List monitor-owned run history entries."),
            new("List Ledgers", "list_ledgers", new Dictionary<string, object?> { ["maxEntries"] = 10 }, "List monitor-owned per-file ledgers."),
            new("Find Program.cs", "find_file", new Dictionary<string, object?> { ["fileNameOrPattern"] = "Program.cs", ["maxResults"] = 10 }, "Find Program.cs under watched project."),
            new("Read Program.cs", "get_file", new Dictionary<string, object?> { ["sourceFilePath"] = "Program.cs" }, "Read full Program.cs through the Server."),
            new("Outline Program.cs", "get_file_outline", new Dictionary<string, object?> { ["path"] = "Program.cs" }, "Read symbol outline for Program.cs."),
            new("Source Map Program.cs", "get_source_map", new Dictionary<string, object?> { ["path"] = "Program.cs", ["scope"] = "file" }, "Read Roslyn source map for Program.cs."),
            new("Get Main Symbol", "get_symbol", new Dictionary<string, object?> { ["path"] = "Program.cs", ["symbolName"] = "Main" }, "Read only Program.Main from Program.cs."),
            new("Get Program Ledger", "get_ledger", new Dictionary<string, object?> { ["sourceFilePath"] = "Program.cs" }, "Read Program.cs ledger if present."),
            new("Stage Program.cs Rejected Candidate", "submit_file", new Dictionary<string, object?> { ["path"] = "Program.cs", ["content"] = BuildProgramRejectedProposal(settings), ["launchDiff"] = false }, "Stage a harmless changed Program.cs proposal without touching source."),
            new("Start Session", "start_monitor_session", new Dictionary<string, object?> { ["purpose"] = "scripted smoke test for Program.cs" }, "Create durable session handle.")
        ];

        int index = 0;
        foreach (ScriptedSmokeStep step in staticSteps)
        {
            index++;
            ScriptedSmokeResult result = await RunScriptedStepAsync(index, step, monitorClient, runRoot);
            results.Add(result);
            sessionId ??= ExtractSessionId(result.ToolResult.ResponseJson);
            stagedRecordId ??= ExtractStagedRecordId(result.ToolResult.ResponseJson);
            WriteScriptedSummary(result);
            MaybePause(pause);
        }

        if (!string.IsNullOrWhiteSpace(stagedRecordId))
        {
            index++;
            ScriptedSmokeResult result = await RunScriptedStepAsync(
                index,
                new ScriptedSmokeStep(
                    "Record Rejected Program.cs Candidate",
                    "record_diff_decision",
                    new Dictionary<string, object?> { ["stagedRecordId"] = stagedRecordId, ["decision"] = "rejected", ["note"] = "Scripted smoke test rejected staged Program.cs proposal." },
                    "Verify vote-plus-hash rejection when the watched source remains at the original baseline."),
                monitorClient,
                runRoot);
            results.Add(result);
            WriteScriptedSummary(result);
            MaybePause(pause);
        }

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            ScriptedSmokeStep[] sessionSteps =
            [
                new("Read Program.cs With Session", "get_file", new Dictionary<string, object?> { ["sourceFilePath"] = "Program.cs", ["sessionId"] = sessionId }, "Read Program.cs and record hash in session."),
                new("Check Program.cs Hash", "check_file_hash", new Dictionary<string, object?> { ["sourceFilePath"] = "Program.cs", ["sessionId"] = sessionId }, "Verify whether Program.cs changed since session fetch."),
                new("List Sessions", "list_monitor_sessions", null, "Verify the created session appears in durable session list.")
            ];

            foreach (ScriptedSmokeStep step in sessionSteps)
            {
                index++;
                ScriptedSmokeResult result = await RunScriptedStepAsync(index, step, monitorClient, runRoot);
                results.Add(result);
                WriteScriptedSummary(result);
                MaybePause(pause);
            }
        }

        string reportPath = Path.Combine(runRoot, "scripted-summary.md");
        await File.WriteAllTextAsync(reportPath, BuildScriptedMarkdownReport(results));
        Console.WriteLine($"Summary report: {reportPath}");
        return results.Any(result => result.ToolResult.IsError) ? 1 : 0;
    }

    private static async Task<int> RunSourceMapSmokeAsync(
        MonitorMcpClientService monitorClient,
        string runRoot,
        string path,
        string scope,
        string mode,
        bool expectTruncated = false)
    {
        Console.WriteLine("MonitorBaseClaude source-map smoke");
        Console.WriteLine($"Target: {(string.IsNullOrWhiteSpace(path) ? "(watched project)" : path)}");
        Console.WriteLine($"Scope: {scope}");
        Console.WriteLine($"Mode: {mode}");
        Console.WriteLine($"Log root: {runRoot}");
        Console.WriteLine();

        Dictionary<string, object?> arguments = new()
        {
            ["scope"] = scope,
            ["mode"] = mode
        };
        if (!string.IsNullOrWhiteSpace(path))
        {
            arguments["path"] = path;
        }

        DateTimeOffset startedAt = DateTimeOffset.Now;
        MonitorMcpToolCallResult toolResult = await monitorClient.CallToolAsync("get_source_map", arguments);
        ScriptedSmokeResult result = new(
            new ScriptedSmokeStep(
                "Source Map Smoke",
                "get_source_map",
                arguments,
                "Generate a reviewable Roslyn source-map artifact for the requested watched source scope."),
            startedAt,
            DateTimeOffset.Now,
            toolResult,
            FormatPayloadForDisplay(toolResult.ResponseJson));

        string rawPath = Path.Combine(runRoot, "source-map-raw.json");
        string summaryPath = Path.Combine(runRoot, "source-map-summary.md");
        await File.WriteAllTextAsync(rawPath, toolResult.ResponseJson);
        await File.WriteAllTextAsync(summaryPath, BuildSourceMapSummary(toolResult.ResponseJson, path, scope, mode));
        WriteScriptedSummary(result);
        Console.WriteLine($"Raw source map: {rawPath}");
        Console.WriteLine($"Summary:        {summaryPath}");
        if (toolResult.IsError)
        {
            return 1;
        }

        if (!expectTruncated)
        {
            return 0;
        }

        JsonNode? root = JsonNode.Parse(toolResult.ResponseJson);
        bool wasTruncated = root?["wasTruncated"]?.GetValue<bool?>()
            ?? root?["WasTruncated"]?.GetValue<bool?>()
            ?? false;
        if (!wasTruncated)
        {
            Console.WriteLine("Expected get_source_map to truncate an over-budget response, but wasTruncated was false.");
            return 1;
        }

        Console.WriteLine("Verified over-budget source-map response was truncated.");
        return 0;
    }

    private static async Task<int> RunSourceMapCorpusSmokeAsync(
        MonitorClientSettings settings,
        MonitorMcpClientService monitorClient,
        string runRoot,
        string? requestedPath)
    {
        string watchedRoot = Path.GetDirectoryName(settings.WatchedSolutionPath)
            ?? throw new InvalidOperationException("Watched solution path does not have a containing folder.");
        string corpusTarget = string.IsNullOrWhiteSpace(requestedPath)
            ? watchedRoot
            : Path.IsPathRooted(requestedPath)
                ? Path.GetFullPath(requestedPath)
                : Path.GetFullPath(Path.Combine(watchedRoot, requestedPath));
        if (!Directory.Exists(corpusTarget) && !File.Exists(corpusTarget))
        {
            Console.WriteLine($"Source-map corpus target not found: {corpusTarget}");
            return 1;
        }

        string corpusRoot = Path.Combine(runRoot, "source-map-corpus");
        string mapsRoot = Path.Combine(corpusRoot, "maps");
        Directory.CreateDirectory(mapsRoot);

        string[] sourceFiles = EnumerateCSharpCorpusFiles(corpusTarget)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Console.WriteLine("MonitorBaseClaude source-map corpus smoke");
        Console.WriteLine($"Watched root: {watchedRoot}");
        Console.WriteLine($"Target:       {corpusTarget}");
        Console.WriteLine($"C# files:     {sourceFiles.Length}");
        Console.WriteLine($"Log root:     {corpusRoot}");
        Console.WriteLine();

        List<SourceMapCorpusFileAnalysis> files = [];
        DateTimeOffset startedAt = DateTimeOffset.Now;
        long totalSourceBytes = 0;
        long totalMapBytes = 0;
        long totalSourceChars = 0;
        long totalMapChars = 0;
        JsonArray compactFiles = [];
        JsonArray navigationFiles = [];

        for (int i = 0; i < sourceFiles.Length; i++)
        {
            string sourcePath = sourceFiles[i];
            string relativePath = Path.GetRelativePath(watchedRoot, sourcePath);
            if (Path.IsPathRooted(relativePath) || relativePath.StartsWith("..", StringComparison.Ordinal))
            {
                relativePath = Path.GetFileName(sourcePath);
            }

            Console.WriteLine($"[{i + 1}/{sourceFiles.Length}] {relativePath}");
            FileInfo sourceInfo = new(sourcePath);
            string sourceText = await File.ReadAllTextAsync(sourcePath);
            Dictionary<string, object?> fullArguments = new()
            {
                ["path"] = relativePath,
                ["scope"] = "file",
                ["mode"] = "full"
            };
            DateTimeOffset fileStartedAt = DateTimeOffset.Now;
            MonitorMcpToolCallResult result = await monitorClient.CallToolAsync("get_source_map", fullArguments);
            DateTimeOffset fileFinishedAt = DateTimeOffset.Now;

            string mapRelativePath = Path.ChangeExtension(relativePath, ".source-map.json");
            string mapPath = Path.Combine(mapsRoot, mapRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(mapPath)!);
            await File.WriteAllTextAsync(mapPath, result.ResponseJson);

            Dictionary<string, object?> selectorArguments = new()
            {
                ["path"] = relativePath,
                ["scope"] = "file",
                ["mode"] = "selector"
            };
            MonitorMcpToolCallResult selectorResult = await monitorClient.CallToolAsync("get_source_map", selectorArguments);

            Dictionary<string, object?> navigationArguments = new()
            {
                ["path"] = relativePath,
                ["scope"] = "file",
                ["mode"] = "navigation"
            };
            MonitorMcpToolCallResult navigationResult = await monitorClient.CallToolAsync("get_source_map", navigationArguments);

            int mapBytes = Encoding.UTF8.GetByteCount(result.ResponseJson);
            if (!result.IsError)
            {
                if (!selectorResult.IsError)
                {
                    compactFiles.Add(ExtractFirstSourceMapFile(selectorResult.ResponseJson));
                }

                if (!navigationResult.IsError)
                {
                    navigationFiles.Add(ExtractFirstSourceMapFile(navigationResult.ResponseJson));
                }
            }

            totalSourceBytes += sourceInfo.Length;
            totalMapBytes += mapBytes;
            totalSourceChars += sourceText.Length;
            totalMapChars += result.ResponseJson.Length;

            SourceMapCorpusFileAnalysis analysis = AnalyzeSourceMapCorpusFile(
                relativePath,
                sourcePath,
                mapPath,
                sourceInfo.Length,
                sourceText.Length,
                mapBytes,
                result.ResponseJson.Length,
                result.IsError || selectorResult.IsError || navigationResult.IsError,
                fileStartedAt,
                fileFinishedAt,
                result.ResponseJson);
            files.Add(analysis);
        }

        JsonObject compactRoot = new()
        {
            ["generatedAt"] = DateTimeOffset.Now.ToString("O"),
            ["watchedRoot"] = watchedRoot,
            ["targetPath"] = corpusTarget,
            ["fileCount"] = compactFiles.Count,
            ["files"] = compactFiles
        };
        string compactJson = compactRoot.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        string compactPath = Path.Combine(corpusRoot, "source-map-compact-index.json");
        await File.WriteAllTextAsync(compactPath, compactJson);
        long compactBytes = Encoding.UTF8.GetByteCount(compactJson);
        long compactChars = compactJson.Length;

        JsonObject navigationRoot = new()
        {
            ["generatedAt"] = DateTimeOffset.Now.ToString("O"),
            ["watchedRoot"] = watchedRoot,
            ["targetPath"] = corpusTarget,
            ["fileCount"] = navigationFiles.Count,
            ["files"] = navigationFiles
        };
        string navigationJson = navigationRoot.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        string navigationPath = Path.Combine(corpusRoot, "source-map-navigation-index.json");
        await File.WriteAllTextAsync(navigationPath, navigationJson);
        long navigationBytes = Encoding.UTF8.GetByteCount(navigationJson);
        long navigationChars = navigationJson.Length;

        SourceMapCorpusAnalysis corpus = new(
            startedAt,
            DateTimeOffset.Now,
            watchedRoot,
            corpusTarget,
            sourceFiles.Length,
            files.Count(file => !file.IsError),
            files.Count(file => file.IsError),
            totalSourceBytes,
            totalMapBytes,
            totalSourceChars,
            totalMapChars,
            compactPath,
            compactBytes,
            compactChars,
            navigationPath,
            navigationBytes,
            navigationChars,
            EstimateTokens(totalSourceChars),
            EstimateTokens(totalMapChars),
            EstimateTokens(compactChars),
            EstimateTokens(navigationChars),
            files.Sum(file => file.SymbolCount),
            files.Sum(file => file.MethodCount),
            files.Sum(file => file.TypeCount),
            files.Sum(file => file.FieldCount),
            files.Sum(file => file.PropertyCount),
            files.Sum(file => file.EventCount),
            files.Sum(file => file.DiagnosticCount),
            files);

        string rawPath = Path.Combine(corpusRoot, "source-map-corpus-analysis.json");
        string summaryPath = Path.Combine(corpusRoot, "source-map-corpus-summary.md");
        await File.WriteAllTextAsync(rawPath, JsonSerializer.Serialize(corpus, JsonOptions));
        await File.WriteAllTextAsync(summaryPath, BuildSourceMapCorpusMarkdownReport(corpus));

        Console.WriteLine();
        Console.WriteLine($"Corpus source bytes: {corpus.TotalSourceBytes:N0}");
        Console.WriteLine($"Corpus map bytes:    {corpus.TotalSourceMapBytes:N0}");
        Console.WriteLine($"Compact map bytes:   {corpus.CompactSourceMapBytes:N0}");
        Console.WriteLine($"Navigation bytes:    {corpus.NavigationIndexBytes:N0}");
        Console.WriteLine($"Source token proxy:  {corpus.EstimatedSourceTokens:N0}");
        Console.WriteLine($"Map token proxy:     {corpus.EstimatedSourceMapTokens:N0}");
        Console.WriteLine($"Compact token proxy: {corpus.EstimatedCompactSourceMapTokens:N0}");
        Console.WriteLine($"Nav token proxy:     {corpus.EstimatedNavigationIndexTokens:N0}");
        Console.WriteLine($"Files with errors:   {corpus.ErrorFileCount}");
        Console.WriteLine($"Diagnostics:         {corpus.DiagnosticCount}");
        Console.WriteLine($"Compact index:       {compactPath}");
        Console.WriteLine($"Navigation index:    {navigationPath}");
        Console.WriteLine($"Raw analysis:        {rawPath}");
        Console.WriteLine($"Summary:             {summaryPath}");
        return corpus.ErrorFileCount == 0 ? 0 : 1;
    }

    private static async Task<int> RunOllamaRouteSmokeAsync(
        LocalMcpDiscoveryService discovery,
        OllamaToolExplorerService ollama,
        string model,
        string runRoot,
        bool pause)
    {
        Console.WriteLine("MonitorBaseClaude Ollama route-only smoke");
        Console.WriteLine($"Model: {model}");
        Console.WriteLine($"Log root: {runRoot}");
        Console.WriteLine();

        SmokeQuestion[] questions = BuildDefaultQuestions()
            .Where(question => !string.IsNullOrWhiteSpace(question.ExpectedTool)
                || !string.IsNullOrWhiteSpace(question.ExpectedServer))
            .ToArray();
        IReadOnlyList<LocalMcpServerSurface> localServers = await discovery.DiscoverAsync();
        List<OllamaRouteSmokeResult> results = [];

        for (int i = 0; i < questions.Length; i++)
        {
            SmokeQuestion question = questions[i];
            Console.WriteLine($"[{i + 1}/{questions.Length}] {question.Name}");
            Console.WriteLine(question.Text);
            Console.WriteLine($"Expected: {question.ExpectedServer ?? "(any server)"} / {question.ExpectedTool ?? "(any tool)"}");

            DateTimeOffset startedAt = DateTimeOffset.Now;
            OllamaActionDecision decision = await ollama.DecideNextActionAsync(localServers, question.Text, model);
            OllamaRouteValidation validation = ValidateRoute(localServers, decision, question);
            OllamaRouteSmokeResult result = new(
                question,
                model,
                startedAt,
                DateTimeOffset.Now,
                localServers,
                decision,
                validation);
            results.Add(result);

            string fileName = $"{i + 1:00}_{SanitizeFileName(question.Name)}.json";
            string logPath = Path.Combine(runRoot, fileName);
            await File.WriteAllTextAsync(logPath, JsonSerializer.Serialize(result, JsonOptions));

            Console.WriteLine($"Decision: {decision.Action} {decision.Server} {decision.Tool}");
            Console.WriteLine($"Resolved: {validation.ResolvedServerName ?? "(none)"}");
            Console.WriteLine($"Pass: {validation.IsExpectedRoute}");
            Console.WriteLine($"Executable: {validation.IsExecutable}");
            Console.WriteLine($"Issue: {validation.Message ?? "(none)"}");
            Console.WriteLine($"Trace: {logPath}");
            Console.WriteLine(new string('-', 80));

            if (pause && i < questions.Length - 1)
            {
                Console.WriteLine("Press Enter for next route smoke question...");
                Console.ReadLine();
            }
        }

        string reportPath = Path.Combine(runRoot, "ollama-route-summary.md");
        await File.WriteAllTextAsync(reportPath, BuildOllamaRouteMarkdownReport(results));
        Console.WriteLine($"Summary report: {reportPath}");
        return results.All(result => result.Validation.IsExpectedRoute) ? 0 : 1;
    }

    private static async Task<int> RunOllamaRouterDrillSmokeAsync(
        OllamaToolExplorerService ollama,
        string model,
        string runRoot,
        bool pause)
    {
        Console.WriteLine("MonitorBaseClaude Ollama fake-router drill smoke");
        Console.WriteLine($"Model: {model}");
        Console.WriteLine($"Log root: {runRoot}");
        Console.WriteLine();

        RouterDrillQuestion[] questions = BuildRouterDrillQuestions();
        List<OllamaRouterDrillSmokeResult> results = [];

        for (int i = 0; i < questions.Length; i++)
        {
            RouterDrillQuestion question = questions[i];
            Console.WriteLine($"[{i + 1}/{questions.Length}] {question.Name}");
            Console.WriteLine(question.Text);
            Console.WriteLine($"Expected: {question.ExpectedAction}");

            DateTimeOffset startedAt = DateTimeOffset.Now;
            OllamaRouterDecision decision = await ollama.DecideRouterActionAsync(question.Text, model);
            bool passed = decision.Action.Equals(question.ExpectedAction, StringComparison.OrdinalIgnoreCase);
            OllamaRouterDrillSmokeResult result = new(
                question,
                model,
                startedAt,
                DateTimeOffset.Now,
                decision,
                passed);
            results.Add(result);

            string fileName = $"{i + 1:00}_{SanitizeFileName(question.Name)}.json";
            string logPath = Path.Combine(runRoot, fileName);
            await File.WriteAllTextAsync(logPath, JsonSerializer.Serialize(result, JsonOptions));

            Console.WriteLine($"Decision: {decision.Action}");
            Console.WriteLine($"Pass: {passed}");
            Console.WriteLine($"Reason: {decision.Reason ?? "(none)"}");
            Console.WriteLine($"Trace: {logPath}");
            Console.WriteLine(new string('-', 80));

            if (pause && i < questions.Length - 1)
            {
                Console.WriteLine("Press Enter for next router drill question...");
                Console.ReadLine();
            }
        }

        string reportPath = Path.Combine(runRoot, "ollama-router-drill-summary.md");
        await File.WriteAllTextAsync(reportPath, BuildOllamaRouterDrillMarkdownReport(results));
        Console.WriteLine($"Summary report: {reportPath}");
        return results.All(result => result.Passed) ? 0 : 1;
    }

    private static SmokeQuestion[] BuildDefaultQuestions()
    {
        return
        [
            new("Discover tools", "What MCP Server tools are available in this Host session?"),
            new("Route: source structure", "Show me the structure of EditorSurface\\ExplorerControl.cs.", "monitor-base-claude", "get_source_map"),
            new("Route: symbol body", "Show me the body of LoadTable in EditorSurface\\EditorSurfaceControl.cs.", "monitor-base-claude", "get_symbol"),
            new("Find Program.cs", "Find file Program.cs in the watched project.", "monitor-base-claude", "find_file"),
            new("Read Program.cs", "Read Program.cs from the watched project and summarize what application starts.", "monitor-base-claude", "get_file"),
            new("Workflow status", "Inspect the current monitor workflow status and tell me whether WinMerge is available.", "monitor-base-claude", "get_workflow_status"),
            new("Start session", "Start a Monitor Server session for investigating Program.cs.", "monitor-base-claude", "start_monitor_session"),
            new("List sessions", "List Monitor Server sessions and identify the most recent session.", "monitor-base-claude", "list_monitor_sessions"),
            new("No tool needed", "Explain in one sentence what a Monitor Server session handle is.")
        ];
    }

    private static RouterDrillQuestion[] BuildRouterDrillQuestions()
    {
        return
        [
            new("Null Guard No Map", "User wants to add a null guard to LoadTable in EditorSurface\\EditorSurfaceControl.cs, but no source map has been read yet.", "SOURCE_MAP"),
            new("Null Guard Has Map", "Source map for EditorSurface\\EditorSurfaceControl.cs already shows LoadTable(BaseTableDefinition table), but the method body has not been read.", "GET_SYMBOL"),
            new("Candidate Ready", "The model has the symbol body and proposes a complete updated file candidate.", "STAGE_FILE"),
            new("Accepted Hash Match", "Operator reports accepted and watched hash equals staged candidate hash.", "RECORD_DECISION"),
            new("Direct Source Write", "User asks the model to write the changed file directly into watched source.", "REFUSE_UNSAFE"),
            new("Partial WinMerge Repair", "Operator wants to fix the few bad lines manually in WinMerge and save the result.", "REFUSE_UNSAFE"),
            new("Unknown Target", "User wants to fix table loading, but no file or symbol name is known.", "ASK_NARROWING_QUESTION")
        ];
    }

    private static async Task<int> RunStageCommentDiffAsync(
        MonitorClientSettings settings,
        MonitorMcpClientService monitorClient,
        string runRoot,
        bool waitForOperator)
    {
        const string relativePath = "Data\\BaseTableRepository.cs";
        const string marker = "            // Monitor proposal smoke test: verify staged repository diffs without touching source.";
        string watchedRoot = Path.GetDirectoryName(settings.WatchedSolutionPath) ?? string.Empty;
        string sourcePath = Path.Combine(watchedRoot, relativePath);
        string original = await File.ReadAllTextAsync(sourcePath);
        const string anchor = "            using var conn = new SqlConnection(ConnectionString);";
        string proposed = original;
        if (!original.Contains(marker, StringComparison.Ordinal))
        {
            int anchorIndex = original.IndexOf(anchor, StringComparison.Ordinal);
            if (anchorIndex >= 0)
            {
                int insertIndex = anchorIndex + anchor.Length;
                string newline = original.IndexOf("\r\n", insertIndex, StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
                proposed = original.Insert(insertIndex, $"{newline}{marker}");
            }
        }

        if (string.Equals(original, proposed, StringComparison.Ordinal))
        {
            Console.WriteLine("Proposal content is identical to source; marker may already exist or anchor was not found.");
            return 1;
        }

        Dictionary<string, object?> arguments = new()
        {
            ["path"] = relativePath,
            ["content"] = proposed,
            ["manifestJson"] = JsonSerializer.Serialize(new
            {
                operation = "submit_file",
                filePath = relativePath,
                added = Array.Empty<string>(),
                removed = Array.Empty<string>(),
                note = "Smoke-test staged repository comment proposal."
            }, JsonOptions),
            ["launchDiff"] = false
        };

        Console.WriteLine("MonitorBaseClaude staged comment diff test");
        Console.WriteLine($"Source: {sourcePath}");
        Console.WriteLine($"Log root: {runRoot}");
        Console.WriteLine();

        DateTimeOffset startedAt = DateTimeOffset.Now;
        MonitorMcpToolCallResult toolResult = await monitorClient.CallToolAsync("submit_file", arguments);
        await Task.Delay(TimeSpan.FromSeconds(3));

        ScriptedSmokeResult result = new(
            new ScriptedSmokeStep(
                "Stage repository comment diff",
                "submit_file",
                arguments,
                "Stage a harmless comment in BaseTableRepository.GetByDatabase and launch WinMerge."),
            startedAt,
            DateTimeOffset.Now,
            toolResult,
            FormatPayloadForDisplay(toolResult.ResponseJson));

        string logPath = Path.Combine(runRoot, "stage-comment-diff.json");
        await File.WriteAllTextAsync(logPath, JsonSerializer.Serialize(result, JsonOptions));
        WriteScriptedSummary(result);
        LaunchOperatorDiffFromSmokeTest(toolResult);

        string after = await File.ReadAllTextAsync(sourcePath);
        bool sourceUnchanged = string.Equals(original, after, StringComparison.Ordinal);
        Console.WriteLine($"Watched source unchanged: {sourceUnchanged}");
        Console.WriteLine($"Trace: {logPath}");
        if (waitForOperator)
        {
            Console.WriteLine();
            Console.WriteLine("WinMerge should now be open. This runner cannot reliably own WinMerge lifetime when WinMerge reuses an existing instance.");
            Console.WriteLine("Leave this command running from a normal terminal if you need an interactive pause, or use the WinForms Host for operator review.");

            string afterOperator = await File.ReadAllTextAsync(sourcePath);
            bool markerMerged = afterOperator.Contains(marker, StringComparison.Ordinal);
            Console.WriteLine($"Marker present after Operator review: {markerMerged}");
        }

        return toolResult.IsError || !sourceUnchanged ? 1 : 0;
    }

    private static async Task<int> RunRecordDecisionAsync(
        string[] args,
        MonitorMcpClientService monitorClient,
        string runRoot)
    {
        string[] values = ReadValuesAfter(args, "--record-decision", 2);
        string stagedRecordId = values[0];
        string decision = values[1];
        string? note = ReadOption(args, "--note");
        string? sessionId = ReadOption(args, "--session-id");
        Dictionary<string, object?> arguments = new()
        {
            ["stagedRecordId"] = stagedRecordId,
            ["decision"] = decision,
            ["note"] = note,
            ["sessionId"] = sessionId
        };

        Console.WriteLine("MonitorBaseClaude record diff decision");
        Console.WriteLine($"Staged record: {stagedRecordId}");
        Console.WriteLine($"Decision: {decision}");
        Console.WriteLine($"Log root: {runRoot}");
        Console.WriteLine();

        DateTimeOffset startedAt = DateTimeOffset.Now;
        MonitorMcpToolCallResult toolResult = await monitorClient.CallToolAsync("record_diff_decision", arguments);
        ScriptedSmokeResult result = new(
            new ScriptedSmokeStep(
                "Record diff decision",
                "record_diff_decision",
                arguments,
                "Record an all-or-nothing Operator diff decision and classify the watched file by hash."),
            startedAt,
            DateTimeOffset.Now,
            toolResult,
            FormatPayloadForDisplay(toolResult.ResponseJson));

        string logPath = Path.Combine(runRoot, "record-diff-decision.json");
        await File.WriteAllTextAsync(logPath, JsonSerializer.Serialize(result, JsonOptions));
        WriteScriptedSummary(result);
        Console.WriteLine($"Trace: {logPath}");
        return toolResult.IsError ? 1 : 0;
    }

    private static async Task<int> RunFixtureAcceptSmokeAsync(
        SmokeFixture fixture,
        MonitorMcpClientService monitorClient,
        string runRoot)
    {
        const string marker = "            // Accepted fixture smoke: Operator-saved staged candidate verified by Tool Server.";
        string original = await File.ReadAllTextAsync(fixture.TargetSourcePath);
        string proposed = original.Replace(
            "            return name.Trim();",
            marker + Environment.NewLine + "            return name.Trim();",
            StringComparison.Ordinal);
        if (string.Equals(original, proposed, StringComparison.Ordinal))
        {
            Console.WriteLine("Fixture proposal is identical to source; target anchor was not found.");
            return 1;
        }

        Console.WriteLine("MonitorBaseClaude fixture accept smoke");
        Console.WriteLine($"Fixture solution: {fixture.SolutionPath}");
        Console.WriteLine($"Target file: {fixture.TargetSourcePath}");
        Console.WriteLine($"Log root: {runRoot}");
        Console.WriteLine();

        List<ScriptedSmokeResult> results = [];
        int index = 0;

        async Task<ScriptedSmokeResult> StepAsync(string name, string toolName, Dictionary<string, object?>? arguments, string question)
        {
            ScriptedSmokeResult result = await RunScriptedStepAsync(
                ++index,
                new ScriptedSmokeStep(name, toolName, arguments, question),
                monitorClient,
                runRoot);
            results.Add(result);
            WriteScriptedSummary(result);
            return result;
        }

        await StepAsync("Fixture Status", "get_monitor_status", null, "Verify the Tool Server is pointed at the disposable fixture solution.");
        await StepAsync(
            "Fixture Source Map",
            "get_source_map",
            new Dictionary<string, object?> { ["path"] = fixture.TargetRelativePath, ["scope"] = "file" },
            "Verify get_source_map works against the fixture target file.");
        ScriptedSmokeResult submitResult = await StepAsync(
            "Stage Fixture Candidate",
            "submit_file",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.TargetRelativePath,
                ["content"] = proposed,
                ["manifestJson"] = JsonSerializer.Serialize(new
                {
                    operation = "submit_file",
                    filePath = fixture.TargetRelativePath,
                    operationKind = "PreservePatternEdit",
                    note = "Fixture accept smoke candidate."
                }, JsonOptions),
                ["launchDiff"] = false
            },
            "Stage a harmless fixture edit without touching the fixture source before Accept.");

        string? stagedRecordId = ExtractStagedRecordId(submitResult.ToolResult.ResponseJson);
        if (string.IsNullOrWhiteSpace(stagedRecordId))
        {
            Console.WriteLine("Could not extract stagedRecordId from submit_file response.");
            return 1;
        }

        string beforeAcceptHash = ComputeSha256(fixture.TargetSourcePath);
        string originalHash = FindPropertyValue(JsonNode.Parse(submitResult.ToolResult.ResponseJson), "originalHash") ?? string.Empty;
        if (!beforeAcceptHash.Equals(originalHash, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Fixture source changed before Accept; refusing to continue.");
            return 1;
        }

        if (!SimulateOperatorSaveFromWinMerge(submitResult.ToolResult.ResponseJson))
        {
            Console.WriteLine("Could not simulate Operator save for fixture Accept.");
            return 1;
        }

        ScriptedSmokeResult decisionResult = await StepAsync(
            "Accept Fixture Candidate",
            "record_diff_decision",
            new Dictionary<string, object?>
            {
                ["stagedRecordId"] = stagedRecordId,
                ["decision"] = "accepted",
                ["note"] = "Fixture accept smoke verifies Operator-saved staged candidate all-or-none."
            },
            "Verify Accept recognizes the Operator-saved staged candidate and returns accepted by hash.");

        string after = await File.ReadAllTextAsync(fixture.TargetSourcePath);
        string afterHash = ComputeSha256(fixture.TargetSourcePath);
        JsonNode? decisionJson = JsonNode.Parse(decisionResult.ToolResult.ResponseJson);
        string classification = FindPropertyValue(decisionJson, "classification") ?? string.Empty;
        string currentHash = FindPropertyValue(decisionJson, "currentHash") ?? string.Empty;
        string stagedHash = FindPropertyValue(decisionJson, "stagedHash") ?? string.Empty;
        bool accepted = !decisionResult.ToolResult.IsError
            && classification.Equals("accepted", StringComparison.OrdinalIgnoreCase)
            && after.Contains(marker, StringComparison.Ordinal)
            && afterHash.Equals(stagedHash, StringComparison.OrdinalIgnoreCase)
            && currentHash.Equals(stagedHash, StringComparison.OrdinalIgnoreCase);

        string reportPath = Path.Combine(runRoot, "fixture-accept-summary.md");
        await File.WriteAllTextAsync(reportPath, BuildScriptedMarkdownReport(results));
        Console.WriteLine();
        Console.WriteLine($"Fixture accept verified: {accepted}");
        Console.WriteLine($"Before Accept hash: {beforeAcceptHash}");
        Console.WriteLine($"After Accept hash:  {afterHash}");
        Console.WriteLine($"Summary report: {reportPath}");

        return accepted ? 0 : 1;
    }

    private static async Task<int> RunFixtureDecisionGateSmokeAsync(
        SmokeFixture fixture,
        MonitorMcpClientService monitorClient,
        string runRoot)
    {
        string original = await File.ReadAllTextAsync(fixture.TargetSourcePath);
        Console.WriteLine("MonitorBaseClaude fixture decision-gate smoke");
        Console.WriteLine($"Fixture solution: {fixture.SolutionPath}");
        Console.WriteLine($"Target file: {fixture.TargetSourcePath}");
        Console.WriteLine($"Log root: {runRoot}");
        Console.WriteLine();

        List<ScriptedSmokeResult> results = [];
        int index = 0;
        string? latestWorkingFilePath = null;

        async Task<ScriptedSmokeResult> StepAsync(string name, string toolName, Dictionary<string, object?>? arguments, string question)
        {
            ScriptedSmokeResult result = await RunScriptedStepAsync(
                ++index,
                new ScriptedSmokeStep(name, toolName, arguments, question),
                monitorClient,
                runRoot);
            results.Add(result);
            WriteScriptedSummary(result);
            return result;
        }

        async Task<(ScriptedSmokeResult Stage, string StagedRecordId)> StageCandidateAsync(string name, string marker)
        {
            string current = await File.ReadAllTextAsync(fixture.TargetSourcePath);
            string proposed = current.Replace(
                "            return name.Trim();",
                marker + Environment.NewLine + "            return name.Trim();",
                StringComparison.Ordinal);
            if (string.Equals(current, proposed, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Could not stage {name}; fixture anchor was not found.");
            }

            ScriptedSmokeResult stage = await StepAsync(
                "Stage " + name,
                "submit_file",
                new Dictionary<string, object?>
                {
                    ["path"] = fixture.TargetRelativePath,
                    ["content"] = proposed,
                    ["manifestJson"] = JsonSerializer.Serialize(new
                    {
                        operation = "submit_file",
                        filePath = fixture.TargetRelativePath,
                        operationKind = "PreservePatternEdit",
                        note = name
                    }, JsonOptions),
                    ["launchDiff"] = false
                },
                $"Stage decision-gate candidate for {name}.");

            string? stagedRecordId = ExtractStagedRecordId(stage.ToolResult.ResponseJson);
            if (stage.ToolResult.IsError || string.IsNullOrWhiteSpace(stagedRecordId))
            {
                throw new InvalidOperationException($"Could not extract stagedRecordId for {name}.");
            }

            return (stage, stagedRecordId);
        }

        async Task<bool> VerifyBlockedStageAsync(string name)
        {
            string current = await File.ReadAllTextAsync(fixture.TargetSourcePath);
            string proposed = current.Replace(
                "            return name.Trim();",
                "            // Decision gate smoke: this stage must be blocked." + Environment.NewLine + "            return name.Trim();",
                StringComparison.Ordinal);
            ScriptedSmokeResult blockedStage = await StepAsync(
                name,
                "submit_file",
                new Dictionary<string, object?>
                {
                    ["path"] = fixture.TargetRelativePath,
                    ["content"] = proposed,
                    ["manifestJson"] = JsonSerializer.Serialize(new
                    {
                        operation = "submit_file",
                        filePath = fixture.TargetRelativePath,
                        operationKind = "BlockedAfterDirtyUnexpected",
                        note = name
                    }, JsonOptions),
                    ["launchDiff"] = false
                },
                "Verify dirty-unexpected blocks additional staging until explicit recovery.");
            return blockedStage.ToolResult.IsError
                && string.IsNullOrWhiteSpace(ExtractStagedRecordId(blockedStage.ToolResult.ResponseJson));
        }

        async Task<bool> RefreshFixtureAsync(string name)
        {
            ScriptedSmokeResult refreshResult = await StepAsync(
                name,
                "refresh_file",
                new Dictionary<string, object?> { ["sourceFilePath"] = fixture.TargetRelativePath },
                "Refresh fixture source to recover from dirty-unexpected after Host/Operator inspection.");
            JsonNode? response = JsonNode.Parse(refreshResult.ToolResult.ResponseJson);
            string status = FindPropertyValue(response, "status") ?? string.Empty;
            latestWorkingFilePath = FindPropertyValue(response, "workingFilePath");
            return !refreshResult.ToolResult.IsError
                && status.Equals("refreshed", StringComparison.OrdinalIgnoreCase);
        }

        async Task<bool> VerifyRevoteBlockedAsync(string name, string stagedRecordId, string decision)
        {
            ScriptedSmokeResult revoteResult = await StepAsync(
                name,
                "record_diff_decision",
                new Dictionary<string, object?>
                {
                    ["stagedRecordId"] = stagedRecordId,
                    ["decision"] = decision,
                    ["note"] = "Decision gate fixture smoke re-vote must not clear a dirty block."
                },
                "Verify re-voting a blocked dirty-unexpected staged record is refused.");
            return revoteResult.ToolResult.IsError;
        }

        async Task<bool> VerifyImplicitCompareRefreshDoesNotRecoverAsync(string name)
        {
            if (!string.IsNullOrWhiteSpace(latestWorkingFilePath) && File.Exists(latestWorkingFilePath))
            {
                File.Delete(latestWorkingFilePath);
            }

            ScriptedSmokeResult compareResult = await StepAsync(
                name,
                "compare_file",
                new Dictionary<string, object?>
                {
                    ["sourceFilePath"] = fixture.TargetRelativePath,
                    ["refreshIfMissing"] = true
                },
                "Verify compare_file can refresh a missing Working copy without recovering dirty-unexpected.");
            bool compareReturned = !compareResult.ToolResult.IsError;
            bool stillBlocked = await VerifyBlockedStageAsync(name + " Still Blocked");
            return compareReturned && stillBlocked;
        }

        async Task<string> RecordDecisionAsync(string name, string stagedRecordId, string decision)
        {
            ScriptedSmokeResult result = await StepAsync(
                name,
                "record_diff_decision",
                new Dictionary<string, object?>
                {
                    ["stagedRecordId"] = stagedRecordId,
                    ["decision"] = decision,
                    ["note"] = "Decision gate fixture smoke."
                },
                $"Record {decision} for {stagedRecordId} and classify by vote-plus-hash agreement.");
            return FindPropertyValue(JsonNode.Parse(result.ToolResult.ResponseJson), "classification") ?? string.Empty;
        }

        await StepAsync("Fixture Status", "get_monitor_status", null, "Verify the Tool Server is pointed at the disposable fixture solution.");
        await StepAsync(
            "Decision Gate Source Map",
            "get_source_map",
            new Dictionary<string, object?> { ["path"] = fixture.TargetRelativePath, ["scope"] = "file" },
            "Read fixture structure before decision-gate scenarios.");

        ScriptedSmokeResult noOpStage = await StepAsync(
            "Stage No-Op Candidate",
            "submit_file",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.TargetRelativePath,
                ["content"] = original,
                ["manifestJson"] = JsonSerializer.Serialize(new
                {
                    operation = "submit_file",
                    filePath = fixture.TargetRelativePath,
                    operationKind = "NoOp",
                    note = "Decision gate fixture no-op candidate."
                }, JsonOptions),
                ["launchDiff"] = true
            },
            "Verify an identical candidate is reported as no-op-staged and does not request a normal diff.");
        string noOpStatus = FindPropertyValue(JsonNode.Parse(noOpStage.ToolResult.ResponseJson), "status") ?? string.Empty;
        string noOpDiffRequested = FindPropertyValue(JsonNode.Parse(noOpStage.ToolResult.ResponseJson), "diffRequested") ?? string.Empty;
        bool noOpPassed = !noOpStage.ToolResult.IsError
            && noOpStatus.Equals("no-op-staged", StringComparison.OrdinalIgnoreCase)
            && noOpDiffRequested.Equals("False", StringComparison.OrdinalIgnoreCase);

        ScriptedSmokeResult syntaxErrorStage = await StepAsync(
            "Reject Syntax Error Candidate",
            "submit_file",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.TargetRelativePath,
                ["content"] = original.Replace(
                    "            return name.Trim();",
                    "            return name.Trim(",
                    StringComparison.Ordinal),
                ["manifestJson"] = JsonSerializer.Serialize(new
                {
                    operation = "submit_file",
                    filePath = fixture.TargetRelativePath,
                    operationKind = "SyntaxError",
                    note = "Decision gate fixture syntax rejection candidate."
                }, JsonOptions),
                ["launchDiff"] = false
            },
            "Verify malformed C# is rejected before any staged record is created.");
        bool syntaxErrorRejected = syntaxErrorStage.ToolResult.IsError
            && string.IsNullOrWhiteSpace(ExtractStagedRecordId(syntaxErrorStage.ToolResult.ResponseJson));

        (ScriptedSmokeResult acceptStage, string acceptRecordId) = await StageCandidateAsync(
            "Clean Accept",
            "            // Decision gate smoke: clean accept.");
        bool acceptSave = SimulateOperatorSaveFromWinMerge(acceptStage.ToolResult.ResponseJson);
        string cleanAccept = await RecordDecisionAsync("Decision Clean Accept", acceptRecordId, "accepted");
        bool cleanAcceptPassed = acceptSave
            && cleanAccept.Equals("accepted", StringComparison.OrdinalIgnoreCase)
            && (await File.ReadAllTextAsync(fixture.TargetSourcePath)).Contains("Decision gate smoke: clean accept.", StringComparison.Ordinal);

        await File.WriteAllTextAsync(fixture.TargetSourcePath, original);
        (ScriptedSmokeResult rejectStage, string rejectRecordId) = await StageCandidateAsync(
            "Clean Reject",
            "            // Decision gate smoke: clean reject.");
        string cleanReject = await RecordDecisionAsync("Decision Clean Reject", rejectRecordId, "rejected");
        bool cleanRejectPassed = cleanReject.Equals("rejected", StringComparison.OrdinalIgnoreCase)
            && string.Equals(await File.ReadAllTextAsync(fixture.TargetSourcePath), original, StringComparison.Ordinal);

        (ScriptedSmokeResult acceptNotAppliedStage, string acceptNotAppliedRecordId) = await StageCandidateAsync(
            "Accept Not Applied",
            "            // Decision gate smoke: accept vote without save.");
        string acceptNotApplied = await RecordDecisionAsync("Decision Accept Not Applied", acceptNotAppliedRecordId, "accepted");
        bool acceptNotAppliedPassed = acceptNotAppliedStage.ToolResult.IsError == false
            && acceptNotApplied.Equals("dirty-unexpected", StringComparison.OrdinalIgnoreCase)
            && string.Equals(await File.ReadAllTextAsync(fixture.TargetSourcePath), original, StringComparison.Ordinal);
        bool acceptNotAppliedRevoteBlocked = await VerifyRevoteBlockedAsync(
            "Re-Vote Blocked After Accept Not Applied",
            acceptNotAppliedRecordId,
            "rejected");
        bool acceptNotAppliedBlocked = await VerifyBlockedStageAsync("Stage Blocked After Accept Not Applied");
        bool acceptNotAppliedRecovered = await RefreshFixtureAsync("Recover After Accept Not Applied");

        (ScriptedSmokeResult rejectAfterSaveStage, string rejectAfterSaveRecordId) = await StageCandidateAsync(
            "Reject After Save",
            "            // Decision gate smoke: reject vote after candidate landed.");
        bool rejectAfterSaveSave = SimulateOperatorSaveFromWinMerge(rejectAfterSaveStage.ToolResult.ResponseJson);
        string rejectAfterSave = await RecordDecisionAsync("Decision Reject After Save", rejectAfterSaveRecordId, "rejected");
        bool rejectAfterSavePassed = rejectAfterSaveSave
            && rejectAfterSave.Equals("dirty-unexpected", StringComparison.OrdinalIgnoreCase);

        await File.WriteAllTextAsync(fixture.TargetSourcePath, original);
        bool rejectAfterSaveRecovered = await RefreshFixtureAsync("Recover After Reject After Save");
        (ScriptedSmokeResult dirtyStage, string dirtyRecordId) = await StageCandidateAsync(
            "Dirty External Edit",
            "            // Decision gate smoke: staged candidate should not match dirty source.");
        await File.AppendAllTextAsync(fixture.TargetSourcePath, Environment.NewLine + "// Decision gate smoke: external dirty edit." + Environment.NewLine);
        string dirtyExternal = await RecordDecisionAsync("Decision Dirty External Edit", dirtyRecordId, "rejected");
        bool dirtyExternalPassed = dirtyExternal.Equals("dirty-unexpected", StringComparison.OrdinalIgnoreCase);
        bool compareRefreshDidNotRecover = await VerifyImplicitCompareRefreshDoesNotRecoverAsync("Compare Missing Working While Blocked");
        bool dirtyExternalBlocked = await VerifyBlockedStageAsync("Stage Blocked After Dirty External Edit");

        bool passed = noOpPassed
            && syntaxErrorRejected
            && cleanAcceptPassed
            && cleanRejectPassed
            && acceptNotAppliedPassed
            && acceptNotAppliedRevoteBlocked
            && acceptNotAppliedBlocked
            && acceptNotAppliedRecovered
            && rejectAfterSavePassed
            && rejectAfterSaveRecovered
            && dirtyExternalPassed
            && compareRefreshDidNotRecover
            && dirtyExternalBlocked;

        string reportPath = Path.Combine(runRoot, "fixture-decision-gate-summary.md");
        await File.WriteAllTextAsync(reportPath, BuildScriptedMarkdownReport(results));
        Console.WriteLine();
        Console.WriteLine($"Fixture decision gate verified: {passed}");
        Console.WriteLine($"no-op staged: {noOpStatus} ({noOpPassed})");
        Console.WriteLine($"syntax error rejected: {syntaxErrorRejected}");
        Console.WriteLine($"clean accept: {cleanAccept} ({cleanAcceptPassed})");
        Console.WriteLine($"clean reject: {cleanReject} ({cleanRejectPassed})");
        Console.WriteLine($"accept not applied: {acceptNotApplied} ({acceptNotAppliedPassed})");
        Console.WriteLine($"accept not applied re-vote blocked: {acceptNotAppliedRevoteBlocked}");
        Console.WriteLine($"accept not applied blocks next stage: {acceptNotAppliedBlocked}");
        Console.WriteLine($"accept not applied recovery: {acceptNotAppliedRecovered}");
        Console.WriteLine($"reject after save: {rejectAfterSave} ({rejectAfterSavePassed})");
        Console.WriteLine($"reject after save recovery: {rejectAfterSaveRecovered}");
        Console.WriteLine($"dirty external edit: {dirtyExternal} ({dirtyExternalPassed})");
        Console.WriteLine($"compare refresh does not recover dirty block: {compareRefreshDidNotRecover}");
        Console.WriteLine($"dirty external edit blocks next stage: {dirtyExternalBlocked}");
        Console.WriteLine($"Summary report: {reportPath}");

        return passed ? 0 : 1;
    }

    private static async Task<int> RunFixtureRoslynSurgerySmokeAsync(
        SmokeFixture fixture,
        MonitorMcpClientService monitorClient,
        string runRoot)
    {
        Console.WriteLine("MonitorBaseClaude fixture Roslyn surgery smoke");
        Console.WriteLine($"Fixture solution: {fixture.SolutionPath}");
        Console.WriteLine($"Target file: {fixture.TargetSourcePath}");
        Console.WriteLine($"Log root: {runRoot}");
        Console.WriteLine();

        List<ScriptedSmokeResult> results = [];
        int index = 0;

        async Task<ScriptedSmokeResult> StepAsync(string name, string toolName, Dictionary<string, object?>? arguments, string question)
        {
            ScriptedSmokeResult result = await RunScriptedStepAsync(
                ++index,
                new ScriptedSmokeStep(name, toolName, arguments, question),
                monitorClient,
                runRoot);
            results.Add(result);
            WriteScriptedSummary(result);
            return result;
        }

        async Task<bool> StageAndAcceptAsync(string name, string toolName, Dictionary<string, object?> arguments, string question)
        {
            ScriptedSmokeResult stageResult = await StepAsync(name, toolName, arguments, question);
            string? stagedRecordId = ExtractStagedRecordId(stageResult.ToolResult.ResponseJson);
            if (stageResult.ToolResult.IsError || string.IsNullOrWhiteSpace(stagedRecordId))
            {
                return false;
            }

            if (!SimulateOperatorSaveFromWinMerge(stageResult.ToolResult.ResponseJson))
            {
                return false;
            }

            ScriptedSmokeResult decisionResult = await StepAsync(
                "Accept " + name,
                "record_diff_decision",
                new Dictionary<string, object?>
                {
                    ["stagedRecordId"] = stagedRecordId,
                    ["decision"] = "accepted",
                    ["note"] = $"Fixture Roslyn surgery smoke accepted {toolName}."
                },
                $"Accept staged result from {toolName}.");
            string classification = FindPropertyValue(JsonNode.Parse(decisionResult.ToolResult.ResponseJson), "classification") ?? string.Empty;
            return !decisionResult.ToolResult.IsError && classification.Equals("accepted", StringComparison.OrdinalIgnoreCase);
        }

        await StepAsync("Fixture Status", "get_monitor_status", null, "Verify the Tool Server is pointed at the disposable fixture solution.");
        ScriptedSmokeResult initialMap = await StepAsync(
            "Initial Fixture Source Map",
            "get_source_map",
            new Dictionary<string, object?> { ["path"] = fixture.TargetRelativePath, ["scope"] = "file" },
            "Read initial fixture structure.");
        string? normalizeStableKey = FindSourceMapSymbolProperty(initialMap.ToolResult.ResponseJson, "NormalizeName", "stableSymbolKey");
        string sourceMapSelector = JsonSerializer.Serialize(new
        {
            stableSymbolKey = normalizeStableKey
        }, JsonOptions);
        ScriptedSmokeResult selectorSymbolRead = await StepAsync(
            "Read NormalizeName By Stable Key",
            "get_symbol",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.TargetRelativePath,
                ["symbolSelectorJson"] = sourceMapSelector
            },
            "Verify get_symbol can read the exact method body by get_source_map stableSymbolKey before editing.");

        bool addUsingAccepted = await StageAndAcceptAsync(
            "Add Using",
            "add_using",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.TargetRelativePath,
                ["namespace"] = "System.Globalization"
            },
            "Stage adding a using directive.");

        string normalizeSelector = JsonSerializer.Serialize(new
        {
            containingNamespace = "SchemaStudio.Data",
            containingType = "BaseTableRepository",
            memberKind = "method",
            name = "NormalizeName",
            parameterTypes = new[] { "string" },
            arity = 0
        }, JsonOptions);
        bool submitSymbolAccepted = await StageAndAcceptAsync(
            "Replace NormalizeName",
            "submit_symbol",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.TargetRelativePath,
                ["symbolSelectorJson"] = normalizeSelector,
                ["code"] = """
                    public string NormalizeName(string name)
                    {
                        ArgumentException.ThrowIfNullOrWhiteSpace(name);
                        return name.Trim().ToUpper(System.Globalization.CultureInfo.InvariantCulture);
                    }
                    """
            },
            "Stage replacing one method by structured selector.");

        bool addSymbolAccepted = await StageAndAcceptAsync(
            "Add HasName Symbol",
            "add_symbol",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.TargetRelativePath,
                ["containingType"] = "BaseTableRepository",
                ["symbolType"] = "method",
                ["afterSymbol"] = "NormalizeName",
                ["code"] = """
                    public bool HasName(string name)
                    {
                        return !string.IsNullOrWhiteSpace(name);
                    }
                    """
            },
            "Stage adding one method to the fixture repository.");

        string hasNameSelector = JsonSerializer.Serialize(new
        {
            containingNamespace = "SchemaStudio.Data",
            containingType = "BaseTableRepository",
            memberKind = "method",
            name = "HasName",
            parameterTypes = new[] { "string" },
            arity = 0
        }, JsonOptions);
        bool removeSymbolAccepted = await StageAndAcceptAsync(
            "Remove HasName Symbol",
            "remove_symbol",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.TargetRelativePath,
                ["symbolSelectorJson"] = hasNameSelector
            },
            "Stage removing the method that was just added.");

        bool removeUsingAccepted = await StageAndAcceptAsync(
            "Remove Using",
            "remove_using",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.TargetRelativePath,
                ["namespace"] = "System.Globalization"
            },
            "Stage removing the using directive that was just added.");

        ScriptedSmokeResult finalMap = await StepAsync(
            "Final Fixture Source Map",
            "get_source_map",
            new Dictionary<string, object?> { ["path"] = fixture.TargetRelativePath, ["scope"] = "file" },
            "Read final fixture structure after Roslyn surgery smoke.");

        string finalText = await File.ReadAllTextAsync(fixture.TargetSourcePath);
        bool finalSourceLooksRight = finalText.Contains("ArgumentException.ThrowIfNullOrWhiteSpace(name);", StringComparison.Ordinal)
            && finalText.Contains("System.Globalization.CultureInfo.InvariantCulture", StringComparison.Ordinal)
            && !finalText.Contains("public bool HasName", StringComparison.Ordinal)
            && !finalText.Contains("using System.Globalization;", StringComparison.Ordinal);
        bool sourceMapHasNormalizeName = finalMap.ToolResult.ResponseJson.Contains("NormalizeName", StringComparison.Ordinal);
        bool selectorReadPassed = !selectorSymbolRead.ToolResult.IsError
            && !string.IsNullOrWhiteSpace(normalizeStableKey)
            && selectorSymbolRead.ToolResult.ResponseJson.Contains("return name.Trim();", StringComparison.Ordinal);
        bool passed = addUsingAccepted
            && selectorReadPassed
            && submitSymbolAccepted
            && addSymbolAccepted
            && removeSymbolAccepted
            && removeUsingAccepted
            && finalSourceLooksRight
            && sourceMapHasNormalizeName;

        string reportPath = Path.Combine(runRoot, "fixture-roslyn-surgery-summary.md");
        await File.WriteAllTextAsync(reportPath, BuildScriptedMarkdownReport(results));
        Console.WriteLine();
        Console.WriteLine($"Fixture Roslyn surgery verified: {passed}");
        Console.WriteLine($"get_symbol stable key read: {selectorReadPassed}");
        Console.WriteLine($"add_using accepted: {addUsingAccepted}");
        Console.WriteLine($"submit_symbol accepted: {submitSymbolAccepted}");
        Console.WriteLine($"add_symbol accepted: {addSymbolAccepted}");
        Console.WriteLine($"remove_symbol accepted: {removeSymbolAccepted}");
        Console.WriteLine($"remove_using accepted: {removeUsingAccepted}");
        Console.WriteLine($"Final source checks: {finalSourceLooksRight}");
        Console.WriteLine($"Summary report: {reportPath}");

        return passed ? 0 : 1;
    }

    private static async Task<int> RunFixtureRazorSmokeAsync(
        SmokeFixture fixture,
        MonitorMcpClientService monitorClient,
        string runRoot)
    {
        string original = await File.ReadAllTextAsync(fixture.TargetSourcePath);
        const string marker = "    <p class=\"status\">Fixture staged value: @currentCount</p>";
        string proposed = original.Replace(
            "    <p role=\"status\">Current count: @currentCount</p>",
            "    <p role=\"status\">Current count: @currentCount</p>" + Environment.NewLine + marker,
            StringComparison.Ordinal);
        if (string.Equals(original, proposed, StringComparison.Ordinal))
        {
            Console.WriteLine("Razor fixture proposal is identical to source; target anchor was not found.");
            return 1;
        }

        Console.WriteLine("MonitorBaseClaude fixture Razor smoke");
        Console.WriteLine($"Fixture solution: {fixture.SolutionPath}");
        Console.WriteLine($"Target file: {fixture.TargetSourcePath}");
        Console.WriteLine($"Log root: {runRoot}");
        Console.WriteLine();

        List<ScriptedSmokeResult> results = [];
        int index = 0;

        async Task<ScriptedSmokeResult> StepAsync(string name, string toolName, Dictionary<string, object?>? arguments, string question)
        {
            ScriptedSmokeResult result = await RunScriptedStepAsync(
                ++index,
                new ScriptedSmokeStep(name, toolName, arguments, question),
                monitorClient,
                runRoot);
            results.Add(result);
            WriteScriptedSummary(result);
            return result;
        }

        await StepAsync("Razor Fixture Status", "get_monitor_status", null, "Verify the Tool Server is pointed at the disposable Razor fixture solution.");
        ScriptedSmokeResult findResult = await StepAsync(
            "Find Razor Files",
            "find_file",
            new Dictionary<string, object?> { ["fileNameOrPattern"] = "*.razor", ["maxResults"] = 10 },
            "Verify the Tool Server can discover Razor files in the fixture.");
        ScriptedSmokeResult readResult = await StepAsync(
            "Read Razor File",
            "get_file",
            new Dictionary<string, object?> { ["sourceFilePath"] = fixture.TargetRelativePath },
            "Read the full Razor fixture file.");
        ScriptedSmokeResult outlineResult = await StepAsync(
            "Outline Razor File",
            "get_file_outline",
            new Dictionary<string, object?> { ["path"] = fixture.TargetRelativePath },
            "Verify Razor does not go through the C# symbol outline path yet.");
        ScriptedSmokeResult submitResult = await StepAsync(
            "Stage Razor Candidate",
            "submit_file",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.TargetRelativePath,
                ["content"] = proposed,
                ["manifestJson"] = JsonSerializer.Serialize(new
                {
                    operation = "submit_file",
                    filePath = fixture.TargetRelativePath,
                    operationKind = "PreservePatternEdit",
                    note = "Razor fixture full-file staging candidate."
                }, JsonOptions),
                ["launchDiff"] = false
            },
            "Stage a Razor full-file candidate and require Razor validation to be visibly pending.");

        string? stagedRecordId = ExtractStagedRecordId(submitResult.ToolResult.ResponseJson);
        if (string.IsNullOrWhiteSpace(stagedRecordId))
        {
            Console.WriteLine("Could not extract stagedRecordId from Razor submit_file response.");
            return 1;
        }

        string beforeAcceptHash = ComputeSha256(fixture.TargetSourcePath);
        JsonNode? submitJson = JsonNode.Parse(submitResult.ToolResult.ResponseJson);
        string originalHash = FindPropertyValue(submitJson, "originalHash") ?? string.Empty;
        string overlayStatus = submitJson?["OverlayValidation"]?["Status"]?.ToString()
            ?? submitJson?["overlayValidation"]?["status"]?.ToString()
            ?? string.Empty;
        bool overlayPending = overlayStatus.Equals("razor-validation-pending", StringComparison.OrdinalIgnoreCase);
        if (!beforeAcceptHash.Equals(originalHash, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Razor fixture source changed before Accept; refusing to continue.");
            return 1;
        }

        if (!SimulateOperatorSaveFromWinMerge(submitResult.ToolResult.ResponseJson))
        {
            Console.WriteLine("Could not simulate Operator save for Razor fixture Accept.");
            return 1;
        }

        ScriptedSmokeResult decisionResult = await StepAsync(
            "Accept Razor Candidate",
            "record_diff_decision",
            new Dictionary<string, object?>
            {
                ["stagedRecordId"] = stagedRecordId,
                ["decision"] = "accepted",
                ["note"] = "Razor fixture smoke verifies Operator-saved staged candidate all-or-none."
            },
            "Verify Accept recognizes the Operator-saved Razor staged candidate and returns accepted by hash.");

        string after = await File.ReadAllTextAsync(fixture.TargetSourcePath);
        string afterHash = ComputeSha256(fixture.TargetSourcePath);
        JsonNode? decisionJson = JsonNode.Parse(decisionResult.ToolResult.ResponseJson);
        string classification = FindPropertyValue(decisionJson, "classification") ?? string.Empty;
        string currentHash = FindPropertyValue(decisionJson, "currentHash") ?? string.Empty;
        string stagedHash = FindPropertyValue(decisionJson, "stagedHash") ?? string.Empty;
        bool discoveredRazor = findResult.ToolResult.ResponseJson.Contains("Counter.razor", StringComparison.OrdinalIgnoreCase);
        bool readRazor = readResult.ToolResult.ResponseJson.Contains("@code", StringComparison.Ordinal);
        bool outlineEmpty = outlineResult.ToolResult.ResponseJson.Contains("\"symbols\": []", StringComparison.OrdinalIgnoreCase)
            || outlineResult.ToolResult.ResponseJson.Contains("\"Symbols\": []", StringComparison.Ordinal);
        bool accepted = !decisionResult.ToolResult.IsError
            && classification.Equals("accepted", StringComparison.OrdinalIgnoreCase)
            && after.Contains(marker, StringComparison.Ordinal)
            && afterHash.Equals(stagedHash, StringComparison.OrdinalIgnoreCase)
            && currentHash.Equals(stagedHash, StringComparison.OrdinalIgnoreCase);
        bool passed = discoveredRazor
            && readRazor
            && outlineEmpty
            && overlayPending
            && accepted;

        string reportPath = Path.Combine(runRoot, "fixture-razor-summary.md");
        await File.WriteAllTextAsync(reportPath, BuildScriptedMarkdownReport(results));
        Console.WriteLine();
        Console.WriteLine($"Fixture Razor verified: {passed}");
        Console.WriteLine($"find_file saw Razor: {discoveredRazor}");
        Console.WriteLine($"get_file read @code: {readRazor}");
        Console.WriteLine($"outline empty for Razor: {outlineEmpty}");
        Console.WriteLine($"overlay status: {overlayStatus}");
        Console.WriteLine($"accepted by hash: {accepted}");
        Console.WriteLine($"Summary report: {reportPath}");

        return passed ? 0 : 1;
    }

    private static void LaunchOperatorDiffFromSmokeTest(MonitorMcpToolCallResult toolResult)
    {
        if (toolResult.IsError)
        {
            return;
        }

        JsonNode? response = JsonNode.Parse(toolResult.ResponseJson);
        string? stagedFilePath = response?["stagedFilePath"]?.GetValue<string>();
        string? sourceFilePath = response?["sourceFilePath"]?.GetValue<string>();
        string? winMergePath = ResolveWinMergePath();

        if (string.IsNullOrWhiteSpace(stagedFilePath)
            || string.IsNullOrWhiteSpace(sourceFilePath)
            || string.IsNullOrWhiteSpace(winMergePath))
        {
            Console.WriteLine("Operator diff not launched; missing staged path, source path, or WinMerge.");
            return;
        }

        string displayName = $"Data\\{Path.GetFileName(sourceFilePath)}";
        ProcessStartInfo startInfo = new()
        {
            FileName = winMergePath,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Maximized
        };
        startInfo.ArgumentList.Add("/u");
        startInfo.ArgumentList.Add("/maximize");
        startInfo.ArgumentList.Add("/ignoreeol:1");
        startInfo.ArgumentList.Add("/dl");
        startInfo.ArgumentList.Add($"New/Proposed (Working) - {displayName}");
        startInfo.ArgumentList.Add("/dr");
        startInfo.ArgumentList.Add($"Existing Source (Right) - {displayName}");
        startInfo.ArgumentList.Add(stagedFilePath);
        startInfo.ArgumentList.Add(sourceFilePath);

        Process.Start(startInfo);
        Console.WriteLine("Operator diff launched directly from smoke-test Host.");
    }

    private static string? ResolveWinMergePath()
    {
        string[] candidates =
        [
            @"C:\Program Files\WinMerge\WinMergeU.exe",
            @"C:\Program Files (x86)\WinMerge\WinMergeU.exe"
        ];

        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task<ScriptedSmokeResult> RunScriptedStepAsync(
        int index,
        ScriptedSmokeStep step,
        MonitorMcpClientService monitorClient,
        string runRoot)
    {
        DateTimeOffset startedAt = DateTimeOffset.Now;
        MonitorMcpToolCallResult toolResult = await monitorClient.CallToolAsync(step.ToolName, step.Arguments);
        ScriptedSmokeResult result = new(
            step,
            startedAt,
            DateTimeOffset.Now,
            toolResult,
            FormatPayloadForDisplay(toolResult.ResponseJson));

        string logPath = Path.Combine(runRoot, $"{index:00}_{SanitizeFileName(step.Name)}.json");
        await File.WriteAllTextAsync(logPath, JsonSerializer.Serialize(result, JsonOptions));
        return result;
    }

    private static void WriteScriptedSummary(ScriptedSmokeResult result)
    {
        Console.WriteLine($"{result.Step.Name} -> {result.Step.ToolName}");
        Console.WriteLine(result.Step.Question);
        Console.WriteLine($"Error: {result.ToolResult.IsError}");
        Console.WriteLine("Response:");
        Console.WriteLine(Indent(TruncateForConsole(result.ToolResultDisplay, 2400), "  "));
        Console.WriteLine(new string('-', 80));
    }

    private static string BuildScriptedMarkdownReport(IReadOnlyList<ScriptedSmokeResult> results)
    {
        List<string> lines =
        [
            "# Scripted Tool Smoke Test Summary",
            "",
            $"Generated: {DateTimeOffset.Now:O}",
            ""
        ];

        foreach (ScriptedSmokeResult result in results)
        {
            lines.Add($"## {result.Step.Name}");
            lines.Add("");
            lines.Add($"Tool: `{result.Step.ToolName}`");
            lines.Add($"Question: {result.Step.Question}");
            lines.Add($"Error: `{result.ToolResult.IsError}`");
            lines.Add("");
            lines.Add("```text");
            lines.Add(NormalizeNewlines(result.ToolResultDisplay));
            lines.Add("```");
            lines.Add("");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildSourceMapSummary(string responseJson, string requestedPath, string scope, string mode)
    {
        List<string> lines =
        [
            "# Source Map Smoke Summary",
            "",
            $"Generated: {DateTimeOffset.Now:O}",
            $"Requested path: `{requestedPath}`",
            $"Requested scope: `{scope}`",
            $"Requested mode: `{mode}`",
            ""
        ];

        try
        {
            JsonNode? root = JsonNode.Parse(responseJson);
            lines.Add($"Reported scope: `{FindPropertyValue(root, "scope") ?? string.Empty}`");
            lines.Add($"Reported mode: `{FindPropertyValue(root, "mode") ?? string.Empty}`");
            lines.Add($"Watched project: `{FindPropertyValue(root, "watchedProjectFolder") ?? string.Empty}`");
            lines.Add($"File count: `{FindPropertyValue(root, "fileCount") ?? "0"}`");
            lines.Add($"Symbol count: `{FindPropertyValue(root, "symbolCount") ?? "0"}`");
            lines.Add($"Estimated token proxy: `{FindPropertyValue(root, "estimatedTokenProxy") ?? "0"}`");
            lines.Add($"Budget limit: `{FindPropertyValue(root, "budgetLimit") ?? "0"}`");
            lines.Add($"Was truncated: `{FindPropertyValue(root, "wasTruncated") ?? "false"}`");
            lines.Add("");
            JsonArray? files = root?["files"] as JsonArray ?? root?["Files"] as JsonArray;
            if (files is not null)
            {
                foreach (JsonNode? fileNode in files.Take(25))
                {
                    string relativePath = FindPropertyValue(fileNode, "relativeSourcePath") ?? string.Empty;
                    string sha = FindPropertyValue(fileNode, "sha256") ?? string.Empty;
                    JsonArray? symbols = fileNode?["symbols"] as JsonArray ?? fileNode?["Symbols"] as JsonArray;
                    lines.Add($"## {relativePath}");
                    lines.Add("");
                    lines.Add($"Hash: `{sha}`");
                    lines.Add($"Parse status: `{FindPropertyValue(fileNode, "parseStatus") ?? string.Empty}`");
                    lines.Add($"Diagnostics: `{FindPropertyValue(fileNode, "diagnosticCount") ?? "0"}`");
                    lines.Add($"Symbols: `{symbols?.Count ?? 0}`");
                    lines.Add("");
                    if (symbols is not null)
                    {
                        lines.Add("| Kind | Name | Lines | Return | Parameters | Attributes | Base Types | Signature |");
                        lines.Add("| --- | --- | --- | --- | --- | --- | --- | --- |");
                        foreach (JsonNode? symbol in symbols.Take(40))
                        {
                            string kind = FindPropertyValue(symbol, "kind") ?? string.Empty;
                            string name = FindPropertyValue(symbol, "name") ?? string.Empty;
                            string startLine = FindPropertyValue(symbol, "startLine") ?? string.Empty;
                            string endLine = FindPropertyValue(symbol, "endLine") ?? string.Empty;
                            string returnType = FindPropertyValue(symbol, "returnType") ?? string.Empty;
                            string parameterTypes = FormatJsonStringArray(symbol?["parameterTypes"] as JsonArray ?? symbol?["ParameterTypes"] as JsonArray);
                            string attributes = FormatAttributeArray(symbol?["attributes"] as JsonArray ?? symbol?["Attributes"] as JsonArray);
                            string baseTypes = FormatJsonStringArray(symbol?["baseTypes"] as JsonArray ?? symbol?["BaseTypes"] as JsonArray);
                            string signature = (FindPropertyValue(symbol, "signature") ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal);
                            lines.Add($"| `{kind}` | `{name}` | `{startLine}-{endLine}` | `{returnType}` | `{parameterTypes}` | `{attributes}` | `{baseTypes}` | `{signature}` |");
                        }
                        lines.Add("");
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            lines.Add("Could not parse source-map JSON.");
            lines.Add("");
            lines.Add("```text");
            lines.Add(ex.Message);
            lines.Add("```");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static SourceMapCorpusFileAnalysis AnalyzeSourceMapCorpusFile(
        string relativePath,
        string sourcePath,
        string sourceMapPath,
        long sourceBytes,
        long sourceChars,
        long sourceMapBytes,
        long sourceMapChars,
        bool isError,
        DateTimeOffset startedAt,
        DateTimeOffset finishedAt,
        string responseJson)
    {
        string parseStatus = string.Empty;
        int diagnosticCount = 0;
        int symbolCount = 0;
        Dictionary<string, int> kindCounts = new(StringComparer.OrdinalIgnoreCase);
        string? errorMessage = null;

        try
        {
            JsonNode? root = JsonNode.Parse(responseJson);
            errorMessage = FindPropertyValue(root, "message") ?? FindPropertyValue(root, "error");
            JsonArray? files = root?["files"] as JsonArray ?? root?["Files"] as JsonArray;
            JsonNode? fileNode = files?.FirstOrDefault();
            if (fileNode is not null)
            {
                parseStatus = FindPropertyValue(fileNode, "parseStatus") ?? string.Empty;
                diagnosticCount = ParseInt(FindPropertyValue(fileNode, "diagnosticCount"));
                JsonArray? symbols = fileNode["symbols"] as JsonArray ?? fileNode["Symbols"] as JsonArray;
                if (symbols is not null)
                {
                    symbolCount = symbols.Count;
                    foreach (JsonNode? symbol in symbols)
                    {
                        string kind = FindPropertyValue(symbol, "kind") ?? "(unknown)";
                        kindCounts[kind] = kindCounts.TryGetValue(kind, out int count) ? count + 1 : 1;
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            errorMessage = ex.Message;
            isError = true;
        }

        return new SourceMapCorpusFileAnalysis(
            relativePath,
            sourcePath,
            sourceMapPath,
            sourceBytes,
            sourceMapBytes,
            sourceChars,
            sourceMapChars,
            EstimateTokens(sourceChars),
            EstimateTokens(sourceMapChars),
            sourceBytes == 0 ? 0 : Math.Round((double)sourceMapBytes / sourceBytes, 3),
            isError,
            parseStatus,
            diagnosticCount,
            symbolCount,
            GetKindCount(kindCounts, "class") + GetKindCount(kindCounts, "record") + GetKindCount(kindCounts, "struct") + GetKindCount(kindCounts, "interface"),
            GetKindCount(kindCounts, "method"),
            GetKindCount(kindCounts, "field"),
            GetKindCount(kindCounts, "property"),
            GetKindCount(kindCounts, "event"),
            kindCounts.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(),
            startedAt,
            finishedAt,
            errorMessage);
    }

    private static JsonObject BuildCompactSourceMapFile(string responseJson)
    {
        JsonNode? root = JsonNode.Parse(responseJson);
        JsonNode? fileNode = (root?["files"] as JsonArray ?? root?["Files"] as JsonArray)?.FirstOrDefault();
        JsonArray compactSymbols = [];
        JsonArray? symbols = fileNode?["symbols"] as JsonArray ?? fileNode?["Symbols"] as JsonArray;
        if (symbols is not null)
        {
            foreach (JsonNode? symbol in symbols)
            {
                JsonObject compactSymbol = new()
                {
                    ["kind"] = FindPropertyValue(symbol, "kind"),
                    ["name"] = FindPropertyValue(symbol, "name"),
                    ["stableSymbolKey"] = FindPropertyValue(symbol, "stableSymbolKey"),
                    ["signature"] = FindPropertyValue(symbol, "signature"),
                    ["containingType"] = FindPropertyValue(symbol, "containingType"),
                    ["returnType"] = FindPropertyValue(symbol, "returnType"),
                    ["startLine"] = ParseInt(FindPropertyValue(symbol, "startLine")),
                    ["endLine"] = ParseInt(FindPropertyValue(symbol, "endLine"))
                };

                JsonArray? parameterTypes = symbol?["parameterTypes"] as JsonArray ?? symbol?["ParameterTypes"] as JsonArray;
                if (parameterTypes is { Count: > 0 })
                {
                    JsonArray compactParameters = [];
                    foreach (JsonNode? parameter in parameterTypes)
                    {
                        compactParameters.Add(parameter?.GetValue<string>());
                    }

                    compactSymbol["parameterTypes"] = compactParameters;
                }

                JsonArray? attributes = symbol?["attributes"] as JsonArray ?? symbol?["Attributes"] as JsonArray;
                if (attributes is { Count: > 0 })
                {
                    JsonArray compactAttributes = [];
                    foreach (JsonNode? attribute in attributes)
                    {
                        compactAttributes.Add(FindPropertyValue(attribute, "name"));
                    }

                    compactSymbol["attributes"] = compactAttributes;
                }

                compactSymbols.Add(compactSymbol);
            }
        }

        return new JsonObject
        {
            ["relativeSourcePath"] = FindPropertyValue(fileNode, "relativeSourcePath"),
            ["sha256"] = FindPropertyValue(fileNode, "sha256"),
            ["parseStatus"] = FindPropertyValue(fileNode, "parseStatus"),
            ["diagnosticCount"] = ParseInt(FindPropertyValue(fileNode, "diagnosticCount")),
            ["symbols"] = compactSymbols
        };
    }

    private static JsonObject ExtractFirstSourceMapFile(string responseJson)
    {
        JsonNode? root = JsonNode.Parse(responseJson);
        JsonNode? fileNode = (root?["files"] as JsonArray ?? root?["Files"] as JsonArray)?.FirstOrDefault();
        if (fileNode is null)
        {
            return [];
        }

        return JsonNode.Parse(fileNode.ToJsonString())?.AsObject() ?? [];
    }

    private static JsonObject BuildNavigationSourceMapFile(string responseJson)
    {
        JsonNode? root = JsonNode.Parse(responseJson);
        JsonNode? fileNode = (root?["files"] as JsonArray ?? root?["Files"] as JsonArray)?.FirstOrDefault();
        JsonArray navigationSymbols = [];
        JsonArray? symbols = fileNode?["symbols"] as JsonArray ?? fileNode?["Symbols"] as JsonArray;
        if (symbols is not null)
        {
            foreach (JsonNode? symbol in symbols)
            {
                navigationSymbols.Add(new JsonObject
                {
                    ["kind"] = FindPropertyValue(symbol, "kind"),
                    ["name"] = FindPropertyValue(symbol, "name"),
                    ["signature"] = FindPropertyValue(symbol, "signature"),
                    ["startLine"] = ParseInt(FindPropertyValue(symbol, "startLine")),
                    ["endLine"] = ParseInt(FindPropertyValue(symbol, "endLine"))
                });
            }
        }

        return new JsonObject
        {
            ["relativeSourcePath"] = FindPropertyValue(fileNode, "relativeSourcePath"),
            ["parseStatus"] = FindPropertyValue(fileNode, "parseStatus"),
            ["diagnosticCount"] = ParseInt(FindPropertyValue(fileNode, "diagnosticCount")),
            ["symbols"] = navigationSymbols
        };
    }

    private static string BuildSourceMapCorpusMarkdownReport(SourceMapCorpusAnalysis corpus)
    {
        double mapToSourceBytes = corpus.TotalSourceBytes == 0
            ? 0
            : (double)corpus.TotalSourceMapBytes / corpus.TotalSourceBytes;
        double mapToSourceTokens = corpus.EstimatedSourceTokens == 0
            ? 0
            : (double)corpus.EstimatedSourceMapTokens / corpus.EstimatedSourceTokens;
        double compactToSourceBytes = corpus.TotalSourceBytes == 0
            ? 0
            : (double)corpus.CompactSourceMapBytes / corpus.TotalSourceBytes;
        double compactToSourceTokens = corpus.EstimatedSourceTokens == 0
            ? 0
            : (double)corpus.EstimatedCompactSourceMapTokens / corpus.EstimatedSourceTokens;
        double navigationToSourceBytes = corpus.TotalSourceBytes == 0
            ? 0
            : (double)corpus.NavigationIndexBytes / corpus.TotalSourceBytes;
        double navigationToSourceTokens = corpus.EstimatedSourceTokens == 0
            ? 0
            : (double)corpus.EstimatedNavigationIndexTokens / corpus.EstimatedSourceTokens;
        List<string> lines =
        [
            "# Source Map Corpus Smoke Summary",
            "",
            $"Generated: {DateTimeOffset.Now:O}",
            $"Started: `{corpus.StartedAt:O}`",
            $"Finished: `{corpus.FinishedAt:O}`",
            $"Watched root: `{corpus.WatchedRoot}`",
            $"Target: `{corpus.TargetPath}`",
            "",
            "## Totals",
            "",
            $"Files scanned: `{corpus.FileCount}`",
            $"Successful maps: `{corpus.SuccessFileCount}`",
            $"Error maps: `{corpus.ErrorFileCount}`",
            $"Source bytes: `{corpus.TotalSourceBytes:N0}`",
            $"Source-map bytes: `{corpus.TotalSourceMapBytes:N0}`",
            $"Compact index bytes: `{corpus.CompactSourceMapBytes:N0}`",
            $"Navigation index bytes: `{corpus.NavigationIndexBytes:N0}`",
            $"Map/source byte ratio: `{mapToSourceBytes:0.000}`",
            $"Compact/source byte ratio: `{compactToSourceBytes:0.000}`",
            $"Navigation/source byte ratio: `{navigationToSourceBytes:0.000}`",
            $"Estimated source tokens: `{corpus.EstimatedSourceTokens:N0}`",
            $"Estimated source-map tokens: `{corpus.EstimatedSourceMapTokens:N0}`",
            $"Estimated compact-index tokens: `{corpus.EstimatedCompactSourceMapTokens:N0}`",
            $"Estimated navigation-index tokens: `{corpus.EstimatedNavigationIndexTokens:N0}`",
            $"Map/source token ratio: `{mapToSourceTokens:0.000}`",
            $"Compact/source token ratio: `{compactToSourceTokens:0.000}`",
            $"Navigation/source token ratio: `{navigationToSourceTokens:0.000}`",
            $"Compact index: `{corpus.CompactSourceMapPath}`",
            $"Navigation index: `{corpus.NavigationIndexPath}`",
            "",
            "Token estimates use a simple character/4 proxy. They are not provider billing numbers, but they are good enough to compare full source context, full-fidelity source-map artifacts, compact selector indexes, and navigation-only indexes.",
            "",
            "## Symbol Totals",
            "",
            $"Symbols: `{corpus.SymbolCount:N0}`",
            $"Types: `{corpus.TypeCount:N0}`",
            $"Methods: `{corpus.MethodCount:N0}`",
            $"Fields: `{corpus.FieldCount:N0}`",
            $"Properties: `{corpus.PropertyCount:N0}`",
            $"Events: `{corpus.EventCount:N0}`",
            $"Diagnostics: `{corpus.DiagnosticCount:N0}`",
            ""
        ];

        SourceMapCorpusFileAnalysis[] largestSource = corpus.Files
            .OrderByDescending(file => file.SourceBytes)
            .Take(20)
            .ToArray();
        lines.Add("## Largest Source Files");
        lines.Add("");
        AppendCorpusTable(lines, largestSource);

        SourceMapCorpusFileAnalysis[] largestMaps = corpus.Files
            .OrderByDescending(file => file.SourceMapBytes)
            .Take(20)
            .ToArray();
        lines.Add("## Largest Source Maps");
        lines.Add("");
        AppendCorpusTable(lines, largestMaps);

        SourceMapCorpusFileAnalysis[] highestRatios = corpus.Files
            .Where(file => file.SourceBytes > 0)
            .OrderByDescending(file => file.MapToSourceByteRatio)
            .Take(20)
            .ToArray();
        lines.Add("## Highest Map/Source Ratios");
        lines.Add("");
        AppendCorpusTable(lines, highestRatios);

        SourceMapCorpusFileAnalysis[] diagnostics = corpus.Files
            .Where(file => file.DiagnosticCount > 0 || !file.ParseStatus.Equals("ok", StringComparison.OrdinalIgnoreCase) || file.IsError)
            .OrderByDescending(file => file.DiagnosticCount)
            .ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        lines.Add("## Diagnostics And Errors");
        lines.Add("");
        if (diagnostics.Length == 0)
        {
            lines.Add("No parse diagnostics or source-map errors were reported.");
            lines.Add("");
        }
        else
        {
            AppendCorpusTable(lines, diagnostics);
        }

        lines.Add("## All Files");
        lines.Add("");
        AppendCorpusTable(lines, corpus.Files.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray());

        return string.Join(Environment.NewLine, lines);
    }

    private static void AppendCorpusTable(List<string> lines, IReadOnlyList<SourceMapCorpusFileAnalysis> files)
    {
        lines.Add("| File | Source Bytes | Map Bytes | Ratio | Symbols | Methods | Diagnostics | Parse |");
        lines.Add("| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |");
        foreach (SourceMapCorpusFileAnalysis file in files)
        {
            string relativePath = file.RelativePath.Replace("|", "\\|", StringComparison.Ordinal);
            lines.Add($"| `{relativePath}` | {file.SourceBytes:N0} | {file.SourceMapBytes:N0} | {file.MapToSourceByteRatio:0.000} | {file.SymbolCount:N0} | {file.MethodCount:N0} | {file.DiagnosticCount:N0} | `{file.ParseStatus}` |");
        }

        lines.Add("");
    }

    private static IEnumerable<string> EnumerateCSharpCorpusFiles(string targetPath)
    {
        if (File.Exists(targetPath))
        {
            if (targetPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                yield return targetPath;
            }

            yield break;
        }

        Stack<string> pending = new();
        pending.Push(targetPath);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            foreach (string directory in Directory.EnumerateDirectories(current))
            {
                if (!IsIgnoredCorpusDirectory(directory))
                {
                    pending.Push(directory);
                }
            }

            foreach (string file in Directory.EnumerateFiles(current, "*.cs"))
            {
                yield return file;
            }
        }
    }

    private static bool IsIgnoredCorpusDirectory(string path)
    {
        string name = Path.GetFileName(path);
        return name.Equals(".git", StringComparison.OrdinalIgnoreCase)
            || name.Equals(".vs", StringComparison.OrdinalIgnoreCase)
            || name.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || name.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || name.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
            || name.Equals("packages", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Working", StringComparison.OrdinalIgnoreCase)
            || name.Equals("archive", StringComparison.OrdinalIgnoreCase);
    }

    private static long EstimateTokens(long characterCount)
    {
        return (characterCount + 3) / 4;
    }

    private static int ParseInt(string? value)
    {
        return int.TryParse(value, out int parsed) ? parsed : 0;
    }

    private static int GetKindCount(IReadOnlyDictionary<string, int> counts, string kind)
    {
        return counts.TryGetValue(kind, out int count) ? count : 0;
    }

    private static string FormatJsonStringArray(JsonArray? values)
    {
        if (values is null || values.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(", ", values
            .Select(value => value?.GetValue<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string FormatAttributeArray(JsonArray? values)
    {
        if (values is null || values.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(", ", values
            .Select(value => FindPropertyValue(value, "name"))
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static async Task<SmokeResult> RunQuestionAsync(
        SmokeQuestion question,
        string model,
        IReadOnlyList<LocalMcpServerSurface> localServers,
        MonitorMcpClientService monitorClient,
        OllamaToolExplorerService ollama)
    {
        DateTimeOffset startedAt = DateTimeOffset.Now;
        OllamaActionDecision decision = await ollama.DecideNextActionAsync(localServers, question.Text, model);
        LocalMcpToolCallResult? toolResult = null;
        string? finalAnswer;

        if (decision.Action.Equals("call_tool", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(decision.Tool))
        {
            OllamaRouteValidation validation = ValidateRoute(localServers, decision, null);
            if (validation.IsExecutable)
            {
                LocalMcpDiscoveryService discovery = new(MonitorClientSettings.Load());
                toolResult = await discovery.CallToolAsync(validation.ResolvedServerName!, decision.Tool, decision.Arguments);
                finalAnswer = await ollama.AnswerWithToolResultAsync(question.Text, decision.Tool, toolResult.ResponseJson, model);
            }
            else
            {
                finalAnswer = $"Routing failed before tool call: {validation.Message}";
            }
        }
        else
        {
            finalAnswer = decision.Answer;
        }

        return new SmokeResult(
            question,
            model,
            startedAt,
            DateTimeOffset.Now,
            localServers,
            decision,
            toolResult,
            FormatPayloadForDisplay(toolResult?.ResponseJson),
            NormalizeNewlines(finalAnswer ?? string.Empty));
    }

    private static string BuildMarkdownReport(IReadOnlyList<SmokeResult> results)
    {
        List<string> lines =
        [
            "# Tool Smoke Test Summary",
            "",
            $"Generated: {DateTimeOffset.Now:O}",
            ""
        ];

        foreach (SmokeResult result in results)
        {
            lines.Add($"## {result.Question.Name}");
            lines.Add("");
            lines.Add($"Question: {result.Question.Text}");
            lines.Add("");
            lines.Add($"Decision: `{result.Decision.Action}` `{result.Decision.Server ?? string.Empty}` `{result.Decision.Tool ?? string.Empty}`");
            lines.Add($"Tool error: `{result.ToolResult?.IsError.ToString() ?? "no tool"}`");
            lines.Add("");
            lines.Add("Answer:");
            lines.Add("");
            lines.Add("```text");
            lines.Add(NormalizeNewlines(result.FinalAnswer));
            lines.Add("```");
            lines.Add("");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildOllamaRouteMarkdownReport(IReadOnlyList<OllamaRouteSmokeResult> results)
    {
        List<string> lines =
        [
            "# Ollama Route-Only Smoke Test Summary",
            "",
            $"Generated: {DateTimeOffset.Now:O}",
            "",
            "These tests ask Ollama to choose a tool from the real discovered MCP surface.",
            "The expected server/tool is recorded in the test harness, not in the prompt sent to Ollama.",
            ""
        ];

        foreach (OllamaRouteSmokeResult result in results)
        {
            lines.Add($"## {result.Question.Name}");
            lines.Add("");
            lines.Add($"Prompt: {result.Question.Text}");
            lines.Add($"Expected: `{result.Question.ExpectedServer ?? "(any server)"}` / `{result.Question.ExpectedTool ?? "(any tool)"}`");
            lines.Add($"Decision: `{result.Decision.Action}` `{result.Decision.Server ?? string.Empty}` `{result.Decision.Tool ?? string.Empty}`");
            lines.Add($"Resolved server: `{result.Validation.ResolvedServerName ?? string.Empty}`");
            lines.Add($"Pass: `{result.Validation.IsExpectedRoute}`");
            lines.Add($"Executable: `{result.Validation.IsExecutable}`");
            lines.Add($"Issue: `{result.Validation.Message ?? string.Empty}`");
            lines.Add("");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildOllamaRouterDrillMarkdownReport(IReadOnlyList<OllamaRouterDrillSmokeResult> results)
    {
        List<string> lines =
        [
            "# Ollama Fake-Router Drill Summary",
            "",
            $"Generated: {DateTimeOffset.Now:O}",
            "",
            "These tests do not expose the real MCP manifest. They ask the model to choose one workflow action from a tiny allowed set.",
            ""
        ];

        foreach (OllamaRouterDrillSmokeResult result in results)
        {
            lines.Add($"## {result.Question.Name}");
            lines.Add("");
            lines.Add($"Prompt: {result.Question.Text}");
            lines.Add($"Expected: `{result.Question.ExpectedAction}`");
            lines.Add($"Decision: `{result.Decision.Action}`");
            lines.Add($"Pass: `{result.Passed}`");
            lines.Add($"Reason: `{result.Decision.Reason ?? string.Empty}`");
            lines.Add("");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static OllamaRouteValidation ValidateRoute(
        IReadOnlyList<LocalMcpServerSurface> localServers,
        OllamaActionDecision decision,
        SmokeQuestion? question)
    {
        if (!decision.Action.Equals("call_tool", StringComparison.OrdinalIgnoreCase))
        {
            bool expectedAnswer = string.IsNullOrWhiteSpace(question?.ExpectedTool);
            return new OllamaRouteValidation(expectedAnswer, expectedAnswer, null, expectedAnswer ? null : "Model answered instead of choosing a tool.");
        }

        if (string.IsNullOrWhiteSpace(decision.Tool))
        {
            return new OllamaRouteValidation(false, false, null, "Model requested a tool call without a tool name.");
        }

        LocalMcpServerSurface? resolvedServer = null;
        if (!string.IsNullOrWhiteSpace(decision.Server))
        {
            resolvedServer = localServers.FirstOrDefault(server => server.Name.Equals(decision.Server, StringComparison.OrdinalIgnoreCase));
            if (resolvedServer is null)
            {
                return new OllamaRouteValidation(false, false, null, $"Model named unknown MCP Server '{decision.Server}'.");
            }
        }
        else
        {
            LocalMcpServerSurface[] matchingServers = localServers
                .Where(server => server.Tools.Any(tool => tool.Name.Equals(decision.Tool, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (matchingServers.Length == 1)
            {
                resolvedServer = matchingServers[0];
            }
            else if (matchingServers.Length == 0)
            {
                return new OllamaRouteValidation(false, false, null, $"No discovered MCP Server exposes tool '{decision.Tool}'.");
            }
            else
            {
                return new OllamaRouteValidation(false, false, null, $"Tool '{decision.Tool}' is exposed by multiple MCP Servers; the model must name a server.");
            }
        }

        bool serverHasTool = resolvedServer.Tools.Any(tool => tool.Name.Equals(decision.Tool, StringComparison.OrdinalIgnoreCase));
        if (!serverHasTool)
        {
            return new OllamaRouteValidation(false, false, resolvedServer.Name, $"Server '{resolvedServer.Name}' does not expose tool '{decision.Tool}'.");
        }

        bool expectedToolMatches = string.IsNullOrWhiteSpace(question?.ExpectedTool)
            || decision.Tool.Equals(question.ExpectedTool, StringComparison.OrdinalIgnoreCase);
        bool expectedServerMatches = string.IsNullOrWhiteSpace(question?.ExpectedServer)
            || resolvedServer.Name.Equals(question.ExpectedServer, StringComparison.OrdinalIgnoreCase);
        bool expectedRoute = expectedToolMatches && expectedServerMatches;
        string? message = expectedRoute
            ? null
            : $"Expected {question?.ExpectedServer ?? "(any server)"}/{question?.ExpectedTool ?? "(any tool)"}.";

        return new OllamaRouteValidation(true, expectedRoute, resolvedServer.Name, message);
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static string? ReadOptionalValueAfter(string[] args, string name)
    {
        int index = Array.FindIndex(args, arg => arg.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length)
        {
            return null;
        }

        string value = args[index + 1];
        return value.StartsWith("--", StringComparison.Ordinal) ? null : value;
    }

    private static string ResolveServerName(IReadOnlyList<LocalMcpServerSurface> localServers, OllamaActionDecision decision)
    {
        if (!string.IsNullOrWhiteSpace(decision.Server))
        {
            return decision.Server;
        }

        LocalMcpServerSurface? matchingServer = localServers.FirstOrDefault(server =>
            server.Tools.Any(tool => tool.Name.Equals(decision.Tool, StringComparison.OrdinalIgnoreCase)));
        return matchingServer?.Name
            ?? throw new InvalidOperationException($"Could not resolve MCP Server for tool '{decision.Tool}'.");
    }

    private static string FormatPayloadForDisplay(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return string.Empty;
        }

        try
        {
            JsonNode? node = JsonNode.Parse(payload);
            string? text = FindTextValue(node);
            if (!string.IsNullOrEmpty(text))
            {
                return NormalizeNewlines(text);
            }
        }
        catch (JsonException)
        {
        }

        return NormalizeNewlines(payload);
    }

    private static string? FindTextValue(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (string key in new[] { "Text", "text" })
            {
                if (obj.TryGetPropertyValue(key, out JsonNode? value) && value is not null)
                {
                    return value.ToString();
                }
            }

            foreach (KeyValuePair<string, JsonNode?> property in obj)
            {
                string? nested = FindTextValue(property.Value);
                if (!string.IsNullOrEmpty(nested))
                {
                    return nested;
                }
            }
        }

        if (node is JsonArray array)
        {
            foreach (JsonNode? child in array)
            {
                string? nested = FindTextValue(child);
                if (!string.IsNullOrEmpty(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string NormalizeNewlines(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", Environment.NewLine, StringComparison.Ordinal);
    }

    private static string Indent(string text, string prefix)
    {
        return prefix + text.Replace(Environment.NewLine, Environment.NewLine + prefix, StringComparison.Ordinal);
    }

    private static string SanitizeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(c => invalid.Contains(c) ? '_' : c));
    }

    private static string TruncateForConsole(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..maxLength] + Environment.NewLine + "...(truncated for console)";
    }

    private static string BuildProgramRejectedProposal(MonitorClientSettings settings)
    {
        string watchedRoot = Path.GetDirectoryName(settings.WatchedSolutionPath) ?? string.Empty;
        string programPath = Path.Combine(watchedRoot, "Program.cs");
        string text = File.ReadAllText(programPath);
        const string anchor = "            ApplicationConfiguration.Initialize();";
        const string marker = "            // Monitor smoke candidate: rejected proposal should never reach source.";
        if (text.Contains(marker, StringComparison.Ordinal))
        {
            return text;
        }

        int anchorIndex = text.IndexOf(anchor, StringComparison.Ordinal);
        if (anchorIndex < 0)
        {
            return text + Environment.NewLine + marker + Environment.NewLine;
        }

        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return text.Insert(anchorIndex, marker + newline);
    }

    private static SmokeFixture CreateDbv2ShapeFixture(MonitorClientSettings settings, string runRoot)
    {
        string fixtureRoot = Path.Combine(settings.UiRoot, "Working", "Fixtures", "Dbv2ShapeAcceptSmoke", DateTime.Now.ToString("yyyyMMdd_HHmmssfff"));
        Directory.CreateDirectory(fixtureRoot);
        Directory.CreateDirectory(Path.Combine(fixtureRoot, "AI"));
        Directory.CreateDirectory(Path.Combine(fixtureRoot, "Configuration"));
        Directory.CreateDirectory(Path.Combine(fixtureRoot, "Data"));
        Directory.CreateDirectory(Path.Combine(fixtureRoot, "Features", "SchemaExplorer"));

        string solutionPath = Path.Combine(fixtureRoot, "Schema Studio.sln");
        string projectPath = Path.Combine(fixtureRoot, "SchemaStudio.csproj");
        string targetRelativePath = Path.Combine("Data", "BaseTableRepository.cs");
        string targetSourcePath = Path.Combine(fixtureRoot, targetRelativePath);
        string serverSettingsPath = Path.Combine(fixtureRoot, "MonitorServer.appsettings.json");

        File.WriteAllText(solutionPath, """
            Microsoft Visual Studio Solution File, Format Version 12.00
            # Visual Studio Version 17
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "SchemaStudio", "SchemaStudio.csproj", "{B8A48B5F-1C31-4E22-9B73-A21CE31995B1}"
            EndProject
            Global
            EndGlobal
            """);
        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(fixtureRoot, "AI", "AIAttributes.cs"), """
            namespace SchemaStudio.AIHelpers
            {
                [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Enum | AttributeTargets.Delegate)]
                internal sealed class AIFileContextAttribute : Attribute
                {
                    public AIFileContextAttribute(string fileName, string summary)
                    {
                        FileName = fileName;
                        Summary = summary;
                    }

                    public string FileName { get; }
                    public string Summary { get; }
                }

                [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Enum | AttributeTargets.Delegate)]
                internal sealed class FileVersionAttribute : Attribute
                {
                    public FileVersionAttribute(string version)
                    {
                        Version = version;
                    }

                    public string Version { get; }
                }
            }
            """);
        File.WriteAllText(Path.Combine(fixtureRoot, "Program.cs"), """
            using SchemaStudio.Configuration;

            namespace SchemaStudio
            {
                internal static class Program
                {
                    public static void Main(string[] args)
                    {
                        AppConfig.Initialize();
                    }
                }
            }
            """);
        File.WriteAllText(Path.Combine(fixtureRoot, "Configuration", "AppConfig.cs"), """
            namespace SchemaStudio.Configuration
            {
                internal static class AppConfig
                {
                    public static void Initialize()
                    {
                    }
                }
            }
            """);
        File.WriteAllText(targetSourcePath, """
            using SchemaStudio.AIHelpers;

            namespace SchemaStudio.Data
            {
                [AIFileContext("BaseTableRepository.cs", "Fixture repository used by MonitorBaseClaude accept smoke tests.")]
                [FileVersion("1.0")]
                internal sealed class BaseTableRepository
                {
                    public string NormalizeName(string name)
                    {
                        ArgumentNullException.ThrowIfNull(name);
                        return name.Trim();
                    }
                }
            }
            """);
        File.WriteAllText(Path.Combine(fixtureRoot, "Features", "SchemaExplorer", "SchemaExplorerState.cs"), """
            namespace SchemaStudio.Features.SchemaExplorer
            {
                internal sealed class SchemaExplorerState
                {
                    public string CurrentDatabase { get; init; } = string.Empty;
                }
            }
            """);
        File.WriteAllText(serverSettingsPath, JsonSerializer.Serialize(new
        {
            MonitorClient = new
            {
                settings.MonitorMcpServerRoot,
                settings.LegacyMonitorRoot,
                WatchedProjectsRoot = settings.UiRoot,
                WatchedSolutionPath = solutionPath
            }
        }, JsonOptions));

        File.WriteAllText(Path.Combine(runRoot, "fixture-root.txt"), fixtureRoot);
        return new SmokeFixture(fixtureRoot, solutionPath, serverSettingsPath, targetRelativePath, targetSourcePath);
    }

    private static SmokeFixture CreateRazorShapeFixture(MonitorClientSettings settings, string runRoot)
    {
        string fixtureRoot = Path.Combine(settings.UiRoot, "Working", "Fixtures", "RazorShapeSmoke", DateTime.Now.ToString("yyyyMMdd_HHmmssfff"));
        Directory.CreateDirectory(fixtureRoot);
        Directory.CreateDirectory(Path.Combine(fixtureRoot, "Components", "Pages"));
        Directory.CreateDirectory(Path.Combine(fixtureRoot, "Properties"));

        string solutionPath = Path.Combine(fixtureRoot, "Razor Fixture.sln");
        string projectPath = Path.Combine(fixtureRoot, "RazorFixture.csproj");
        string targetRelativePath = Path.Combine("Components", "Pages", "Counter.razor");
        string targetSourcePath = Path.Combine(fixtureRoot, targetRelativePath);
        string serverSettingsPath = Path.Combine(fixtureRoot, "MonitorServer.appsettings.json");

        File.WriteAllText(solutionPath, """
            Microsoft Visual Studio Solution File, Format Version 12.00
            # Visual Studio Version 17
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "RazorFixture", "RazorFixture.csproj", "{8369C24F-E7B0-4D18-84C8-90AC0E9B36F8}"
            EndProject
            Global
            EndGlobal
            """);
        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk.Razor">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(fixtureRoot, "Program.cs"), """
            namespace RazorFixture
            {
                internal static class Program
                {
                    public static void Main(string[] args)
                    {
                    }
                }
            }
            """);
        File.WriteAllText(Path.Combine(fixtureRoot, "Properties", "launchSettings.json"), """
            {
              "profiles": {
                "RazorFixture": {
                  "commandName": "Project"
                }
              }
            }
            """);
        File.WriteAllText(targetSourcePath, """
            @page "/counter"

            <PageTitle>Counter</PageTitle>

            <h1>Counter</h1>

            <button class="btn btn-primary" @onclick="IncrementCount">Click me</button>

            @if (currentCount > 0)
            {
                <p role="status">Current count: @currentCount</p>
            }

            @code {
                private int currentCount;

                private void IncrementCount()
                {
                    currentCount++;
                }
            }
            """);
        File.WriteAllText(serverSettingsPath, JsonSerializer.Serialize(new
        {
            MonitorClient = new
            {
                settings.MonitorMcpServerRoot,
                settings.LegacyMonitorRoot,
                WatchedProjectsRoot = settings.UiRoot,
                WatchedSolutionPath = solutionPath
            }
        }, JsonOptions));

        File.WriteAllText(Path.Combine(runRoot, "fixture-root.txt"), fixtureRoot);
        return new SmokeFixture(fixtureRoot, solutionPath, serverSettingsPath, targetRelativePath, targetSourcePath);
    }

    private static string ComputeSha256(string path)
    {
        byte[] hash = SHA256.HashData(File.ReadAllBytes(path));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void MaybePause(bool pause)
    {
        if (!pause)
        {
            return;
        }

        Console.WriteLine("Press Enter for next smoke step...");
        Console.ReadLine();
    }

    private static string? ExtractSessionId(string responseJson)
    {
        try
        {
            JsonNode? node = JsonNode.Parse(responseJson);
            return FindPropertyValue(node, "SessionId") ?? FindPropertyValue(node, "sessionId");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractStagedRecordId(string responseJson)
    {
        try
        {
            JsonNode? node = JsonNode.Parse(responseJson);
            return FindPropertyValue(node, "StagedRecordId") ?? FindPropertyValue(node, "stagedRecordId");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool SimulateOperatorSaveFromWinMerge(string responseJson)
    {
        try
        {
            JsonNode? node = JsonNode.Parse(responseJson);
            string? stagedFilePath = FindPropertyValue(node, "StagedFilePath") ?? FindPropertyValue(node, "stagedFilePath");
            string? sourceFilePath = FindPropertyValue(node, "SourceFilePath") ?? FindPropertyValue(node, "sourceFilePath");
            if (string.IsNullOrWhiteSpace(stagedFilePath)
                || string.IsNullOrWhiteSpace(sourceFilePath)
                || !File.Exists(stagedFilePath)
                || !File.Exists(sourceFilePath))
            {
                return false;
            }

            File.Copy(stagedFilePath, sourceFilePath, overwrite: true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string? FindPropertyValue(JsonNode? node, string propertyName)
    {
        if (node is JsonObject obj)
        {
            foreach (KeyValuePair<string, JsonNode?> property in obj)
            {
                if (property.Key.Equals(propertyName, StringComparison.OrdinalIgnoreCase) && property.Value is not null)
                {
                    return property.Value.ToString();
                }

                string? nested = FindPropertyValue(property.Value, propertyName);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        if (node is JsonArray array)
        {
            foreach (JsonNode? child in array)
            {
                string? nested = FindPropertyValue(child, propertyName);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string? FindSourceMapSymbolProperty(string responseJson, string symbolName, string propertyName)
    {
        try
        {
            JsonNode? root = JsonNode.Parse(responseJson);
            JsonArray? files = root?["files"] as JsonArray ?? root?["Files"] as JsonArray;
            if (files is null)
            {
                return null;
            }

            foreach (JsonNode? fileNode in files)
            {
                JsonArray? symbols = fileNode?["symbols"] as JsonArray ?? fileNode?["Symbols"] as JsonArray;
                if (symbols is null)
                {
                    continue;
                }

                foreach (JsonNode? symbolNode in symbols)
                {
                    string? name = FindPropertyValue(symbolNode, "name");
                    if (string.Equals(name, symbolName, StringComparison.Ordinal))
                    {
                        return FindPropertyValue(symbolNode, propertyName);
                    }
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string[] ReadValuesAfter(string[] args, string name, int count)
    {
        int index = Array.FindIndex(args, arg => arg.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + count >= args.Length + 1)
        {
            throw new ArgumentException($"Expected {count} value(s) after {name}.");
        }

        string[] values = args.Skip(index + 1).Take(count).ToArray();
        if (values.Length != count || values.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException($"Expected {count} non-empty value(s) after {name}.");
        }

        return values;
    }
}

internal sealed record ScriptedSmokeStep(
    string Name,
    string ToolName,
    IReadOnlyDictionary<string, object?>? Arguments,
    string Question);

internal sealed record SmokeFixture(
    string Root,
    string SolutionPath,
    string ServerSettingsPath,
    string TargetRelativePath,
    string TargetSourcePath);

internal sealed record ScriptedSmokeResult(
    ScriptedSmokeStep Step,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    MonitorMcpToolCallResult ToolResult,
    string ToolResultDisplay);

internal sealed record SmokeQuestion(
    string Name,
    string Text,
    string? ExpectedServer = null,
    string? ExpectedTool = null);

internal sealed record RouterDrillQuestion(
    string Name,
    string Text,
    string ExpectedAction);

internal sealed record SmokeResult(
    SmokeQuestion Question,
    string Model,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    IReadOnlyList<LocalMcpServerSurface> DiscoveredServers,
    OllamaActionDecision Decision,
    LocalMcpToolCallResult? ToolResult,
    string? ToolResultDisplay,
    string FinalAnswer);

internal sealed record OllamaRouteValidation(
    bool IsExecutable,
    bool IsExpectedRoute,
    string? ResolvedServerName,
    string? Message);

internal sealed record OllamaRouteSmokeResult(
    SmokeQuestion Question,
    string Model,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    IReadOnlyList<LocalMcpServerSurface> DiscoveredServers,
    OllamaActionDecision Decision,
    OllamaRouteValidation Validation);

internal sealed record OllamaRouterDrillSmokeResult(
    RouterDrillQuestion Question,
    string Model,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    OllamaRouterDecision Decision,
    bool Passed);

internal sealed record SourceMapCorpusAnalysis(
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    string WatchedRoot,
    string TargetPath,
    int FileCount,
    int SuccessFileCount,
    int ErrorFileCount,
    long TotalSourceBytes,
    long TotalSourceMapBytes,
    long TotalSourceChars,
    long TotalSourceMapChars,
    string CompactSourceMapPath,
    long CompactSourceMapBytes,
    long CompactSourceMapChars,
    string NavigationIndexPath,
    long NavigationIndexBytes,
    long NavigationIndexChars,
    long EstimatedSourceTokens,
    long EstimatedSourceMapTokens,
    long EstimatedCompactSourceMapTokens,
    long EstimatedNavigationIndexTokens,
    int SymbolCount,
    int MethodCount,
    int TypeCount,
    int FieldCount,
    int PropertyCount,
    int EventCount,
    int DiagnosticCount,
    IReadOnlyList<SourceMapCorpusFileAnalysis> Files);

internal sealed record SourceMapCorpusFileAnalysis(
    string RelativePath,
    string SourcePath,
    string SourceMapPath,
    long SourceBytes,
    long SourceMapBytes,
    long SourceChars,
    long SourceMapChars,
    long EstimatedSourceTokens,
    long EstimatedSourceMapTokens,
    double MapToSourceByteRatio,
    bool IsError,
    string ParseStatus,
    int DiagnosticCount,
    int SymbolCount,
    int TypeCount,
    int MethodCount,
    int FieldCount,
    int PropertyCount,
    int EventCount,
    IReadOnlyDictionary<string, int> KindCounts,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    string? ErrorMessage);
