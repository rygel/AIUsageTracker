# Verifies that Slim and Web theme catalogs stay in sync.

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot

$manifestPath = Join-Path $projectRoot "design/theme-catalog.json"
$appPreferencesPath = Join-Path $projectRoot "AIUsageTracker.Core/Models/AppPreferences.cs"
$slimSettingsPath = Join-Path $projectRoot "AIUsageTracker.UI.Slim/SettingsWindow.xaml.cs"
$slimAppPath = Join-Path $projectRoot "AIUsageTracker.UI.Slim/App.xaml.cs"
$webThemesJsPath = Join-Path $projectRoot "AIUsageTracker.Web/wwwroot/js/theme.js"
$webLayoutPath = Join-Path $projectRoot "AIUsageTracker.Web/Pages/Shared/_Layout.cshtml"
$webThemesCssPath = Join-Path $projectRoot "AIUsageTracker.Web/wwwroot/css/themes.css"

function Read-Json([string]$path)
{
    $content = Read-File $path
    return ConvertFrom-Json -InputObject $content
}

function Read-File([string]$path)
{
    if (-not (Test-Path $path))
    {
        throw "Missing expected file: $path"
    }

    return Get-Content -Path $path -Raw
}

function To-UniqueSet([System.Collections.IEnumerable]$values)
{
    $set = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($value in $values)
    {
        if ([string]::IsNullOrWhiteSpace($value))
        {
            continue
        }

        [void]$set.Add($value.Trim())
    }

    return $set
}

function Get-EnumThemes([string]$content)
{
    $match = [regex]::Match($content, "public enum AppTheme\s*\{(?<body>[\s\S]*?)\}")
    if (-not $match.Success)
    {
        throw "Unable to parse AppTheme enum"
    }

    $themes = @()
    $lines = $match.Groups["body"].Value -split "`r?`n"
    foreach ($line in $lines)
    {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith("//"))
        {
            continue
        }

        $trimmed = $trimmed.TrimEnd(',')
        if ($trimmed -match "^[A-Za-z][A-Za-z0-9]*$")
        {
            $themes += $trimmed
        }
    }

    return $themes
}

function Assert-SetsEqual([string]$name, $expectedSet, $actualSet)
{
    $missing = @($expectedSet | Where-Object { -not $actualSet.Contains($_) } | Sort-Object)
    $extra = @($actualSet | Where-Object { -not $expectedSet.Contains($_) } | Sort-Object)

    if ($missing.Count -eq 0 -and $extra.Count -eq 0)
    {
        Write-Host "PASS: $name" -ForegroundColor Green
        return
    }

    Write-Host "FAIL: $name" -ForegroundColor Red
    if ($missing.Count -gt 0)
    {
        Write-Host "  Missing: $($missing -join ', ')" -ForegroundColor Yellow
    }

    if ($extra.Count -gt 0)
    {
        Write-Host "  Extra:   $($extra -join ', ')" -ForegroundColor Yellow
    }

    $script:hadFailure = $true
}

function Assert-MapsEqual([string]$name, [hashtable]$expectedMap, [hashtable]$actualMap)
{
    $expectedKeys = To-UniqueSet $expectedMap.Keys
    $actualKeys = To-UniqueSet $actualMap.Keys
    Assert-SetsEqual "$name (keys)" $expectedKeys $actualKeys

    foreach ($key in $expectedMap.Keys)
    {
        if (-not $actualMap.ContainsKey($key))
        {
            continue
        }

        if ($expectedMap[$key] -ne $actualMap[$key])
        {
            Write-Host "FAIL: $name value mismatch for '$key'" -ForegroundColor Red
            Write-Host "  Expected: $($expectedMap[$key])" -ForegroundColor Yellow
            Write-Host "  Actual:   $($actualMap[$key])" -ForegroundColor Yellow
            $script:hadFailure = $true
        }
    }
}

$script:hadFailure = $false

$manifest = Read-Json $manifestPath
$appPreferences = Read-File $appPreferencesPath
$slimSettings = Read-File $slimSettingsPath
$slimApp = Read-File $slimAppPath
$webThemesJs = Read-File $webThemesJsPath
$webLayout = Read-File $webLayoutPath
$webThemesCss = Read-File $webThemesCssPath

$manifestThemes = @($manifest.themes)
if ($manifestThemes.Count -eq 0)
{
    throw "Theme manifest has no entries: $manifestPath"
}

$manifestEnumNames = @($manifestThemes | ForEach-Object { $_.enumName })
$manifestWebKeys = @($manifestThemes | ForEach-Object { $_.webKey })
$manifestDisplayNames = @{}
foreach ($theme in $manifestThemes)
{
    $manifestDisplayNames[$theme.enumName] = $theme.displayName
}

$enumThemes = Get-EnumThemes $appPreferences
$enumThemeSet = To-UniqueSet $enumThemes
$manifestEnumSet = To-UniqueSet $manifestEnumNames

$slimOptionMatches = [regex]::Matches($slimSettings, 'Value\s*=\s*AppTheme\.(?<value>[A-Za-z0-9]+)\s*,\s*Label\s*=\s*"(?<label>[^"]+)"')
$slimOptionThemes = $slimOptionMatches | ForEach-Object { $_.Groups["value"].Value }
$slimOptionThemeSet = To-UniqueSet $slimOptionThemes

$slimLabelMap = @{}
foreach ($match in $slimOptionMatches)
{
    $slimLabelMap[$match.Groups["value"].Value] = $match.Groups["label"].Value
}

$slimCaseThemes = [regex]::Matches($slimApp, "case\s+AppTheme\.(?<value>[A-Za-z0-9]+)\s*:") | ForEach-Object { $_.Groups["value"].Value }
$slimCaseThemeSet = To-UniqueSet $slimCaseThemes

$expectedSlimCases = To-UniqueSet ($enumThemes | Where-Object { $_ -ne "Dark" })

$webExpectedKeys = To-UniqueSet $manifestWebKeys

$themesArrayMatch = [regex]::Match($webThemesJs, "themes\s*:\s*\[(?<body>[\s\S]*?)\]")
if (-not $themesArrayMatch.Success)
{
    throw "Unable to parse themes array in theme.js"
}

$webJsKeys = [regex]::Matches($themesArrayMatch.Groups["body"].Value, "'(?<value>[^']+)'") | ForEach-Object { $_.Groups["value"].Value }
$webJsKeySet = To-UniqueSet $webJsKeys

$webLayoutOptionMatches = [regex]::Matches($webLayout, '<option\s+value="(?<value>[^"]+)">(?<label>[^<]+)</option>')
$webLayoutKeys = $webLayoutOptionMatches | ForEach-Object { $_.Groups["value"].Value }
$webLayoutKeySet = To-UniqueSet $webLayoutKeys

$manifestLabelByWebKey = @{}
foreach ($theme in $manifestThemes)
{
    $manifestLabelByWebKey[$theme.webKey] = $theme.displayName
}

$webLayoutLabelMap = @{}
foreach ($match in $webLayoutOptionMatches)
{
    $webLayoutLabelMap[$match.Groups["value"].Value] = $match.Groups["label"].Value
}

$webCssKeys = [regex]::Matches($webThemesCss, '\[data-theme="(?<value>[^"]+)"\]') | ForEach-Object { $_.Groups["value"].Value }
$webCssKeySet = To-UniqueSet $webCssKeys

Write-Host "Verifying theme contracts..."
Assert-SetsEqual "AppTheme enum matches theme manifest" $manifestEnumSet $enumThemeSet
Assert-SetsEqual "Slim settings options match theme manifest" $manifestEnumSet $slimOptionThemeSet
Assert-SetsEqual "Slim ApplyTheme switch cases cover enum (Dark can use default)" $expectedSlimCases $slimCaseThemeSet
Assert-MapsEqual "Slim settings labels match manifest" $manifestDisplayNames $slimLabelMap
Assert-SetsEqual "Web JS theme list matches manifest" $webExpectedKeys $webJsKeySet
Assert-SetsEqual "Web layout dropdown matches manifest" $webExpectedKeys $webLayoutKeySet
Assert-MapsEqual "Web layout labels match manifest" $manifestLabelByWebKey $webLayoutLabelMap
Assert-SetsEqual "Web CSS selectors match manifest" $webExpectedKeys $webCssKeySet

if ($script:hadFailure)
{
    Write-Host "Theme contract verification failed." -ForegroundColor Red
    exit 1
}

Write-Host "Theme contract verification succeeded." -ForegroundColor Green
