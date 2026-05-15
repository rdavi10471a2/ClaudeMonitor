# MCP Client Testing

This workspace has a project-scoped Claude Code MCP configuration in `.mcp.json`.

## Server

```text
C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.McpServer\bin\Debug\net10.0\MonitorBaseClaude.McpServer.exe
```

Build it before opening Claude Code:

```powershell
dotnet build C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.McpServer\MonitorBaseClaude.McpServer.csproj
```

## Claude Code / VS Code Test

1. Open `C:\VSCodeProjects\MonitorBaseClaude` in VS Code.
2. Install/open Claude Code.
3. In the Claude Code panel, run `/mcp`.
4. Approve the project-scoped server if prompted.
5. Confirm `monitor-base-claude` is connected.
6. Ask Claude:

```text
Use the monitor-base-claude MCP server. Call get_monitor_status, then call get_tool_manifest, and summarize what monitor project is being ported.
```

## Expected Tools

- `get_monitor_status`
- `get_tool_manifest`
- `list_watched_projects` temporary discovery helper

The real migration rule is that the legacy monitor command/help surface becomes typed MCP tools. The manifest is the bridge document while the tools are ported one by one.
