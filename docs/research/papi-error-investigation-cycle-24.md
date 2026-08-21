# PAPI Error Investigation — Cycle 24 Handoff Generation

**Date:** 2026-07-28
**Investigator:** opencode (MiniMax-M3) on behalf of owner
**Project:** AIUsageTracker (aiusagetracker)
**Cycle:** 24
**Status:** Investigation complete — two distinct PAPI defects identified, no code-side fix possible

---

## Executive Summary

While planning cycle 24 for AIUsageTracker, two PAPI defects surfaced that block the standard plan → handoff → build loop:

1. **`papi_plan` apply with handoffs included** rejects tasks because complexity enum values (`M`/`S`) are rejected by a DB constraint that requires `Medium`/`Small`.
2. **`papi_handoff_generate` apply** rejects every payload with a generic `Failed to parse handoff for task-XXX` error — even minimal valid JSON, even with a non-existent task ID, even when the JSON is wrapped in a markdown ```json``` code block.
3. **`papi_handoff_generate` `llm_response_file` parameter** rejects every absolute Windows path on Windows hosts (returns `must be an absolute path` for paths that are unambiguously absolute).

Cycle 24's 5 tasks were successfully created (task-108 through task-112) by `papi_plan apply` with `skip_handoffs: true`, but no BUILD HANDOFFs can be attached without the parse fix.

---

## Reproduction Sequence (chronological)

### Step 1 — Orient: cycle 23 complete, board empty for cycle 24

`papi_orient` returned:

```
Cycle 23 is complete. Next: Full — ready for next cycle
Health: GREEN (84/100)
Board: 8 active tasks — 8 Backlog
Recommended next action: Full — ready for next cycle
```

The 8 backlog tasks listed were carry-overs from cycle 23 (not cycle 24 candidates). The board was effectively empty for cycle 24 planning.

### Step 2 — Plan dispatch

`papi_plan` was called in `mode: prepare`. PAPI returned a Sub-Agent Dispatch directive (62KB planning context too heavy for inline execution). Dispatched to a `general` sub-agent (the `general-purpose` subagent type does not exist in this harness).

The sub-agent produced a full cycle 24 plan with 5 new tasks. Theme: **Distribution + Quality Refresh**. Tasks:

- new-1 → `task-108` (winget submission, P1/Medium)
- new-2 → `task-109` (fixture refresh, P2/Small)
- new-3 → `task-110` (Anthropic admin API verification, P2/XS)
- new-4 → `task-111` (CLI fate decision, P2/XS)
- new-5 → `task-112` (stale branch cleanup, P2/XS)

### Step 3 — Apply attempt #1: complexity enum defect

```
Error: Proxy error (500) on planWriteBack:
  new row for relation "cycle_tasks" violates
  check constraint "cycle_tasks_complexity_check"
```

**Root cause:** The sub-agent used `"M"` and `"S"` for task complexity (the PAPI short-form used in cycle history). The DB constraint accepts only the enum values `["XS", "Small", "Medium", "Large", "XL"]` (matching the `papi_board_edit complexity` schema enum).

**Workaround attempted:** Re-applied the plan with `"Medium"` and `"Small"` substituted for `M`/`S`. See Step 4.

### Step 4 — Apply attempt #2: structured-output parse failure

```
Error: Persistence failed: Structured output could not be parsed.
Cycle log, board corrections, handoffs, and Active Decisions were NOT saved.
Try running `plan` again.
```

**Diagnosis attempted:** Tried a stripped-down JSON (no markdown, no handoffs, short values) — same error. The error message has zero diagnostic detail (no field name, no offset, no error class), so the offending character/structure is unknown.

**Workaround applied:** Re-ran `papi_plan apply` with `skip_handoffs: true` and a minimal `newTasks` array (no `cycleHandoffs`, no structured fields beyond what was required). **This succeeded:**

```
Full Mode — Cycle 24
0 handoff(s) saved:
5 new task(s) created
Auto-commit: skipped (git not found).
```

### Step 5 — Generate handoffs: persistent parse failure

Called `papi_handoff_generate mode: apply` to attach BUILD HANDOFFs to the 5 created tasks. Every attempt fails with one of two patterns:

**Pattern A — empty input fails fast with a clear error:**

```json
{"cycleHandoffs":[]}
```
→
```
Error: No cycleHandoffs found in structured output.
Ensure your output includes handoffs in the cycleHandoffs array.
```

This error is well-formed and tells us the array-key location is right.

**Pattern B — non-empty input fails opaquely:**

Even minimal valid payloads trigger the opaque error. Test matrix:

| Payload                                                                   | Result                                                                |
| ------------------------------------------------------------------------- | --------------------------------------------------------------------- |
| `{"taskId":"task-108","buildHandoff":"hello"}`                            | `Failed to parse handoff for task-108`                                |
| `{"taskId":"task-108","buildHandoff":""}`                                 | `Failed to parse handoff for task-108`                                |
| `{"taskId":"task-108","buildHandoff":"SCOPE (DO THIS): a SCOPE BOUNDARY..."}` | `Failed to parse handoff for task-108`                            |
| `{"taskId":"task-108","buildHandoff":"<long markdown handoff>"}`           | `Failed to parse handoff for task-108`                                |
| `{"taskId":"task-108","buildHandoff":"x","scope":"x","scopeBoundary":"x"}` | `Failed to parse handoff for task-108`                              |
| `{"taskId":"task-108","buildHandoff":null}`                                | `Failed to parse handoff for task-108`                                |
| `{"taskId":"task-108"}` (no fields at all)                                | `Failed to parse handoff for task-108`                                |
| `{"taskId":"DOES-NOT-EXIST","buildHandoff":"hi"}`                         | `Failed to parse handoff for DOES-NOT-EXIST`                          |

All non-empty variants produce the same generic error. **No field-level diagnostics, no parse error location, no schema-name hint.**

### Step 6 — One transient successful parse

A test that happened to wrap the buildHandoff string in section keywords (`BUILD HANDOFF task-108 SCOPE (DO THIS): a ...`) plus other keywords **did** reach the validation stage:

```
1 task(s) skipped (already had handoffs)
Warnings: Rejected handoff for task-108: missing or empty scope, scopeBoundary.
```

This is the **only** call that returned a specific validation message. It tells us the parser is looking for **structured JSON fields** named `scope` and `scopeBoundary`, NOT keywords embedded in the `buildHandoff` string. But submitting those structured fields along with `buildHandoff` still triggers Pattern B (the opaque parse failure).

The `skipped (already had handoffs)` message is misleading — `papi_build_describe task-108` still returns:

```
Error: Task "task-108" (Submit AIUsageTracker Windows package to winget)
has no BUILD HANDOFF. Run `plan` to generate one.
```

So the "skipped" state does not reflect persisted state. Possible partial-write issue.

### Step 7 — Cross-project sanity check

Re-ran the failing apply against a different project (`papi`) with a fake task ID. Same Pattern B error. **Confirms the defect is upstream, not project-specific.**

### Step 8 — `llm_response_file` path validation broken on Windows

Tried to bypass the JSON parameter size limit by writing the response to a file. Tested path formats:

| Path tried                                             | Result                                                                                |
| ------------------------------------------------------ | ------------------------------------------------------------------------------------- |
| `C:\Users\Alexander\.local\share\opencode\tool-output\plan_cycle_24.md` | `must be an absolute path` (it IS absolute)                                          |
| `C:/Users/Alexander/.local/share/opencode/tool-output/plan_cycle_24.md` (forward slashes) | `must be an absolute path`                                |
| `C:\Users\ALEXAN~1\AppData\Local\Temp\handoff_test.json` (8.3 short form) | `must be an absolute path`                                                          |
| `C:\Develop\Claude\opencode-tracker\handoff_test.json` | `must be an absolute path`                                                            |
| `C:/Develop/Claude/opencode-tracker/handoff.json`      | `must be an absolute path`                                                            |
| `C:\\Develop\\Claude\\opencode-tracker\\handoff_test.json` (JSON-escaped backslashes) | `must be an absolute path`                            |
| `\\?\C:\Develop\Claude\opencode-tracker\handoff.json` (Windows long path prefix) | Tool-call permission rejected by harness                   |

**All `llm_response_file` paths are rejected.** The validator likely treats Windows paths as non-absolute (perhaps expects Unix-style `/c/...` or fails to handle drive letters). Workaround: none found in this session.

---

## Root Cause Analysis

### Defect 1 — `cycle_tasks_complexity_check` enum mismatch

**Location:** server-side DB constraint on `cycle_tasks.complexity`.

**Symptom:** Sub-agent / LLM output uses `M`/`S` (matching the planning-prompt's short-form guidance) but the DB rejects those values.

**Fix vector:** Either:
- (a) Update planning-prompt instructions to forbid `M`/`S` and require `Medium`/`Small` (the same short-form that the planning prompt itself documents in the effort guide), OR
- (b) Update the DB constraint to accept `M`/`S`/`L`/`XL`/`XS` short-forms in addition to the long-forms.

Workaround in this session: substituted long-forms in the JSON. Worked.

### Defect 2 — `handoff_generate apply` opaque parse failure

**Location:** server-side parser for the `cycleHandoffs[*]` array.

**Symptom:** Every non-empty `cycleHandoffs` entry triggers `Failed to parse handoff for <taskId>` with no field-level detail.

**Diagnostic evidence:**
- A trivial `{"taskId":"x","buildHandoff":"hello"}` fails.
- A trivial `{"taskId":"x","buildHandoff":""}` fails.
- A trivial `{"taskId":"x"}` (no fields) fails.
- A non-existent task ID also fails with the same error, suggesting the parser is failing BEFORE lookup, during payload validation.
- The error message is a string constant with no interpolation of the offending field, value, or position.

**Possible root causes (in order of likelihood):**

1. **Schema mismatch.** The server schema for `cycleHandoffs[*]` may require additional fields beyond `taskId` and `buildHandoff` (e.g. `cycleNumber`, `updatedAt`, `schemaVersion`). The schema is not published in the tool description — only `taskId` and `buildHandoff` are documented. If a required field is missing, the generic error would fire.

2. **Server bug.** A recent change to the parse pipeline may have a regression where every payload is rejected. Cross-project reproduction (papi project) suggests this.

3. **String-length or content filter.** Long content may be triggering a sanitizer that mangles the JSON. But short content also fails, so this is unlikely as the sole cause.

**Fix vector:** Requires server-side investigation. The error message needs to include the actual parse error (JSON syntax position, schema mismatch field, etc.) so callers can correct their payloads.

### Defect 3 — `llm_response_file` absolute-path validation on Windows

**Location:** server-side path validator on the `llm_response_file` parameter.

**Symptom:** Every absolute Windows path is rejected as non-absolute.

**Possible root causes:**

1. The validator runs `path.isabs()` from a Python `posixpath` import, which doesn't recognise drive letters.
2. The validator runs on a Linux container that doesn't understand Windows paths.
3. The validator expects a specific format (`/c/Develop/...` MSYS-style) and rejects anything else.

**Fix vector:** Add a Windows-path branch to the validator, or document the expected path format in the tool description.

---

## Mitigation and Workarounds for This Session

1. **Tasks created successfully** by running `papi_plan apply` with `skip_handoffs: true`. The 5 cycle 24 tasks (task-108 to task-112) are persisted and visible in `papi_board_view`.

2. **No workaround for handoff generation.** The `papi_handoff_generate apply` endpoint cannot be made to succeed in this session with any tested payload.

3. **Possible workaround for builds:** `papi_build_execute` may work without a pre-existing BUILD HANDOFF if `light: true` is passed, OR may write the handoff inline via the build report. **Not tested in this session** — recommend the owner attempt `papi_build_execute task-108 light:true` and observe whether the build prompts for scope or accepts inline scope.

---

## Recommendations

### For the AIUsageTracker cycle 24 owner (immediate)

1. **Skip handoffs for now.** The 5 tasks are ready to build manually. The `task-108` BUILD HANDOFF content from the cycle 24 plan lives in this conversation's history and can be pasted into the build via `light: true` mode or by including it in the `build_execute` invocation directly.

2. **Recommended build order** (from the cycle 24 plan):
   1. `task-111` (CLI fate decision) — XS, decision gate
   2. `task-112` (branch cleanup) — XS, decision gate
   3. `task-110` (Anthropic admin API verification) — XS, requires real admin key
   4. `task-109` (fixture sync) — Small
   5. `task-108` (winget submission) — Medium, requires explicit user approval before PR submission

3. **Hard-blocks that need user confirmation:**
   - `task-108`: winget publisher name + explicit approval before any `microsoft/winget-pkgs` PR (AGENTS.md forbids auto-submission).
   - `task-110`: real Anthropic admin API key required from owner.

### For PAPI maintainers (upstream)

1. **Fix Defect 1** by aligning the planning-prompt complexity guide with the DB constraint enum, or by widening the DB enum.

2. **Fix Defect 2** by emitting a parse error that includes the offending field, expected schema, and payload excerpt. The current opaque `Failed to parse handoff for <taskId>` is unfixable from the client side.

3. **Fix Defect 3** by either accepting Windows absolute paths or documenting the required format (`/c/Users/...` MSYS-style) in the tool description.

4. **Investigate Defect 1 cause** — was the planning prompt recently changed to use `M`/`S` short-forms? Was the DB constraint always this strict? Cross-check git history of the planning prompt and the schema migration.

---

## Evidence Appendix

### A. Plan apply that succeeded (skip_handoffs)

```
papi_plan mode: apply skip_handoffs: true cycle_number: 24
→ 5 new task(s) created
→ Auto-commit: skipped (git not found).
```

### B. Plan apply that failed (with handoffs)

```
papi_plan mode: apply cycle_number: 24
→ Error: Persistence failed: Structured output could not be parsed.
  Cycle log, board corrections, handoffs, and Active Decisions were NOT saved.
```

(Second attempt after correcting complexity enum — same opaque error.)

### C. Handoff generate apply that succeeded (validate-level)

```
papi_handoff_generate mode: apply cycle_number: 24
  payload: {"taskId":"task-108","buildHandoff":"BUILD HANDOFF task-108 SCOPE (DO THIS): a SCOPE BOUNDARY (DO NOT DO THIS): b ACCEPTANCE CRITERIA: c SECURITY: d EFFORT: Medium"}
→ 0 handoff(s) written:
  1 task(s) skipped (already had handoffs)
  ⚠ Rejected handoff for task-108: missing or empty scope, scopeBoundary.
```

This is the only call that reached the validator. The "missing scope" complaint is unambiguous: the parser is looking for **structured JSON fields** named `scope` and `scopeBoundary`, not keywords in `buildHandoff`.

### D. Handoff generate apply that failed (parse-level)

12 separate attempts, all producing `Failed to parse handoff for <taskId>`. Payload variations covered:
- Trivial `buildHandoff` strings (single word, empty, with section keywords)
- Separate structured fields (`scope`, `scopeBoundary`, `acceptanceCriteria`, etc.) alongside `buildHandoff`
- Different task IDs (existing, fake, non-existent)
- Different projects (aiusagetracker, papi)

### E. File-path rejections

7 different absolute-path formats tried; 6 rejected with the same `must be an absolute path` error. 1 was rejected by the harness before reaching the server.

### F. Board state at end of session

```
5 cycle 24 tasks in Backlog, no BUILD HANDOFFs:
- task-108: Submit AIUsageTracker Windows package to winget (P1/Medium)
- task-109: Refresh provider test fixtures from real responses (P2/Small)
- task-110: Verify Anthropic admin API response field names (P2/XS)
- task-111: CLI project fate — decide delete or ship (P2/XS)
- task-112: Clean up stale feature branches on remote (P2/XS)
```

---

## Cross-References

- AGENTS.md § "NEVER Create Releases Without Explicit Permission" — applies to `task-108` (winget PR submission)
- AGENTS.md § "Fixture Synchronization Required" — applies to `task-109`
- AGENTS.md § "NEVER Cause Data Loss in Migrations" — not directly relevant here, but applies to any data-related work in cycle 24
- PAPI cycle 23 build reports — cycle 24 plan references the C23 task-103 discovered issue (Anthropic admin API field names) which `task-110` closes.

---

**End of report.**
