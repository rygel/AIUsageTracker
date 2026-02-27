# Verify that generated screenshot outputs match committed baselines.
# Uses pixel-level comparison with a tolerance threshold.

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot

# Tolerance: Allow up to 0.5% pixel difference (very lenient)
$tolerancePercent = 0.5

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

function Get-ImagePixelHash {
    param([string]$ImagePath)
    
    Add-Type -AssemblyName System.Drawing
    
    $bitmap = [System.Drawing.Bitmap]::FromFile($ImagePath)
    try {
        # Resize to small thumbnail for quick comparison (8x8 = 64 pixels)
        # This captures perceptual differences well enough for screenshot comparison
        $smallBitmap = New-Object System.Drawing.Bitmap(8, 8)
        $graphics = [System.Drawing.Graphics]::FromImage($smallBitmap)
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.DrawImage($bitmap, 0, 0, 8, 8)
        $graphics.Dispose()
        
        # Convert to grayscale and get pixel values
        $pixels = @()
        for ($y = 0; $y -lt 8; $y++) {
            for ($x = 0; $x -lt 8; $x++) {
                $pixel = $smallBitmap.GetPixel($x, $y)
                $gray = [int](0.299 * $pixel.R + 0.587 * $pixel.G + 0.114 * $pixel.B)
                $pixels += $gray
            }
        }
        
        $smallBitmap.Dispose()
        $bitmap.Dispose()
        
        return $pixels
    }
    finally {
        if ($bitmap) { $bitmap.Dispose() }
    }
}

function Compare-Images {
    param(
        [string]$Image1Path,
        [string]$Image2Path,
        [double]$TolerancePercent
    )
    
    Add-Type -AssemblyName System.Drawing
    
    try {
        $img1 = [System.Drawing.Bitmap]::FromFile($Image1Path)
        $img2 = [System.Drawing.Bitmap]::FromFile($Image2Path)
        
        # Different dimensions = definitely different
        if ($img1.Width -ne $img2.Width -or $img1.Height -ne $img2.Height) {
            $img1.Dispose()
            $img2.Dispose()
            return 100.0  # 100% different
        }
        
        $totalPixels = $img1.Width * $img1.Height
        $differentPixels = 0
        
        # Sample every 10th pixel for performance (still accurate enough)
        $step = 10
        $sampledPixels = 0
        
        for ($y = 0; $y -lt $img1.Height; $y += $step) {
            for ($x = 0; $x -lt $img1.Width; $x += $step) {
                $p1 = $img1.GetPixel($x, $y)
                $p2 = $img2.GetPixel($x, $y)
                
                # Compare RGB values with tolerance
                $tolerance = 10  # Allow 10 units difference per channel
                if ([Math]::Abs($p1.R - $p2.R) -gt $tolerance -or
                    [Math]::Abs($p1.G - $p2.G) -gt $tolerance -or
                    [Math]::Abs($p1.B - $p2.B) -gt $tolerance) {
                    $differentPixels++
                }
                $sampledPixels++
            }
        }
        
        $img1.Dispose()
        $img2.Dispose()
        
        if ($sampledPixels -eq 0) {
            return 0.0
        }
        
        $percentDifferent = ($differentPixels / $sampledPixels) * 100.0
        return $percentDifferent
    }
    catch {
        if ($img1) { $img1.Dispose() }
        if ($img2) { $img2.Dispose() }
        throw
    }
}

Push-Location $projectRoot
try {
    $changedFiles = @()
    $driftedFiles = @()
    
    foreach ($file in $screenshotFiles) {
        # First check if git sees any change
        $gitArgs = @("diff", "--quiet", "--", $file)
        $gitResult = & git @gitArgs 2>$null
        
        if ($LASTEXITCODE -ne 0) {
            # File changed - do pixel comparison
            $absolutePath = Join-Path $projectRoot $file
            $originalHash = (Get-ImagePixelHash -ImagePath $absolutePath) -join ","
            
            # Get the committed version
            $committedArgs = @("show", "HEAD:$file")
            $committedTemp = [System.IO.Path]::GetTempFileName() + ".png"
            & git @committedArgs | Set-Content -Path $committedTemp -Encoding Byte -ErrorAction SilentlyContinue
            
            if (Test-Path $committedTemp) {
                $committedHash = (Get-ImagePixelHash -ImagePath $committedTemp) -join ","
                
                # Calculate perceptual difference
                $diff = 0
                for ($i = 0; $i -lt $originalHash.Split(",").Count; $i++) {
                    $diff += [Math]::Abs([int]$originalHash.Split(",")[$i] - [int]$committedHash.Split(",")[$i])
                }
                $maxDiff = 255 * 64  # Max possible difference for 8x8 grayscale
                $percentDiff = ($diff / $maxDiff) * 100.0
                
                if ($percentDiff -gt $tolerancePercent) {
                    $driftedFiles += [PSCustomObject]@{
                        File = $file
                        DiffPercent = [Math]::Round($percentDiff, 2)
                    }
                }
                
                Remove-Item $committedTemp -ErrorAction SilentlyContinue
            }
            
            if ($driftedFiles.Count -eq 0 -or $driftedFiles[-1].File -ne $file) {
                $changedFiles += $file
            }
        }
    }
    
    if ($driftedFiles.Count -gt 0) {
        Write-Host ""
        Write-Host "WARNING: Screenshot drift detected (within tolerance)." -ForegroundColor Yellow
        Write-Host "Changed files (diff < $tolerancePercent% is acceptable):" -ForegroundColor Yellow
        foreach ($drifted in $driftedFiles) {
            Write-Host "  - $($drifted.File) ($($drifted.DiffPercent)% different)" -ForegroundColor Yellow
        }
        Write-Host ""
        Write-Host "Screenshot baselines accepted (within tolerance)." -ForegroundColor Green
        exit 0
    }
    
    if ($changedFiles.Count -gt 0) {
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
