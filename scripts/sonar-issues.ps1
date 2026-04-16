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

$resp = Invoke-RestMethod -Uri "$base&impactSeverities=MEDIUM&impactSoftwareQualities=MAINTAINABILITY&inNewCodePeriod=true&resolved=false&statuses=OPEN,CONFIRMED" -Headers $headers
Write-Host "`n=== MEDIUM MAINTAINABILITY in New Code ($($resp.total)) ==="

$byRule = @{}
foreach ($issue in $resp.issues) {
    if (-not $byRule.ContainsKey($issue.rule)) {
        $byRule[$issue.rule] = @()
    }
    $byRule[$issue.rule] += $issue
}

foreach ($rule in ($byRule.Keys | Sort-Object)) {
    $issues = $byRule[$rule]
    $msg = $issues[0].message.Substring(0, [Math]::Min(70, $issues[0].message.Length))
    Write-Host "`n  --- $($rule) ($($issues.Count)) ---"
    Write-Host "  Example: $msg"
    foreach ($issue in $issues) {
        $comp = $issue.component -replace "AIUsageTracker:", ""
        $line = $issue.textRange.startLine
        Write-Host "    ${comp}:$line"
    }
}
