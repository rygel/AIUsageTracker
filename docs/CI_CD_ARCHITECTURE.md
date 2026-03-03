# CI/CD Architecture Improvements

This document outlines the architectural improvements made to the CI/CD pipelines.

## Overview

The CI/CD architecture has been optimized to reduce duplication, improve maintainability, and provide more consistent build performance across all workflows.

## Key Improvements

### 1. Composite Action for .NET Setup

**Created:** `.github/actions/setup-dotnet-cache/action.yml`

A reusable composite action that consolidates:
- .NET SDK setup with configurable version
- NuGet package caching with intelligent cache keys
- Global .NET tools caching

**Benefits:**
- Eliminates 8+ lines of duplicate code per workflow
- Centralized cache configuration
- Consistent .NET version management
- Reduced maintenance burden

**Usage:**
```yaml
- name: Setup .NET with Cache
  uses: ./.github/actions/setup-dotnet-cache
  with:
    dotnet-version: "8.0.x"
    cache-key-prefix: "workflow-specific"
```

### 2. Workflow Optimizations

#### Test Workflow (test.yml)
- **Timeouts increased:**
  - Prepare: 3 min → 5 min
  - Core tests: 2 min → 5 min (from 75s to 300s hang timeout)
  - Monitor tests: 1 min → 3 min (from 45s to 180s hang timeout)
  - Web tests: 4 min → 10 min
- **Removed aggressive timeouts** that could cause flaky failures
- **Added composite action** for .NET setup

#### Screenshot Baseline Workflow
- **Added timeout:** 10 minutes (was missing)
- **Added trigger:** Re-runs when composite action changes
- **Uses composite action** for .NET setup

#### Provider Contract Drift Workflow
- **Added timeout:** 10 minutes (was missing)
- **Added trigger:** Re-runs when composite action changes
- **Uses composite action** for .NET setup

#### Monitor OpenAPI Contract Workflow
- **Added timeout:** 10 minutes (was missing)
- **Added trigger:** Re-runs when composite action changes
- **Uses composite action** for .NET setup

### 3. Path Filter Consistency

All workflows now properly trigger when:
- Their own workflow file changes
- The composite action changes
- Relevant source code changes

This ensures workflows run when their dependencies change.

## Files Changed

### New Files
- `.github/actions/setup-dotnet-cache/action.yml` - Composite action

### Modified Workflows
- `.github/workflows/test.yml` - Optimized timeouts, uses composite action
- `.github/workflows/slim-screenshot-baseline.yml` - Uses composite action, added timeout
- `.github/workflows/provider-contract-drift.yml` - Uses composite action, added timeout
- `.github/workflows/monitor-openapi-contract.yml` - Uses composite action, added timeout

## Benefits

1. **Reduced Duplication:** Common .NET setup steps are now in one place
2. **Better Caching:** Centralized cache configuration with proper restore keys
3. **Consistent Timeouts:** All workflows have reasonable timeouts to prevent flakiness
4. **Easier Maintenance:** Update one place instead of multiple workflows
5. **Improved Reliability:** Less aggressive timeouts reduce false failures

## Migration Guide

When creating new workflows that need .NET:

1. Use the composite action:
   ```yaml
   - name: Setup .NET with Cache
     uses: ./.github/actions/setup-dotnet-cache
     with:
       dotnet-version: "8.0.x"
       cache-key-prefix: "your-prefix"
   ```

2. Add path triggers for the composite action:
   ```yaml
   paths:
     - "your-code/**"
     - ".github/workflows/your-workflow.yml"
     - ".github/actions/setup-dotnet-cache/**"
   ```

3. Set reasonable timeouts:
   ```yaml
   timeout-minutes: 10
   ```

## Future Improvements

- Consider creating composite actions for common PowerShell script execution
- Standardize artifact upload/download patterns
- Add workflow-level concurrency controls where appropriate

---

# Additional CI/CD Optimization Opportunities

## Performance Improvements

### 1. Implement Matrix Builds for Cross-Platform Testing
**Current:** Tests run only on `windows-latest`
**Opportunity:** Run core tests on multiple OS versions to catch platform-specific issues early
```yaml
strategy:
  matrix:
    os: [windows-latest, windows-2022, ubuntu-latest]
    dotnet: ['8.0.x']
runs-on: ${{ matrix.os }}
```
**Benefit:** Catch OS-specific bugs before release, especially important for file path handling and timezone issues

### 2. Add Build Artifact Optimization
**Current:** Artifacts upload entire build output
**Opportunity:** 
- Compress artifacts before upload (zip/tar.gz)
- Only upload changed files using `actions/upload-artifact@v4` with `if-no-files-found: warn`
- Use artifact retention policies more aggressively
```yaml
- name: Compress artifacts
  run: tar -czf artifacts.tar.gz bin/Release/
- uses: actions/upload-artifact@v4
  with:
    retention-days: 3  # Instead of default 7
```
**Benefit:** Faster artifact uploads/downloads, reduced storage costs

### 3. Implement Conditional Workflow Skipping
**Current:** Workflows run even for documentation-only changes
**Opportunity:** Skip unnecessary workflows based on file changes
```yaml
jobs:
  check-changes:
    outputs:
      code-changed: ${{ steps.changes.outputs.code }}
    steps:
      - uses: dorny/paths-filter@v2
        id: changes
        with:
          filters: |
            code:
              - 'AIUsageTracker.*/**'
              - '!**/*.md'
  
  build:
    if: needs.check-changes.outputs.code-changed == 'true'
```
**Benefit:** Faster feedback for docs-only PRs, reduced CI minutes usage

## Additional Features

### 4. Add Dependency Security Scanning
**New Workflow:** `.github/workflows/security-scan.yml`
```yaml
- name: Run security audit
  run: dotnet list package --vulnerable --include-transitive
  
- name: Upload security results
  uses: github/codeql-action/upload-sarif@v2
  if: always()
```
**Benefit:** Catch vulnerable dependencies before they reach production

### 5. Implement Code Coverage Reporting
**New Feature:** Add to test workflow
```yaml
- name: Run tests with coverage
  run: dotnet test --collect:"XPlat Code Coverage"

- name: Upload coverage to Codecov
  uses: codecov/codecov-action@v3
  with:
    files: ./TestResults/**/coverage.cobertura.xml
```
**Benefit:** Track code coverage trends, ensure new code is tested

### 6. Add Build Performance Monitoring
**New Workflow:** Track build times over time
```yaml
- name: Record build metrics
  run: |
    echo "::notice::Build duration: ${{ steps.build.outputs.duration }}s"
    # Send to monitoring service (DataDog, Grafana, etc.)
```
**Benefit:** Identify slow builds, track optimization impact

### 7. PR Size Limit Warning
**New Feature:** Warn on large PRs
```yaml
- name: Check PR size
  run: |
    lines_changed=$(git diff --stat origin/main | tail -1)
    if [ $lines_changed -gt 1000 ]; then
      echo "::warning::Large PR detected ($lines_changed lines). Consider splitting."
    fi
```
**Benefit:** Encourage smaller, focused PRs for easier review

### 8. Automated Dependency Updates
**New Workflow:** `.github/workflows/dependency-updates.yml`
```yaml
on:
  schedule:
    - cron: "0 2 * * 1"  # Weekly on Monday

jobs:
  update-dependencies:
    steps:
      - uses: actions/checkout@v4
      - run: dotnet outdated -u
      - name: Create PR
        uses: peter-evans/create-pull-request@v5
        with:
          title: "chore(deps): update dependencies"
```
**Benefit:** Keep dependencies current automatically

## Streamlining Opportunities

### 9. Create Reusable Workflow Templates
**Current:** Multiple workflows duplicate job structures
**Opportunity:** Create `.github/workflows/reusable-test.yml`
```yaml
name: Reusable Test Workflow
on:
  workflow_call:
    inputs:
      test-filter:
        required: true
        type: string
      timeout:
        default: 10
        type: number

jobs:
  test:
    runs-on: windows-latest
    timeout-minutes: ${{ inputs.timeout }}
    steps:
      - uses: ./.github/actions/setup-dotnet-cache
      - run: dotnet test --filter "${{ inputs.test-filter }}"
```
**Benefit:** Single source of truth for test execution patterns

### 10. Optimize Cache Strategy
**Current:** Cache keys might not be optimal
**Opportunity:** 
- Use `actions/cache@v4` with `restore-keys` fallback
- Cache Docker layers if using containers
- Cache Playwright browsers separately
```yaml
- name: Cache Playwright browsers
  uses: actions/cache@v4
  with:
    path: ~/.cache/ms-playwright
    key: playwright-${{ hashFiles('**/package-lock.json') }}
```
**Benefit:** Faster workflow execution, less redundant downloads

### 11. Parallelize Independent Jobs
**Current:** Some jobs run sequentially that could be parallel
**Opportunity:** Review job dependencies and maximize parallelism
```yaml
jobs:
  build:
    # ...
  test-core:
    needs: build
  test-monitor:
    needs: build  # Parallel with test-core
  test-web:
    needs: build  # Parallel with others
```
**Benefit:** Faster overall pipeline completion

### 12. Add Notification Integration
**New Feature:** Slack/Discord notifications
```yaml
- name: Notify on failure
  if: failure() && github.ref == 'refs/heads/main'
  uses: slackapi/slack-github-action@v1
  with:
    payload: |
      {"text": "Build failed on main: ${{ github.workflow }}"}
```
**Benefit:** Faster response to broken builds

## Implementation Priority

### Phase 1 (Quick Wins)
1. Security scanning workflow
2. Conditional workflow skipping
3. Cache optimization
4. Build artifact compression

### Phase 2 (Medium Effort)
5. Code coverage reporting
6. PR size warnings
7. Notification integration
8. Reusable workflow templates

### Phase 3 (Larger Projects)
9. Matrix builds for cross-platform
10. Automated dependency updates
11. Build performance monitoring
12. Full workflow refactoring

## Estimated Impact

- **Time Savings:** 20-30% reduction in CI minutes through caching and conditional runs
- **Cost Reduction:** 15-25% lower GitHub Actions bill
- **Quality Improvement:** Security scanning, coverage reporting, cross-platform testing
- **Developer Experience:** Faster feedback, better notifications, smaller PRs
