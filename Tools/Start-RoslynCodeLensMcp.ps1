$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$settingsPath = Join-Path $repoRoot 'appsettings.json'

if (-not (Test-Path -LiteralPath $settingsPath)) {
    $settingsPath = Join-Path $repoRoot 'appsettings.template.json'
}

$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
$solutionPath = $settings.MonitorClient.CodeLensSolutionPath

if ([string]::IsNullOrWhiteSpace($solutionPath) -and $settings.WorkflowSettings.ObservedRoot) {
    $observedRoot = $settings.WorkflowSettings.ObservedRoot
    if (-not [System.IO.Path]::IsPathRooted($observedRoot)) {
        $observedRoot = Join-Path (Split-Path -Parent $settingsPath) $observedRoot
    }

    $solutionPath = Get-ChildItem -LiteralPath $observedRoot -Filter *.sln -File |
        Sort-Object FullName |
        Select-Object -First 1 -ExpandProperty FullName
}

if (-not [System.IO.Path]::IsPathRooted($solutionPath)) {
    $solutionPath = Join-Path (Split-Path -Parent $settingsPath) $solutionPath
}

if (-not (Test-Path -LiteralPath $solutionPath)) {
    throw "CodeLens solution path not found: $solutionPath"
}

$hubBridgeCommand = Join-Path $repoRoot 'Tools\McpHubBridge\bin\Debug\net10.0\McpHubBridge.exe'
if (-not (Test-Path -LiteralPath $hubBridgeCommand)) {
    throw "McpHubBridge.exe not found. Build the solution first: dotnet build `"$repoRoot\MonitorBaseClaude.slnx`""
}

& $hubBridgeCommand --server roslyn --solution $solutionPath
exit $LASTEXITCODE
