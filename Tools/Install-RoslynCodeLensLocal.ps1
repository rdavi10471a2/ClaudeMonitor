$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$toolPath = Join-Path $repoRoot 'Tools\RoslynCodeLens'
$packageId = 'RoslynCodeLens.Mcp'

New-Item -ItemType Directory -Force -Path $toolPath | Out-Null

$existingExe = Join-Path $toolPath 'roslyn-codelens-mcp.exe'
Write-Host "Installing roslyn-codelens-mcp locally for MonitorBaseClaude."
Write-Host "This is intentional even if roslyn-codelens-mcp is already installed globally; the dashboard hub prefers this repo-local copy."

if (Test-Path -LiteralPath $existingExe) {
    dotnet tool update $packageId --tool-path $toolPath
} else {
    dotnet tool install $packageId --tool-path $toolPath
}

if (-not (Test-Path -LiteralPath $existingExe)) {
    $resolvedCommand = Get-Command 'roslyn-codelens-mcp.exe' -ErrorAction SilentlyContinue
    if ($null -eq $resolvedCommand) {
        $resolvedCommand = Get-Command 'roslyn-codelens-mcp' -ErrorAction SilentlyContinue
    }

    if ($null -ne $resolvedCommand -and (Test-Path -LiteralPath $resolvedCommand.Source)) {
        Copy-Item -LiteralPath $resolvedCommand.Source -Destination $existingExe -Force
    }
}

if (-not (Test-Path -LiteralPath $existingExe)) {
    throw "Local roslyn-codelens-mcp install did not produce $existingExe"
}

Write-Host "Installed local roslyn-codelens-mcp: $existingExe"
