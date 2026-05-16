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

& $roslynCodelensCommand $solutionPath
exit $LASTEXITCODE
