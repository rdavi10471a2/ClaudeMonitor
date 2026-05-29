using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace McpHubBridge;

internal static class Program
{
    private const string PipeName = "MonitorBaseClaude.McpProxyHub";

    private static async Task<int> Main(string[] args)
    {
        try
        {
            BridgeOptions options = BridgeOptions.Parse(args);
            if (options.ShowHelp || string.IsNullOrWhiteSpace(options.Server))
            {
                PrintUsage();
                return options.ShowHelp ? 0 : 2;
            }

            if (!IsKnownServer(options.Server))
            {
                Console.Error.WriteLine($"Unknown MCP hub server '{options.Server}'. Expected 'monitor' or 'roslyn'.");
                PrintUsage();
                return 3;
            }

            using NamedPipeClientStream pipe = new(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(10000);
            }
            catch (TimeoutException)
            {
                Console.Error.WriteLine("MonitorBaseClaude WinForms hub did not accept the MCP bridge connection within 10 seconds.");
                Console.Error.WriteLine("Start or restart MonitorBaseClaude.exe, then fully restart the VS Code window that owns Claude Code MCP bindings. VS Code reload may not respawn MCP launchers.");
                return 10;
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"Unable to connect to MonitorBaseClaude WinForms hub pipe '{PipeName}': {ex.Message}");
                Console.Error.WriteLine("Start MonitorBaseClaude.exe and fully restart the VS Code window if Claude Code still shows stale MCP bindings.");
                return 11;
            }

            using StreamReader pipeReader = new(pipe, Encoding.UTF8, leaveOpen: true);
            await using StreamWriter pipeWriter = new(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            using StreamReader stdin = new(Console.OpenStandardInput(), Encoding.UTF8);
            await using StreamWriter stdout = new(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };

            await pipeWriter.WriteLineAsync(JsonSerializer.Serialize(new
            {
                server = options.Server,
                solutionPath = options.SolutionPath,
                serverCommand = options.ServerCommand
            }));

            Task inputPump = PumpAsync(stdin, pipeWriter);
            Task outputPump = PumpAsync(pipeReader, stdout);
            Task completed = await Task.WhenAny(inputPump, outputPump);
            if (completed.IsFaulted)
            {
                Exception ex = completed.Exception?.GetBaseException() ?? new InvalidOperationException("Unknown bridge pump failure.");
                Console.Error.WriteLine($"MCP hub bridge disconnected with an error: {ex.Message}");
                return 20;
            }

            if (ReferenceEquals(completed, outputPump))
            {
                Console.Error.WriteLine("MCP hub bridge disconnected because the WinForms hub closed the server stream.");
                Console.Error.WriteLine("If the Host was restarted, fully restart the VS Code window so Claude Code respawns MCP launchers.");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"MCP hub bridge failed before startup completed: {ex.Message}");
            return 1;
        }
    }

    private static bool IsKnownServer(string server)
    {
        return server.Equals("monitor", StringComparison.OrdinalIgnoreCase)
            || server.Equals("roslyn", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task PumpAsync(TextReader reader, TextWriter writer)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            await writer.WriteLineAsync(line);
            await writer.FlushAsync();
        }
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("McpHubBridge --server monitor");
        Console.Error.WriteLine("McpHubBridge --server roslyn [--solution <solution-or-folder>] [--server-command <roslyn-codelens-mcp.exe>]");
        Console.Error.WriteLine("When --solution is omitted, the WinForms hub uses MonitorClient:WatchedSolutionPath.");
    }
}

internal sealed record BridgeOptions(
    string? Server,
    string? SolutionPath,
    string? ServerCommand,
    bool ShowHelp)
{
    public static BridgeOptions Parse(string[] args)
    {
        string? server = null;
        string? solution = null;
        string? command = null;
        bool showHelp = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("-h", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("/?", StringComparison.OrdinalIgnoreCase))
            {
                showHelp = true;
                continue;
            }

            if (arg.Equals("--server", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                server = args[++i];
                continue;
            }

            if (arg.Equals("--solution", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                solution = args[++i];
                continue;
            }

            if (arg.Equals("--server-command", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                command = args[++i];
            }
        }

        return new BridgeOptions(server, solution, command, showHelp);
    }
}
