# Release process

Source of truth for the version string: `Directory.Build.props` `<TrackerVersion>`. Tags: `v{VERSION}`.

Format: `MAJOR.MINOR.PATCH` or `MAJOR.MINOR.PATCH-beta.N`. Beta PRs target `develop`; stable PRs target `main`. Never push those branches directly (`AGENTS.md`).

On push of version files to `develop` (beta) or `main` (stable), `.github/workflows/auto-tag-release.yml` reads `<TrackerVersion>`, requires `## [$VERSION]` in `CHANGELOG.md`, and pushes the tag. Paths that trigger it: `Directory.Build.props`, `CHANGELOG.md`, `README.md`, `scripts/setup.iss`, `scripts/publish-app.ps1`.

The tag runs `.github/workflows/publish.yml` (installers + appcast; stable also Winget `AIConsumption.Tracker`). Manual fallback: workflow “Create Release” (`.github/workflows/release.yml`) with inputs `version`, `channel` (`stable`/`beta`), `skip_file_updates`. Installer basename: `AIUsageTracker_Setup_v{version}_{arch}` (`scripts/setup.iss`).

Appcast files under `appcast/` (`appcast.xml`, `appcast_x64.xml`, `appcast_arm64.xml`, `appcast_x86.xml`, and `appcast_beta*.xml`) are generated in CI (`scripts/generate-appcast.sh`).

Also bump: `README.md` version badge, `scripts/setup.iss` `MyAppVersion`. Do not tag unless the user asks (`AGENTS.md`).

```powershell
git tag -l "v*" --sort=-v:refname
bash scripts/validate-release-consistency.sh "<version>"
```
