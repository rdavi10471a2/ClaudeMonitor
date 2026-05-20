param(
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
    $OutputRoot = Join-Path $repoRoot 'Working\History\ClaudeCode'
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$raw = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($raw)) {
    'Claude status unavailable'
    exit 0
}

$latestPath = Join-Path $OutputRoot 'statusline-latest.json'
$historyPath = Join-Path $OutputRoot 'statusline-history.jsonl'
Set-Content -LiteralPath $latestPath -Value $raw -Encoding UTF8
$compact = ($raw | ConvertFrom-Json | ConvertTo-Json -Compress -Depth 64)
Add-Content -LiteralPath $historyPath -Value $compact -Encoding UTF8

$data = $raw | ConvertFrom-Json
$model = if ($data.model.display_name) { $data.model.display_name } elseif ($data.model.id) { $data.model.id } else { 'Claude' }
$ctx = if ($null -ne $data.context_window.used_percentage) { '{0:0.#}%' -f [double]$data.context_window.used_percentage } else { 'ctx ?' }
$remaining = if ($null -ne $data.context_window.remaining_percentage) { '{0:0.#}%' -f [double]$data.context_window.remaining_percentage } else { '?%' }
$cost = if ($null -ne $data.cost.total_cost_usd) { '${0:N4}' -f [double]$data.cost.total_cost_usd } else { '$?' }
$fiveHour = if ($data.rate_limits.five_hour -and $null -ne $data.rate_limits.five_hour.used_percentage) { '5h {0:0.#}%' -f [double]$data.rate_limits.five_hour.used_percentage } else { '5h ?' }

"$model | context $ctx used / $remaining left | $cost | $fiveHour"
