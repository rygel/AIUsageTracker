# Analyzer Cleanup Work Packages

## Status

Execution against the 2026-07-19 baseline (`cb78e429`), targeting `develop`:

| Agent | Package | PR | State |
| --- | --- | --- | --- |
| C | 02A — provider tests | #736 | open |
| A | 01A — non-test using order | #738 | open (6 files deferred to B/E) |
| D | 02B — test using order | #739 | open |
| B | 01B — Monitor/UI layout | #740 | open (picks up 01A's deferred SA1210/IDE0005) |
| E | 03A — async serialization | #741 | open |
| F | 03B — exception handling | #742 | open |
| G | 03C–F — runtime correctness | #743 | open (4 atomic commits) |
| — | IDE0009/SA1101 — Gemini production site | #745 | open |

Agent H (rule promotion, Package 04) is **blocked** until A–G merge — it can only promote rule families whose live backlog is zero across the merged tree.

**Known structural issue (#744):** the pre-commit analyzer gate runs all style/analyzer rules on every changed file, but this plan decomposes work by rule family per agent. Single-package PRs are landable only when a file's co-located warnings all belong to the same agent's scope. See issue #744 for options.

The `file:line` references in `01-mechanical-style.md`, `02-test-project-findings.md`, and `03-runtime-correctness.md` are baseline-relative and will shift as the PRs above merge. Re-derive them with the inventory command below; do not trust the literal numbers post-merge.

## Purpose

These documents divide the remaining analyzer backlog into non-overlapping assignments. They are designed for agents to execute after the beta release without mixing cleanup with product changes.

The governing rule is simple: fix the code or make a narrow, documented policy decision. Do not make a warning disappear by adding `NoWarn`, changing its severity to `none`, or adding a pragma unless the suppression-retirement package explicitly calls for that policy change.

## Current Baseline

Snapshot date: 2026-07-19.

Inventory command:

```powershell
$env:AGENT_OWNER='agent-name'; $env:AGENT_TASK='analyzer-inventory'
dotnet build AIUsageTracker.sln --configuration Release --no-restore --no-incremental --verbosity quiet -m:1
```

The snapshot contains 99 unique source file/line findings. The raw build reports 144 warnings because the WPF temporary project repeats some UI diagnostics and StyleCop emits project-level `SA0001` warnings. NuGet `NU1510` package-pruning warnings are separate dependency-cleanup work.

| Family | Unique findings | Package |
| --- | ---: | --- |
| `SA1210` | 37 | 01 mechanical style |
| `IDE0009` and `SA1101` | 12 locations, reported twice | 02 tests plus one provider |
| `MA0006` | 7 | 02 tests plus one web mapper |
| `SA1501/03/07`, `SA1512/13/15`, `SA1127` | 19 | 01 mechanical style |
| `VSTHRD003/103`, `CA1031/2024`, `CS8601` | 12 | 03 runtime correctness |

## Why Findings Appeared To Return

- Previous handoff documents recorded old counts and completed batches but remained in the repository.
- Incremental builds can compile no changed source and therefore print only NuGet warnings. Use `--no-incremental` for inventories.
- `dotnet build` succeeds when analyzers are warnings, so a green exit code did not mean a clean analyzer result.
- `scripts/pre-push-validation.ps1` treats analyzer failures as non-blocking unless `-StrictAnalyzers` is supplied.
- `.analyzer-gate/scope.json` currently tracks only Web and Web.Tests.
- `.editorconfig` contains broad legacy suppressions. A rule configured as `none` cannot be enforced by any hook.

## Enforcement Now Available

The checked-in hook `.githooks/pre-commit` invokes `scripts/pre-commit-analyzer-gate.ps1`.

Enable it once per checkout:

```powershell
git config core.hooksPath .githooks
```

For a commit containing C# changes, the gate:

1. rejects analyzer-relevant files that contain both staged and unstaged hunks;
2. checks whitespace on every staged C# file;
3. checks code-style diagnostics at warning severity;
4. checks analyzer diagnostics at warning severity;
5. builds the Release solution;
6. runs the core and Monitor test projects.

The hook is a local safety net. It does not make suppressed rules visible and it can be bypassed with `--no-verify`; repository policy must forbid bypassing it for ordinary work.

## Work Package Order

1. Assign mechanical style packages first. They reduce noise without behavior changes.
2. Assign test-project findings separately from production files.
3. Assign runtime correctness findings one file cluster at a time with focused tests.
4. Retire suppressions only after the corresponding live backlog is zero.
5. After each rule family reaches zero, promote it to `error` so later agents cannot reintroduce it.

Every agent must run the commands in its package and report before/after counts. “Build passed” is not an acceptable result unless the output contains no findings from the assigned rule family.
