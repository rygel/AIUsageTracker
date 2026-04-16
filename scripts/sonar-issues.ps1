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
$base = "http://mac-mini-alexander.local:9000/api"

$p = 1
do {
    $url = "$base/measures/component_tree?component=AIUsageTracker&metricKeys=uncovered_lines,coverage,lines_to_cover&ps=500&p=$p&qualifiers=FIL&strategy=leaves"
    $resp = Invoke-RestMethod -Uri $url -Headers $headers
    
    foreach ($comp in $resp.components) {
        $unc = ($comp.measures | Where-Object { $_.metric -eq 'uncovered_lines' } | Select-Object -First 1)
        if ($unc -and [int]$unc.value -gt 15) {
            $uncVal = $unc.value
            $cov = ($comp.measures | Where-Object { $_.metric -eq 'coverage' } | Select-Object -First 1).value
            $total = ($comp.measures | Where-Object { $_.metric -eq 'lines_to_cover' } | Select-Object -First 1).value
            Write-Host "$cov% | $uncVal uncovered / $total total | $($comp.name)"
        }
    }
    $p++
} while ($resp.paging.total -gt ($resp.paging.pageIndex * $resp.paging.pageSize))
