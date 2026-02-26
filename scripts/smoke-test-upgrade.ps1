param(
    [Parameter(Mandatory = $true)]
    [string]$OldSetupPath,
    [Parameter(Mandatory = $true)]
    [string]$NewSetupPath,
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $OldSetupPath)) {
    throw "Old installer not found: $OldSetupPath"
}

if (-not (Test-Path -LiteralPath $NewSetupPath)) {
    throw "New installer not found: $NewSetupPath"
}

$tempRoot = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) { [System.IO.Path]::GetTempPath() } else { $env:RUNNER_TEMP }
$installDir = Join-Path $tempRoot "AIUsageTracker-UpgradeSmoke-$Runtime"
if (Test-Path -LiteralPath $installDir) {
    Remove-Item -LiteralPath $installDir -Recurse -Force
}
New-Item -Path $installDir -ItemType Directory -Force | Out-Null

$setupArgs = @(
    "/VERYSILENT",
    "/SUPPRESSMSGBOXES",
    "/NORESTART",
    "/SP-",
    "/DIR=$installDir"
)

$userProfile = $env:USERPROFILE
$localAppData = $env:LOCALAPPDATA
$trackerConfigDir = Join-Path $userProfile ".ai-consumption-tracker"
$agentDataDir = Join-Path $localAppData "AIUsageTracker"
$slimDataDir = Join-Path $localAppData "AIUsageTracker"

$authPath = Join-Path $trackerConfigDir "auth.json"
$providersPath = Join-Path $trackerConfigDir "providers.json"
$slimPrefsPath = Join-Path $slimDataDir "preferences.json"
$managedFiles = @($authPath, $providersPath, $slimPrefsPath)

$backupRoot = Join-Path $tempRoot "AIUsageTracker-UpgradeSmoke-Backup-$([Guid]::NewGuid().ToString('N'))"
New-Item -Path $backupRoot -ItemType Directory -Force | Out-Null

$agentProcess = $null
try {
    foreach ($filePath in $managedFiles) {
        if (Test-Path -LiteralPath $filePath) {
            $backupPath = Join-Path $backupRoot ($filePath -replace "[:\\]", "_")
            Copy-Item -LiteralPath $filePath -Destination $backupPath -Force
        }
    }

    Write-Host "Installing previous release artifact..." -ForegroundColor Cyan
    $oldInstall = Start-Process -FilePath $OldSetupPath -ArgumentList $setupArgs -PassThru -Wait
    if ($oldInstall.ExitCode -ne 0) {
        throw "Old installer exited with code $($oldInstall.ExitCode)"
    }

    $legacyOrCurrentUiExe = @(
        Join-Path $installDir "AIUsageTracker.exe"
        Join-Path $installDir "AIConsumptionTracker.exe"
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

    if (-not $legacyOrCurrentUiExe) {
        throw "Old installation did not produce a recognized UI executable (AIUsageTracker.exe or AIConsumptionTracker.exe)."
    }

    New-Item -Path $trackerConfigDir -ItemType Directory -Force | Out-Null
    New-Item -Path $agentDataDir -ItemType Directory -Force | Out-Null
    New-Item -Path $slimDataDir -ItemType Directory -Force | Out-Null

    @"
{
  "openai": { "key": "upgrade-test-key" },
  "app_settings": {
    "always_on_top": true,
    "notification_threshold": 88.0,
    "is_privacy_mode": true,
    "upgrade_marker": "legacy-agent-settings"
  }
}
"@ | Set-Content -LiteralPath $authPath -Encoding UTF8

    @"
{
  "openai": {
    "type": "usage",
    "show_in_tray": true,
    "enable_notifications": true,
    "upgrade_marker": "legacy-provider-settings"
  }
}
"@ | Set-Content -LiteralPath $providersPath -Encoding UTF8

    @"
{
  "AlwaysOnTop": true,
  "InvertProgressBar": true,
  "FontFamily": "Consolas",
  "FontSize": 14,
  "IsPrivacyMode": true,
  "UpgradeMarker": "legacy-slim-settings"
}
"@ | Set-Content -LiteralPath $slimPrefsPath -Encoding UTF8

    $expectedHashes = @{}
    foreach ($filePath in $managedFiles) {
        $expectedHashes[$filePath] = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash
    }

    Write-Host "Installing current release artifact over previous version..." -ForegroundColor Cyan
    $newInstall = Start-Process -FilePath $NewSetupPath -ArgumentList $setupArgs -PassThru -Wait
    if ($newInstall.ExitCode -ne 0) {
        throw "New installer exited with code $($newInstall.ExitCode)"
    }

    foreach ($filePath in $managedFiles) {
        if (-not (Test-Path -LiteralPath $filePath)) {
            throw "Expected settings file missing after upgrade: $filePath"
        }

        $actualHash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash
        if (-not [string]::Equals($actualHash, $expectedHashes[$filePath], [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Settings file changed during upgrade: $filePath"
        }
    }

    $agentExe = Join-Path $installDir "AIUsageTracker.Monitor.exe"
    if (-not (Test-Path -LiteralPath $agentExe)) {
        throw "Upgraded installation missing agent executable: $agentExe"
    }

    Write-Host "Starting upgraded Agent executable for health check..." -ForegroundColor Cyan
    $agentProcess = Start-Process -FilePath $agentExe -PassThru

    $healthOk = $false
    for ($attempt = 0; $attempt -lt 30 -and -not $healthOk; $attempt++) {
        Start-Sleep -Seconds 1
        foreach ($port in 5000..5010) {
            try {
                $response = Invoke-RestMethod -Uri "http://localhost:$port/api/health" -TimeoutSec 2
                if ($response.status -eq "healthy") {
                    $healthOk = $true
                    break
                }
            }
            catch {
                # Keep polling fallback ports.
            }
        }
    }

    if (-not $healthOk) {
        throw "Upgraded agent health endpoint did not become ready on ports 5000-5010."
    }

    Write-Host "Upgrade smoke test passed with preserved settings." -ForegroundColor Green
}
finally {
    if ($agentProcess -and -not $agentProcess.HasExited) {
        Stop-Process -Id $agentProcess.Id
    }

    foreach ($filePath in $managedFiles) {
        $backupPath = Join-Path $backupRoot ($filePath -replace "[:\\]", "_")
        if (Test-Path -LiteralPath $backupPath) {
            $dir = Split-Path -Parent $filePath
            if ($dir) {
                New-Item -Path $dir -ItemType Directory -Force | Out-Null
            }
            Copy-Item -LiteralPath $backupPath -Destination $filePath -Force
        }
        elseif (Test-Path -LiteralPath $filePath) {
            Remove-Item -LiteralPath $filePath -Force
        }
    }
}
