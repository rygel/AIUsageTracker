# Release Process Guide

This document describes the complete release process for AI Consumption Tracker.

## Overview

The release process uses GitHub Actions to automate validation and release creation. The workflow is designed with **security in mind** for a public repository.

## Why Manual Tag Creation?

This project uses a **two-step release process** where you manually create the git tag after validation:

### Security Benefits

1. **Repository Protection**: The `main` branch and tags are protected to prevent unauthorized changes
2. **No Long-Lived Secrets**: We don't need Personal Access Tokens (PATs) with elevated permissions
3. **Access Control**: Only repository admins can push tags, ensuring releases are controlled
4. **Audit Trail**: Manual tag creation provides a clear audit trail of who released what and when

### Why Not Fully Automated?

In public repositories, fully automated releases pose risks:
- Compromised GitHub Actions could create unauthorized releases
- Malicious PRs could exploit the release workflow
- Secrets (PATs) could be leaked

By requiring manual tag creation:
- **You maintain full control** over when releases happen
- **No elevated permissions** needed for GitHub Actions
- **Protection rules remain intact** for branches and tags

## Prerequisites

- All changes for the release must be merged to `main`
- CHANGELOG.md must be updated with the new version
- Version files must be updated (see step 1 below)

## Release Steps

### Step 1: Update Version Files (via Pull Request)

**IMPORTANT**: Due to branch protection rules, version files cannot be updated directly by the workflow. You must create a PR manually first.

Create a feature branch and update these files:

#### 1.1 Shared Version Source
Update `Directory.Build.props`:

Example changes:
```xml
<TrackerVersion>1.8.7-alpha.2</TrackerVersion>
<TrackerAssemblyVersion>1.8.7</TrackerAssemblyVersion>
```

#### 1.2 README.md
Update the version badge:
```markdown
![Version](https://img.shields.io/badge/version-1.8.7--alpha.2-orange)
```

#### 1.3 Installer Script
Update `scripts/setup.iss`:
```pascal
#define MyAppVersion "1.8.7-alpha.2"
```

#### 1.4 Publish Script
Update `scripts/publish-app.ps1`:
```powershell
# Usage: .\scripts\publish-app.ps1 -Runtime win-x64 -Version 1.8.7-alpha.2
```

#### 1.5 Create and Merge PR
```bash
# Create branch
git checkout -b feature/v1.8.7-alpha.2-version-update

# Stage and commit changes
git add .
git commit -m "chore(release): update version files to v1.8.7-alpha.2"
git push -u origin feature/v1.8.7-alpha.2-version-update

# Create PR (or use GitHub UI)
gh pr create --base main --head feature/v1.8.7-alpha.2-version-update \
  --title "chore(release): update version files to v1.8.7-alpha.2"

# Merge the PR
```

### Step 2: Trigger Release Workflow

After the version update PR is merged to `main`, trigger the release workflow:

**Via GitHub UI:**
1. Go to Actions > Create Release
2. Click "Run workflow"
3. Enter version: `1.8.7-alpha.2`
4. Check "Skip automatic file updates" (since files are already updated)
5. Click "Run workflow"

**Via CLI:**
```bash
gh workflow run "Create Release" --repo rygel/AIConsumptionTracker \
  -f version="1.8.7-alpha.2" \
  -f skip_file_updates="true"
```

### Step 3: Create Git Tag Manually

After the validation workflow passes, you must create the git tag manually:

```bash
# Ensure you're on latest main
git checkout main
git pull origin main

# Create and push the tag
git tag v1.8.7-alpha.2
git push origin v1.8.7-alpha.2
```

**Important**: Only repository admins can push tags due to protection rules.

### Step 4: Automatic Build and Release

Pushing the tag automatically triggers the `Publish & Distribute` workflow which will:
1. Run **pre-publish validation** (version/changelog consistency checks via `scripts/validate-release-consistency.sh`)
2. Build the application for all architectures (x64, x86, ARM64)
3. Create Inno Setup installers
4. Generate `appcast.xml` for NetSparkle auto-updater
5. Create GitHub release with all artifacts including `appcast.xml`
6. Upload release notes from CHANGELOG.md

**No manual steps required** - the workflow handles everything including the appcast file!

Monitor progress at: `https://github.com/rygel/AIConsumptionTracker/actions`

### Step 5: Post-Release

After the release is complete:

1. **Download and test** the installer from the GitHub release
2. **Update winget** package (if applicable)
3. **Announce** the release to users

## Troubleshooting

### Workflow fails with "Protected branch update failed"
This means you didn't use `skip_file_updates=true`. The workflow tried to push version updates directly to main, which is blocked by branch protection.

**Fix**: Ensure the version files are already updated in main via PR, then re-run with `skip_file_updates=true`.

### Workflow fails with "version does not match"
One or more version files doesn't match the input version.

**Fix**: Check which file failed validation and update it manually via PR.

### Workflow fails with "CHANGELOG.md missing version section"
The changelog doesn't have an entry for this version.

**Fix**: Add a changelog entry via PR before triggering the release.

### Publish workflow fails during pre-publish validation
The tag version and repository version files are out of sync.

**Fix**: Ensure `Directory.Build.props`, `README.md` badge, `scripts/setup.iss`, `scripts/publish-app.ps1`, and `CHANGELOG.md` all match the tag version, then push a corrected tag.

## Files Managed by Release Workflow

The release workflow automatically generates:
- Git tag (e.g., `v1.7.14`)
- GitHub Release with release notes
- `appcast.xml` for NetSparkle auto-updater

The following must be updated manually before triggering:
- `Directory.Build.props` (`TrackerVersion`, `TrackerAssemblyVersion`)
- `README.md` (version badge)
- `scripts/setup.iss` (MyAppVersion)
- `scripts/publish-app.ps1` (example version)
- `CHANGELOG.md` (version entry)

## Version Numbering

Use a consistent release string for `TrackerVersion` and use its base numeric part for `TrackerAssemblyVersion`.

Examples:
- `1.8.7`
- `1.8.7-alpha.2` (with `TrackerAssemblyVersion` set to `1.8.7`)

## Related Documentation

- [CHANGELOG.md](../CHANGELOG.md) - Release notes and version history
- [AGENTS.md](../AGENTS.md) - Development guidelines including release notes guidelines
- `.github/workflows/release.yml` - Release workflow definition
