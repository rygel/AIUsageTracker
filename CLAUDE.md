# CLAUDE.md

## Build & Test

Before building, kill any running app/monitor instances that lock DLLs:

```powershell
pwsh -File scripts/kill-all.ps1
```

Run tests (capped at 4 cores per global CLAUDE.md):

```bash
dotnet test AIUsageTracker.Tests/AIUsageTracker.Tests.csproj -T 4
```

## Analyzer Rules — Do Not Weaken

The following `.editorconfig` rules are enforced at `error` severity with zero violations.
**Never lower their severity, add suppressions, or work around them.**
Fix the underlying code instead.

| Rule | Severity | What it enforces |
|------|----------|-----------------|
| CA1031 | warning | No catching general `Exception` — use specific types |
| CA1062 | error | Validate public method parameters for null |
| CA1307 | error | Explicit `StringComparison` on all string operations |
| CA2016 | error | Forward `CancellationToken` to async methods |
| CA2254 | error | Use structured logging templates, not interpolation |

If a new violation appears, fix it before pushing. Do not:
- Change severity to `suggestion` or `none`
- Add `#pragma warning disable`
- Add `[SuppressMessage]` attributes
- Raise analyzer thresholds to make CI pass

## Architecture

- **AIUsageTracker.UI.Slim** — WPF main window + settings dialog (net8.0-windows)
- **AIUsageTracker.Monitor** — Background agent that polls providers and serves usage data over HTTP
- **AIUsageTracker.Core** — Shared models, interfaces, monitor client
- **AIUsageTracker.Infrastructure** — Provider implementations (Synthetic, Codex, OpenAI, etc.)

### Data flow: Monitor → Main Window

1. Monitor polls each configured provider via `IProvider.GetUsageAsync(config)`
2. Results are grouped into `AgentGroupedUsageSnapshot` and served via HTTP with ETag caching
3. Main window polls `MonitorService.GetGroupedUsageAsync()` every 2-60 seconds
4. `GroupedUsageDisplayAdapter.Expand()` flattens the snapshot into `List<ProviderUsage>`
5. `MainWindowRuntimeLogic.PrepareForMainWindow()` filters by visibility and state
6. `RenderProviders()` builds the card UI

### Settings dialog interaction

- Settings loads its own copy of `_configs` and `_usages` from the monitor
- Config changes are auto-saved with 600ms debounce via `PersistAllSettingsAsync`
- Settings always shows all default providers (ShowInSettings=true) as configuration slots
- On close, `DialogResult = true` triggers main window to call `InitializeAsync()` which re-fetches everything
- After config saves/removals, `MonitorService.InvalidateGroupedUsageCache()` is called to prevent stale ETag responses

### Provider settings modes

- **StandardApiKey** — User-editable API key field (Synthetic, Mistral, Kimi, etc.)
- **SessionAuthStatus** — Session-based auth with status display (Codex, OpenAI)
- **AutoDetectedStatus** — Auto-discovered, read-only (Antigravity, OpenCode Zen)
- **ExternalAuthStatus** — External auth flow (GitHub Copilot)

Only StandardApiKey providers can have their keys deleted by the user.



## Documentation Maintenance

Before creating a new doc, check `docs/INDEX.md` — it may already exist. When creating or archiving docs, update the index.

After implementing any code change, check if the change affects any documentation in `docs/`. If a doc describes behaviour, architecture, or file interactions that your change modified, update the doc to stay accurate.

When updating a doc, add or update a review header immediately below the title:

```
# Document Title
> Last reviewed: task-NNN — DD-MM-YYYY
```

Replace `task-NNN` with the task ID that triggered the update, and `DD-MM-YYYY` with today's date.

## Session Start

When a conversation starts — fresh window, new session, or after context compression — orient before doing anything else:

1. **Run `orient`** — single call that returns cycle number, task counts, in-progress/in-review tasks, strategy review cadence, trends, and recommended next action.
2. **Fix orphaned tasks silently** — check for feat/task-XXX branches that don't match board status. Fix and report after.
3. **Summarise:** "You're on Cycle N. X tasks to build, Y builds pending review." or "Cycle N is complete — ready for the next plan."
4. **Run `build_list` when picking a task** — `orient` shows counts only. `build_list` shows the full task list with handoffs.

**CRITICAL: Check task statuses before acting.**
- **In Review** = already built. Suggest `review_list` → `review_submit`. **NEVER re-build an In Review task.**
- **In Progress** = build started but not completed. Check the branch and existing changes before writing new code.
- **Backlog** = not started. But first check if a `feat/task-XXX` branch already exists with commits — fix it, don't rebuild.
- If all cycle tasks are Done, suggest `release` or next `plan`.

## Workflow Sequences

PAPI tools follow structured flows. The agent manages the cycle workflow automatically — the user should never need to type tool names or remember the flow. Handle the plumbing, surface the summaries.

### Cycle Workflow (auto-managed)

- **Run tools automatically** — don't ask the user to invoke MCP tools manually
- Before implementing: silently run `build_execute <task_id>` (start phase)
- After implementing: run `build_execute <task_id>` (complete phase) with report fields
- After build_execute completes: audit the branch changes for bugs, convention violations, and doc drift (see Post-Build Audit below)
- After audit with findings: *MUST* automatically run `review_submit` with verdict `request-changes` and a concise summary of the audit findings as the changes requested — the builder fixes these before the task goes to human review
- After audit clean: present for human review — "Ready for your review — approve or request changes?"
- User approves/requests changes → run `review_submit` behind the scenes

### The Cycle (main flow)

```
plan → build_list → build_execute → audit → review_list → review_submit → build_list
```

1. **plan** — Run at the start of each cycle to generate the cycle plan and populate the board.
   Next: `build_list` to see prioritised tasks.
2. **build_list** — View tasks ready for execution, ordered by priority.
   Next: `build_execute <task_id>` to start a task.
3. **build_execute** (start) — Creates a feature branch and marks the task In Progress. Returns the build handoff.
   Next: Implement the task, then `build_execute <task_id>` again with report fields to complete.
4. **build_execute** (complete) — Submits the build report, commits, and marks the task In Review.
   Next: Run the post-build audit automatically.
5. **Post-build audit** — Review branch changes for bugs, convention violations, and doc drift (see Post-Build Audit section below).
   Next: If findings exist, run `review_submit` with `request-changes` and the audit findings. If clean, proceed to `review_list`.
6. **review_list** — Shows tasks pending human review (handoff-review or build-acceptance).
   Next: `review_submit` to approve, accept, or request changes.
7. **review_submit** — Records the review verdict and updates task status.
   Next: `build_list` to view next build

### Strategy Review

```
strategy_review → strategy_change
```

- **strategy_review** — Analyses project health, velocity, and estimation accuracy.
  Next: `strategy_change` if the review recommends adjustments.
- **strategy_change** — Updates active decisions, north star, or project direction based on review findings.

### Detect Strategic Decisions in Conversation

Watch for: direction changes, architecture shifts, deprioritisation with reasoning, new principles, competitive positioning decisions.

When detected:
1. Flag it: "That sounds like a strategic direction change — should I run `strategy_change`?"
2. If confirmed, run `strategy_change` immediately.
3. If mid-build, finish the current task first.

### Idea Capture

```
idea → (picked up by next plan)
```

- **idea** — Captures a new task idea and writes it to the backlog.
  Next: The next `plan` run will prioritise and schedule it.

### Project Bootstrap

```
setup → plan
```

- **setup** — Initialises the project in the database and scaffolds config files.
  Next: `plan` to run the first cycle planning session.

### Board Management

- **board_view** — Read-only view of all tasks on the board.
- **board_archive** — Removes completed/cancelled tasks from the board to an archive.
- **board_deprioritise** — Moves a task to a later phase.

### Quick Reference: Tool → Next Step

| Tool | Next Step |
|------|-----------|
| `setup` | `plan` |
| `plan` | `build_list` |
| `build_list` | `build_execute <task_id>` |
| `build_execute` (start) | Implement, then `build_execute` (complete) |
| `build_execute` (complete) | Post-build audit (automatic) |
| Audit (findings) | `review_submit` with `request-changes` |
| Audit (clean) | `review_list` |
| `review_list` | `review_submit` |
| `review_submit` (approve/accept) | `build_list` |
| `review_submit` (request-changes) | `build_execute` (redo) or `build_list` |
| `strategy_review` | `strategy_change` (if needed) |
| `idea` | Next `plan` picks it up |

## Post-Build Audit

After every `build_execute` (complete), audit the branch before presenting for human review. This catches bugs and convention violations early.

1. **Identify changed files:** Run `git diff origin/main --name-only` to find modified files. If no changes, report "No changes to audit" and skip.
2. **Review each changed file** for:
   - Logic errors, off-by-one mistakes, incorrect conditions
   - Unhandled edge cases (null, undefined, empty inputs)
   - Convention violations defined in this CLAUDE.md
   - Incorrect type narrowing or unsafe casts
3. **Documentation check:** If any `docs/` files describe behaviour that the change modified, flag as "Doc drift".
4. **Report:** For each issue: file path, severity (Bug/Convention/Doc drift), what's wrong, how to fix.
5. **If findings exist:** Run `review_submit` with `request-changes` and the findings. Fix before human review.
6. **If clean:** Present for human review — "Ready for your review — approve or request changes?"

## When to Start a New Conversation

Start a fresh window when:
- **After a release** — cycle is done, context is heavy. New window orients in seconds via `orient`.
- **After 3+ tasks built** — accumulated file reads, diffs, and discussions bloat context. Quality degrades.
- **Switching modes** — going from building to planning, or from strategy review to building. Each mode benefits from clean context.
- **After context compression fires** — if you notice earlier messages are missing, the window is getting stale. Open fresh.

Stay in the same window when:
- Building sequential tasks in a batch (especially XS/S tasks)
- Mid-task and not yet complete
- Having a strategic discussion that informs the next action

**Rule of thumb:** If you've been in the same window for 30+ minutes or 3+ tasks, it's time for a fresh one.

## Housekeeping — Fix Orphaned Tasks

On session start, silently check for and fix:
1. **Orphaned branches:** `git branch | grep feat/task` — cross-reference with board status. Fix by merging or flagging.
2. **In Review tasks with no PR:** If branch is already merged to main, the review_submit step was missed.
3. **Stale In Progress:** Branch has no recent commits — flag it.
4. **Config mismatches:** `.mcp.json` has DATABASE_URL but PAPI_ADAPTER is still `md` — flag it.

**Do this automatically and silently.** Report what you found and fixed.

## Plumbing Is Autonomous

Board status updates, branch cleanup, orphaned task fixes, commit/PR/merge for housekeeping — these are mechanical plumbing. **Do them end-to-end without stopping to ask.** Report after the fact.

## Context Compression Recovery

When the system compresses prior messages, immediately:
1. **Run `orient`** — single call for cycle state
2. Check your todo list for in-progress work
3. Run housekeeping checks
4. **NEVER re-build a task that is already In Review or Done.**
5. Continue where you left off — don't restart or re-plan

## Branching & PR Convention

- **XS/S tasks in the same cycle and module:** Group on shared branch. One PR, one merge.
- **M/L tasks or different modules:** Own branch per task. Isolated PRs.
- **Dependent tasks (any size):** When a task's BUILD HANDOFF lists a `DEPENDS ON` task from the same cycle, `build_execute` automatically reuses the upstream task's branch so commits stack for a single PR. Do not create a separate branch manually.
- **Commit per task within grouped branches** — traceable git history.
- **Never use `build_execute` with `light=true` on shared branches.** Light mode commits directly to the current branch without creating a PR. When a shared branch is squash-merged, those commits are collapsed — any CLAUDE.md or documentation changes are stripped. Use light mode only on isolated single-task branches where no squash-merge will occur.

## Quick Work vs PAPI Work

PAPI is for planned work. Quick fixes — just do them. No need for plan or build_execute.

**After completing quick/ad-hoc work** (bug fixes, config changes, small improvements done outside the cycle), call `ad_hoc` to record it. This creates a Done task + build report so the work appears in cycle history and metrics. Don't skip this — unrecorded work is invisible work.

## Data Integrity

- **Use MCP tools for all project data operations.** DB is the source of truth when using the pg adapter.
- Do NOT read `.papi/` files for context — use MCP tools.
- `.papi/` files may be stale when using pg adapter. This is expected.
- **`board_edit` never updates the `cycle` field.** When moving a task into or out of a cycle, always run a SQL update alongside `board_edit`:
  - Adding to current cycle: `UPDATE cycle_tasks SET cycle = <N> WHERE display_id = '<task-id>';`
  - Removing from cycle (backlog): `UPDATE cycle_tasks SET cycle = null WHERE display_id = '<task-id>';`

## Code Before Claims — No Assumptions

**Before making any claim about how the codebase works, read the relevant file first.**

This includes:
- How a feature is implemented ("it works like X") → read the source
- Whether something exists ("there's no baseline migration") → check the directory
- Whether a flow is broken or working → trace it in code
- What a user would experience → check the actual page/component

Do NOT rely on memory, prior conversation, or inference. Read first, then answer.
If the answer requires checking 2-3 files, check them all before responding.

## Process Rules

These rules come from 80+ cycles of dogfooding. They prevent the most common sources of wasted time and rework.

### Building
- **Verify before claiming done.** Hit the endpoint, check the rendered output, confirm the data round-trips. Never say "should work" — prove it works.
- **Preview frontend changes.** After any UI/styling build, provide the localhost URL so the user can visually review. Don't make them ask for it.
- **Debug one change at a time.** When fixing issues, make one change, verify it, then move on. Don't stack multiple untested fixes.
- **Test the write-read roundtrip.** Every data write path must have a verified read path. If you write to DB, confirm the read query returns what was written. This is the #1 source of silent failures.
- **Test after every build.** Run the project's test suite after implementing. Suggest follow-up tasks from learnings when meaningful.
- **Build patiently.** Validate each phase against the last. Don't rush through implementation — test through the UI, not just the API.

### Security
- **Audit before widening access.** Before any build that adds endpoints, modifies auth/RLS, introduces new user types, or changes access controls — review the security implications first. Fix findings before shipping.
- **Flag access-widening changes.** If a build touches auth, RLS policies, API keys, or user-facing access, note "Security surface reviewed" in the build report's `discovered_issues` or `architecture_notes`.
- **Never ship secrets.** Do not commit .env files, API keys, or credentials. Check `.gitignore` covers sensitive files before pushing.
- **Telemetry opt-out.** PAPI collects anonymous usage data (tool name, duration, project ID). To disable, add `"PAPI_TELEMETRY": "off"` to the `env` block in your `.mcp.json`.

### Planning & Scope
- **NEVER run `plan` more than once per cycle.** Adjust the cycle with `board_deprioritise` or `idea` instead.
- **NEVER skip cycles.** Complete and release the current cycle before running the next `plan`.
- **Only build tasks assigned to the current cycle.** Use `build_list` — it filters to current-cycle tasks with handoffs.
- **Don't ask premature questions.** If the project is in early cycles, don't ask about deployment accounts, hosting providers, OAuth setup, or commercial features. Focus on building core functionality first.
- **Split large ideas.** If an idea has 3+ concerns, submit it as 2-3 separate ideas so the planner creates properly scoped tasks — not kitchen-sink handoffs.
- **Auto-release completed cycles.** When all cycle tasks are Done and reviews accepted, run `release` immediately. Forgetting causes cycle number drift and merge conflicts in the next session.

### Communication
- **Show task names, not just IDs.** When summarising board state or reconciliation, include task names — e.g. "task-42: Add supplier form" not just "task-42".
- **Surface the next command.** After each step, tell the user what comes next. Commands should be surfaced, not memorised.

### Stage Readiness
- **Access-widening stages require auth/security phases.** Before declaring a stage complete, check if it widens who can access the product (e.g. Alpha Distribution, Alpha Cohort). If so, auth hardening and security review must be completed first — not discovered after the fact.
- **Pattern:** Audit access surface → fix vulnerabilities → then widen access. Never ship access-widening without a security phase.

## PAPI Project Conventions

### Build & Test Commands
- Build: `dotnet build AIUsageTracker.sln --configuration Debug`
- Test: `dotnet test AIUsageTracker.Tests/AIUsageTracker.Tests.csproj --configuration Debug`
- Pre-commit validation: `./scripts/pre-commit-check.sh`
- Publish: `.\scripts\publish-app.ps1 -Runtime win-x64`

### Code Style Conventions
- File-scoped namespaces: `namespace AIUsageTracker.Core.Models;`
- Allman-style braces (opening brace on new line)
- 4-space indentation, no tabs
- Private fields: `_camelCase` with underscore prefix
- PascalCase for classes, methods, properties
- Async methods end with `Async` suffix
- Nullable reference types enabled globally — always handle potential nulls

### Testing Conventions
- xUnit with Moq for mocking
- Arrange-Act-Assert pattern
- Descriptive test names: `MethodName_Scenario_ExpectedBehavior`
- Test both success and failure paths
- Provider fixtures must be based on real provider responses (sanitized), not invented values
- UI startup tests must verify no deadlocks in async loading

### Error Handling
- Never use silent catch blocks — always log with context
- Use `_logger.LogError(ex, "message")` for unexpected errors
- Return error state objects for provider failures rather than throwing
- Never throw in async void methods — use async Task

### Architecture Conventions
- Database stores raw values only — ProviderDefinition classes are the authority for interpretation
- Never duplicate provider metadata into DB tables
- Serve cached data immediately on startup; only refresh system providers (no external API hammering)
- Never delete customer data automatically — filter placeholder data at the source before storing

## Dogfood Logging

After each `release`, append a dogfood entry capturing observations from the cycle.
Call the adapter method with structured entries for each observation:

- **friction** — workflow pain points, confusing flows, things that broke or slowed you down
- **methodology** — what worked or didn't in the plan/build/review cycle
- **signal** — indicators of product-market fit, user value, or growth potential
- **commercial** — cost, pricing, or business model observations

This is autonomous plumbing — log observations after release without asking.
