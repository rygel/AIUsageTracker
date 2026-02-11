# AI Token Tracker - Smart Debug Script
# Builds only when source files have changed, otherwise runs existing executable

param(
    [switch]$Help,
    [switch]$ForceBuild
)

# Color output functions
function Write-Success { param([string]$msg) Write-Host "✓ $msg" -ForegroundColor Green }
function Write-Error { param([string]$msg) Write-Host "❌ $msg" -ForegroundColor Red }
function Write-Info { param([string]$msg) Write-Host "ℹ️  $msg" -ForegroundColor Blue }
function Write-Warn { param([string]$msg) Write-Host "⚠️  $msg" -ForegroundColor Yellow }

if ($Help) {
    Write-Host "AI Token Tracker - Smart Debug Script"
    Write-Host "Usage: .\debug-build.ps1 [OPTIONS]"
    Write-Host ""
    Write-Host "Options:"
    Write-Host "  -ForceBuild  Always build, even if no changes detected"
    Write-Host "  -Help        Show this help"
    Write-Host ""
    Write-Host "This script automatically detects if source files have changed"
    Write-Host "and only rebuilds when necessary."
    exit 0
}

Write-Host "`n🚀 AI Token Tracker - Smart Debug`n"

# Store the original directory to return to it later
$originalDirectory = Get-Location

# Check directory - script is in rust/scripts/, so we need to go up one level
if (-not (Test-Path "..\aic_app\Cargo.toml")) {
    Write-Error "Must run from rust/scripts/ directory"
    Write-Info "Current: $(Get-Location)"
    Write-Info "Use: cd rust/scripts; .\debug-build.ps1"
    exit 1
}

# Change to rust/ directory for building
Set-Location ".."

# Check dependencies
try { $null = cargo --version; Write-Success "Rust found" } 
catch { Write-Error "Rust not found - install from https://rustup.rs/"; exit 1 }

try { $null = node --version; Write-Success "Node.js found" } 
catch { Write-Error "Node.js not found - install from https://nodejs.org/"; exit 1 }

# Function to check if build is needed
# Returns $true if:
#   - The executable doesn't exist
#   - Any source file (*.rs, *.toml, *.html, *.css, *.js) is newer than the executable
function Test-BuildNeeded {
    param([string]$TargetPath)
    
    # If executable doesn't exist, we need to build
    if (-not (Test-Path $TargetPath)) {
        Write-Info "Executable not found, build required"
        return $true
    }
    
    # Get the last write time of the executable
    $exeTime = (Get-Item $TargetPath).LastWriteTime
    
    # Check all source files in the project
    # Monitors: Rust files, Cargo.toml, and frontend assets (HTML/CSS/JS)
    $sourceFiles = Get-ChildItem -Path "." -Recurse -Include "*.rs", "*.toml", "*.html", "*.css", "*.js" | 
        Where-Object { 
            $_.FullName -notlike "*\target\*" -and 
            $_.FullName -notlike "*\.git\*" -and
            $_.FullName -notlike "*\node_modules\*"
        }
    
    $newestSource = $sourceFiles | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    
    if ($newestSource -and $newestSource.LastWriteTime -gt $exeTime) {
        Write-Info "Source files changed: $($newestSource.Name)"
        Write-Info "  Modified: $($newestSource.LastWriteTime)"
        Write-Info "  Exe time: $exeTime"
        return $true
    }
    
    return $false
}

# Determine build path based on platform
$buildPath = if ($IsWindows -or $env:OS -eq "Windows_NT") {
    "target\debug\aic_app.exe"
} else {
    "target/debug/aic_app"
}

Set-Location "aic_app"

# Decide whether to build
$needsBuild = $ForceBuild -or (Test-BuildNeeded -TargetPath "..\$buildPath")

if ($needsBuild) {
    Write-Info "Building debug version..."
    cargo tauri build --no-bundle
    if ($LASTEXITCODE -ne 0) { 
        Write-Error "Build failed"
        exit 1 
    }
    Write-Success "Build complete"
} else {
    Write-Success "No changes detected, skipping build"
}

# Check if executable exists
if (-not (Test-Path "..\$buildPath")) {
    Write-Error "Executable not found at $buildPath"
    Write-Info "Try running with -ForceBuild to force a rebuild"
    exit 1
}

Write-Info "Starting application..."
Write-Host ""

# Run the executable directly instead of using cargo run
& "..\$buildPath"

Write-Host "`nApplication closed."

# Return to original directory
Set-Location $originalDirectory
