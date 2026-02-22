# Web UI Screenshot Generation Script
# Automates the process of capturing screenshots for documentation

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$webProject = Join-Path $projectRoot "AIUsageTracker.Web"
$testProject = Join-Path $projectRoot "AIUsageTracker.Web.Tests"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "Web UI Screenshot Generator" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan

# 1. Build and Install Dependencies
Write-Host "Building Test Project..." -ForegroundColor Yellow
dotnet build $testProject -c Debug

# Install Playwright browsers if needed
Write-Host "Ensuring Playwright browsers are installed..." -ForegroundColor Yellow
$playwrightScript = Join-Path $testProject "bin/Debug/net8.0/playwright.ps1"
if (Test-Path $playwrightScript) {
    & $playwrightScript install
} else {
    Write-Host "Warning: playwright.ps1 not found at expected location. Skipping browser install." -ForegroundColor Red
}

# 2. Start Web Application
Write-Host "Starting Web Application..." -ForegroundColor Yellow
$process = Start-Process dotnet -ArgumentList "run --project $webProject --urls=http://localhost:5100" -PassThru -NoNewWindow
$job = $process.Id

Write-Host "Web App PID: $job" -ForegroundColor Gray

# Wait for app to be ready
Write-Host "Waiting for Web App to start..." -ForegroundColor Yellow
$retries = 0
$maxRetries = 30
$ready = $false

while ($retries -lt $maxRetries) {
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:5100" -UseBasicParsing -ErrorAction Stop
        if ($response.StatusCode -eq 200) {
            $ready = $true
            break
        }
    } catch {
        Start-Sleep -Seconds 1
        Write-Host "." -NoNewline
    }
    $retries++
}
Write-Host ""

if (-not $ready) {
    Write-Host "Failed to start Web App!" -ForegroundColor Red
    Stop-Process -Id $job -Force -ErrorAction SilentlyContinue
    exit 1
}

Write-Host "Web App is running!" -ForegroundColor Green

# 3. Run Screenshot Tests
Write-Host "Running Screenshot Tests..." -ForegroundColor Cyan
try {
    dotnet test $testProject --logger "console;verbosity=normal"
    Write-Host "Screenshots captured successfully!" -ForegroundColor Green
} catch {
    Write-Host "Test execution failed!" -ForegroundColor Red
} finally {
    # 4. cleanup
    Write-Host "Stopping Web Application..." -ForegroundColor Yellow
    Stop-Process -Id $job -Force -ErrorAction SilentlyContinue
}

Write-Host "Done. Check docs/ folder for screenshots." -ForegroundColor Green

