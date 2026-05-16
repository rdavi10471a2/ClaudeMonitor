$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$projectPath = Join-Path $repoRoot 'MonitorBaseClaude.McpServer\MonitorBaseClaude.McpServer.csproj'
$settingsPath = Join-Path $repoRoot 'appsettings.json'

if (-not (Test-Path -LiteralPath $settingsPath)) {
    $settingsPath = Join-Path $repoRoot 'appsettings.template.json'
}

dotnet run --project $projectPath -- --settings $settingsPath
exit $LASTEXITCODE
