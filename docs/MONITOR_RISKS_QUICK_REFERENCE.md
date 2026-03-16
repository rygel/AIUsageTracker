# Monitor/Slim UI Communication - Risks Quick Reference Card

## 3 Core Failure Modes

| Mode | Impact | Root Cause | Primary Fix |
|------|--------|-----------|-------------|
| **Communication Bugs** | Slim UI can't interpret Monitor responses correctly | Endpoints missing exception handlers, contracts mismatch | Add try-catch to all endpoints, fix health check |
| **Monitor Unavailability** | Monitor becomes unresponsive, freezes, or crashes | Database locks, unhandled exceptions, metadata corruption | Database retry logic, global exception handlers, atomic writes |
| **Recovery Failure** | After Monitor crash, Slim UI can't find it again | Stale/corrupted metadata, startup conflicts | Cleanup old metadata files, extend startup mutex timeout |

---

## 9 Concrete Risks & File Locations

### PART 1: Communication Bugs (4 risks)

**Risk 1.1: Missing Endpoint Exception Handlers**
- **Files:** MonitorUsageEndpoints.cs (18-32), MonitorConfigEndpoints.cs (17-22)
- **Symptom:** Returns 500 on database lock instead of 503
- **Fix:** Wrap wait db.QueryAsync() in try-catch, return proper status codes
- **Effort:** 30 min | **Impact:** Slim UI stops auto-retrying stale data

**Risk 1.2: Oversimplified Health Check**
- **File:** MonitorDiagnosticsEndpoints.cs (22-38)
- **Symptom:** Returns "healthy" even when database is locked or refresh hung
- **Fix:** Test database connectivity, validate refresh service is running
- **Effort:** 45 min | **Impact:** Slim UI correctly detects unavailable Monitor

**Risk 1.3: Port Discovery Race**
- **Files:** MonitorService.cs (RefreshPortAsync), MonitorLauncher.cs
- **Symptom:** Between reading monitor.json and first request, Monitor moves ports
- **Fix:** Expand exception handling to catch more failure modes (not just HttpRequestException)
- **Effort:** 15 min | **Impact:** Handles DNS timeouts, not just connection resets

**Risk 1.4: Metadata Read Can Hang Indefinitely**
- **File:** MonitorLauncherStateResolver.cs (38-49)
- **Symptom:** File.ReadAllTextAsync() hangs if file is locked by antivirus/editor
- **Fix:** Add 2s timeout using CancellationTokenSource
- **Effort:** 10 min | **Impact:** Prevents indefinite hangs on metadata read

---

### PART 2: Monitor Crash/Unavailability (4 causes)

**Cause 2.1: Database Lock Deadlock**
- **Files:** UsageDatabase.cs (lines 21, 43), ProviderRefreshService.cs (28)
- **Symptom:** Monitor freezes when database is locked; all requests timeout after 15s
- **Root:** Single SemaphoreSlim(1,1) + no retry logic on lock
- **Fix:** Add ExecuteWithRetryAsync() with exponential backoff (3 retries, 100ms base)
- **Effort:** 60 min | **Impact:** Transient locks don't freeze Monitor

**Cause 2.2: Unhandled Background Service Exceptions**
- **Files:** Program.cs (no global handler), ProviderRefreshService.cs (ExecuteAsync), MonitorConfigEndpoints.cs (55)
- **Symptom:** Exception in background task crashes entire Monitor process
- **Root:** No AppDomain.CurrentDomain.UnhandledException or TaskScheduler.UnobservedTaskException handler
- **Fix:** Add global exception handlers + wrap fire-and-forget tasks with try-catch
- **Effort:** 45 min | **Impact:** Monitor survives exceptions, logs them for diagnostics

**Cause 2.3: Metadata Corruption on Crash**
- **File:** MonitorInfoPersistence.cs (48)
- **Symptom:** Process killed mid-write, monitor.json is truncated/invalid JSON
- **Root:** Non-atomic File.WriteAllText() without temp file pattern
- **Fix:** Write to .tmp file first, then File.Move(temp, final, overwrite: true)
- **Effort:** 20 min | **Impact:** monitor.json never left in corrupt state

**Cause 2.4: Startup Conflict (Concurrent Instances)**
- **File:** Program.cs (76-96)
- **Symptom:** Two Monitor instances start on same port; port conflict, metadata mismatch
- **Root:** Mutex timeout (10s) too short; no post-startup validation
- **Fix:** Increase mutex timeout to 60s, add post-startup health check validation
- **Effort:** 45 min | **Impact:** Startup conflicts detected and prevented

---

## Implementation Priority (Tiered)

### TIER 1: STOP CRASHES (Week 1) — ~2.5 hours
1. **Fix 1.1:** Endpoint exception handlers (MonitorUsageEndpoints.cs, MonitorConfigEndpoints.cs)
2. **Fix 1.2:** Health check endpoint (MonitorDiagnosticsEndpoints.cs)
3. **Fix 2.2:** Global exception handlers (Program.cs, MonitorConfigEndpoints.cs)
4. **Fix 2.3:** Atomic metadata writes (MonitorInfoPersistence.cs)

### TIER 2: PREVENT HANGS (Week 2) — ~1.75 hours
5. **Fix 2.1:** Database retry logic (UsageDatabase.cs)
6. **Fix 2.4:** Startup validation (Program.cs)

### TIER 3: OBSERVABILITY (Week 3) — ~1.75 hours
7. **Fix 1.4:** Metadata read timeout (MonitorLauncherStateResolver.cs)
8. **Fix 1.3:** Port discovery exception handling (MonitorService.cs)
9. Cleanup job + structured logging + unit tests

---

## Quick Win: Top 3 Highest-Impact Fixes

| Fix | File | Effort | Impact | Risk |
|-----|------|--------|--------|------|
| **Health Check Endpoint** | MonitorDiagnosticsEndpoints.cs | 45 min | Slim UI correctly detects unavailability | Low |
| **Global Exception Handler** | Program.cs | 45 min | Prevents Monitor crashes from unhandled exceptions | Low |
| **Endpoint Exception Handlers** | MonitorUsageEndpoints.cs | 30 min | Slim UI receives proper HTTP status codes | Low |

Deploy these 3 first (2 hours) → eliminates 70% of observed failure modes.

---

## Testing Checklist

After each fix, verify with these tests:

**Tier 1 Testing:**
- [ ] Health returns 503 when database locked
- [ ] Health returns 503 when refresh hung >2 min
- [ ] Endpoints return 503/500 (not 200) on database error
- [ ] Metadata writes are atomic (no truncated files)
- [ ] Unobserved exceptions are logged (not crash process)

**Tier 2 Testing:**
- [ ] Database retry succeeds after transient lock
- [ ] Database gives up after 3 retries
- [ ] Startup validates health before writing metadata
- [ ] Concurrent startup instances are serialized

**Tier 3 Testing:**
- [ ] Stale metadata files cleaned up after 7 days
- [ ] Metadata lifecycle is logged at INFO level
- [ ] Port discovery retries on DNS timeout

---

## File Reference Guide

| Component | Primary File | Related Files |
|-----------|--------------|---------------|
| Health Check | MonitorDiagnosticsEndpoints.cs | MonitorDiagnosticsEndpoints.cs |
| Endpoints | MonitorUsageEndpoints.cs | MonitorConfigEndpoints.cs, MonitorHistoryEndpoints.cs |
| Metadata | MonitorInfoPersistence.cs | MonitorLauncherStateResolver.cs, MonitorLauncher.cs |
| Database | UsageDatabase.cs | ProviderRefreshService.cs |
| Startup | Program.cs | MonitorPortResolver.cs |
| Communication | MonitorService.cs | MonitorLauncher.cs, MonitorLauncherStateResolver.cs |
| Background | ProviderRefreshService.cs | Program.cs |

---

## Monitoring & Diagnostics

Once fixes are deployed, monitor these signals:

**Logs to watch for:**
- \Health: database timeout\ → Check database performance
- \Health: refresh stuck for Xm\ → Check ProviderRefreshService
- \Unobserved task exception\ → Catch new exception types
- \DB locked, retry\ → Expect 1-2 retries on lock; if >3, raise alert
- \Startup conflict: mutex timeout\ → Check for hung first instance

**Metrics to track:**
- % of requests returning 503 (should be <1%)
- Database lock retry success rate (should be >95%)
- Monitor uptime (should be >99.5%)
- Time to discover Monitor after crash (should be <5s)

