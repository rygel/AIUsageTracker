# Test script for agent startup
Write-Host "=== AI Consumption Tracker Agent Diagnostics ===" -ForegroundColor Cyan

# Check if agent exists in common locations
$agentName = "aic_agent.exe"
$searchPaths = @(
    ".\$agentName",
    "..\target\debug\$agentName",
    "..\target\release\$agentName",
    "..\..\target\debug\$agentName",
    "..\..\target\release\$agentName"
)

Write-Host "`nSearching for agent executable..." -ForegroundColor Yellow
$found = $false
foreach ($path in $searchPaths) {
    $fullPath = Resolve-Path $path -ErrorAction SilentlyContinue
    if ($fullPath -and (Test-Path $fullPath)) {
        Write-Host "  [FOUND] $path" -ForegroundColor Green
        Write-Host "         -> $fullPath" -ForegroundColor Gray
        $found = $true
        
        # Try to get version
        try {
            $version = & $fullPath --version 2>$null
            if ($version) {
                Write-Host "         Version: $version" -ForegroundColor Gray
            }
        } catch {}
    } else {
        Write-Host "  [NOT FOUND] $path" -ForegroundColor DarkGray
    }
}

if (-not $found) {
    Write-Host "`n[ERROR] Agent executable not found!" -ForegroundColor Red
    Write-Host "Please build it first:" -ForegroundColor Yellow
    Write-Host "  cargo build -p aic_agent" -ForegroundColor White
    exit 1
}

# Check if port 8080 is already in use
Write-Host "`nChecking port 8080..." -ForegroundColor Yellow
try {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 8080)
    $listener.Start()
    $listener.Stop()
    Write-Host "  [OK] Port 8080 is available" -ForegroundColor Green
} catch {
    Write-Host "  [IN USE] Port 8080 is already in use" -ForegroundColor Yellow
    Write-Host "         Another agent instance may be running" -ForegroundColor Gray
    
    # Try to check if it's the agent
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:8080/health" -UseBasicParsing -TimeoutSec 2
        Write-Host "  [RUNNING] Agent is responding on port 8080" -ForegroundColor Green
        Write-Host "         Response: $($response.Content)" -ForegroundColor Gray
    } catch {
        Write-Host "  [ERROR] Port 8080 is in use but not responding to health checks" -ForegroundColor Red
    }
}

# Check current working directory
Write-Host "`nCurrent working directory: $(Get-Location)" -ForegroundColor Yellow

# Build instructions
Write-Host "`n=== Build Instructions ===" -ForegroundColor Cyan
Write-Host "To build the agent:" -ForegroundColor White
Write-Host "  cargo build -p aic_agent" -ForegroundColor Yellow
Write-Host "`nTo build and run the UI with agent:" -ForegroundColor White
Write-Host "  .\scripts\debug-build.ps1" -ForegroundColor Yellow

Write-Host "`n=== Testing ===" -ForegroundColor Cyan
Write-Host "You can now start the UI and click the 🤖 button to start the agent." -ForegroundColor White
Write-Host "The updated code will search multiple locations for the agent executable." -ForegroundColor White
