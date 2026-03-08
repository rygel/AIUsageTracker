#!/usr/bin/env pwsh
# Pre-push validation script - Run this before pushing to catch CI/CD failures locally
# Mimics the key checks from GitHub workflows

param(
    [switch]$SkipTests,
    [switch]$SkipThemeValidation
)

$ErrorActionPreference = "Stop"
$hadFailure = $false

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  PRE-PUSH VALIDATION" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Build
Write-Host "[1/4] Building solution..." -ForegroundColor Yellow
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

# Step 2: Run tests
if (-not $SkipTests) {
    Write-Host "[2/4] Running unit tests..." -ForegroundColor Yellow
    $testOutput = dotnet test AIUsageTracker.Tests/AIUsageTracker.Tests.csproj --configuration Release --no-build --verbosity minimal 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED" -ForegroundColor Red
        Write-Host $testOutput
        $hadFailure = $true
    } else {
        Write-Host "PASSED" -ForegroundColor Green
    }
} else {
    Write-Host "[2/4] Skipping tests" -ForegroundColor Yellow
}

Write-Host ""

# Step 3: Theme validation
if (-not $SkipThemeValidation) {
    Write-Host "[3/4] Running theme validation..." -ForegroundColor Yellow
    
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
    Write-Host "[3/4] Skipping theme validation" -ForegroundColor Yellow
}

Write-Host ""

# Step 4: Check for compile errors and new warnings
Write-Host "[4/4] Checking for build issues..." -ForegroundColor Yellow
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
