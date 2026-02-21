# Current Slim screenshot entrypoint (kept for backwards compatibility).
# Delegates to the maintained generator script.

param(
    [string]$Configuration = "Release",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$generatorScript = Join-Path $PSScriptRoot "generate_screenshots.ps1"
if (-not (Test-Path $generatorScript)) {
    Write-Host "ERROR: Missing screenshot generator script at $generatorScript" -ForegroundColor Red
    exit 1
}

$generatorArgs = @{
    Configuration = $Configuration
}

if ($SkipBuild) {
    $generatorArgs.SkipBuild = $true
}

& $generatorScript @generatorArgs
exit $LASTEXITCODE
