# Fails PR validation if theme-related files changed without updating design/theme-catalog.json.

$ErrorActionPreference = "Stop"

if ($env:GITHUB_EVENT_NAME -ne "pull_request")
{
    Write-Host "Skipping theme-manifest change check (event: $($env:GITHUB_EVENT_NAME))." -ForegroundColor Yellow
    exit 0
}

$baseRef = $env:GITHUB_BASE_REF
if ([string]::IsNullOrWhiteSpace($baseRef))
{
    Write-Host "Skipping theme-manifest change check (GITHUB_BASE_REF missing)." -ForegroundColor Yellow
    exit 0
}

git fetch origin $baseRef --depth=1 | Out-Null
$changedFiles = @(git diff --name-only "origin/$baseRef...HEAD")

if ($changedFiles.Count -eq 0)
{
    Write-Host "No changed files detected for theme-manifest check." -ForegroundColor Green
    exit 0
}

$manifestPath = "design/theme-catalog.json"
$isManifestChanged = $changedFiles -contains $manifestPath

$themeRelatedRegex =
    '^(AIUsageTracker\.UI\.Slim/App\.xaml\.cs|' +
    'AIUsageTracker\.Web/wwwroot/js/theme\.js|' +
    'AIUsageTracker\.Web\.Tests/ScreenshotTests\.cs|' +
    'scripts/verify-theme-contract\.ps1|' +
    'scripts/sync-theme-catalog\.ps1)$'

$themeRelatedChanged = @($changedFiles | Where-Object { $_ -match $themeRelatedRegex })

if ($themeRelatedChanged.Count -gt 0 -and -not $isManifestChanged)
{
    Write-Host "FAIL: Theme-related files changed without updating $manifestPath" -ForegroundColor Red
    Write-Host "Changed theme-related files:" -ForegroundColor Yellow
    $themeRelatedChanged | Sort-Object | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
    Write-Host "Update design/theme-catalog.json (or include it in this PR) to keep the catalog authoritative." -ForegroundColor Yellow
    exit 1
}

Write-Host "Theme-manifest change check passed." -ForegroundColor Green
