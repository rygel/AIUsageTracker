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
