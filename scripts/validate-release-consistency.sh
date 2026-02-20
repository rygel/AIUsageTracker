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
  "AIConsumptionTracker.Core/AIConsumptionTracker.Core.csproj"
  "AIConsumptionTracker.Infrastructure/AIConsumptionTracker.Infrastructure.csproj"
  "AIConsumptionTracker.UI/AIConsumptionTracker.UI.csproj"
  "AIConsumptionTracker.CLI/AIConsumptionTracker.CLI.csproj"
  "AIConsumptionTracker.UI.Slim/AIConsumptionTracker.UI.Slim.csproj"
  "AIConsumptionTracker.Agent/AIConsumptionTracker.Agent.csproj"
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

if [[ "$failed" -ne 0 ]]; then
  echo "Release consistency validation failed."
  exit 1
fi

echo "Release consistency validation passed for $version."
