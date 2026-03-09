# Monitor/Slim UI Communication - Hardening Roadmap

## Executive Summary

Three core failure modes threaten Monitor/Slim UI communication stability:

1. **Communication Bugs** (endpoints missing error handling, contracts mismatch)
2. **Monitor Unavailability** (database locks, background service hangs, metadata corruption)
3. **Recovery Failures** (stale metadata, startup races, unhandled exceptions)

This roadmap prioritizes 9 concrete fixes across Tier 1 (Stop Crashes) → Tier 2 (Prevent Hangs) → Tier 3 (Observability).

---

## PART 1: LIKELY COMMUNICATION BUGS

### Bug #1: Endpoints Missing Exception Handlers
**Files:**
- AIUsageTracker.Monitor/Endpoints/MonitorUsageEndpoints.cs (lines 18-32, 35-46)
- AIUsageTracker.Monitor/Endpoints/MonitorConfigEndpoints.cs (lines 17-22)
- AIUsageTracker.Monitor/Services/UsageDatabase.cs (connection failures)

**Risk:** When database is locked or unavailable, endpoints throw 500 errors instead of returning proper status codes. Slim UI can't distinguish transient failures from permanent ones.

**Root Cause:**
- Endpoints call wait db.GetLatestHistoryAsync() with no try-catch
- Database lock (SQLite code 5) causes TaskCanceledException after 15s timeout
- HTTP 500 returned instead of 503 Service Unavailable

**Example Failure Scenario:**
1. ProviderRefreshService is running a large query (holding semaphore)
2. Slim UI clicks "Refresh Usage" → calls GET /api/usage
3. Endpoint tries to acquire semaphore, times out after 15s
4. Slim UI receives 500 error, stops retrying
5. Actually, refresh finishes 30s later, but Slim UI never retries

**Hardening Fix:**
`csharp
// Pattern for ALL endpoints in Monitor/Endpoints/
app.MapGet(route, async (UsageDatabase db, ILogger logger) => {
    try {
        var data = await db.QueryAsync();
        return Results.Ok(data);
    } catch (TaskCanceledException) {
        logger.LogWarning("Database timeout");
        return Results.StatusCode(503);  // Service Unavailable
    } catch (Exception ex) {
        logger.LogError(ex, "Unexpected error");
        return Results.StatusCode(500);
    }
});
`

**Effort:** ~30 min (copy pattern to 5 endpoints)
**Impact:** Slim UI stops receiving misleading 500 errors for transient database issues

---

### Bug #2: Health Endpoint Too Simple
**File:** AIUsageTracker.Monitor/Endpoints/MonitorDiagnosticsEndpoints.cs (lines 22-38)

**Risk:** Health endpoint returns 200 OK instantly without checking if Monitor actually works. Slim UI believes Monitor is healthy when database is corrupted or refresh service is hung.

**Current Implementation:**
`csharp
app.MapGet(MonitorApiRoutes.Health, (ILogger<Program> logger) => {
    return Results.Ok(new { status = "healthy", ... });
});
`

**Problem:**
- No database connectivity test
- No refresh service health check
- No verification that binding actually succeeded

**Hardening Fix:**
`csharp
app.MapGet(MonitorApiRoutes.Health, async (UsageDatabase db, ProviderRefreshService refresh, ILogger logger) => {
    try {
        // Test database (5s timeout)
        var historyTask = db.GetLatestHistoryAsync();
        var completed = await Task.WhenAny(historyTask, Task.Delay(5000)).ConfigureAwait(false);
        if (completed != historyTask) {
            logger.LogWarning("Health: database timeout");
            return Results.StatusCode(503);
        }
        
        // Check refresh service (should complete within 2 min)
        var telemetry = refresh.GetRefreshTelemetrySnapshot();
        var lastRefresh = telemetry.LastRefreshCompletedUtc ?? DateTime.UtcNow;
        var timeSinceRefresh = (DateTime.UtcNow - lastRefresh).TotalMinutes;
        if (timeSinceRefresh > 2 && telemetry.RefreshCount > 0) {
            logger.LogWarning("Health: refresh stuck for {Minutes}m", timeSinceRefresh);
            return Results.StatusCode(503);
        }
        
        return Results.Ok(new { status = "healthy", ... });
    } catch (Exception ex) {
        logger.LogError(ex, "Health check failed");
        return Results.StatusCode(503);
    }
});
`

**Effort:** ~45 min
**Impact:** Slim UI correctly identifies Monitor unavailability

---

### Bug #3: Port Discovery Race
**Files:**
- AIUsageTracker.Core/MonitorClient/MonitorService.cs (RefreshPortAsync, RefreshAgentInfoAsync)
- AIUsageTracker.Core/MonitorClient/MonitorLauncher.cs (GetAndValidateMonitorInfoAsync)

**Risk:** Between reading monitor.json and first request, Monitor might have crashed or moved to a different port. Slim UI makes request on stale port.

**Current Flow:**
1. Slim UI reads monitor.json at startup → port 5000
2. Monitor crashes, new instance starts on port 5001
3. Slim UI calls /api/usage on localhost:5000 → fails
4. Catches HttpRequestException (line 286-304), calls RefreshPortAsync()
5. Retries on correct port

**Gap:** If the first failure is not HttpRequestException (e.g., OperationCanceledException from DNS timeout), the retry logic is not triggered. Slim UI falls back to empty list.

**Hardening Fix:**
`csharp
// In MonitorService.GetUsageAsync(), expand exception handling:
catch (HttpRequestException) {
    await this.RefreshPortAsync().ConfigureAwait(false);
    // ... retry ...
}
catch (TaskCanceledException) {  // DNS/timeout — also refresh
    LogDiagnostic("Request timeout, refreshing port...");
    await this.RefreshPortAsync().ConfigureAwait(false);
    // ... retry ...
}
`

**Effort:** ~15 min
**Impact:** Handles more failure modes during port discovery

---

### Bug #4: JSON Deserialization Timeout
**File:** AIUsageTracker.Core/MonitorClient/MonitorLauncherStateResolver.cs (lines 38-49)

**Risk:** If monitor.json is locked by another process (e.g., file editor, antivirus scan), File.ReadAllTextAsync() hangs indefinitely.

**Current Code:**
`csharp
var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
`

**Hardening Fix:**
`csharp
try {
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    var json = await File.ReadAllTextAsync(path, cts.Token).ConfigureAwait(false);
    // ... deserialize ...
} catch (OperationCanceledException) {
    MonitorService.LogDiagnostic($"Timeout reading metadata from {path}, skipping");
    return (null, path);
}
`

**Effort:** ~10 min
**Impact:** Prevents indefinite hangs on metadata read

---

## PART 2: LIKELY MONITOR CRASH/UNAVAILABILITY CAUSES

### Cause #1: Database Lock Deadlock
**Files:**
- AIUsageTracker.Monitor/Services/UsageDatabase.cs (SemaphoreSlim(1,1), DefaultTimeout=15s)
- AIUsageTracker.Monitor/Services/ProviderRefreshService.cs (SemaphoreSlim(1,1) at line 28)

**Risk:** SQLite lock contention or semaphore starvation causes Monitor to freeze. All subsequent requests timeout.

**Current Architecture:**
- UsageDatabase: Single writer (1 semaphore) + shared SQLite connection pool
- ProviderRefreshService: Single refresh (1 semaphore) — blocks all provider queries

**Failure Scenario:**
1. ProviderRefreshService holds semaphore, running 5 provider queries (slow API)
2. Slim UI tries GET /api/usage → waits for semaphore
3. Slim UI tries GET /api/config → waits for semaphore
4. Both are blocked indefinitely
5. Monitor appears hung (9s request timeout passes, Slim UI gives up)

**Hardening Fix:**
`csharp
// In UsageDatabase, add retry logic:
private async Task<T> ExecuteWithRetryAsync<T>(
    Func<SqliteConnection, Task<T>> operation,
    string operationName) {
    
    const int maxRetries = 3;
    var delayMs = 100;
    
    for (int attempt = 0; attempt < maxRetries; attempt++) {
        try {
            await _semaphore.WaitAsync().ConfigureAwait(false);
            try {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync().ConfigureAwait(false);
                return await operation(connection).ConfigureAwait(false);
            } finally {
                _semaphore.Release();
            }
        } catch (SqliteException ex) when (ex.SqliteErrorCode == 5 && attempt < maxRetries - 1) {
            // Database locked — retry with exponential backoff
            logger.LogWarning("DB locked, retry {Op} in {DelayMs}ms", operationName, delayMs);
            await Task.Delay(delayMs).ConfigureAwait(false);
            delayMs *= 2;
        }
    }
    
    throw new InvalidOperationException(\$"Failed to execute {operationName} after {maxRetries} retries");
}
`

**Effort:** ~1 hour (refactor all UsageDatabase methods to use new helper)
**Impact:** Transient database locks no longer freeze Monitor

---

### Cause #2: Unhandled Exceptions in Background Services
**Files:**
- AIUsageTracker.Monitor/Program.cs (no global exception handler)
- AIUsageTracker.Monitor/Services/ProviderRefreshService.cs (fire-and-forget tasks)
- AIUsageTracker.Monitor/Endpoints/MonitorConfigEndpoints.cs (line 55: Task.Run)

**Risk:** An exception in ProviderRefreshService.ExecuteAsync or a fire-and-forget task will crash the entire Monitor process.

**Current Issues:**
1. No AppDomain.CurrentDomain.UnhandledException handler
2. No TaskScheduler.Current.UnobservedTaskException handler
3. Fire-and-forget task in MonitorConfigEndpoints.cs line 55 has no exception handler

**Failure Scenario:**
1. POST /api/config/scan-keys triggers \Task.Run(async () => refreshService.TriggerRefreshAsync())\
2. If TriggerRefreshAsync() throws OutOfMemoryException, task is unobserved
3. .NET runtime raises UnobservedTaskException
4. No handler → process crashes
5. Slim UI gets connection reset, Monitor is gone

**Hardening Fix:**
`csharp
// In Program.cs, before CreateBuilder():
AppDomain.CurrentDomain.UnhandledException += (sender, e) => {
    logger.LogCritical((Exception)e.ExceptionObject, "Unhandled AppDomain exception - crashing");
    MonitorInfoPersistence.ReportError(\$"Crash: {e.ExceptionObject}", pathProvider, logger);
    // Let process crash (will be restarted by Slim UI)
};

TaskScheduler.Current.UnobservedTaskException += (e) => {
    logger.LogCritical(e.Exception, "Unobserved task exception");
    e.SetObserved();  // Prevent immediate crash
    MonitorInfoPersistence.ReportError(\$"Task crash: {e.Exception.Message}", pathProvider, logger);
};

// In MonitorConfigEndpoints.cs line 55, wrap Task.Run:
_ = Task.Run(async () => {
    try {
        await refreshService.TriggerRefreshAsync(forceAll: true).ConfigureAwait(false);
    } catch (Exception ex) {
        logger.LogError(ex, "Background refresh failed");
    }
});
`

**Effort:** ~45 min
**Impact:** Monitor survives unhandled exceptions, logs them for diagnostics

---

### Cause #3: Metadata Corruption on Crash
**File:** AIUsageTracker.Monitor/Services/MonitorInfoPersistence.cs (line 48)

**Risk:** If process is killed during File.WriteAllText(), the monitor.json file is left in a partially-written state. Next startup, JsonDeserializer fails to parse it.

**Current Code:**
`csharp
File.WriteAllText(infoPath, json);  // Not atomic
`

**Failure Scenario:**
1. Monitor saves metadata: \File.WriteAllText("monitor.json", "{\n  port: 5000,...")\
2. Process crashes mid-write, file contains: \{\n  port: 5000,\ (truncated)
3. Next startup, Slim UI reads monitor.json, JsonDeserializer throws
4. File is quarantined (.stale.XXX), Slim UI loses port info
5. Slim UI has to wait for health check to find Monitor (adds latency)

**Hardening Fix:**
`csharp
// Atomic write using temp file + move
var tempPath = infoPath + ".tmp." + Guid.NewGuid();
try {
    File.WriteAllText(tempPath, json);
    File.Move(tempPath, infoPath, overwrite: true);
    logger.LogDebug("Wrote metadata atomically to {Path}", infoPath);
} catch (Exception ex) {
    logger.LogWarning(ex, "Failed to write metadata to {Path}", infoPath);
    // Clean up temp file
    try { File.Delete(tempPath); } catch { }
}
`

**Effort:** ~20 min
**Impact:** monitor.json is never left in a corrupt state

---

### Cause #4: Startup Conflict (Concurrent Instances)
**File:** AIUsageTracker.Monitor/Program.cs (lines 76-96)

**Risk:** Two Monitor instances can start simultaneously on the same port if first instance hangs during initialization.

**Current Code:**
`csharp
var startupMutex = new Mutex(true, mutexName, out createdNew);
if (!createdNew) {
    if (!startupMutex.WaitOne(TimeSpan.FromSeconds(10))) {  // 10s timeout
        logger.LogError("Timeout waiting for other Monitor instance");
        return;
    }
}
`

**Failure Scenario:**
1. Monitor A starts, acquires mutex, begins initialization (slow database migration)
2. Monitor B starts 2 seconds later, tries to acquire mutex
3. Monitor A is still stuck in database migration (>10s)
4. Mutex timeout expires, Monitor B proceeds
5. Both try to bind to port 5000 → port conflict
6. Monitor B falls back to random port, but metadata has old port
7. Slim UI still points to Monitor A's port (or Monitor B if A crashes)

**Hardening Fix:**
`csharp
const string mutexName = @"Global\AIUsageTracker_Monitor_" + Environment.UserName;
bool createdNew;
using var startupMutex = new Mutex(true, mutexName, out createdNew);

if (!createdNew) {
    logger.LogWarning("Another Monitor instance detected. Waiting up to 60s...");
    if (!startupMutex.WaitOne(TimeSpan.FromSeconds(60))) {
        logger.LogError("Startup conflict: other instance did not complete. Exiting.");
        MonitorInfoPersistence.ReportError("Startup conflict: mutex timeout", pathProvider, logger);
        Environment.Exit(1);
    }
}

try {
    // ... rest of startup ...
    
    // IMPORTANT: Post-startup validation that port actually bound
    using (var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) }) {
        try {
            var health = await client.GetAsync(\$"http://localhost:{port}/api/health");
            if (!health.IsSuccessStatusCode) {
                throw new InvalidOperationException("Health check failed after startup");
            }
        } catch (Exception ex) {
            logger.LogError(ex, "Failed to validate port binding");
            throw;
        }
    }
    
    MonitorInfoPersistence.SaveMonitorInfo(port, isDebugMode, logger, pathProvider, startupStatus: "running");
} finally {
    startupMutex?.Dispose();
}
`

**Effort:** ~45 min
**Impact:** Startup conflicts are detected and prevented

---

## PART 3: BEST NEXT FIXES/TESTS

### Fix #1: Endpoint Exception Handlers (TIER 1 — STOP CRASHES)
**Target Files:**
- AIUsageTracker.Monitor/Endpoints/MonitorUsageEndpoints.cs
- AIUsageTracker.Monitor/Endpoints/MonitorConfigEndpoints.cs
- AIUsageTracker.Monitor/Endpoints/MonitorHistoryEndpoints.cs

**Implementation:**
- Wrap all wait db.QueryAsync() calls in try-catch
- Return 503 Service Unavailable for database locks (TaskCanceledException, SqliteException)
- Return 500 Internal Server Error for unexpected exceptions
- Log all errors at warning or error level

**Test Cases:**
- \	est_usage_endpoint_returns_503_on_database_timeout\
- \	est_config_endpoint_returns_500_on_json_error\
- \	est_history_endpoint_returns_error_not_200_on_failure\

**Estimated Effort:** 30 minutes
**Risk Level:** Low (purely defensive, no logic changes)

---

### Fix #2: Health Check Endpoint (TIER 1 — CRITICAL)
**Target File:**
- \AIUsageTracker.Monitor/Endpoints/MonitorDiagnosticsEndpoints.cs\

**Implementation:**
- Test database connectivity (5s timeout)
- Check refresh service health (last refresh < 2 min old)
- Return 503 if either subsystem is unhealthy

**Test Cases:**
- \	est_health_returns_503_when_database_locked\
- \	est_health_returns_503_when_refresh_hung_2_minutes\

**Estimated Effort:** 45 minutes
**Risk Level:** Low (no logic changes, defensive only)

---

### Fix #3: Global Exception Handler (TIER 1 — PREVENT CRASHES)
**Target Files:**
- \AIUsageTracker.Monitor/Program.cs\
- \AIUsageTracker.Monitor/Endpoints/MonitorConfigEndpoints.cs\

**Implementation:**
- Add AppDomain.CurrentDomain.UnhandledException handler
- Add TaskScheduler.Current.UnobservedTaskException handler
- Wrap fire-and-forget Task.Run() calls with try-catch

**Test Cases:**
- \	est_unobserved_task_exception_is_logged\
- \	est_unhandled_appdomain_exception_writes_diagnostic\

**Estimated Effort:** 45 minutes
**Risk Level:** Low (exception handlers only)

---

### Fix #4: Atomic Metadata Writes (TIER 1 — DATA INTEGRITY)
**Target File:**
- \AIUsageTracker.Monitor/Services/MonitorInfoPersistence.cs\

**Implementation:**
- Write to temp file first
- Use File.Move(temp, final, overwrite: true) for atomicity
- Clean up temp files on failure

**Test Cases:**
- \	est_metadata_write_is_atomic_no_corruption\
- \	est_process_crash_during_write_leaves_valid_json\ (simulated)

**Estimated Effort:** 20 minutes
**Risk Level:** Low (improved version of existing code)

---

### Fix #5: Database Retry Logic (TIER 2 — PREVENT HANGS)
**Target File:**
- \AIUsageTracker.Monitor/Services/UsageDatabase.cs\

**Implementation:**
- Create \ExecuteWithRetryAsync<T>()\ helper method
- Refactor all query methods to use it (exponential backoff on lock, 3 retries)
- Log each retry at debug level

**Test Cases:**
- \	est_database_retry_on_lock_succeeds_after_2nd_attempt\
- \	est_database_giveup_after_3_retries\

**Estimated Effort:** 1 hour (refactoring multiple methods)
**Risk Level:** Medium (behavioral change to error handling)

---

### Fix #6: Startup Validation (TIER 2 — CONFLICT PREVENTION)
**Target File:**
- \AIUsageTracker.Monitor/Program.cs\

**Implementation:**
- Increase mutex timeout to 60s
- Add post-startup health check validation
- Exit cleanly if validation fails

**Test Cases:**
- \	est_startup_validates_health_endpoint_before_metadata\
- \	est_concurrent_startup_serialization\

**Estimated Effort:** 45 minutes
**Risk Level:** Medium (affects startup path)

---

### Fix #7: Metadata File Cleanup (TIER 3 — HOUSEKEEPING)
**Target File:**
- \AIUsageTracker.Monitor/Services/ProviderRefreshService.cs\

**Implementation:**
- Add cleanup job in ExecuteAsync() after first successful refresh
- Delete .stale.* files older than 7 days

**Test Cases:**
- \	est_stale_metadata_cleanup_removes_old_files\
- \	est_cleanup_does_not_delete_recent_files\

**Estimated Effort:** 30 minutes
**Risk Level:** Low (background cleanup only)

---

### Fix #8: Structured Logging for Metadata (TIER 3 — OBSERVABILITY)
**Target Files:**
- \AIUsageTracker.Monitor/Services/MonitorInfoPersistence.cs\
- \AIUsageTracker.Core/MonitorClient/MonitorLauncherStateResolver.cs\

**Implementation:**
- Log at Info level when writing/invalidating metadata
- Include port, PID, health status in logs
- Make it easy to follow metadata lifecycle in logs

**Estimated Effort:** 20 minutes
**Risk Level:** Very Low (logging only)

---

### Fix #9: Startup Race Detection Tests (TIER 3 — TEST COVERAGE)
**Target File:**
- \AIUsageTracker.Monitor.Tests/MonitorStartupTests.cs\ (NEW)

**Implementation:**
- Unit tests for concurrent startup scenarios
- Tests for metadata validation
- Tests for exception handlers

**Estimated Effort:** 1 hour
**Risk Level:** Very Low (tests only)

---

## Implementation Roadmap

**Phase 1 (Week 1) — STOP CRASHES:**
1. Fix #1: Endpoint exception handlers (30 min)
2. Fix #2: Health check endpoint (45 min)
3. Fix #3: Global exception handlers (45 min)
4. Fix #4: Atomic metadata writes (20 min)
- **Total:** ~2.5 hours, 4 tests

**Phase 2 (Week 2) — PREVENT HANGS:**
5. Fix #5: Database retry logic (60 min)
6. Fix #6: Startup validation (45 min)
- **Total:** ~1.75 hours, 4 tests

**Phase 3 (Week 3+) — OBSERVABILITY:**
7. Fix #7: Metadata cleanup (30 min)
8. Fix #8: Structured logging (20 min)
9. Fix #9: Startup race tests (60 min)
- **Total:** ~1.75 hours, 5 tests

---

## Success Criteria

✅ All Tier 1 fixes deployed → Monitor no longer crashes from unhandled exceptions
✅ All Tier 2 fixes deployed → Monitor doesn't freeze on database locks; startup conflicts prevented
✅ Slim UI receives proper HTTP error codes (503 for transient, not 200 or 500)
✅ monitor.json is never corrupted by partial writes
✅ Concurrent Monitor startup scenarios are serialized without port conflicts
✅ Test suite covers all new exception handlers and retry logic
✅ Logs clearly show metadata lifecycle for diagnostics
