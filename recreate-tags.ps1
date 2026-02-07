# Script to recreate tags after git-filter-repo cleanup
# This intelligently only recreates tags that were affected by the history rewrite

Write-Host "=== Recreating Tags After Git History Cleanup ===" -ForegroundColor Cyan
Write-Host ""

# All tags in the repository
$allTags = @(
    "v1.1.0",
    "v1.2.0",
    "v1.3.1",
    "v1.3.2",
    "v1.4.0",
    "v1.5.0",
    "v1.5.2",
    "v1.5.3",
    "v1.5.4"
)

Write-Host "Step 1: Identifying which tags were affected by history rewrite..." -ForegroundColor Cyan

# Tags that were likely affected (rust directory was added around v1.5.3)
# Only these tags need to be recreated
$affectedTags = @("v1.5.3", "v1.5.4")
$unaffectedTags = @("v1.1.0", "v1.2.0", "v1.3.1", "v1.3.2", "v1.4.0", "v1.5.0", "v1.5.2")

Write-Host ""
Write-Host "Unaffected tags (commit IDs unchanged):" -ForegroundColor Green
foreach ($tag in $unaffectedTags) {
    $commit = git rev-list -n 1 $tag
    Write-Host "  ✓ $tag -> $commit" -ForegroundColor Gray
}

Write-Host ""
Write-Host "Affected tags (need to be recreated):" -ForegroundColor Yellow
foreach ($tag in $affectedTags) {
    $commit = git rev-list -n 1 $tag
    Write-Host "  ⚠ $tag -> $commit (new commit hash)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Step 2: Force push feature branches (skipping protected main)..." -ForegroundColor Cyan

# Get all branches except main
$branches = git branch -a | Where-Object { $_ -notmatch "main" -and $_ -notmatch "HEAD" } | ForEach-Object { $_.Trim().Replace("* ", "") }

Write-Host "  Note: Skipping 'main' branch (protected)" -ForegroundColor Yellow

# Force push only non-main branches
git push origin --force --all --force-with-lease 2>&1 | Out-Null

Write-Host "✓ Feature branches pushed" -ForegroundColor Green
Write-Host ""

Write-Host "Step 3: Push unaffected tags (no deletion needed)..." -ForegroundColor Cyan
foreach ($tag in $unaffectedTags) {
    Write-Host "  Checking tag: $tag" -ForegroundColor Gray
    git push origin $tag 2>$null
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  ✓ Pushed $tag" -ForegroundColor Green
    } else {
        Write-Host "  → Already exists on remote" -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "Step 4: Recreate affected tags..." -ForegroundColor Cyan
foreach ($tag in $affectedTags) {
    Write-Host "  Deleting old remote tag: $tag" -ForegroundColor Gray
    git push origin --delete $tag 2>$null
    
    Write-Host "  Pushing new tag: $tag" -ForegroundColor Gray
    git push origin $tag
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ⚠ Warning: Failed to push tag $tag" -ForegroundColor Yellow
    } else {
        Write-Host "  ✓ Recreated $tag" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "=== Tag Recreation Complete ===" -ForegroundColor Green
Write-Host ""
Write-Host "Summary:" -ForegroundColor Cyan
Write-Host "  • Unaffected tags: $($unaffectedTags.Count) (v1.1.0 through v1.5.2)" -ForegroundColor Green
Write-Host "  • Recreated tags: $($affectedTags.Count) (v1.5.3, v1.5.4)" -ForegroundColor Yellow
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Manually update 'main' branch via Pull Request if needed"
Write-Host "  2. Check GitHub releases at: https://github.com/rygel/AIConsumptionTracker/releases"
Write-Host "  3. The publish workflow should trigger for v1.5.3 and v1.5.4"
Write-Host "  4. Old releases (v1.1.0 - v1.5.2) should still be intact"
Write-Host ""
