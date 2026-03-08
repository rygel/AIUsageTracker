param(
    [ValidateSet("core", "monitor", "web")]
    [string[]]$Suites = @("core", "monitor", "web"),
    [string]$Configuration = "Debug",
    [int]$MaxParallel = 8,
    [int]$TotalTimeoutMinutes = 10,
    [switch]$SkipBuild,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    # Keep local test runs deterministic under this environment.
    $env:MSBuildEnableWorkloadResolver = "false"
    $env:MSBUILDDISABLENODEREUSE = "1"
    $env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = "1"
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

    $runner = Join-Path $PSScriptRoot "run-local-tests-safe.ps1"
    if (-not (Test-Path -LiteralPath $runner)) {
        throw "Missing script: $runner"
    }

    Write-Host "Running local test suite(s): $($Suites -join ', ')" -ForegroundColor Cyan
    Write-Host "Configuration: $Configuration"
    Write-Host "MaxParallel: $MaxParallel"
    Write-Host "TotalTimeoutMinutes: $TotalTimeoutMinutes"

    & (Resolve-Path $runner) `
        -Suites $Suites `
        -Configuration $Configuration `
        -MaxParallel $MaxParallel `
        -TotalTimeoutMinutes $TotalTimeoutMinutes `
        -SkipBuild:$SkipBuild `
        -DryRun:$DryRun
}
finally {
    Pop-Location
}
