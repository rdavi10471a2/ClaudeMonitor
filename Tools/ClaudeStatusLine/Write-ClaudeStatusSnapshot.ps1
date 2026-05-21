param(
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
    $settingsPath = Join-Path $repoRoot 'appsettings.json'
    if (-not (Test-Path -LiteralPath $settingsPath)) {
        $settingsPath = Join-Path $repoRoot 'appsettings.template.json'
    }

    $uiRoot = $repoRoot.Path
    if (Test-Path -LiteralPath $settingsPath) {
        $settingsDirectory = Split-Path -Parent $settingsPath
        $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
        if ($settings.MonitorClient -and -not [string]::IsNullOrWhiteSpace($settings.MonitorClient.UiRoot)) {
            $configuredUiRoot = [string]$settings.MonitorClient.UiRoot
            $uiRoot = if ([System.IO.Path]::IsPathRooted($configuredUiRoot)) {
                $configuredUiRoot
            }
            else {
                Join-Path $settingsDirectory $configuredUiRoot
            }
        }
    }

    $OutputRoot = Join-Path $uiRoot 'Working\History\ClaudeCode'
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$raw = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($raw)) {
    'Claude status unavailable'
    exit 0
}

$latestPath = Join-Path $OutputRoot 'statusline-latest.json'
$historyPath = Join-Path $OutputRoot 'statusline-history.jsonl'
$tempLatestPath = Join-Path $OutputRoot ("statusline-latest.{0:N}.tmp" -f [guid]::NewGuid())
Set-Content -LiteralPath $tempLatestPath -Value $raw -Encoding UTF8
Move-Item -LiteralPath $tempLatestPath -Destination $latestPath -Force
$compact = ($raw | ConvertFrom-Json | ConvertTo-Json -Compress -Depth 64)
Add-Content -LiteralPath $historyPath -Value $compact -Encoding UTF8

$data = $raw | ConvertFrom-Json
$model = if ($data.model.display_name) { $data.model.display_name } elseif ($data.model.id) { $data.model.id } else { 'Claude' }
$ctx = if ($null -ne $data.context_window.used_percentage) { '{0:0.#}%' -f [double]$data.context_window.used_percentage } else { 'ctx ?' }
$remaining = if ($null -ne $data.context_window.remaining_percentage) { '{0:0.#}%' -f [double]$data.context_window.remaining_percentage } else { '?%' }
$cost = if ($null -ne $data.cost.total_cost_usd) { '${0:N4}' -f [double]$data.cost.total_cost_usd } else { '$?' }
$fiveHour = if ($data.rate_limits.five_hour -and $null -ne $data.rate_limits.five_hour.used_percentage) { '5h {0:0.#}%' -f [double]$data.rate_limits.five_hour.used_percentage } else { '5h ?' }

"$model | context $ctx used / $remaining left | $cost | $fiveHour"
