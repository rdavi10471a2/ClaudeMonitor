$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$exePath = Join-Path $repoRoot 'Tools\McpHubBridge\bin\Debug\net10.0\McpHubBridge.exe'

if (-not (Test-Path -LiteralPath $exePath)) {
    throw "McpHubBridge.exe not found. Build the solution first: dotnet build `"$repoRoot\MonitorBaseClaude.slnx`""
}

& $exePath --server monitor
exit $LASTEXITCODE
