# Verifies that Slim and Web theme catalogs stay in sync.

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot

$appPreferencesPath = Join-Path $projectRoot "AIUsageTracker.Core/Models/AppPreferences.cs"
$slimSettingsPath = Join-Path $projectRoot "AIUsageTracker.UI.Slim/SettingsWindow.xaml.cs"
$slimAppPath = Join-Path $projectRoot "AIUsageTracker.UI.Slim/App.xaml.cs"
$webThemesJsPath = Join-Path $projectRoot "AIUsageTracker.Web/wwwroot/js/theme.js"
$webLayoutPath = Join-Path $projectRoot "AIUsageTracker.Web/Pages/Shared/_Layout.cshtml"
$webThemesCssPath = Join-Path $projectRoot "AIUsageTracker.Web/wwwroot/css/themes.css"

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

function Convert-EnumToWebThemeKey([string]$enumName)
{
    $withDashes = [regex]::Replace($enumName, "([a-z0-9])([A-Z])", '$1-$2')
    return $withDashes.ToLowerInvariant()
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

$script:hadFailure = $false

$appPreferences = Read-File $appPreferencesPath
$slimSettings = Read-File $slimSettingsPath
$slimApp = Read-File $slimAppPath
$webThemesJs = Read-File $webThemesJsPath
$webLayout = Read-File $webLayoutPath
$webThemesCss = Read-File $webThemesCssPath

$enumThemes = Get-EnumThemes $appPreferences
$enumThemeSet = To-UniqueSet $enumThemes

$slimOptionThemes = [regex]::Matches($slimSettings, "Value\s*=\s*AppTheme\.(?<value>[A-Za-z0-9]+)") | ForEach-Object { $_.Groups["value"].Value }
$slimOptionThemeSet = To-UniqueSet $slimOptionThemes

$slimCaseThemes = [regex]::Matches($slimApp, "case\s+AppTheme\.(?<value>[A-Za-z0-9]+)\s*:") | ForEach-Object { $_.Groups["value"].Value }
$slimCaseThemeSet = To-UniqueSet $slimCaseThemes

$expectedSlimCases = To-UniqueSet ($enumThemes | Where-Object { $_ -ne "Dark" })

$webExpectedKeys = To-UniqueSet ($enumThemes | ForEach-Object { Convert-EnumToWebThemeKey $_ })

$themesArrayMatch = [regex]::Match($webThemesJs, "themes\s*:\s*\[(?<body>[\s\S]*?)\]")
if (-not $themesArrayMatch.Success)
{
    throw "Unable to parse themes array in theme.js"
}

$webJsKeys = [regex]::Matches($themesArrayMatch.Groups["body"].Value, "'(?<value>[^']+)'") | ForEach-Object { $_.Groups["value"].Value }
$webJsKeySet = To-UniqueSet $webJsKeys

$webLayoutKeys = [regex]::Matches($webLayout, '<option\s+value="(?<value>[^"]+)"') | ForEach-Object { $_.Groups["value"].Value }
$webLayoutKeySet = To-UniqueSet $webLayoutKeys

$webCssKeys = [regex]::Matches($webThemesCss, '\[data-theme="(?<value>[^"]+)"\]') | ForEach-Object { $_.Groups["value"].Value }
$webCssKeySet = To-UniqueSet $webCssKeys

Write-Host "Verifying theme contracts..."
Assert-SetsEqual "Slim settings options match AppTheme enum" $enumThemeSet $slimOptionThemeSet
Assert-SetsEqual "Slim ApplyTheme switch cases cover enum (Dark can use default)" $expectedSlimCases $slimCaseThemeSet
Assert-SetsEqual "Web JS theme list matches AppTheme enum" $webExpectedKeys $webJsKeySet
Assert-SetsEqual "Web layout dropdown matches AppTheme enum" $webExpectedKeys $webLayoutKeySet
Assert-SetsEqual "Web CSS selectors match AppTheme enum" $webExpectedKeys $webCssKeySet

if ($script:hadFailure)
{
    Write-Host "Theme contract verification failed." -ForegroundColor Red
    exit 1
}

Write-Host "Theme contract verification succeeded." -ForegroundColor Green
