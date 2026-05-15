using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using MonitorBaseClaude.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MonitorBaseClaude;

[AIFileContext("RoslynCodeLensMcpClientService.cs", "Owns roslyn-codelens-mcp process startup, stdio MCP client calls, and solution summary formatting.")]
[FileVersion("1.6")]
public sealed class RoslynCodeLensMcpClientService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private CancellationTokenSource? activeLoadCancellation;
    private readonly SemaphoreSlim clientGate = new(1, 1);
    private McpClient? activeClient;
    private string? activeSolutionPath;

    public static string? ResolveSolutionPath(string path)
    {
        if (File.Exists(path))
        {
            return path;
        }

        if (!Directory.Exists(path))
        {
            return null;
        }

        string[] solutionFiles = Directory.GetFiles(path, "*.sln", SearchOption.TopDirectoryOnly);
        if (solutionFiles.Length == 1)
        {
            return solutionFiles[0];
        }

        return solutionFiles
            .OrderBy(solutionFile => solutionFile, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static (string Name, string Command) ResolveMcpCommand()
    {
        string? proxyPath = FindCodeLensProxyPath();
        if (!string.IsNullOrWhiteSpace(proxyPath))
        {
            return ("codelens-telemetry-proxy", proxyPath);
        }

        return ("roslyn-codelens-mcp", "roslyn-codelens-mcp");
    }

    private static string? FindCodeLensProxyPath()
    {
        const string relativeProxyPath = @"Tools\CodeLensTelemetryProxy\bin\Debug\net10.0\CodeLensTelemetryProxy.exe";

        List<string> candidates = [];
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && current is not null; i++)
        {
            candidates.Add(Path.Combine(current.FullName, relativeProxyPath));
            candidates.Add(Path.Combine(current.FullName, "ClaudeMonitor", relativeProxyPath));
            current = current.Parent;
        }

        string currentDirectory = Directory.GetCurrentDirectory();
        candidates.Add(Path.Combine(currentDirectory, relativeProxyPath));
        DirectoryInfo? cwd = new(currentDirectory);
        if (cwd.Parent is not null)
        {
            candidates.Add(Path.Combine(cwd.Parent.FullName, "ClaudeMonitor", relativeProxyPath));
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    public async Task<string> LoadSolutionSummaryAsync(
        string solutionPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        LoadedSolutionSession session = await LoadSolutionSessionAsync(solutionPath, progress, cancellationToken);
        return session.FullSummary;
    }

    public async Task<LoadedSolutionSession> LoadSolutionSessionAsync(
        string solutionPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await clientGate.WaitAsync(cancellationToken);
        try
        {
            await RestartClientAsync(solutionPath, progress, cancellationToken);
            McpClient client = activeClient ?? throw new InvalidOperationException("MCP client did not start.");

            progress?.Report("Connected. Listing MCP tools...");
            IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
            progress?.Report("Fetching loaded solution state...");
            CallToolResult listSolutions = await client.CallToolAsync(
                "list_solutions",
                cancellationToken: cancellationToken);

            progress?.Report("Fetching NuGet dependencies...");
            CallToolResult nugetDependencies = await client.CallToolAsync(
                "get_nuget_dependencies",
                cancellationToken: cancellationToken);

            JsonNode? nugetJson = ToJsonNode(nugetDependencies);
            IReadOnlyList<string> projectNames = GetProjectNames(solutionPath, nugetJson);
            Dictionary<string, CallToolResult> projectDependencies = [];
            for (int i = 0; i < projectNames.Count; i++)
            {
                string projectName = projectNames[i];
                progress?.Report($"Fetching project dependencies ({i + 1}/{projectNames.Count}): {projectName}");
                projectDependencies[projectName] = await client.CallToolAsync(
                    "get_project_dependencies",
                    new Dictionary<string, object?> { ["project"] = projectName },
                    cancellationToken: cancellationToken);
            }

            progress?.Report("Formatting solution summary...");
            return BuildSession(solutionPath, tools, listSolutions, nugetDependencies, projectDependencies);
        }
        finally
        {
            clientGate.Release();
        }
    }

    public async Task<McpToolInvocation> InvokeToolAsync(
        string solutionPath,
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await clientGate.WaitAsync(cancellationToken);
        try
        {
            if (activeClient is null || !string.Equals(activeSolutionPath, solutionPath, StringComparison.OrdinalIgnoreCase))
            {
                await RestartClientAsync(solutionPath, progress, cancellationToken);
            }

            McpClient client = activeClient ?? throw new InvalidOperationException("MCP client did not start.");

            if (toolName.Equals("tools/list", StringComparison.Ordinal))
            {
                progress?.Report("Listing MCP tools...");
                IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
                string summary = $"{tools.Count} tool(s): {string.Join(", ", tools.Select(tool => tool.Name).OrderBy(name => name).Take(6))}";
                if (tools.Count > 6)
                {
                    summary += ", ...";
                }

                return new McpToolInvocation(
                    toolName,
                    false,
                    summary,
                    FormatRequestJson(toolName, arguments),
                    JsonSerializer.Serialize(tools.Select(tool => tool.Name).OrderBy(name => name), JsonOptions));
            }

            progress?.Report($"Calling {toolName}...");
            CallToolResult result = await client.CallToolAsync(
                toolName,
                arguments?.ToDictionary(pair => pair.Key, pair => pair.Value),
                cancellationToken: cancellationToken);
            JsonNode? resultJson = ToJsonNode(result);
            return new McpToolInvocation(
                toolName,
                result.IsError == true,
                SummarizeToolResult(resultJson),
                FormatRequestJson(toolName, arguments),
                resultJson?.ToJsonString(JsonOptions) ?? JsonSerializer.Serialize(result, JsonOptions));
        }
        finally
        {
            clientGate.Release();
        }
    }

    public void Dispose()
    {
        activeLoadCancellation?.Cancel();
        activeLoadCancellation?.Dispose();
        clientGate.Wait();
        try
        {
            activeClient?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            activeClient = null;
            activeSolutionPath = null;
        }
        finally
        {
            clientGate.Release();
            clientGate.Dispose();
        }
    }

    private async Task CancelActiveLoadAsync()
    {
        if (activeLoadCancellation is null)
        {
            return;
        }

        await activeLoadCancellation.CancelAsync();
        activeLoadCancellation.Dispose();
        activeLoadCancellation = null;
    }

    private async Task RestartClientAsync(
        string solutionPath,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        await CancelActiveLoadAsync();

        if (activeClient is not null)
        {
            await activeClient.DisposeAsync();
            activeClient = null;
            activeSolutionPath = null;
        }

        activeLoadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken loadCancellation = activeLoadCancellation.Token;

        string workingDirectory = Path.GetDirectoryName(solutionPath) ?? Environment.CurrentDirectory;
        (string transportName, string command) = ResolveMcpCommand();
        StdioClientTransportOptions transportOptions = new()
        {
            Name = transportName,
            Command = command,
            Arguments = [solutionPath],
            WorkingDirectory = workingDirectory
        };

        StdioClientTransport transport = new(transportOptions);
        progress?.Report($"Starting {transportName}...");
        activeClient = await McpClient.CreateAsync(transport, cancellationToken: loadCancellation);
        activeSolutionPath = solutionPath;
    }

    private static LoadedSolutionSession BuildSession(
        string solutionPath,
        IList<McpClientTool> tools,
        CallToolResult listSolutions,
        CallToolResult nugetDependencies,
        IReadOnlyDictionary<string, CallToolResult> projectDependencies)
    {
        JsonNode? solutionJson = ToJsonNode(listSolutions);
        JsonNode? nugetJson = ToJsonNode(nugetDependencies);
        IReadOnlyList<McpProjectSummary> projects = BuildProjectFacts(solutionPath, nugetJson, projectDependencies);
        string solutionSummary = FormatSolutionSummary(solutionPath, solutionJson, projects);
        string fullSummary = FormatFullSummary(solutionPath, tools, listSolutions, nugetDependencies, projectDependencies, solutionJson, nugetJson, projects);

        return new LoadedSolutionSession(
            solutionPath,
            Path.GetFileNameWithoutExtension(solutionPath),
            GetSolutionStatus(solutionJson),
            projects,
            BuildToolSummaries(tools),
            solutionSummary,
            fullSummary);
    }

    private static IReadOnlyList<McpToolSummary> BuildToolSummaries(IList<McpClientTool> tools)
    {
        return tools
            .OrderBy(tool => tool.Name, StringComparer.Ordinal)
            .Select(tool => new McpToolSummary(
                tool.Name,
                tool.Description ?? string.Empty,
                JsonSerializer.Serialize(tool.JsonSchema, JsonOptions),
                CategorizeTool(tool.Name)))
            .ToArray();
    }

    private static string CategorizeTool(string toolName)
    {
        if (toolName.Contains("solution", StringComparison.OrdinalIgnoreCase))
        {
            return "Solution";
        }

        if (toolName.Contains("project", StringComparison.OrdinalIgnoreCase)
            || toolName.Contains("nuget", StringComparison.OrdinalIgnoreCase)
            || toolName.Contains("depend", StringComparison.OrdinalIgnoreCase))
        {
            return "Project";
        }

        if (toolName.Contains("file", StringComparison.OrdinalIgnoreCase)
            || toolName.Contains("type", StringComparison.OrdinalIgnoreCase)
            || toolName.Contains("symbol", StringComparison.OrdinalIgnoreCase)
            || toolName.Contains("definition", StringComparison.OrdinalIgnoreCase))
        {
            return "File / Symbol";
        }

        if (toolName.Contains("diagnostic", StringComparison.OrdinalIgnoreCase)
            || toolName.Contains("health", StringComparison.OrdinalIgnoreCase)
            || toolName.Contains("complexity", StringComparison.OrdinalIgnoreCase)
            || toolName.Contains("violation", StringComparison.OrdinalIgnoreCase))
        {
            return "Diagnostics";
        }

        if (toolName.Contains("test", StringComparison.OrdinalIgnoreCase)
            || toolName.Contains("coverage", StringComparison.OrdinalIgnoreCase)
            || toolName.Contains("uncovered", StringComparison.OrdinalIgnoreCase))
        {
            return "Tests";
        }

        if (toolName.Contains("reference", StringComparison.OrdinalIgnoreCase)
            || toolName.Contains("caller", StringComparison.OrdinalIgnoreCase)
            || toolName.Contains("implementation", StringComparison.OrdinalIgnoreCase)
            || toolName.Contains("hierarchy", StringComparison.OrdinalIgnoreCase))
        {
            return "References";
        }

        if (toolName.Contains("code_action", StringComparison.OrdinalIgnoreCase)
            || toolName.Contains("fix", StringComparison.OrdinalIgnoreCase)
            || toolName.Contains("change", StringComparison.OrdinalIgnoreCase))
        {
            return "Code Actions";
        }

        return "Other";
    }

    private static string FormatSolutionSummary(
        string solutionPath,
        JsonNode? solutionJson,
        IReadOnlyList<McpProjectSummary> projects)
    {
        StringBuilder builder = new();
        builder.AppendLine("Solution Summary");
        builder.AppendLine("================");
        builder.AppendLine($"Name: {Path.GetFileNameWithoutExtension(solutionPath)}");
        builder.AppendLine($"Path: {solutionPath}");
        builder.AppendLine($"Projects: {projects.Count}");
        builder.AppendLine($"MCP status: {GetSolutionStatus(solutionJson)}");
        builder.AppendLine();
        builder.AppendLine("Loaded projects");
        builder.AppendLine("---------------");
        foreach (McpProjectSummary project in projects)
        {
            builder.AppendLine($"{project.Name}  |  {project.TargetFramework}  |  {project.SourceFileCount} source files");
        }

        return builder.ToString();
    }

    private static string FormatFullSummary(
        string solutionPath,
        IList<McpClientTool> tools,
        CallToolResult listSolutions,
        CallToolResult nugetDependencies,
        IReadOnlyDictionary<string, CallToolResult> projectDependencies,
        JsonNode? solutionJson,
        JsonNode? nugetJson,
        IReadOnlyList<McpProjectSummary> projects)
    {
        StringBuilder builder = new();
        builder.AppendLine(FormatSolutionSummary(solutionPath, solutionJson, projects));
        builder.AppendLine("Project Details");
        builder.AppendLine("---------------");
        AppendProjectSummary(builder, projects);
        builder.AppendLine();
        builder.AppendLine("Relevant MCP tool schemas");
        builder.AppendLine("-------------------------");
        AppendToolSchema(builder, tools, "list_solutions");
        AppendToolSchema(builder, tools, "get_nuget_dependencies");
        AppendToolSchema(builder, tools, "get_project_dependencies");
        builder.AppendLine();
        builder.AppendLine("Available MCP tools:");
        foreach (McpClientTool tool in tools.OrderBy(tool => tool.Name))
        {
            builder.AppendLine($"- {tool.Name}");
        }

        builder.AppendLine();
        builder.AppendLine("list_solutions");
        builder.AppendLine("--------------");
        AppendJsonSummary(builder, solutionJson);

        builder.AppendLine();
        builder.AppendLine("get_nuget_dependencies");
        builder.AppendLine("----------------------");
        AppendJsonSummary(builder, nugetJson);

        foreach (KeyValuePair<string, CallToolResult> dependencyResult in projectDependencies)
        {
            builder.AppendLine();
            builder.AppendLine($"get_project_dependencies: {dependencyResult.Key}");
            builder.AppendLine(new string('-', 26 + dependencyResult.Key.Length));
            AppendJsonSummary(builder, ToJsonNode(dependencyResult.Value));
        }

        builder.AppendLine();
        builder.AppendLine("Raw MCP responses");
        builder.AppendLine("-----------------");
        builder.AppendLine("list_solutions:");
        builder.AppendLine(JsonSerializer.Serialize(listSolutions, JsonOptions));
        builder.AppendLine();
        builder.AppendLine("get_nuget_dependencies:");
        builder.AppendLine(JsonSerializer.Serialize(nugetDependencies, JsonOptions));
        foreach (KeyValuePair<string, CallToolResult> dependencyResult in projectDependencies)
        {
            builder.AppendLine();
            builder.AppendLine($"get_project_dependencies ({dependencyResult.Key}):");
            builder.AppendLine(JsonSerializer.Serialize(dependencyResult.Value, JsonOptions));
        }

        return builder.ToString();
    }

    private static JsonNode? ToJsonNode(CallToolResult result)
    {
        if (result.StructuredContent is not null)
        {
            return JsonSerializer.SerializeToNode(result.StructuredContent, JsonOptions);
        }

        JsonNode? resultJson = JsonSerializer.SerializeToNode(result, JsonOptions);
        string? textContent = GetFirstTextContent(resultJson);
        if (!string.IsNullOrWhiteSpace(textContent))
        {
            JsonNode? parsedText = TryParseJson(textContent);
            if (parsedText is not null)
            {
                return parsedText;
            }
        }

        return resultJson;
    }

    private static void AppendJsonSummary(StringBuilder builder, JsonNode? node)
    {
        if (node is null)
        {
            builder.AppendLine("(No structured content returned.)");
            return;
        }

        builder.AppendLine(node.ToJsonString(JsonOptions));
    }

    public static string FormatProjectSummary(McpProjectSummary project)
    {
        StringBuilder builder = new();
        builder.AppendLine("Project Summary");
        builder.AppendLine("===============");
        builder.AppendLine($"Name: {project.Name}");
        builder.AppendLine($"Path: {project.ProjectPath}");
        builder.AppendLine($"Target framework: {project.TargetFramework}");
        builder.AppendLine($"Source files: {project.SourceFileCount}");
        builder.AppendLine();
        builder.AppendLine("NuGet packages");
        builder.AppendLine("--------------");
        if (project.Packages.Count == 0)
        {
            builder.AppendLine("(none reported)");
        }
        else
        {
            foreach (string package in project.Packages)
            {
                builder.AppendLine(package);
            }
        }

        builder.AppendLine();
        builder.AppendLine("Project dependencies");
        builder.AppendLine("--------------------");
        if (project.DependencySummary.Count == 0)
        {
            builder.AppendLine("(none reported)");
        }
        else
        {
            foreach (string dependency in project.DependencySummary)
            {
                builder.AppendLine(dependency);
            }
        }

        return builder.ToString();
    }

    private static string GetSolutionStatus(JsonNode? solutionJson)
    {
        JsonObject? activeSolution = null;
        if (solutionJson is JsonArray { Count: > 0 } array)
        {
            activeSolution = array.OfType<JsonObject>().FirstOrDefault(solution =>
                string.Equals(GetString(solution, "isActive"), "true", StringComparison.OrdinalIgnoreCase))
                ?? array[0] as JsonObject;
        }
        else if (solutionJson is JsonObject obj)
        {
            activeSolution = obj;
        }

        string? status = GetString(activeSolution, "status");
        return string.IsNullOrWhiteSpace(status) ? "ready" : status;
    }

    private static void AppendProjectSummary(StringBuilder builder, IReadOnlyList<McpProjectSummary> projects)
    {
        if (projects.Count == 0)
        {
            builder.AppendLine("(No projects found.)");
            return;
        }

        foreach (McpProjectSummary project in projects)
        {
            builder.AppendLine($"- {project.Name}");
            builder.AppendLine($"  Project path: {project.ProjectPath}");
            builder.AppendLine($"  Target framework: {project.TargetFramework}");
            builder.AppendLine($"  Source file count: {project.SourceFileCount}");
            builder.AppendLine("  NuGet package references:");
            if (project.Packages.Count == 0)
            {
                builder.AppendLine("  - (none reported)");
            }
            else
            {
                foreach (string package in project.Packages)
                {
                    builder.AppendLine($"  - {package}");
                }
            }

            builder.AppendLine("  Project dependencies:");
            if (project.DependencySummary.Count == 0)
            {
                builder.AppendLine("  - (none reported)");
            }
            else
            {
                foreach (string dependency in project.DependencySummary)
                {
                    builder.AppendLine($"  - {dependency}");
                }
            }
        }
    }

    private static void AppendToolSchema(StringBuilder builder, IList<McpClientTool> tools, string toolName)
    {
        McpClientTool? tool = tools.FirstOrDefault(candidate => candidate.Name.Equals(toolName, StringComparison.Ordinal));
        if (tool is null)
        {
            builder.AppendLine($"{toolName}: unavailable");
            return;
        }

        builder.AppendLine($"{tool.Name}: {tool.Description}");
        builder.AppendLine(JsonSerializer.Serialize(tool.JsonSchema, JsonOptions));
    }

    private static IReadOnlyList<string> GetProjectNames(string solutionPath, JsonNode? nugetJson)
    {
        SortedSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        JsonArray? packages = FindArrayByName(nugetJson, "packages");
        if (packages is not null)
        {
            foreach (JsonNode? package in packages)
            {
                string? projectName = GetString(package, "project", "projectName", "Project", "ProjectName");
                if (!string.IsNullOrWhiteSpace(projectName))
                {
                    names.Add(projectName);
                }
            }
        }

        foreach (string csprojPath in Directory.GetFiles(Path.GetDirectoryName(solutionPath) ?? Environment.CurrentDirectory, "*.csproj", SearchOption.AllDirectories))
        {
            if (IsInBuildOutput(csprojPath))
            {
                continue;
            }

            names.Add(Path.GetFileNameWithoutExtension(csprojPath));
        }

        return names.ToArray();
    }

    private static IReadOnlyList<McpProjectSummary> BuildProjectFacts(
        string solutionPath,
        JsonNode? nugetJson,
        IReadOnlyDictionary<string, CallToolResult> projectDependencies)
    {
        string solutionDirectory = Path.GetDirectoryName(solutionPath) ?? Environment.CurrentDirectory;
        Dictionary<string, string> projectPaths = Directory
            .GetFiles(solutionDirectory, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsInBuildOutput(path))
            .ToDictionary(
                path => Path.GetFileNameWithoutExtension(path),
                path => path,
                StringComparer.OrdinalIgnoreCase);

        Dictionary<string, List<string>> packagesByProject = BuildPackagesByProject(nugetJson);
        SortedSet<string> projectNames = new(projectPaths.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (string projectName in packagesByProject.Keys)
        {
            projectNames.Add(projectName);
        }

        foreach (string projectName in projectDependencies.Keys)
        {
            projectNames.Add(projectName);
        }

        List<McpProjectSummary> projects = [];
        foreach (string projectName in projectNames)
        {
            projectPaths.TryGetValue(projectName, out string? csprojPath);
            projects.Add(new McpProjectSummary(
                projectName,
                csprojPath ?? "(project path not found)",
                GetTargetFramework(csprojPath),
                GetSourceFileCount(csprojPath),
                packagesByProject.TryGetValue(projectName, out List<string>? packages) ? packages : [],
                projectDependencies.TryGetValue(projectName, out CallToolResult? dependencies)
                    ? SummarizeDependencies(ToJsonNode(dependencies))
                    : []));
        }

        return projects;
    }

    private static Dictionary<string, List<string>> BuildPackagesByProject(JsonNode? nugetJson)
    {
        Dictionary<string, List<string>> packagesByProject = new(StringComparer.OrdinalIgnoreCase);
        JsonArray? packages = FindArrayByName(nugetJson, "packages");
        if (packages is null)
        {
            return packagesByProject;
        }

        foreach (JsonNode? package in packages)
        {
            string? projectName = GetString(package, "project", "projectName", "Project", "ProjectName");
            if (string.IsNullOrWhiteSpace(projectName))
            {
                continue;
            }

            if (!packagesByProject.TryGetValue(projectName, out List<string>? projectPackages))
            {
                projectPackages = [];
                packagesByProject[projectName] = projectPackages;
            }

            projectPackages.Add(FormatPackage(package));
        }

        return packagesByProject;
    }

    private static IReadOnlyList<string> SummarizeDependencies(JsonNode? dependencyJson)
    {
        if (dependencyJson is null)
        {
            return [];
        }

        JsonArray? dependencies = FindArrayByName(dependencyJson, "dependencies")
            ?? FindArrayByName(dependencyJson, "directDependencies")
            ?? FindArrayByName(dependencyJson, "projectReferences");
        if (dependencies is null || dependencies.Count == 0)
        {
            return [];
        }

        return dependencies
            .Select(dependency => dependency is JsonValue ? dependency.ToString() : GetString(dependency, "name", "project", "projectName", "path") ?? dependency?.ToJsonString(JsonOptions))
            .Where(dependency => !string.IsNullOrWhiteSpace(dependency))
            .Cast<string>()
            .ToArray();
    }

    private static string GetTargetFramework(string? csprojPath)
    {
        if (string.IsNullOrWhiteSpace(csprojPath) || !File.Exists(csprojPath))
        {
            return "(target framework not found)";
        }

        XDocument document = XDocument.Load(csprojPath);
        IEnumerable<string> frameworks = document
            .Descendants()
            .Where(element => element.Name.LocalName is "TargetFramework" or "TargetFrameworks")
            .Select(element => element.Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value));

        return string.Join(", ", frameworks.DefaultIfEmpty("(target framework not found)"));
    }

    private static int GetSourceFileCount(string? csprojPath)
    {
        if (string.IsNullOrWhiteSpace(csprojPath) || !File.Exists(csprojPath))
        {
            return 0;
        }

        string projectDirectory = Path.GetDirectoryName(csprojPath) ?? Environment.CurrentDirectory;
        return Directory
            .GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Count(path => !IsInBuildOutput(path));
    }

    private static bool IsInBuildOutput(string path)
    {
        string normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        string separator = Path.DirectorySeparatorChar.ToString();
        return normalized.Contains($"{separator}bin{separator}", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains($"{separator}obj{separator}", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatPackage(JsonNode? package)
    {
        if (package is JsonValue)
        {
            return package.ToString();
        }

        string name = GetString(package, "name", "id", "packageId", "packageName", "Name", "PackageId", "PackageName") ?? "(unnamed package)";
        string? version = GetString(package, "version", "Version");
        return string.IsNullOrWhiteSpace(version) ? name : $"{name} {version}";
    }

    private static JsonArray? FindArrayByName(JsonNode? node, string propertyName)
    {
        if (node is JsonObject obj)
        {
            foreach (KeyValuePair<string, JsonNode?> property in obj)
            {
                if (property.Key.Equals(propertyName, StringComparison.OrdinalIgnoreCase)
                    && property.Value is JsonArray array)
                {
                    return array;
                }

                JsonArray? nested = FindArrayByName(property.Value, propertyName);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? item in array)
            {
                JsonArray? nested = FindArrayByName(item, propertyName);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string? GetString(JsonNode? node, params string[] propertyNames)
    {
        if (node is not JsonObject obj)
        {
            return null;
        }

        foreach (string propertyName in propertyNames)
        {
            KeyValuePair<string, JsonNode?> property = obj.FirstOrDefault(
                candidate => candidate.Key.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(property.Key) && property.Value is not null)
            {
                return property.Value switch
                {
                    JsonValue => property.Value.ToString(),
                    JsonArray array => array.Count.ToString(),
                    _ => property.Value.ToJsonString(JsonOptions)
                };
            }
        }

        return null;
    }

    private static string? GetFirstTextContent(JsonNode? node)
    {
        JsonArray? content = FindArrayByName(node, "content");
        if (content is null)
        {
            return null;
        }

        foreach (JsonNode? item in content)
        {
            string? text = GetString(item, "text");
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static JsonNode? TryParseJson(string text)
    {
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string SummarizeToolResult(JsonNode? node)
    {
        if (node is null)
        {
            return "(no content)";
        }

        if (node is JsonArray array)
        {
            return $"{array.Count} item(s)";
        }

        if (node is JsonObject obj)
        {
            JsonArray? packages = FindArrayByName(obj, "packages");
            if (packages is not null)
            {
                return $"{packages.Count} package reference(s)";
            }

            JsonArray? direct = FindArrayByName(obj, "direct");
            JsonArray? transitive = FindArrayByName(obj, "transitive");
            if (direct is not null || transitive is not null)
            {
                return $"{direct?.Count ?? 0} direct, {transitive?.Count ?? 0} transitive";
            }

            string[] keys = obj.Select(property => property.Key).Take(5).ToArray();
            return keys.Length == 0 ? "{}" : string.Join(", ", keys);
        }

        string text = node.ToString();
        return text.Length <= 140 ? text : $"{text[..140]}...";
    }

    private static string FormatRequestJson(string toolName, IReadOnlyDictionary<string, object?>? arguments)
    {
        object request = toolName.Equals("tools/list", StringComparison.Ordinal)
            ? new { method = "tools/list" }
            : new
            {
                method = "tools/call",
                @params = new
                {
                    name = toolName,
                    arguments = arguments ?? new Dictionary<string, object?>()
                }
            };

        return JsonSerializer.Serialize(request, JsonOptions);
    }

    public sealed record LoadedSolutionSession(
        string SolutionPath,
        string SolutionName,
        string Status,
        IReadOnlyList<McpProjectSummary> Projects,
        IReadOnlyList<McpToolSummary> Tools,
        string SolutionSummary,
        string FullSummary);

    public sealed record McpProjectSummary(
        string Name,
        string ProjectPath,
        string TargetFramework,
        int SourceFileCount,
        IReadOnlyList<string> Packages,
        IReadOnlyList<string> DependencySummary);

    public sealed record McpToolSummary(
        string Name,
        string Description,
        string JsonSchema,
        string Category);

    public sealed record McpToolInvocation(
        string ToolName,
        bool IsError,
        string Summary,
        string RequestJson,
        string RawJson);
}
