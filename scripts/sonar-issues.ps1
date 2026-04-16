$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$envFile = Join-Path $repoRoot ".env"

if (Test-Path $envFile) {
    Get-Content $envFile | ForEach-Object {
        if ($_ -match '^\s*([^#=]+)=(.*)$') {
            [Environment]::SetEnvironmentVariable($Matches[1].Trim(), $Matches[2].Trim(), "Process")
        }
    }
}

$token = $env:SONAR_TOKEN
$headers = @{Authorization = "Bearer $token"}
$base = "http://mac-mini-alexander.local:9000/api/issues/search?componentKeys=AIUsageTracker&ps=500"

$resp = Invoke-RestMethod -Uri "$base&impactSeverities=BLOCKER&resolved=false&statuses=OPEN,CONFIRMED" -Headers $headers
Write-Host "`n=== BLOCKER Issues - Open/Confirmed ($($resp.total)) ==="
foreach ($issue in $resp.issues) {
    $comp = $issue.component -replace "AIUsageTracker:", ""
    $line = $issue.textRange.startLine
    Write-Host "  $($issue.rule) | ${comp}:$line | $($issue.severity) | $($issue.message)"
}
