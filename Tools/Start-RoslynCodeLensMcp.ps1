$ErrorActionPreference = 'Stop'

$defaultRoslynCodelensExe = Join-Path $env:USERPROFILE '.dotnet\tools\roslyn-codelens-mcp.exe'
$roslynCodelensCommand = $null
if (Test-Path -LiteralPath $defaultRoslynCodelensExe) {
    $roslynCodelensCommand = $defaultRoslynCodelensExe
} else {
    $resolvedCommand = Get-Command 'roslyn-codelens-mcp.exe' -ErrorAction SilentlyContinue
    if ($null -eq $resolvedCommand) {
        $resolvedCommand = Get-Command 'roslyn-codelens-mcp' -ErrorAction SilentlyContinue
    }

    if ($null -ne $resolvedCommand) {
        $roslynCodelensCommand = $resolvedCommand.Source
    }
}

if ([string]::IsNullOrWhiteSpace($roslynCodelensCommand)) {
    throw "roslyn-codelens-mcp was not found at $defaultRoslynCodelensExe or on PATH. Install the global tool or update the launcher."
}

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

$telemetryProxyCandidates = @(
    (Join-Path $repoRoot 'Tools\CodeLensTelemetryProxy\bin\Debug\net10.0\CodeLensTelemetryProxy.exe'),
    (Join-Path (Split-Path -Parent $repoRoot) 'ClaudeMonitor\Tools\CodeLensTelemetryProxy\bin\Debug\net10.0\CodeLensTelemetryProxy.exe')
)

$telemetryProxyCommand = $telemetryProxyCandidates |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1

if ($telemetryProxyCommand) {
    $logRoot = Join-Path $repoRoot 'Working\History\McpTelemetry\RoslynCodeLens'
    & $telemetryProxyCommand $solutionPath --server-command $roslynCodelensCommand --log-root $logRoot
    exit $LASTEXITCODE
}

& $roslynCodelensCommand $solutionPath
exit $LASTEXITCODE
