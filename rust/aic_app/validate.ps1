# Validate HTML and JavaScript files for Tauri app
param(
    [switch]$Fix
)

Write-Host "Validating HTML/JavaScript files..." -ForegroundColor Cyan
Write-Host ""

$errors = 0
$warnings = 0

Get-ChildItem -Path "src" -Filter "*.html" | ForEach-Object {
    $file = $_.FullName
    $content = Get-Content $file -Raw
    $lines = $content -split "`n"
    
    Write-Host "Checking $($_.Name)..." -ForegroundColor Yellow
    
    # Check for unclosed script tags (basic check)
    $scriptOpens = ([regex]::Matches($content, '<script[^>]*>')).Count
    $scriptCloses = ([regex]::Matches($content, '</script>')).Count
    
    if ($scriptOpens -ne $scriptCloses) {
        Write-Host "  ERROR: Mismatched script tags ($scriptOpens opening, $scriptCloses closing)" -ForegroundColor Red
        $errors++
    }
    
    # Check for unclosed div tags (basic check)
    $divOpens = ([regex]::Matches($content, '<div[^/>]*[^/]>')).Count
    $divCloses = ([regex]::Matches($content, '</div>')).Count
    
    if ($divOpens -ne $divCloses) {
        Write-Host "  WARNING: Possible mismatched div tags ($divOpens opening, $divCloses closing)" -ForegroundColor Yellow
        $warnings++
    }
    
    # Check for JavaScript syntax errors
    $scriptBlocks = [regex]::Matches($content, '(?s)<script[^>]*>(.*?)</script>')
    $blockNum = 0
    
    foreach ($block in $scriptBlocks) {
        $blockNum++
        $js = $block.Groups[1].Value
        
        # Count braces
        $openBraces = ($js -replace '[^{]', '').Length
        $closeBraces = ($js -replace '[^}]', '').Length
        
        if ($openBraces -ne $closeBraces) {
            Write-Host "  ERROR: Script block $blockNum - Mismatched braces ($openBraces opening, $closeBraces closing)" -ForegroundColor Red
            $errors++
            
            # Show context
            $lineNum = ($content.Substring(0, $block.Index) -split "`n").Count
            Write-Host "    at line ~$lineNum" -ForegroundColor Gray
        }
        
        # Count parentheses
        $openParens = ($js -replace '[^(]', '').Length
        $closeParens = ($js -replace '[^)]', '').Length
        
        if ($openParens -ne $closeParens) {
            Write-Host "  ERROR: Script block $blockNum - Mismatched parentheses ($openParens opening, $closeParens closing)" -ForegroundColor Red
            $errors++
            
            $lineNum = ($content.Substring(0, $block.Index) -split "`n").Count
            Write-Host "    at line ~$lineNum" -ForegroundColor Gray
        }
        
        # Check for common Tauri issues
        if ($js -match 'window\.__TAURI__[^?]' -and $js -notmatch 'window\.__TAURI__\?\.') {
            Write-Host "  WARNING: Possible unsafe __TAURI__ access without optional chaining (?.)" -ForegroundColor Yellow
            $warnings++
        }
    }
    
    # Check for inline event handlers (Tauri security best practice)
    $inlineEvents = [regex]::Matches($content, '\son\w+="[^"]*"')
    if ($inlineEvents.Count -gt 0) {
        Write-Host "  INFO: Found $($inlineEvents.Count) inline event handlers (consider moving to JavaScript)" -ForegroundColor DarkGray
    }
}

Write-Host ""
Write-Host "Validation Results:" -ForegroundColor Cyan
Write-Host "  Errors: $errors" -ForegroundColor $(if ($errors -gt 0) { "Red" } else { "Green" })
Write-Host "  Warnings: $warnings" -ForegroundColor $(if ($warnings -gt 0) { "Yellow" } else { "Green" })

if ($errors -gt 0) {
    Write-Host ""
    Write-Host "Build blocked due to errors!" -ForegroundColor Red
    exit 1
} else {
    Write-Host ""
    Write-Host "Validation passed!" -ForegroundColor Green
    exit 0
}
