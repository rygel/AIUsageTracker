# Analyzer Remediation Plan

The former analyzer plan described a March 2026 backlog that no longer matched the repository. The current, independently assignable work packages live in [`docs/analyzer-cleanup/`](analyzer-cleanup/README.md).

Start with the overview, then assign one work package per agent. Do not assign two agents files from the same package at the same time.

- [Current baseline and enforcement](analyzer-cleanup/README.md)
- [Mechanical style findings](analyzer-cleanup/01-mechanical-style.md)
- [Test-project findings](analyzer-cleanup/02-test-project-findings.md)
- [Behavior-sensitive findings](analyzer-cleanup/03-runtime-correctness.md)
- [Suppression retirement and rule promotion](analyzer-cleanup/04-suppression-retirement.md)
- [Copy-paste agent assignments](analyzer-cleanup/05-agent-assignments.md)

The repository-local pre-commit gate is `scripts/pre-commit-analyzer-gate.ps1`. Enable the checked-in hook in each checkout with:

```powershell
git config core.hooksPath .githooks
```
