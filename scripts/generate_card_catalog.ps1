# Card Catalog Screenshot Generator for AI Usage Tracker
# Captures screenshots for all important card setting permutations
# and generates a markdown documentation file.
#
# Usage:
#   .\scripts\generate_card_catalog.ps1
#   .\scripts\generate_card_catalog.ps1 -SkipBuild -OutputDir "C:\temp\catalog"

param(
    [string]$Configuration = "Release",
    [switch]$SkipBuild,
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "AI Usage Tracker Card Catalog Generator" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

$projectRoot = Split-Path -Parent $PSScriptRoot
$binPath = Join-Path $projectRoot "AIUsageTracker.UI.Slim\bin\$Configuration\net8.0-windows10.0.17763.0"
$exePath = Join-Path $binPath "AIUsageTracker.exe"

if (-not $SkipBuild) {
    Write-Host "Building project..." -ForegroundColor Cyan
    $buildOutput = & dotnet build (Join-Path $projectRoot "AIUsageTracker.UI.Slim\AIUsageTracker.UI.Slim.csproj") --configuration $Configuration 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed!" -ForegroundColor Red
        $buildOutput | ForEach-Object { Write-Host $_ }
        exit 1
    }
    Write-Host "Build successful!" -ForegroundColor Green
    Write-Host ""
}

if (-not (Test-Path $exePath)) {
    Write-Host "ERROR: Executable not found at: $exePath" -ForegroundColor Red
    exit 1
}

# Kill any existing instances
$existingProcesses = Get-Process -Name "AIUsageTracker" -ErrorAction SilentlyContinue
if ($existingProcesses) {
    Write-Host "Stopping existing instances..." -ForegroundColor Yellow
    $existingProcesses | Stop-Process -Force
    Start-Sleep -Seconds 2
}

$screenshotsDir = if ($OutputDir) { $OutputDir } else { Join-Path $projectRoot "docs" }
New-Item -ItemType Directory -Path $screenshotsDir -Force | Out-Null

Write-Host "Capturing card catalog screenshots..." -ForegroundColor Cyan
Write-Host "Output: $screenshotsDir\card-catalog\" -ForegroundColor Gray
Write-Host ""

$appArgs = @("--test", "--screenshot", "--card-catalog", "--output-dir", $screenshotsDir)
$process = Start-Process -FilePath $exePath -ArgumentList $appArgs -PassThru -WindowStyle Hidden

$timeout = 120
$timer = [System.Diagnostics.Stopwatch]::StartNew()
while (-not $process.HasExited -and $timer.Elapsed.TotalSeconds -lt $timeout) {
    Start-Sleep -Milliseconds 500
}

if (-not $process.HasExited) {
    Write-Host "WARNING: Process timed out after ${timeout}s, forcing exit..." -ForegroundColor Yellow
    $process.Kill()
    $process.WaitForExit(5000)
    exit 1
}

$catalogDir = Join-Path $screenshotsDir "card-catalog"
if (-not (Test-Path $catalogDir)) {
    Write-Host "ERROR: Card catalog directory was not created." -ForegroundColor Red
    exit 1
}

$pngFiles = Get-ChildItem -Path $catalogDir -Filter "card_*.png" -ErrorAction SilentlyContinue
$markdownFile = Join-Path $catalogDir "CARD-CATALOG.md"

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "Card Catalog Results" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

foreach ($file in ($pngFiles | Sort-Object Name)) {
    $sizeKB = [math]::Round($file.Length / 1KB, 1)
    Write-Host "  OK: $($file.Name) (${sizeKB} KB)" -ForegroundColor Green
}

Write-Host ""
if (Test-Path $markdownFile) {
    Write-Host "  OK: CARD-CATALOG.md" -ForegroundColor Green
} else {
    Write-Host "  MISSING: CARD-CATALOG.md" -ForegroundColor Red
}

Write-Host ""
Write-Host "Total: $($pngFiles.Count) card screenshots captured." -ForegroundColor $(if ($pngFiles.Count -gt 0) { 'Green' } else { 'Red' })
Write-Host "Location: $catalogDir" -ForegroundColor Gray

if ($pngFiles.Count -eq 0) {
    exit 1
}
