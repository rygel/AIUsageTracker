# Runs a headless Slim UI smoke test for all themes.

param(
    [string]$Configuration = "Debug",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$enumPath = Join-Path $projectRoot "AIUsageTracker.Core/Models/AppPreferences.cs"
$exePath = Join-Path $projectRoot "AIUsageTracker.UI.Slim/bin/$Configuration/net8.0-windows10.0.17763.0/AIUsageTracker.exe"

if (-not (Test-Path $enumPath))
{
    Write-Host "ERROR: Could not find theme enum file at $enumPath" -ForegroundColor Red
    exit 1
}

if (-not $SkipBuild)
{
    Write-Host "Building Slim UI ($Configuration)..." -ForegroundColor Cyan
    dotnet build (Join-Path $projectRoot "AIUsageTracker.UI.Slim/AIUsageTracker.UI.Slim.csproj") --configuration $Configuration | Out-Host
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }
}

if (-not (Test-Path $exePath))
{
    Write-Host "ERROR: Slim UI executable not found at $exePath" -ForegroundColor Red
    exit 1
}

$enumContent = Get-Content -Path $enumPath -Raw
$enumMatch = [regex]::Match($enumContent, "public enum AppTheme\s*\{(?<body>[\s\S]*?)\}")
if (-not $enumMatch.Success)
{
    Write-Host "ERROR: Could not parse AppTheme enum." -ForegroundColor Red
    exit 1
}

$themes = @()
foreach ($line in ($enumMatch.Groups["body"].Value -split "`r?`n"))
{
    $trimmed = $line.Trim().TrimEnd(',')
    if ($trimmed -match "^[A-Za-z][A-Za-z0-9]*$")
    {
        $themes += $trimmed
    }
}

if ($themes.Count -eq 0)
{
    Write-Host "ERROR: No themes found in AppTheme enum." -ForegroundColor Red
    exit 1
}

$outputDir = Join-Path ([System.IO.Path]::GetTempPath()) ("AIUsageTracker/theme-smoke-" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

Write-Host "Running Slim theme smoke test for $($themes.Count) themes..." -ForegroundColor Cyan

$existingProcesses = Get-Process -Name "AIUsageTracker" -ErrorAction SilentlyContinue
if ($existingProcesses)
{
    Write-Host "Stopping existing AIUsageTracker processes before smoke run..." -ForegroundColor Yellow
    $existingProcesses | Stop-Process -Force
    Start-Sleep -Seconds 2
}

foreach ($theme in $themes)
{
    Write-Host "  -> $theme" -ForegroundColor Gray

    $proc = Start-Process -FilePath $exePath -ArgumentList @("--test", "--screenshot", "--theme-smoke", "--theme", $theme, "--output-dir", $outputDir) -PassThru -WindowStyle Hidden -Wait
    $exitCode = $proc.ExitCode

    if ($exitCode -ne 0)
    {
        Write-Host "ERROR: Theme '$theme' failed with exit code $exitCode." -ForegroundColor Red
        exit 1
    }

    $expectedFile = Join-Path $outputDir ("theme_smoke_" + $theme.ToLowerInvariant() + ".png")
    if (-not (Test-Path $expectedFile))
    {
        Write-Host "ERROR: Theme '$theme' did not produce expected screenshot '$expectedFile'." -ForegroundColor Red
        exit 1
    }
}

Write-Host "Slim theme smoke test passed for all themes." -ForegroundColor Green
