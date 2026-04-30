# Release Process

This document defines the complete release process for AI Usage Tracker. Both beta and stable releases follow the same automated pipeline with different branching and versioning rules.

## Version Scheme

**Format:** `MAJOR.MINOR.PATCH[-beta.N]`

| Component | Meaning |
|-----------|---------|
| `MAJOR.MINOR.PATCH` | Semantic version (e.g., `2.3.4`) |
| `-beta.N` | Beta prerelease suffix with sequential number (e.g., `-beta.1`, `-beta.34`) |
| No suffix | Stable release (e.g., `2.3.4`) |

**Rules:**
- The beta number is **sequential per major.minor.patch version** — it does NOT reset between versions. If v2.3.3 ended at `-beta.32`, v2.3.4 starts at `-beta.1`.
- Only one stable release exists per `MAJOR.MINOR.PATCH` version.
- The source of truth for the current version is `Directory.Build.props` (`<TrackerVersion>`).
- Tags follow the format `v{VERSION}` (e.g., `v2.3.4`, `v2.3.5-beta.1`).

## Branching Model

| Branch | Purpose | Release Channel |
|--------|---------|-----------------|
| `develop` | Active development, betas | Beta |
| `main` | Stable releases only | Stable |
| `feature/*`, `fix/*`, etc. | Feature branches targeting `develop` | N/A |

**Rules:**
- All changes go through PRs to `develop` (for betas) or `main` (for stable).
- Never push directly to `main` or `develop`.
- Beta PRs target `develop`. Stable release PRs target `main`.

## Beta Releases

Beta releases are the normal output of development on `develop`.

### When to Cut a Beta

- After merging a meaningful set of changes to `develop`.
- Before a stable release to gather testing feedback.
- On demand — no fixed schedule.

### Beta Release Steps

1. **Update version files** on a feature branch targeting `develop`:
   - `Directory.Build.props`: Set `<TrackerVersion>` to the new beta version (e.g., `2.3.5-beta.1`)
   - `CHANGELOG.md`: Move entries from `## [Unreleased]` to `## [2.3.5-beta.1] - YYYY-MM-DD`
   - `README.md`: Update version badge
   - `scripts/setup.iss`: Update `MyAppVersion`

2. **Create PR** to `develop` with the version bump.

3. **After merge**, the `auto-tag-release.yml` workflow detects the version change and:
   - Validates the changelog has an entry for the version
   - Creates and pushes the tag (e.g., `v2.3.5-beta.1`)

4. **Tag triggers `publish.yml`** which:
   - Builds installers for all platforms (win-x64, win-x86, win-arm64, linux-x64, osx-x64, osx-arm64)
   - Generates appcast files (`appcast_beta.xml`, `appcast_beta_x64.xml`, etc.)
   - Creates a GitHub pre-release with all assets

5. **Appcast is auto-generated** by the `generate-appcast.sh` script in CI — no manual update needed.

### Beta Number Increment

- Look at existing tags: `git tag -l "v{MAJOR.MINOR.PATCH}-beta.*" --sort=-v:refname`
- The next beta number is the highest existing + 1.
- If no betas exist for this version, start at `-beta.1`.

## Stable Releases

A stable release promotes a tested beta (or set of betas) to production.

### When to Cut a Stable

- After one or more betas have been tested.
- When critical fixes need wide distribution.
- On explicit user/owner decision.

### Stable Release Steps

1. **Update version files** on a feature branch targeting `main`:
   - `Directory.Build.props`: Set `<TrackerVersion>` to the stable version (e.g., `2.3.5` — no `-beta` suffix)
   - `CHANGELOG.md`: Consolidate beta changelogs into a single stable entry, or create a fresh entry
   - `README.md`: Update version badge
   - `scripts/setup.iss`: Update `MyAppVersion`

2. **Create PR** to `main` with the version bump.

3. **After merge**, the `auto-tag-release.yml` workflow detects the version change on `main` and:
   - Validates the changelog has an entry for the version
   - Creates and pushes the tag (e.g., `v2.3.5`)

4. **Tag triggers `publish.yml`** which:
   - Builds installers for all platforms
   - Generates stable appcast files (`appcast.xml`, `appcast_x64.xml`, etc.)
   - Creates a GitHub release (not pre-release)
   - Submits to Winget (Windows package manager)

5. **Appcast is auto-generated** by CI.

### Manual Release Workflow

If the auto-tag workflow is skipped or fails, use the manual `release.yml` workflow:

```
Workflow: "Create Release"
Inputs:
  version: 2.3.5-beta.1 (or 2.3.5)
  channel: beta (or stable)
  skip_file_updates: true (if version files already updated in PR)
```

This workflow:
- Updates version files (if `skip_file_updates` is false)
- Creates and pushes the tag
- The tag then triggers `publish.yml` as normal

## PAPI Cycle Releases vs Product Releases

**PAPI cycle releases and product releases are separate concepts.**

| Concept | What it is | Produces a tag? | Produces installers? |
|---------|------------|-----------------|---------------------|
| PAPI cycle | A planning/build/review unit in the PAPI system | No (unless explicitly triggered) | No |
| Product release | A versioned release distributed to users | Yes | Yes |

**How they relate:**
- PAPI cycles track what work gets done (tasks, build reports, reviews).
- A PAPI cycle does NOT automatically produce a product release.
- The user decides when accumulated work warrants a product release.
- Multiple PAPI cycles may happen between betas. Multiple betas may happen between stables.
- When the user wants to release, they follow the beta or stable release steps above — PAPI is not involved in the release mechanics.

**PAPI's `release` command** creates a changelog entry and git tag from cycle work. It should ONLY be used when the user explicitly asks for a release. It is NOT automatically tied to cycle completion.

## Changelog Workflow

### Format

```markdown
# Changelog

## [Unreleased]

## [2.3.5] - 2026-04-28

### Added
- Description of new feature

### Fixed
- Description of bug fix

### Changed
- Description of change

## [2.3.5-beta.1] - 2026-04-27

### Added
- ...
```

### Rules

- Always maintain a `## [Unreleased]` section at the top for tracking upcoming changes.
- Each version gets its own section with date.
- Beta changelogs are kept separate — when cutting stable, either consolidate or add a stable entry.
- Use `Added`, `Fixed`, `Changed`, `Performance` subsections.
- Keep entries concise — one line per change.

## Appcast (Auto-Update) Workflow

Appcast files are **auto-generated by CI** during the `publish.yml` workflow. The `generate-appcast.sh` script reads the installer sizes and generates:

**Stable channel:**
- `appcast/appcast.xml` (default/x64)
- `appcast/appcast_x64.xml`
- `appcast/appcast_arm64.xml`
- `appcast/appcast_x86.xml`

**Beta channel:**
- `appcast/appcast_beta.xml` (default/x64)
- `appcast/appcast_beta_x64.xml`
- `appcast/appcast_beta_arm64.xml`
- `appcast/appcast_beta_x86.xml`

### Manual Appcast Update (If Needed)

If auto-generation fails or appcast needs manual correction:

```bash
# Check release assets
gh release view v2.3.5 --json assets --jq '.assets[].name'

# Run appcast generator locally
bash scripts/generate-appcast.sh "2.3.5" "stable"
```

## Pre-Release Checklist

Before triggering any release:

- [ ] All tests pass: `dotnet test`
- [ ] Build succeeds: `dotnet build --configuration Release`
- [ ] Format check passes: `dotnet format --verify-no-changes --severity warn`
- [ ] `CHANGELOG.md` has an entry for the target version
- [ ] `Directory.Build.props` has the correct `<TrackerVersion>`
- [ ] Version tag does not already exist: `git tag -l "v{VERSION}"`
- [ ] Correct target branch: `develop` for beta, `main` for stable

## Troubleshooting

| Problem | Solution |
|---------|----------|
| Auto-tag didn't fire | Check `auto-tag-release.yml` ran. Manually trigger `release.yml` workflow. |
| Publish workflow failed | Check the workflow run logs. Re-run with `workflow_dispatch` passing the tag. |
| Wrong version in tag | Delete the tag (`git push --delete origin vWRONG`), fix version files, re-trigger. |
| Appcast not updated | Run `bash scripts/generate-appcast.sh "VERSION" "channel"` locally and commit. |
| Tag already exists | If the release was published, you need a new version number. If it failed, delete and re-tag. |
