# Syncs Web theme list/options from design/theme-catalog.json

param(
    [switch]$Check
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $projectRoot "design/theme-catalog.json"
$themeJsPath = Join-Path $projectRoot "AIUsageTracker.Web/wwwroot/js/theme.js"
$layoutPath = Join-Path $projectRoot "AIUsageTracker.Web/Pages/Shared/_Layout.cshtml"

function Read-File([string]$path)
{
    if (-not (Test-Path $path))
    {
        throw "Missing expected file: $path"
    }

    return Get-Content -Path $path -Raw
}

function Replace-BetweenMarkers(
    [string]$content,
    [string]$startMarker,
    [string]$endMarker,
    [string]$replacement,
    [string]$label)
{
    $startIndex = $content.IndexOf($startMarker, [System.StringComparison]::Ordinal)
    if ($startIndex -lt 0)
    {
        throw "Start marker not found for ${label}: $startMarker"
    }

    $endIndex = $content.IndexOf($endMarker, $startIndex, [System.StringComparison]::Ordinal)
    if ($endIndex -lt 0)
    {
        throw "End marker not found for ${label}: $endMarker"
    }

    $replacementBlock = $startMarker + "`r`n" + $replacement + "`r`n" + $endMarker
    $prefix = $content.Substring(0, $startIndex)
    $suffix = $content.Substring($endIndex + $endMarker.Length)
    return $prefix + $replacementBlock + $suffix
}

$manifest = Get-Content -Path $manifestPath -Raw | ConvertFrom-Json
$themes = @($manifest.themes)
if ($themes.Count -eq 0)
{
    throw "Theme catalog has no entries: $manifestPath"
}

$jsLines = @("    themes: [")
for ($i = 0; $i -lt $themes.Count; $i++)
{
    $suffix = if ($i -lt $themes.Count - 1) { "," } else { "" }
    $jsLines += "        '$($themes[$i].webKey)'$suffix"
}
$jsLines += "    ],"
$jsReplacement = ($jsLines -join "`r`n")

$optionLines = @()
foreach ($theme in $themes)
{
    $optionLines += ('                        <option value="{0}">{1}</option>' -f $theme.webKey, $theme.displayName)
}
$optionsReplacement = ($optionLines -join "`r`n")

$themeJsContent = Read-File $themeJsPath
$layoutContent = Read-File $layoutPath

$newThemeJsContent = Replace-BetweenMarkers -content $themeJsContent -startMarker "    // GENERATED-THEME-LIST-START" -endMarker "    // GENERATED-THEME-LIST-END" -replacement $jsReplacement -label "theme.js"

$newLayoutContent = Replace-BetweenMarkers -content $layoutContent -startMarker "                        @* GENERATED-THEME-OPTIONS-START *@" -endMarker "                        @* GENERATED-THEME-OPTIONS-END *@" -replacement $optionsReplacement -label "_Layout.cshtml"

if ($Check)
{
    $hasDiff = $false
    if ($newThemeJsContent -ne $themeJsContent)
    {
        Write-Host "FAIL: AIUsageTracker.Web/wwwroot/js/theme.js is out of sync with design/theme-catalog.json" -ForegroundColor Red
        $hasDiff = $true
    }

    if ($newLayoutContent -ne $layoutContent)
    {
        Write-Host "FAIL: AIUsageTracker.Web/Pages/Shared/_Layout.cshtml is out of sync with design/theme-catalog.json" -ForegroundColor Red
        $hasDiff = $true
    }

    if ($hasDiff)
    {
        Write-Host "Run: pwsh ./scripts/sync-theme-catalog.ps1" -ForegroundColor Yellow
        exit 1
    }

    Write-Host "Theme catalog sync check passed." -ForegroundColor Green
    exit 0
}

Set-Content -Path $themeJsPath -Value $newThemeJsContent -NoNewline
Set-Content -Path $layoutPath -Value $newLayoutContent -NoNewline

Write-Host "Synchronized Web theme files from design/theme-catalog.json" -ForegroundColor Green
