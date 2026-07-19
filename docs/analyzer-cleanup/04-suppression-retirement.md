# Work Package 04: Suppression Retirement and Rule Promotion

## Problem

The current `.editorconfig` contains a legacy suppression block that sets several analyzer families to `none`. Agents cannot adhere to findings that Roslyn never emits. The repository also leaves active warnings non-fatal, so normal builds can succeed while findings remain.

Examples currently suppressed repo-wide include:

- member ordering: `SA1201`, `SA1202`, `SA1204`, `SA1214`;
- layout: `SA1117`, `SA1118`, `SA1402`;
- documentation: `SA1600`, `SA1601`, `SA1602`, `SA1604`, `SA1611`, `SA1616`, `SA1618`, `SA1651`;
- type/file and collection guidance: `MA0016`, `MA0048`, `MA0051`;
- selected xUnit rules.

Some suppressions are intentional policy and should remain, such as `SA1309` because repository conventions require underscore-prefixed private fields. Do not remove suppressions indiscriminately.

## Required Process Per Rule Family

1. Change one rule from `none` to `warning` on a dedicated branch.
2. Run a non-incremental solution build and record every unique file/line finding.
3. Divide the live findings into non-overlapping file clusters.
4. Fix all clusters without adding pragmas or new suppressions.
5. Run the full build and tests.
6. Promote the rule from `warning` to `error` in `.editorconfig`.
7. Verify the local pre-commit gate rejects a deliberately introduced violation, then revert that deliberate violation.

Do not enable multiple high-volume families together. That obscures ownership and makes it easy for agents to claim an unrelated warning is baseline.

## Recommended Promotion Order

1. `SA1210`, `SA1101`, `IDE0009`, `MA0006` after Packages 01 and 02 reach zero.
2. Active layout rules (`SA1501`, `SA1503`, `SA1507`, `SA1512`, `SA1513`, `SA1515`) after Package 01B.
3. Behavior-sensitive active rules (`CA1031`, `CA2024`, `VSTHRD003`, `VSTHRD103`) after Package 03.
4. Currently suppressed member-ordering rules, one at a time.
5. Documentation rules only after a repository policy decision on required XML documentation.

## Repository Configuration Target

For rules with a zero backlog, use:

```ini
dotnet_diagnostic.SA1210.severity = error
```

Keep `TreatWarningsAsErrors` off globally until the NuGet and project-level warning backlog is addressed; otherwise unrelated SDK/package warnings can block emergency builds. Prefer explicit per-rule `error` severity so the contract is stable and reviewable.

## Gate Limitations To Address Later

The local hook is enabled now. GitHub changes are intentionally deferred for the beta release. A later enforcement PR should:

- make `-StrictAnalyzers` the default in `scripts/pre-push-validation.ps1`;
- expand `.analyzer-gate/scope.json` from Web-only to every solution project;
- make CI invoke the same changed-file analyzer gate;
- prohibit `--no-verify` in agent instructions except for a documented emergency;
- add an architecture test that rejects newly added `NoWarn`, pragma suppressions, or `.editorconfig` severity reductions unless allowlisted.
