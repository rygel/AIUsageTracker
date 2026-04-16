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

$quality = if ($args.Count -gt 0) { $args[0] } else { "LOW" }
$resp = Invoke-RestMethod -Uri "$base&impactSoftwareQualities=$quality&resolved=false&statuses=OPEN,CONFIRMED" -Headers $headers
Write-Host "`n=== $quality Issues - Open/Confirmed ($($resp.total)) ==="

$byRule = @{}
foreach ($issue in $resp.issues) {
    if (-not $byRule.ContainsKey($issue.rule)) {
        $byRule[$issue.rule] = @()
    }
    $byRule[$issue.rule] += $issue
}

foreach ($rule in ($byRule.Keys | Sort-Object { $_ -replace 'external_roslyn:', '' })) {
    $issues = $byRule[$rule]
    $msg = $issues[0].message.Substring(0, [Math]::Min(80, $issues[0].message.Length))
    Write-Host "`n  --- $($rule) ($($issues.Count)) ---"
    Write-Host "  $msg"
    foreach ($issue in $issues) {
        $comp = $issue.component -replace "AIUsageTracker:", ""
        $line = $issue.textRange.startLine
        Write-Host "    ${comp}:$line"
    }
}
