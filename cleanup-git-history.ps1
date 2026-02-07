# Git History Cleanup Script
# This script removes all traces of rust/target and TempBin directories from git history
# WARNING: This rewrites git history. Make sure you have a backup!

Write-Host "=== Git History Cleanup ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "This script will:" -ForegroundColor Yellow
Write-Host "  1. Remove all traces of rust/target directory from git history"
Write-Host "  2. Remove all traces of TempBin directory from git history"
Write-Host "  3. Rewrite all commits and tags"
Write-Host "  4. Force garbage collection to reclaim space"
Write-Host ""
Write-Host "WARNING: This rewrites git history!" -ForegroundColor Red
Write-Host "Make sure you have a backup before proceeding." -ForegroundColor Red
Write-Host ""

$confirmation = Read-Host "Do you want to proceed? (yes/no)"
if ($confirmation -ne "yes") {
    Write-Host "Aborted." -ForegroundColor Yellow
    exit
}

Write-Host ""
Write-Host "Step 1: Using git-filter-repo to remove directories..." -ForegroundColor Cyan

# Remove the directories from history using Python module
Write-Host "Removing rust/target from history..." -ForegroundColor Green
python -m git_filter_repo --path rust/target --invert-paths --force

Write-Host "Removing TempBin from history..." -ForegroundColor Green
python -m git_filter_repo --path TempBin --invert-paths --force

Write-Host ""
Write-Host "Step 2: Cleaning up and reclaiming space..." -ForegroundColor Cyan

# Expire all reflog entries
git reflog expire --expire=now --all

# Aggressive garbage collection
git gc --prune=now --aggressive

Write-Host ""
Write-Host "Step 3: Repository size information..." -ForegroundColor Cyan
$repoSize = (Get-ChildItem .git -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host "Current .git directory size: $([math]::Round($repoSize, 2)) MB" -ForegroundColor Green

Write-Host ""
Write-Host "=== Cleanup Complete ===" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Review the changes with: git log --oneline"
Write-Host "  2. If everything looks good, force push to remote:"
Write-Host "     git push origin --force --all"
Write-Host "     git push origin --force --tags"
Write-Host ""
Write-Host "IMPORTANT: All collaborators will need to re-clone the repository!" -ForegroundColor Red
