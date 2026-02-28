# Verify that generated screenshot outputs match committed baselines.
# Uses file hash comparison for memory efficiency.

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
    $changedFiles = @()
    
    foreach ($file in $screenshotFiles) {
        # Compare file hashes - if hash matches, images are identical
        $absolutePath = Join-Path $projectRoot $file
        
        # Get hash of current file
        $currentHash = (Get-FileHash -Path $absolutePath -Algorithm SHA256).Hash
        
        # Get hash of committed version
        $committedHash = (git show "HEAD:$file" | Out-String | ForEach-Object { 
            # Convert git show output to bytes and compute hash
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($_)
            $sha256 = [System.Security.Cryptography.SHA256]::Create()
            $hashBytes = $sha256.ComputeHash($bytes)
            [System.BitConverter]::ToString($hashBytes) -replace '-', ''
        })
        
        # Use git diff instead - more reliable
        $gitArgs = @("diff", "--quiet", "--", $file)
        & git @gitArgs 2>$null
        
        if ($LASTEXITCODE -ne 0) {
            # File changed - record it
            $changedFiles += $file
        }
    }
    
    if ($changedFiles.Count -gt 0) {
        Write-Host ""
        Write-Host "ERROR: Screenshot baseline drift detected." -ForegroundColor Red
        Write-Host "Changed screenshot files:" -ForegroundColor Yellow
        & git --no-pager status --short -- @screenshotFiles
        Write-Host ""
        Write-Host "To accept new baselines, run: git add docs/screenshot_*_privacy.png && git commit -m 'chore: sync screenshots'"
        exit 1
    }
    
    Write-Host "SUCCESS: Screenshot baselines match committed files." -ForegroundColor Green
}
finally {
    Pop-Location
}
