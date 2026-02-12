# AI Token Tracker - Smart Debug Script
# Builds only when source files have changed, otherwise runs existing executable

param(
    [switch]$Help,
    [switch]$ForceBuild
)

# Color output functions
function Write-Success { param([string]$Message) Write-Host "✓ $Message" -ForegroundColor Green }
function Write-ErrorMsg { param([string]$Message) Write-Host "❌ $Message" -ForegroundColor Red }
function Write-InfoMsg { param([string]$Message) Write-Host "ℹ️  $Message" -ForegroundColor Blue }
function Write-WarnMsg { param([string]$Message) Write-Host "⚠️  $Message" -ForegroundColor Yellow }

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
    Write-ErrorMsg "Must run from rust/scripts/ directory"
    Write-InfoMsg "Current: $(Get-Location)"
    Write-InfoMsg "Use: cd rust/scripts; .\debug-build.ps1"
    exit 1
}

# Change to rust/ directory for building
Set-Location ".."

# Check dependencies
try { $null = cargo --version; Write-Success "Rust found" } 
catch { Write-ErrorMsg "Rust not found - install from https://rustup.rs/"; exit 1 }

try { $null = node --version; Write-Success "Node.js found" } 
catch { Write-ErrorMsg "Node.js not found - install from https://nodejs.org/"; exit 1 }

# Function to check if build is needed
# Returns $true if:
#   - The executable doesn't exist
#   - Any source file (*.rs, *.toml, *.html, *.css, *.js) is newer than the executable
function Test-BuildNeeded {
    param([string]$TargetPath)
    
    # If executable doesn't exist, we need to build
    if (-not (Test-Path $TargetPath)) {
        Write-InfoMsg "Executable not found, build required"
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
        Write-InfoMsg "Source files changed: $($newestSource.Name)"
        Write-InfoMsg "  Modified: $($newestSource.LastWriteTime)"
        Write-InfoMsg "  Exe time: $exeTime"
        return $true
    }
    
    return $false
}

# Determine build path based on platform
$buildPath = if ($IsWindows -or $env:OS -eq "Windows_NT") {
    "target\release\aic_app.exe"
} else {
    "target/release/aic_app"
}

Set-Location "aic_app"

# Validate HTML/JS files before building
Write-InfoMsg "Validating HTML/JavaScript files..."
if (Test-Path "validate.ps1") {
    & "./validate.ps1"
    if ($LASTEXITCODE -ne 0) {
        Write-ErrorMsg "Validation failed! Fix errors before building."
        exit 1
    }
} else {
    Write-WarnMsg "Validation script not found, skipping validation"
}

# Decide whether to build
$needsBuild = $ForceBuild -or (Test-BuildNeeded -TargetPath "..\$buildPath")

if ($needsBuild) {
    Write-InfoMsg "Building release version..."
    cargo tauri build --no-bundle
    if ($LASTEXITCODE -ne 0) { 
        Write-ErrorMsg "Build failed"
        exit 1 
    }
    Write-Success "Build complete"
} else {
    Write-Success "No changes detected, skipping build"
}

# Check if executable exists
if (-not (Test-Path "..\$buildPath")) {
    Write-ErrorMsg "Executable not found at $buildPath"
    Write-InfoMsg "Try running with -ForceBuild to force a rebuild"
    exit 1
}

Write-InfoMsg "Starting application..."
Write-Host ""

# Run the executable directly to capture console output
& "..\$buildPath"

Write-Host "`nApplication closed with exit code: $LASTEXITCODE"

# Return to original directory
Set-Location $originalDirectory
