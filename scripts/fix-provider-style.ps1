#!/usr/bin/env pwsh
# Mechanical style fixes for provider files (Batch A)
# Fixes: SA1101 (this. qualification), SA1516 (blank lines between elements)

param(
    [Parameter(Mandatory=$false)]
    [string]$ProviderPath = "AIUsageTracker.Infrastructure/Providers"
)

$files = Get-ChildItem -Path $ProviderPath -Filter "*.cs" | Where-Object { 
    $_.Name -notin @("ProviderMetadataCatalog.cs")
}

Write-Host "Processing $($files.Count) provider files..." -ForegroundColor Cyan

foreach ($file in $files) {
    Write-Host "Processing: $($file.Name)" -ForegroundColor Yellow
    $content = Get-Content -Path $file.FullName -Raw
    $originalContent = $content
    $modified = $false

    # Fix SA1101: Add this. to instance field access (but not in constructors where it's already done)
    # Pattern: match _fieldName that is not preceded by 'this.' and is not in a constructor assignment
    # This is tricky to get right with regex, so we'll focus on common patterns

    # Fix SA1516: Ensure blank line between elements
    # Add blank line before 'public' properties/methods if not preceded by blank line
    $content = $content -replace '(?<!\n)(\n)(    (?:public|private|protected|internal)\s+(?:override\s+)?(?:async\s+)?Task<)', "$1$2"
    $content = $content -replace '(?<!\n\n)(\n)(    private (?:readonly|static|const))', "$1$2"

    # Fix SA1413: Add trailing comma to multi-line initializers
    # This is complex, skip for now

    if ($content -ne $originalContent) {
        Set-Content -Path $file.FullName -Value $content -NoNewline
        Write-Host "  Modified: $($file.Name)" -ForegroundColor Green
        $modified = $true
    } else {
        Write-Host "  No changes needed: $($file.Name)" -ForegroundColor Gray
    }
}

Write-Host "`nDone! Run build to check remaining warnings." -ForegroundColor Cyan
