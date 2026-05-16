# Bug: MCP servers fail to attach when launched by Claude Desktop on Windows (MSIX/Microsoft Store build)

**Project**: `MonitorBaseClaude` (`C:\VSCodeProjects\MonitorBaseClaude`)

**Affected files**:
- `.mcp.json`
- `Tools/Start-MonitorBaseClaudeMcp.ps1`
- `Tools/Start-RoslynCodeLensMcp.ps1`
- README (Claude Desktop section, if any)

## Symptom

With the shipped PowerShell launcher scripts wired into Claude Desktop's `claude_desktop_config.json`, both MCP servers show "Server disconnected" / "Could not attach to MCP server" banners. Tool list never appears in Desktop. The same scripts work fine when invoked by the Claude Code VS Code extension.

## Evidence from Desktop's MCP logs

Log path: `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\logs\`

### 1. Initial failure — PATH not inherited despite log claiming it is

```
dotnet : The term 'dotnet' is not recognized as the name of a cmdlet...
At C:\VSCodeProjects\MonitorBaseClaude\Tools\Start-MonitorBaseClaudeMcp.ps1:11 char:1
+ dotnet run --project $projectPath -- --settings $settingsPath
```

```
roslyn-codelens-mcp : The term 'roslyn-codelens-mcp' is not recognized...
```

Even though the log's pre-spawn `paths:` array includes both `C:\Program Files\dotnet\` and `C:\Users\<user>\.dotnet\tools`, the MSIX-sandboxed PowerShell child does not actually resolve them.

### 2. After patching scripts to set `$env:PATH = "..."` early

Same error. PowerShell 5.1's command resolution does not re-scan PATH for the running session under this sandbox.

### 3. After patching scripts to call `& "C:\Program Files\dotnet\dotnet.exe" run ...` directly

`dotnet run` succeeds enough to start, but Desktop's MCP transport closes ~600ms after handshake with no `initialize` response. Suspect: MSBuild banner output ("Determining projects to restore...") goes to stdout, contaminating the JSON-RPC stream, plus startup time exceeds Desktop's init timeout.

### 4. After switching to a pre-built `.exe` invoked via `& $exePath` from PowerShell

`initialize` from Desktop never reaches the .exe's stdin. The .exe stays alive as an orphan process; Desktop disconnects; PowerShell middleman in MSIX is not reliably plumbing stdin through to the child.

## Root cause (confirmed)

Claude Desktop's MSIX-packaged Windows build cannot reliably bridge stdio through a PowerShell-wrapper child to a second-level child `.exe`. Each PowerShell-launcher layer breaks the stdin pipe to the actual server.

## Fix that works

Have Claude Desktop launch the server `.exe`s directly with no PowerShell wrapper, and require the Monitor server `.exe` to be pre-built (not `dotnet run`):

```json
{
  "mcpServers": {
    "monitor-base-claude": {
      "command": "C:\\VSCodeProjects\\MonitorBaseClaude\\MonitorBaseClaude.McpServer\\bin\\Debug\\net10.0\\MonitorBaseClaude.McpServer.exe",
      "args": ["--settings", "C:\\VSCodeProjects\\MonitorBaseClaude\\appsettings.json"]
    },
    "roslyn-codelens": {
      "command": "C:\\Users\\<user>\\.dotnet\\tools\\roslyn-codelens-mcp.exe",
      "args": ["C:\\Schema Studio - DBV2\\Schema Studio.sln"]
    }
  }
}
```

With that config, `get_monitor_status` returned real data on the first try.

## Recommended project changes

1. **Do not ship PowerShell wrappers as the canonical MCP entry point.** Keep them for dev convenience but mark them VS Code / Claude Code only.
2. **Update `.mcp.json`** to invoke the `.exe`s directly. Add a `Tools\Build-McpServers.ps1` (or document `dotnet build`) and call it out as a prerequisite.
3. **Avoid `dotnet run` for any MCP host.** Build once, run the `.exe`. `dotnet run` pollutes stdout with MSBuild output and rebuilds on every launch, which breaks JSON-RPC and locks the `.exe` against concurrent clients.
4. **Add a README section "Claude Desktop on Windows (MSIX) setup"** with:
   - The direct-.exe config snippet (with placeholders for username/solution path)
   - The pre-build requirement
   - A note that PowerShell wrappers do not work for Desktop (with a one-line summary of the stdio-bridge issue)
5. **Optional hardening**: have `MonitorBaseClaude.McpServer.exe` write a single-line startup banner to **stderr** (not stdout) — e.g. `"Monitor MCP server v<X> listening on stdio"`. Stderr is captured to Desktop's log but does not pollute JSON-RPC, making future debugging visible without breaking the protocol.
6. **Consider a `dotnet publish -c Release` self-contained output path** and point the config at that — eliminates Debug-folder pathing and JIT cold-start time.

## Environment

- Windows 11
- Claude Desktop MSIX build `Claude_1.7196.0.0_x64__pzs8sxrjxfjjc`
- .NET 10.0.201
- PowerShell 5.1 (`C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe` — the only PowerShell the MSIX sandbox spawns)
