param(
    [string]$AgentExecutablePath = "",
    [string]$OpenApiPath = "AIUsageTracker.Monitor\openapi.yaml",
    [int]$StartupTimeoutSeconds = 45
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($AgentExecutablePath)) {
    $defaultExe = Join-Path $projectRoot "AIUsageTracker.Monitor\bin\Debug\net8.0-windows10.0.17763.0\AIUsageTracker.Monitor.exe"
    if (-not (Test-Path -LiteralPath $defaultExe)) {
        throw "Agent executable not found at $defaultExe. Build the solution before running this check."
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

        if ($line -match '^\s{2}\S.+:\s*$' -and $line -notmatch '^\s{2}/api/') {
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

    $agentInfoPaths = @(
        (Join-Path $env:LOCALAPPDATA "AIUsageTracker\monitor.json"),
        (Join-Path $env:LOCALAPPDATA "AIConsumptionTracker\monitor.json")
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    while ((Get-Date) -lt $deadline) {
        foreach ($agentInfoPath in $agentInfoPaths) {
            if (Test-Path -LiteralPath $agentInfoPath) {
                try {
                    $agentInfo = Get-Content -LiteralPath $agentInfoPath -Raw | ConvertFrom-Json
                    if ($agentInfo -and [int]$agentInfo.processId -eq $ProcessId -and [int]$agentInfo.port -gt 0) {
                        return [int]$agentInfo.port
                    }
                }
                catch {
                    # Keep polling while agent writes startup file.
                }
            }
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
    $agentProcess = Start-Process -FilePath $AgentExecutablePath -PassThru

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


