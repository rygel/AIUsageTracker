#!/usr/bin/env bash
set -euo pipefail

version="${1:-}"
if [[ -z "$version" ]]; then
  echo "Usage: scripts/validate-release-consistency.sh <version>"
  exit 2
fi

clean_version="${version%%-*}"
escaped_version="${version//-/--}"
failed=0

project_files=(
  "AIUsageTracker.Core/AIUsageTracker.Core.csproj"
  "AIUsageTracker.Infrastructure/AIUsageTracker.Infrastructure.csproj"
  "AIUsageTracker.CLI/AIUsageTracker.CLI.csproj"
  "AIUsageTracker.UI.Slim/AIUsageTracker.UI.Slim.csproj"
  "AIUsageTracker.Monitor/AIUsageTracker.Monitor.csproj"
)

if ! grep -Fq "<TrackerVersion>$version</TrackerVersion>" Directory.Build.props; then
  echo "ERROR: Directory.Build.props TrackerVersion does not match $version"
  failed=1
fi

if ! grep -Fq "<TrackerAssemblyVersion>$clean_version</TrackerAssemblyVersion>" Directory.Build.props; then
  echo "ERROR: Directory.Build.props TrackerAssemblyVersion does not match $clean_version"
  failed=1
fi

for file in "${project_files[@]}"; do
  if [[ ! -f "$file" ]]; then
    echo "ERROR: Missing project file: $file"
    failed=1
    continue
  fi

  if ! grep -Fq '<Version>$(TrackerVersion)</Version>' "$file"; then
    echo "ERROR: $file Version does not reference TrackerVersion"
    failed=1
  fi

  if grep -q "<AssemblyVersion>" "$file" && ! grep -Fq '<AssemblyVersion>$(TrackerAssemblyVersion)</AssemblyVersion>' "$file"; then
    echo "ERROR: $file AssemblyVersion does not reference TrackerAssemblyVersion"
    failed=1
  fi

  if grep -q "<FileVersion>" "$file" && ! grep -Fq '<FileVersion>$(TrackerAssemblyVersion)</FileVersion>' "$file"; then
    echo "ERROR: $file FileVersion does not reference TrackerAssemblyVersion"
    failed=1
  fi
done

if ! grep -Fq "version-$escaped_version-" README.md; then
  echo "ERROR: README.md version badge does not match $version"
  failed=1
fi

if ! grep -Fq "MyAppVersion \"$version\"" scripts/setup.iss; then
  echo "ERROR: scripts/setup.iss version does not match $version"
  failed=1
fi

if ! grep -Fq "Version $version" scripts/publish-app.ps1; then
  echo "ERROR: scripts/publish-app.ps1 version does not match $version"
  failed=1
fi

if ! grep -Fq "## [$version]" CHANGELOG.md; then
  echo "ERROR: CHANGELOG.md is missing section for $version"
  failed=1
fi

# Detect leftover git conflict markers across the entire tracked tree.
# Regression guard for the v2.4.6 stable release: the dev->main merge
# during release prep left <<<<<<< / ======= / >>>>>>> markers in
# scripts/publish-app.ps1 that the existing version-content checks did
# not catch. PowerShell then aborted the osx-arm64 publish step with
# "Missing file specification after redirection operator" and the
# v2.4.6 release had to be re-tagged.
# Scan all files that git knows about (excluding this validation script
# itself and a few noise paths) and fail on any conflict marker.
echo ""
echo "Scanning tracked files for unresolved git conflict markers..."
marker_violations=$(git ls-files \
  | grep -v -E '^(\.gitignore|README\.md|AGENTS\.md|docs/.*\.md)$' \
  | grep -v -E '^scripts/validate-release-consistency\.sh$' \
  | xargs -I {} sh -c 'grep -lE "^(<{7}|={7}|>{7})( |$)" "{}" 2>/dev/null' \
  | xargs -I {} grep -lE "^(<{7}|={7}|>{7})( |$)" "{}" 2>/dev/null \
  || true)
if [[ -n "$marker_violations" ]]; then
  echo "ERROR: Unresolved git conflict markers found in:"
  for f in $marker_violations; do
    echo "  - $f"
    grep -nE "^(<{7}|={7}|>{7})( |$)" "$f" | head -3 | sed 's/^/      /'
  done
  failed=1
else
  echo "  no conflict markers found"
fi

# PowerShell syntax check on the release scripts. This catches the
# exact failure mode from the v2.4.6 release (PowerShell parsing
# `<<<<<<< HEAD` as a redirection operator with no target). The
# `[ScriptBlock]::Create()` call parses without executing; if the file
# has a syntax error, the call throws.
# (Note: the GitHub Actions windows-latest runner ships with PowerShell
# 7.x where `[System.Management.Automation.LanguageParser]` is not
# directly accessible. Using `[ScriptBlock]::Create()` works on every
# supported runtime.)
echo ""
echo "Validating PowerShell syntax on release scripts..."
ps_scripts=(
  "scripts/publish-app.ps1"
)
for ps in "${ps_scripts[@]}"; do
  if [[ ! -f "$ps" ]]; then
    echo "WARNING: $ps missing — skipping syntax check"
    continue
  fi
  if command -v pwsh >/dev/null 2>&1; then
    if ! pwsh -NoProfile -Command "try { [scriptblock]::Create((Get-Content -Raw './$ps')) } catch { Write-Host \$_.Exception.Message; exit 1 }" 2>&1; then
      echo "ERROR: PowerShell syntax error in $ps"
      failed=1
    else
      echo "  $ps OK"
    fi
  else
    echo "  pwsh not on PATH — skipping PowerShell syntax check on $ps"
  fi
done

if [[ "$failed" -ne 0 ]]; then
  echo "Release consistency validation failed."
  exit 1
fi

echo "Release consistency validation passed for $version."
