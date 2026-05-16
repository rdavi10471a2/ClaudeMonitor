$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$exePath = Join-Path $repoRoot 'MonitorBaseClaude.McpServer\bin\Debug\net10.0\MonitorBaseClaude.McpServer.exe'
$settingsPath = Join-Path $repoRoot 'appsettings.json'

if (-not (Test-Path -LiteralPath $settingsPath)) {
    $settingsPath = Join-Path $repoRoot 'appsettings.template.json'
}

if (-not (Test-Path -LiteralPath $exePath)) {
    throw "MonitorBaseClaude.McpServer.exe not found. Build the project first: dotnet build $($exePath -replace '\\bin\\.*$','\\MonitorBaseClaude.McpServer.csproj')"
}

& $exePath --settings $settingsPath
exit $LASTEXITCODE
