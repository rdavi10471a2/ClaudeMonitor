using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace McpHubBridge;

internal static class Program
{
    private const string PipeName = "MonitorBaseClaude.McpProxyHub";

    private static async Task<int> Main(string[] args)
    {
        BridgeOptions options = BridgeOptions.Parse(args);
        if (options.ShowHelp || string.IsNullOrWhiteSpace(options.Server))
        {
            PrintUsage();
            return options.ShowHelp ? 0 : 2;
        }

        using NamedPipeClientStream pipe = new(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(10000);
        }
        catch (TimeoutException)
        {
            Console.Error.WriteLine("MonitorBaseClaude WinForms hub is not running. Start MonitorBaseClaude.exe before launching MCP clients.");
            return 10;
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
        await Task.WhenAny(inputPump, outputPump);
        return 0;
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
        Console.Error.WriteLine("McpHubBridge --server roslyn --solution <solution-or-folder> [--server-command <roslyn-codelens-mcp.exe>]");
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
