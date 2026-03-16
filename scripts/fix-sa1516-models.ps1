#!/usr/bin/env pwsh
# Fix SA1516 blank lines between elements in Core model files (Batch B)

param(
    [Parameter(Mandatory=$false)]
    [string]$ModelsPath = "AIUsageTracker.Core/Models"
)

$files = Get-ChildItem -Path $ModelsPath -Filter "*.cs" | Where-Object { 
    $_.Name -in @(
        "ProviderDefinition.cs",
        "ProviderUsage.cs",
        "ProviderUsageDetail.cs",
        "UsageComparison.cs",
        "BudgetStatus.cs",
        "ProviderReliabilitySnapshot.cs",
        "UsageAnomalySnapshot.cs",
        "BurnRateForecast.cs"
    )
}

Write-Host "Processing $($files.Count) model files for SA1516..." -ForegroundColor Cyan

foreach ($file in $files) {
    Write-Host "Processing: $($file.Name)" -ForegroundColor Yellow
    $content = Get-Content -Path $file.FullName -Raw
    $originalContent = $content
    
    # Add blank line between consecutive property declarations
    # Pattern: match property declaration followed by another property declaration
    $content = $content -replace '(?<=\s*{\s*get;\s*set;\s*}\s*)(\r?\n)(?=\s*public\s+\w+\s+\w+\s*{\s*get;\s*set;)', "`n`n"
    
    # Add blank line between auto-properties with initializers
    $content = $content -replace '(?<=}=\s*[^;]+;\s*)(\r?\n)(?=\s*public\s+\w+\s+\w+\s*{\s*get;\s*set;)', "`n`n"
    
    if ($content -ne $originalContent) {
        Set-Content -Path $file.FullName -Value $content -NoNewline
        Write-Host "  Fixed: $($file.Name)" -ForegroundColor Green
    } else {
        Write-Host "  No changes: $($file.Name)" -ForegroundColor Gray
    }
}

Write-Host "`nDone! Run build to check remaining warnings." -ForegroundColor Cyan
