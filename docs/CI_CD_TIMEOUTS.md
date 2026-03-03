# CI/CD Timeout Strategy

This document explains the timeout configuration for all CI/CD workflows.

## Philosophy

**Aggressive timeouts prevent runaway jobs.** GitHub Actions default is **6 hours** per job, which can waste significant CI minutes and block other workflows if a job hangs.

We use **tight timeouts** based on empirical observation of typical runtimes:
- Fast operations: 2-5 minutes
- Build/test operations: 10-15 minutes
- Complex workflows: 15-30 minutes

## Workflow Timeouts

| Workflow | Timeout | Rationale |
|----------|---------|-----------|
| `theme-validation.yml` | 5 min | PowerShell validation scripts typically complete in <2 min |
| `release-script-validation.yml` | 10 min | Inno Setup installation + validation typically takes 3-5 min |
| `docs-image-integrity.yml` | 5 min | Image reference checks complete in <1 min |
| `release.yml` | 10 min | Git operations (tagging, pushing) complete in <2 min |
| `security-scan.yml` | 10 min | Vulnerability scan typically takes 2-3 min |
| `slim-screenshot-baseline.yml` | 10 min | Screenshot generation takes 5-8 min |
| `provider-contract-drift.yml` | 10 min | Provider tests complete in 3-5 min |
| `monitor-openapi-contract.yml` | 10 min | Contract validation takes 2-4 min |
| `publish.yml` - publish | 15 min | Cross-platform builds take 5-10 min per platform |
| `publish.yml` - generate-appcast | 2 min | Manifest generation takes <30 sec |
| `publish.yml` - create-release | 5 min | Artifact upload takes 1-2 min |
| `test.yml` - prepare | 5 min | Build + restore typically takes 2-3 min |
| `test.yml` - core-tests | 5 min | Test execution takes 2-4 min |
| `test.yml` - monitor-tests | 3 min | Monitor tests take 1-2 min |
| `test.yml` - web-tests | 10 min | Web UI tests take 5-8 min |
| `experimental-rust.yml` | 10 min | Rust builds take 3-8 min per target |

## Timeout Calculation

Timeouts are calculated as:
```
timeout = observed_typical_duration × 2
```

This provides:
- **Buffer** for slower runners or transient slowdowns
- **Early failure** for hung jobs (no waiting hours)
- **Cost savings** (jobs fail fast instead of running 6 hours)

## When Timeouts Are Too Tight

If you see frequent timeout failures:
1. Check if the job is genuinely slower (infrastructure degradation)
2. Check if the job is hanging (infinite loop, deadlocked)
3. Consider increasing by 2-3 minutes if consistent with empirical data

## Monitoring Timeout Failures

Track timeout failures in GitHub Actions logs:
```
The job running on runner GitHub Actions X has exceeded the maximum execution time of X minutes.
```

If a job consistently fails with timeouts:
1. Review the step logs to identify slow steps
2. Consider adding step-level timeouts (see below)

## Step-Level Timeouts

For critical steps that should never hang, add step-level timeouts:

```yaml
- name: Run Tests
  timeout-minutes: 3
  run: dotnet test
```

Recommended for:
- Long-running operations (builds, tests)
- Network operations (downloads, API calls)
- File operations (large copies, compressions)

## Emergency Override

If you need to run a workflow without timeouts (debugging):
1. Use `workflow_dispatch` trigger
2. Temporarily comment out timeout in a feature branch
3. Re-enable before merging

## Total Pipeline Runtime

With these timeouts, a full CI pipeline run (all workflows) has a maximum theoretical runtime of:
- **Serial execution**: ~75 minutes
- **Parallel execution**: ~25 minutes (longest critical path)

This prevents:
- Runaway jobs consuming CI minutes
- Queue blockage from hung jobs
- Delayed feedback from slow pipelines

## References

- [GitHub Actions: Workflow syntax](https://docs.github.com/en/actions/using-workflows/workflow-syntax-for-github-actions#jobsjob_idtimeout-minutes)
- Default timeout: 360 minutes (6 hours) - way too long for most use cases
- Recommended: Match timeout to observed duration + 50-100% buffer
