# Verify that generated screenshot outputs match committed baselines.

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot

$screenshotFiles = @(
    "docs/screenshot_dashboard_privacy.png",
    "docs/screenshot_settings_privacy.png",
    "docs/screenshot_settings_providers_privacy.png",
    "docs/screenshot_settings_layout_privacy.png",
    "docs/screenshot_settings_history_privacy.png",
    "docs/screenshot_settings_agent_privacy.png",
    "docs/screenshot_info_privacy.png",
    "docs/screenshot_context_menu_privacy.png"
)

$missingFiles = @()
foreach ($relativePath in $screenshotFiles) {
    $absolutePath = Join-Path $projectRoot $relativePath
    if (-not (Test-Path $absolutePath)) {
        $missingFiles += $relativePath
    }
}

if ($missingFiles.Count -gt 0) {
    Write-Host "ERROR: Missing expected screenshot files:" -ForegroundColor Red
    foreach ($missing in $missingFiles) {
        Write-Host "  - $missing" -ForegroundColor Red
    }
    exit 1
}

Push-Location $projectRoot
try {
    $gitArgs = @("--no-pager", "diff", "--exit-code", "--")
    $gitArgs += $screenshotFiles
    & git @gitArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "ERROR: Screenshot baseline drift detected." -ForegroundColor Red
        Write-Host "Changed screenshot files:" -ForegroundColor Yellow
        & git --no-pager status --short -- @screenshotFiles
        exit 1
    }

    Write-Host "SUCCESS: Screenshot baselines match committed files." -ForegroundColor Green
}
finally {
    Pop-Location
}
