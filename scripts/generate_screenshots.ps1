# Screenshot Capture Script for AI Consumption Tracker
# This script runs the application in headless mode and captures screenshots automatically

param(
    [string]$Configuration = "Release",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "AI Consumption Tracker Screenshot Capture" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# Determine the correct paths
$projectRoot = Split-Path -Parent $PSScriptRoot
$binPath = Join-Path $projectRoot "AIConsumptionTracker.UI.Slim\bin\$Configuration\net8.0-windows10.0.17763.0"
$exePath = Join-Path $binPath "AIConsumptionTracker.exe"

# Check if executable exists before optional build
if (-not (Test-Path $exePath) -and $SkipBuild) {
    Write-Host "ERROR: Executable not found at: $exePath" -ForegroundColor Red
    Write-Host "Build was skipped. Run build first or omit -SkipBuild." -ForegroundColor Yellow
    exit 1
}

if (Test-Path $exePath) {
    Write-Host "Found executable: $exePath" -ForegroundColor Green
}
else {
    Write-Host "Executable not found yet, building project..." -ForegroundColor Yellow
}
Write-Host ""

# Create screenshots directory if it doesn't exist
$screenshotsDir = Join-Path $projectRoot "docs"
if (-not (Test-Path $screenshotsDir)) {
    New-Item -ItemType Directory -Path $screenshotsDir -Force | Out-Null
    Write-Host "Created screenshots directory: $screenshotsDir" -ForegroundColor Gray
}

# Backup existing screenshots
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$backupDir = Join-Path $screenshotsDir "backup_$timestamp"
$existingScreenshots = Get-ChildItem -Path $screenshotsDir -Filter "screenshot_*.png" -ErrorAction SilentlyContinue
if ($existingScreenshots) {
    New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
    $existingScreenshots | Copy-Item -Destination $backupDir
    Write-Host "Backed up existing screenshots to: $backupDir" -ForegroundColor Gray
}

Write-Host "Starting screenshot capture process..." -ForegroundColor Cyan
Write-Host "Privacy Mode: ENABLED (hardcoded for screenshots)" -ForegroundColor Green
Write-Host ""

# Kill any existing instances
$existingProcesses = Get-Process -Name "AIConsumptionTracker" -ErrorAction SilentlyContinue
if ($existingProcesses) {
    Write-Host "Stopping existing instances..." -ForegroundColor Yellow
    $existingProcesses | Stop-Process -Force
    Start-Sleep -Seconds 2
}

if (-not $SkipBuild) {
    Write-Host "Building project..." -ForegroundColor Cyan
    $buildOutput = & dotnet build (Join-Path $projectRoot "AIConsumptionTracker.UI.Slim\AIConsumptionTracker.UI.Slim.csproj") --configuration $Configuration 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed!" -ForegroundColor Red
        $buildOutput | ForEach-Object { Write-Host $_ }
        exit 1
    }
    Write-Host "Build successful!" -ForegroundColor Green
    Write-Host ""
}
else {
    Write-Host "Skipping build (--SkipBuild)." -ForegroundColor Gray
    Write-Host ""
}

if (-not (Test-Path $exePath)) {
    Write-Host "ERROR: Executable not found after build at: $exePath" -ForegroundColor Red
    exit 1
}

# Run the application with screenshot flags
Write-Host "Capturing screenshots with PRIVACY MODE enabled..." -ForegroundColor Cyan
Write-Host "This will take 15-20 seconds..." -ForegroundColor Gray
Write-Host ""

$process = Start-Process -FilePath $exePath -ArgumentList @("--test", "--screenshot") -PassThru -WindowStyle Hidden

# Wait for the process to complete (screenshot mode auto-exits)
$timeout = 60
$timer = [System.Diagnostics.Stopwatch]::StartNew()

$dots = 0
while (-not $process.HasExited -and $timer.Elapsed.TotalSeconds -lt $timeout) {
    Start-Sleep -Milliseconds 500
    $dots++
    if ($dots % 10 -eq 0) {
        Write-Host "." -NoNewline
    }
}
Write-Host ""

if (-not $process.HasExited) {
    Write-Host "WARNING: Process timed out, forcing exit..." -ForegroundColor Yellow
    $process.Kill()
    $process.WaitForExit(5000)
}

# Check if screenshots were created
$expectedScreenshots = @(
    "screenshot_dashboard_privacy.png",
    "screenshot_settings_privacy.png",
    "screenshot_settings_providers_privacy.png",
    "screenshot_settings_layout_privacy.png",
    "screenshot_settings_history_privacy.png",
    "screenshot_settings_agent_privacy.png",
    "screenshot_info_privacy.png",
    "screenshot_context_menu_privacy.png"
)

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "Screenshot Capture Results" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

$successCount = 0
foreach ($screenshot in $expectedScreenshots) {
    $screenshotPath = Join-Path $screenshotsDir $screenshot
    if (Test-Path $screenshotPath) {
        $fileInfo = Get-Item $screenshotPath
        $sizeKB = [math]::Round($fileInfo.Length / 1KB, 1)
        Write-Host "OK: $screenshot (${sizeKB} KB)" -ForegroundColor Green
        $successCount++
    } else {
        Write-Host "MISSING: $screenshot" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Total: $successCount of $($expectedScreenshots.Count) screenshots captured" -ForegroundColor $(if ($successCount -eq $expectedScreenshots.Count) { 'Green' } else { 'Yellow' })
Write-Host "Location: $screenshotsDir" -ForegroundColor Gray
Write-Host ""

if ($successCount -eq $expectedScreenshots.Count) {
    Write-Host "SUCCESS! All screenshots captured with privacy mode enabled." -ForegroundColor Green
    Write-Host ""
    Write-Host "Generated files:" -ForegroundColor Cyan
    foreach ($screenshot in $expectedScreenshots) {
        Write-Host "  - docs/$screenshot" -ForegroundColor Gray
    }
    exit 0
} else {
    Write-Host "WARNING: Some screenshots are missing." -ForegroundColor Yellow
    Write-Host "Check the application logs:" -ForegroundColor Gray
    Write-Host "  %LOCALAPPDATA%\AIConsumptionTracker\logs\app_*.log" -ForegroundColor Gray
    exit 1
}
