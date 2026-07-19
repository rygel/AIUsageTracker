# Work Package 05: Copy-Paste Agent Assignments

Assign only one block to an agent. Each agent must work on its own branch and PR targeting `develop`.

## Agent A: Non-Test Using Order

> Fix Package 01A from `docs/analyzer-cleanup/01-mechanical-style.md`. Change only the listed non-test files and only import ordering. Do not change behavior, dependencies, or suppress analyzer rules. Run the changed-file format/analyzer gate and a non-incremental Release build. Report the before/after `SA1210` count for your files. Commit atomically and open a PR to `develop`.

## Agent B: Monitor Layout

> Fix Package 01B from `docs/analyzer-cleanup/01-mechanical-style.md`. Preserve every database statement and control-flow condition; add braces and whitespace only. Do not touch runtime-correctness warnings. Run Monitor-focused tests, the changed-file analyzer gate, and a non-incremental Release build. Report each removed warning code and file. Commit atomically and open a PR to `develop`.

## Agent C: Provider Tests

> Fix Package 02A from `docs/analyzer-cleanup/02-test-project-findings.md`. Use explicit instance qualification and contract-correct ordinal string equality. Do not weaken assertions or alter fixture data. Run the provider test classes plus the full `AIUsageTracker.Tests` project. Report zero remaining `IDE0009`, `SA1101`, and `MA0006` findings in the scoped files. Commit atomically and open a PR to `develop`.

## Agent D: Test Using Order

> Fix Package 02B from `docs/analyzer-cleanup/02-test-project-findings.md`. Reorder imports only in the listed test files. Do not combine this with async serialization fixes. Run the changed-file analyzer gate and both test projects. Report zero `SA1210` findings in scope. Commit atomically and open a PR to `develop`.

## Agent E: Async Serialization

> Fix Package 03A from `docs/analyzer-cleanup/03-runtime-correctness.md`. Replace synchronous serialization with true async APIs, propagate awaits and cancellation, and preserve output bytes/JSON contracts. Add or identify regression tests for each production path. Do not use `Task.Run` or suppress `VSTHRD103`. Run focused tests and the full suite. Commit atomically and open a PR to `develop`.

## Agent F: Exception Handling

> Fix Package 03B from `docs/analyzer-cleanup/03-runtime-correctness.md`. Identify the concrete recoverable exceptions at each location, retain required user-facing fallback behavior, and log every caught failure with context. Do not add catch-all suppression or silent fallback. Add focused failure-path tests. Run the owning project tests and full suite. Commit atomically and open a PR to `develop`.

## Agent G: Remaining Correctness Findings

> Fix Packages 03C through 03F from `docs/analyzer-cleanup/03-runtime-correctness.md` as four atomic commits. Preserve WPF thread affinity, dispose import resources, model Minimax nullability honestly, and use contract-correct provider-ID equality. Each commit needs a focused regression test and zero findings for its assigned code. Open one PR to `develop` with the commits kept separate.

## Agent H: Rule Promotion

> After Agents A through G merge, execute `docs/analyzer-cleanup/04-suppression-retirement.md` only for analyzer families whose live backlog is zero. Promote those rules to `error`, prove the hook rejects a temporary violation, revert the temporary violation, and run a non-incremental Release build plus all tests. Do not enable a suppressed high-volume family without first producing its exact inventory and follow-up work packages.
