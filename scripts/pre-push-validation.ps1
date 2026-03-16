#!/usr/bin/env pwsh
# Pre-push validation script - Run this before pushing to catch CI/CD failures locally
# Mimics the key checks from GitHub workflows

param(
    [switch]$SkipTests,
    [switch]$SkipThemeValidation,
    [switch]$SkipFormat,
    [switch]$StrictAnalyzers
)

$ErrorActionPreference = "Stop"
$hadFailure = $false

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  PRE-PUSH VALIDATION" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Stabilize local .NET execution on this machine.
$env:MSBuildEnableWorkloadResolver = "false"
$env:MSBUILDDISABLENODEREUSE = "1"
$env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

# Step 1: Build
Write-Host "[1/5] Building solution..." -ForegroundColor Yellow
$buildOutput = dotnet build AIUsageTracker.sln --configuration Release --verbosity minimal 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "FAILED" -ForegroundColor Red
    Write-Host $buildOutput
    $hadFailure = $true
} else {
    Write-Host "PASSED" -ForegroundColor Green
}

if ($hadFailure) {
    Write-Host ""
    Write-Host "Build failed. Fix errors before continuing." -ForegroundColor Red
    exit 1
}

Write-Host ""

# Step 2: Format/analyzer verification on changed files
if (-not $SkipFormat) {
    Write-Host "[2/5] Verifying formatting/analyzers on changed files..." -ForegroundColor Yellow

    $mergeBase = git merge-base HEAD origin/develop
    if (-not $mergeBase) {
        Write-Host "FAILED (could not determine merge-base with origin/develop)" -ForegroundColor Red
        $hadFailure = $true
    }
    else {
        $changedFiles = git diff --name-only --diff-filter=ACMR $mergeBase HEAD -- '*.cs' '*.csproj' '*.props' '*.targets' '.editorconfig'
        if (-not $changedFiles) {
            Write-Host "PASSED (no format-relevant changes detected)" -ForegroundColor Green
        }
        else {
            $formatWhitespaceOutput = dotnet format whitespace AIUsageTracker.sln --verify-no-changes --verbosity minimal --include $changedFiles 2>&1
            $formatWhitespaceExitCode = $LASTEXITCODE
            $formatStyleOutput = dotnet format style AIUsageTracker.sln --verify-no-changes --severity warn --verbosity minimal --include $changedFiles 2>&1
            $formatStyleExitCode = $LASTEXITCODE
            $formatAnalyzerOutput = dotnet format analyzers AIUsageTracker.sln --verify-no-changes --severity warn --verbosity minimal --include $changedFiles 2>&1
            $formatAnalyzerExitCode = $LASTEXITCODE

            if ($formatWhitespaceExitCode -ne 0 -or $formatStyleExitCode -ne 0 -or ($StrictAnalyzers -and $formatAnalyzerExitCode -ne 0)) {
                Write-Host "FAILED" -ForegroundColor Red
                Write-Host $formatWhitespaceOutput
                Write-Host $formatStyleOutput
                if ($StrictAnalyzers) {
                    Write-Host $formatAnalyzerOutput
                }
                $hadFailure = $true
            }
            else {
                if (-not $StrictAnalyzers -and $formatAnalyzerExitCode -ne 0) {
                    Write-Host "Analyzer verification reported warnings on changed files (non-blocking without -StrictAnalyzers)." -ForegroundColor Yellow
                }

                Write-Host "PASSED" -ForegroundColor Green
            }
        }
    }
}
else {
    Write-Host "[2/5] Skipping format/analyzer verification" -ForegroundColor Yellow
}

Write-Host ""

# Step 3: Run tests
if (-not $SkipTests) {
    Write-Host "[3/5] Running unit tests..." -ForegroundColor Yellow
    $testOutput = dotnet test AIUsageTracker.Tests/AIUsageTracker.Tests.csproj --configuration Release --no-build --verbosity minimal 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED" -ForegroundColor Red
        Write-Host $testOutput
        $hadFailure = $true
    } else {
        Write-Host "PASSED" -ForegroundColor Green
    }
} else {
    Write-Host "[3/5] Skipping tests" -ForegroundColor Yellow
}

Write-Host ""

# Step 4: Theme validation
if (-not $SkipThemeValidation) {
    Write-Host "[4/5] Running theme validation..." -ForegroundColor Yellow
    
    $themeContract = powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-theme-contract.ps1 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Theme contract: FAILED" -ForegroundColor Red
        Write-Host $themeContract
        $hadFailure = $true
    } else {
        Write-Host "Theme contract: PASSED" -ForegroundColor Green
    }
    
    $themeSync = powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\sync-theme-catalog.ps1 -Check 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Theme catalog sync: FAILED" -ForegroundColor Red
        Write-Host $themeSync
        $hadFailure = $true
    } else {
        Write-Host "Theme catalog sync: PASSED" -ForegroundColor Green
    }
} else {
    Write-Host "[4/5] Skipping theme validation" -ForegroundColor Yellow
}

Write-Host ""

# Step 5: Check for compile errors and new warnings
Write-Host "[5/5] Checking for build issues..." -ForegroundColor Yellow
$buildCheck = dotnet build AIUsageTracker.sln --configuration Release --verbosity minimal 2>&1
$errors = $buildCheck | Select-String '(:|\s)error\s+(CS|MSB|NETSDK|NU)\d+:'
$nonFatalWarnings = $buildCheck | Select-String "warning NU1900:"

if ($errors) {
    Write-Host "Build errors found:" -ForegroundColor Red
    Write-Host $errors
    $hadFailure = $true
} else {
    if ($nonFatalWarnings) {
        Write-Host "Ignoring non-fatal NuGet audit warnings (NU1900) during local validation." -ForegroundColor Yellow
    }

    Write-Host "PASSED" -ForegroundColor Green
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan

if ($hadFailure) {
    Write-Host "  VALIDATION FAILED" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Fix the errors above before pushing to GitHub." -ForegroundColor Red
    exit 1
} else {
    Write-Host "  ALL CHECKS PASSED" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Ready to push to GitHub." -ForegroundColor Green
    exit 0
}
