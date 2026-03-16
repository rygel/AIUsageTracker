#!/usr/bin/env bash
# Pre-commit validation script
# Run this before committing to ensure code quality.
# By default, whitespace formatting is checked only for staged files.
# Use --all to validate the full solution.
# Use --fix to apply formatting before verification.

set -euo pipefail

AUTO_FIX=false
CHECK_MODE="staged"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --fix)
            AUTO_FIX=true
            ;;
        --all)
            CHECK_MODE="all"
            ;;
        --staged)
            CHECK_MODE="staged"
            ;;
        *)
            echo "Unknown argument: $1"
            echo "Usage: ./scripts/pre-commit-check.sh [--fix] [--all|--staged]"
            exit 1
            ;;
    esac
    shift
done

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
cd "${REPO_ROOT}"

echo "Running pre-commit validation..."

# Stabilize local .NET execution on this machine.
export MSBuildEnableWorkloadResolver=false
export MSBUILDDISABLENODEREUSE=1
export DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1

echo ""
echo "Step 1: Checking formatting (whitespace/style)..."

declare -a include_paths=()
if [[ "${CHECK_MODE}" == "staged" ]]; then
    while IFS= read -r staged_file; do
        include_paths+=("${staged_file}")
    done < <(git diff --cached --name-only --diff-filter=ACMR -- \
        '*.cs' '*.csproj' '*.props' '*.targets' '.editorconfig')

    if [[ ${#include_paths[@]} -eq 0 ]]; then
        echo "No staged format-relevant files detected; skipping formatting step."
    fi
fi

if [[ "${CHECK_MODE}" == "all" ]] || [[ ${#include_paths[@]} -gt 0 ]]; then
    if [[ "${AUTO_FIX}" == "true" ]]; then
        if [[ "${CHECK_MODE}" == "all" ]]; then
            dotnet format whitespace AIUsageTracker.sln --verbosity minimal
        else
            dotnet format whitespace AIUsageTracker.sln --verbosity minimal --include "${include_paths[@]}"
        fi
    fi

    if [[ "${CHECK_MODE}" == "all" ]]; then
        dotnet format whitespace AIUsageTracker.sln --verify-no-changes --verbosity minimal
        dotnet format style AIUsageTracker.sln --verify-no-changes --severity warn --verbosity minimal
    else
        dotnet format whitespace AIUsageTracker.sln --verify-no-changes --verbosity minimal --include "${include_paths[@]}"
        dotnet format style AIUsageTracker.sln --verify-no-changes --severity warn --verbosity minimal --include "${include_paths[@]}"
    fi
fi

if [[ "${AUTO_FIX}" == "true" ]]; then
    echo "Formatting was applied. Re-stage changed files before committing."
fi

echo "Formatting check passed."

echo ""
echo "Step 2: Building solution..."
dotnet build AIUsageTracker.sln --configuration Release --verbosity minimal
echo "Build successful."

echo ""
echo "Step 3: Running core tests..."
dotnet test AIUsageTracker.Tests/AIUsageTracker.Tests.csproj --configuration Release --no-build --verbosity minimal
echo "Core tests passed."

echo ""
echo "Step 4: Running monitor tests..."
dotnet test AIUsageTracker.Monitor.Tests/AIUsageTracker.Monitor.Tests.csproj --configuration Release --no-build --verbosity minimal
echo "Monitor tests passed."

echo ""
echo "All validation checks passed."
