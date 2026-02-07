# Manual Git History Cleanup Instructions

This document provides manual commands to remove `rust/target` and `TempBin` directories from git history.

## Prerequisites

Install `git-filter-repo` (recommended method):

```powershell
pip install git-filter-repo
```

Alternatively, you can use the older `git filter-branch` method (slower, not recommended).

## Method 1: Using git-filter-repo (Recommended)

### Step 1: Backup your repository

```powershell
# Create a backup
cd ..
Copy-Item -Path "opencode-tracker" -Destination "opencode-tracker-backup" -Recurse
cd opencode-tracker
```

### Step 2: Remove directories from history

```powershell
# Remove rust/target directory
git filter-repo --path rust/target --invert-paths --force

# Remove TempBin directory
git filter-repo --path TempBin --invert-paths --force
```

### Step 3: Clean up and reclaim space

```powershell
# Expire reflog
git reflog expire --expire=now --all

# Aggressive garbage collection
git gc --prune=now --aggressive
```

### Step 4: Check repository size

```powershell
# Check .git directory size
Get-ChildItem .git -Recurse | Measure-Object -Property Length -Sum
```

### Step 5: Force push to remote (DESTRUCTIVE!)

```powershell
# Push all branches
git push origin --force --all

# Push all tags
git push origin --force --tags
```

## Method 2: Using git filter-branch (Legacy, Slower)

### Step 1: Backup your repository

```powershell
cd ..
Copy-Item -Path "opencode-tracker" -Destination "opencode-tracker-backup" -Recurse
cd opencode-tracker
```

### Step 2: Remove directories from history

```powershell
# Remove rust/target
git filter-branch --force --index-filter `
  "git rm -r --cached --ignore-unmatch rust/target" `
  --prune-empty --tag-name-filter cat -- --all

# Remove TempBin
git filter-branch --force --index-filter `
  "git rm -r --cached --ignore-unmatch TempBin" `
  --prune-empty --tag-name-filter cat -- --all
```

### Step 3: Clean up

```powershell
# Remove backup refs
Remove-Item -Path .git/refs/original -Recurse -Force

# Expire reflog
git reflog expire --expire=now --all

# Garbage collection
git gc --prune=now --aggressive
```

### Step 4: Force push to remote

```powershell
git push origin --force --all
git push origin --force --tags
```

## Important Notes

⚠️ **WARNING**: These operations rewrite git history!

- **Backup first**: Always create a backup before running these commands
- **Coordinate with team**: All collaborators will need to re-clone the repository
- **Force push**: You'll need to force push to overwrite remote history
- **Tags**: All tags will be rewritten with new commit hashes
- **CI/CD**: May need to re-trigger workflows after force push

## After Force Push

All collaborators should:

```powershell
# Delete their local repository
cd ..
Remove-Item -Path "opencode-tracker" -Recurse -Force

# Re-clone from remote
git clone https://github.com/rygel/AIConsumptionTracker.git opencode-tracker
```

## Verify Cleanup

Check that the directories are gone:

```powershell
# Search for any remaining references
git log --all --full-history -- rust/target
git log --all --full-history -- TempBin

# Should return no results if successful
```

## Repository Size Comparison

Before cleanup:
```powershell
Get-ChildItem .git -Recurse | Measure-Object -Property Length -Sum
```

After cleanup (should be significantly smaller):
```powershell
Get-ChildItem .git -Recurse | Measure-Object -Property Length -Sum
```
