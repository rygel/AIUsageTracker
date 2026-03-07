param(
    [string]$AgentExecutablePath = "",
    [string]$OpenApiPath = "AIUsageTracker.Monitor\openapi.yaml",
    [int]$StartupTimeoutSeconds = 45
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($AgentExecutablePath)) {
    $candidateExecutables = @(
        (Join-Path $projectRoot "AIUsageTracker.Monitor\bin\Debug\net8.0\AIUsageTracker.Monitor.exe")
    )

    $defaultExe = $candidateExecutables | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($defaultExe)) {
        $searched = $candidateExecutables -join ", "
        throw "Agent executable not found. Searched: $searched. Build the solution before running this check."
    }

    $AgentExecutablePath = $defaultExe
}
elseif (-not (Test-Path -LiteralPath $AgentExecutablePath)) {
    throw "Agent executable not found: $AgentExecutablePath"
}

$openApiFullPath = if ([System.IO.Path]::IsPathRooted($OpenApiPath)) {
    $OpenApiPath
}
else {
    Join-Path $projectRoot $OpenApiPath
}

if (-not (Test-Path -LiteralPath $openApiFullPath)) {
    throw "OpenAPI file not found: $openApiFullPath"
}

function Get-OpenApiOperations {
    param([string]$Path)

    $operations = New-Object System.Collections.Generic.HashSet[string]([System.StringComparer]::OrdinalIgnoreCase)
    $currentPath = $null

    foreach ($line in Get-Content -LiteralPath $Path) {
        if ($line -match '^\s{2}(/api/[^:]+):\s*$') {
            $currentPath = $Matches[1]
            continue
        }

        if ($currentPath -and $line -match '^\s{4}(get|post|put|delete|patch|head|options|trace):\s*$') {
            [void]$operations.Add("$($Matches[1].ToUpperInvariant()) $currentPath")
            continue
        }

        if ($currentPath -and $line -match '^\s{2}\S.+:\s*$' -and $line -notmatch '^\s{2}/api/') {
            $currentPath = $null
        }
    }

    return $operations
}

function Wait-ForAgentPort {
    param(
        [int]$ProcessId,
        [int]$TimeoutSeconds
    )

    $localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    $agentInfoPath = Join-Path $localAppData "AIUsageTracker\monitor.json"
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    while ((Get-Date) -lt $deadline) {
        if ($ProcessId -gt 0) {
            try {
                $proc = Get-Process -Id $ProcessId -ErrorAction Stop
                if ($proc.HasExited) {
                    throw "Agent process $ProcessId exited before publishing monitor port."
                }
            }
            catch [System.Management.Automation.ItemNotFoundException] {
                throw "Agent process $ProcessId exited before publishing monitor port."
            }
        }

        if (Test-Path -LiteralPath $agentInfoPath) {
            try {
                $agentInfo = Get-Content -LiteralPath $agentInfoPath -Raw | ConvertFrom-Json
                if ($agentInfo -and [int]$agentInfo.port -gt 0) {
                    $reportedPort = [int]$agentInfo.port
                    if (-not $agentInfo.processId -and -not $agentInfo.process_id -or
                        [int]$agentInfo.processId -eq $ProcessId -or [int]$agentInfo.process_id -eq $ProcessId) {
                        return $reportedPort
                    }
                }
            }
            catch {
                # Keep polling while monitor writes startup file.
            }
        }

        try {
            $health = Invoke-RestMethod -Uri "http://localhost:5000/api/health" -TimeoutSec 1
            $healthPort = [int]$health.port
            if ($healthPort -le 0) {
                continue
            }

            $healthProcessId = $null
            if ($health.PSObject.Properties.Name -contains "processId" -and $health.processId) { $healthProcessId = [int]$health.processId }
            elseif ($health.PSObject.Properties.Name -contains "process_id" -and $health.process_id) { $healthProcessId = [int]$health.process_id }

            if (-not $healthProcessId -or $healthProcessId -eq $ProcessId) {
                return $healthPort
            }
        }
        catch {
            # Health endpoint not ready yet.
        }

        Start-Sleep -Milliseconds 500
    }

    throw "Timed out waiting for agent port discovery."
}

$agentProcess = $null
try {
    $openApiOperations = Get-OpenApiOperations -Path $openApiFullPath
    if ($openApiOperations.Count -eq 0) {
        throw "No API operations discovered in OpenAPI file: $openApiFullPath"
    }

    Write-Host "Starting Agent for live endpoint inspection..." -ForegroundColor Cyan
    $agentProcess = Start-Process -FilePath $AgentExecutablePath `
        -ArgumentList "--urls", "http://localhost:5000", "--debug" `
        -PassThru

    $port = Wait-ForAgentPort -ProcessId $agentProcess.Id -TimeoutSeconds $StartupTimeoutSeconds
    $diagnosticsUri = "http://localhost:$port/api/diagnostics"

    $diagnostics = $null
    $deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $diagnostics = Invoke-RestMethod -Uri $diagnosticsUri -TimeoutSec 2
            break
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }

    if (-not $diagnostics) {
        throw "Timed out waiting for diagnostics endpoint at $diagnosticsUri"
    }

    if (-not $diagnostics.endpoints) {
        throw "Diagnostics payload did not include endpoint metadata."
    }

    $liveOperations = New-Object System.Collections.Generic.HashSet[string]([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($endpoint in @($diagnostics.endpoints)) {
        $route = [string]$endpoint.route
        if ([string]::IsNullOrWhiteSpace($route) -or -not $route.StartsWith("/api/", [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        foreach ($method in @($endpoint.methods)) {
            $methodName = [string]$method
            if ([string]::IsNullOrWhiteSpace($methodName)) {
                continue
            }

            [void]$liveOperations.Add("$($methodName.ToUpperInvariant()) $route")
        }
    }

    $missingInLive = @($openApiOperations | Where-Object { -not $liveOperations.Contains($_) } | Sort-Object)
    $missingInOpenApi = @($liveOperations | Where-Object { -not $openApiOperations.Contains($_) } | Sort-Object)

    if ($missingInLive.Count -gt 0 -or $missingInOpenApi.Count -gt 0) {
        if ($missingInLive.Count -gt 0) {
            Write-Host "OpenAPI operations missing in live endpoints:" -ForegroundColor Red
            foreach ($entry in $missingInLive) {
                Write-Host "  - $entry" -ForegroundColor Red
            }
        }

        if ($missingInOpenApi.Count -gt 0) {
            Write-Host "Live endpoints missing in openapi.yaml:" -ForegroundColor Red
            foreach ($entry in $missingInOpenApi) {
                Write-Host "  - $entry" -ForegroundColor Red
            }
        }

        exit 1
    }

    Write-Host "SUCCESS: openapi.yaml matches live Agent endpoints." -ForegroundColor Green
}
finally {
    if ($agentProcess -and -not $agentProcess.HasExited) {
        Stop-Process -Id $agentProcess.Id
    }
}
