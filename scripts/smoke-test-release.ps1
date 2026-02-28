param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactsPath,
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ArtifactsPath)) {
    throw "Artifacts path not found: $ArtifactsPath"
}

$setupPattern = "AIUsageTracker_Setup_v${Version}_$Runtime.exe"
$zipPattern = "AIUsageTracker_v${Version}_$Runtime.zip"

$setupFile = Get-ChildItem -Path $ArtifactsPath -Recurse -File |
    Where-Object { $_.Name -eq $setupPattern } |
    Select-Object -First 1

$zipFile = Get-ChildItem -Path $ArtifactsPath -Recurse -File |
    Where-Object { $_.Name -eq $zipPattern } |
    Select-Object -First 1

if (-not $setupFile) {
    throw "Setup artifact not found: $setupPattern"
}

if (-not $zipFile) {
    throw "ZIP artifact not found: $zipPattern"
}

Write-Host "Found setup artifact: $($setupFile.FullName)" -ForegroundColor Green
Write-Host "Found zip artifact: $($zipFile.FullName)" -ForegroundColor Green

$tempRoot = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) { [System.IO.Path]::GetTempPath() } else { $env:RUNNER_TEMP }
$installDir = Join-Path $tempRoot "AIUsageTracker-Smoke"
if (Test-Path $installDir) {
    Remove-Item -Path $installDir -Recurse -Force
}
New-Item -Path $installDir -ItemType Directory -Force | Out-Null

$installerArgs = @(
    "/VERYSILENT",
    "/SUPPRESSMSGBOXES",
    "/NORESTART",
    "/SP-",
    "/DIR=$installDir"
)

Write-Host "Running installer smoke test..." -ForegroundColor Cyan
$installerProcess = Start-Process -FilePath $setupFile.FullName -ArgumentList $installerArgs -PassThru -Wait
if ($installerProcess.ExitCode -ne 0) {
    throw "Installer exited with code $($installerProcess.ExitCode)"
}

$agentExe = Join-Path $installDir "AIUsageTracker.Monitor.exe"
$trackerExe = Join-Path $installDir "AIUsageTracker.exe"
if (-not (Test-Path $agentExe)) {
    throw "Installed agent executable not found: $agentExe"
}
if (-not (Test-Path $trackerExe)) {
    throw "Installed tracker executable not found: $trackerExe"
}

Write-Host "Starting installed Agent executable..." -ForegroundColor Cyan
$agentProcess = Start-Process -FilePath $agentExe -PassThru

try {
    $healthOk = $false
    $healthResponse = $null
    for ($attempt = 0; $attempt -lt 30 -and -not $healthOk; $attempt++) {
        Start-Sleep -Seconds 1
        foreach ($port in 5000..5010) {
            try {
                $response = Invoke-RestMethod -Uri "http://localhost:$port/api/health" -TimeoutSec 2
                if ($response.status -eq "healthy") {
                    $healthOk = $true
                    $healthResponse = $response
                    break
                }
            }
            catch {
                # Keep polling fallback ports.
            }
        }
    }

    if (-not $healthOk) {
        throw "Agent health endpoint did not become ready on ports 5000-5010."
    }

    Write-Host "Health check passed: status=$($healthResponse.status) port=$($healthResponse.port)" -ForegroundColor Green

    Write-Host "Tracker executable presence check passed." -ForegroundColor Green
}
finally {
    if ($agentProcess -and -not $agentProcess.HasExited) {
        Stop-Process -Id $agentProcess.Id
    }
}

Write-Host "Release smoke test completed successfully." -ForegroundColor Green
