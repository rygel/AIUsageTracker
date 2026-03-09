# Monitor/Slim UI Communication - Failure Mode Analysis & Hardening Roadmap

**Focus:** Concrete, code-backed risks in Monitor/Slim UI communication stability with emphasis on Monitor crashes, unavailability, and recovery scenarios.

---

## PART 1: LIKELY COMMUNICATION BUGS

### **1. Endpoint Contract Mismatches (High Risk)**

**Files Involved:**
- AIUsageTracker.Monitor/Endpoints/*.cs (all endpoint handlers)
- AIUsageTracker.Core/MonitorClient/MonitorService.cs (client-side parsing)

**Risk:** Endpoints return responses that don't match the contract expected by MonitorService.

**Concrete Issues:**
1. **Config Endpoint Missing Error Propagation** (MonitorConfigEndpoints.cs lines 32-33)
   - POST /api/config and DELETE endpoints **silently swallow exceptions** from SaveConfigAsync() and RemoveConfigAsync()
   - If file I/O fails mid-save, Slim UI receives 200 OK but config was not persisted
   - **Code:** wait configService.SaveConfigAsync(config) — ConfigService.cs throws on error, but endpoint doesn't catch
   - Actually it does throw (line 72), so the ASP.NET framework catches unhandled exceptions. **The bug:** No status code differentiation. Both success and partial failures return 200.

2. **Refresh Endpoint Has No Timeout/Cancellation** (MonitorUsageEndpoints.cs line 51)
   - POST /api/refresh calls TriggerRefreshAsync() with no explicit timeout
   - If ProviderRefreshService is stuck in semaphore, endpoint hangs forever
   - Slim UI request timeout (8 seconds default) kicks in, but Monitor process is still hung

3. **Health Endpoint Too Simple** (MonitorDiagnosticsEndpoints.cs lines 22-38)
   - Returns 200 OK immediately without checking database connectivity or background service health
   - A Monitor with a corrupted database or stuck ProviderRefreshService reports "healthy"
   - **Missing:** Health check should validate (a) database is accessible, (b) ProviderRefreshService is running, (c) last refresh succeeded within N minutes

4. **Usage/History Endpoints Missing Null/Empty Guards**
   - If GetLatestHistoryAsync() fails with database lock, endpoint throws unhandled exception
   - Slim UI receives 500 instead of graceful degradation with cached data
   - **Code:** MonitorUsageEndpoints.cs lines 18-32 have no try-catch

### **2. Port Discovery Races (Medium-High Risk)**

**Files:** 
- AIUsageTracker.Core/MonitorClient/MonitorService.cs (RefreshPortAsync)
- AIUsageTracker.Core/MonitorClient/MonitorLauncher.cs (StartAgentAsync)

**Risk:** Between Slim UI startup and first refresh, Monitor might move ports or crash mid-startup.

**Concrete Issues:**
1. **Port Read-Refresh Race** 
   - Slim UI reads monitor.json at startup (stale port info)
   - Monitor starts on a different port (if 5000 is in use)
   - Slim UI calls /api/usage on old port and gets 404
   - Then calls RefreshPortAsync() at first request failure (line 291)
   - **Gap:** If first request fails with non-HTTP error, the retry logic (lines 293-304) may not be reached

2. **Metadata File TOCTOU Vulnerability**
   - Slim UI reads monitor.json, confirms health on port X
   - Monitor crashes **between** Slim UI reading the file and making the request
   - Slim UI thinks Monitor is healthy but network call fails
   - The call catches the failure and refreshes port (correct), but there's a small window of confusion

3. **StartupMutex Doesn't Prevent Port Conflicts**
   - Program.cs lines 76-96: Mutex prevents **concurrent startup**, not **port reuse**
   - If Monitor A crashes and Monitor B starts before metadata is invalidated, both try port 5000
   - Port resolver falls back to random port, but Slim UI still has old metadata
   - **Gap:** Mutex is held for 10 seconds max (WaitOne timeout), then released. If Monitor init takes >10s, second instance can start.

### **3. JSON Deserialization Failures (Medium Risk)**

**Files:**
- AIUsageTracker.Core/MonitorClient/MonitorLauncherStateResolver.cs (lines 54-62)
- AIUsageTracker.Monitor/Services/UsageDatabase.cs (lines 318-327)

**Risk:** Malformed monitor.json or response JSON causes hard crashes.

**Concrete Issues:**
1. **Monitor.json Parsing** (MonitorLauncherStateResolver.cs lines 39-49)
   - If monitor.json contains invalid JSON, JsonSerializer.Deserialize() throws
   - Exception is caught (line 54), file is quarantined (line 59)
   - **BUT:** No timeout on File.ReadAllTextAsync() — if file is locked by another process, hangs indefinitely

2. **Usage Details JSON Deserialization** (UsageDatabase.cs lines 318-327)
   - Partial failure: one provider's details JSON is corrupted
   - The exception is caught and logged (line 326), but the entire result set continues
   - **Missing:** Should have a transaction rollback or at least flag the row as invalid

---

## PART 2: LIKELY MONITOR CRASH/UNAVAILABILITY CAUSES

### **1. Database Lock Deadlocks (High Risk)**

**Files:**
- AIUsageTracker.Monitor/Services/UsageDatabase.cs (all methods with semaphore)
- AIUsageTracker.Monitor/Services/ProviderRefreshService.cs (line 206: semaphore.WaitAsync())

**Risk:** SQLite shared-cache contention or semaphore deadlock causes Monitor to freeze.

**Concrete Issues:**
1. **Single-Writer SemaphoreSlim + SQLite Lock Contention**
   - UsageDatabase uses a single SemaphoreSlim(1, 1) (line 21) to serialize all database writes
   - ProviderRefreshService also uses a single SemaphoreSlim(1, 1) (line 28) to serialize refresh cycles
   - If a database write holds the semaphore and SQLite is locked (e.g., by another Monitor instance or file system lock), the await on connection.OpenAsync() times out after 15 seconds (DefaultTimeout line 43)
   - **Gap:** No retry logic or circuit breaker. Next write attempt acquires the semaphore, times out again immediately.

2. **PRAGMA optimize Without Timeout** (UsageDatabase.cs line 252)
   - PRAGMA optimize can take 5-10 seconds on a large database
   - No timeout is set on this operation
   - If called during a Slim UI request, the entire Monitor becomes unresponsive for that duration

3. **Connection Pool Exhaustion**
   - SqliteConnectionStringBuilder sets Pooling = true and default connection pool is 5
   - If ProviderRefreshService is running 3+ concurrent provider queries + Slim UI makes 2 requests, all 5 connections are in use
   - 6th request waits indefinitely (or times out after 15 seconds)
   - **Code Gap:** No max connection limit or connection timeout configuration

4. **GetLatestHistoryAsync Full Table Scan**
   - Query (lines 299-314) has WHERE h.id IN (SELECT MAX(id) FROM provider_history GROUP BY provider_id)
   - On large history table (100k+ rows), this is an expensive scan without index
   - **Missing:** No index on (provider_id, id DESC) to optimize the subquery

### **2. Unhandled Exceptions in Background Services (High Risk)**

**Files:**
- AIUsageTracker.Monitor/Services/ProviderRefreshService.cs (ExecuteAsync, lines 81-123)
- AIUsageTracker.Monitor/Program.cs (Main, no global exception handler)

**Risk:** Exception in ProviderRefreshService kills the entire Monitor process.

**Concrete Issues:**
1. **No Global Exception Handler in Main**
   - Program.cs lines 244-248: Catches exception in startup, but Main itself doesn't have try-catch
   - If an unhandled exception is thrown after pp.WaitForShutdownAsync(), the process terminates without cleanup
   - **Missing:** AppDomain.CurrentDomain.UnhandledException handler to log and attempt graceful shutdown

2. **Fire-and-Forget Tasks in Endpoints** (MonitorConfigEndpoints.cs lines 55-65)
   - Task.Run(async () => { await refreshService.TriggerRefreshAsync() }) is fire-and-forget
   - If the task throws, the exception is **unobserved**
   - TaskScheduler.UnobservedTaskException could crash the process if not globally handled
   - **Code:** No .ContinueWith() or .GetAwaiter().OnCompleted() to log exceptions

3. **No Exception Handler in ProviderRefreshService.ExecuteAsync Loop** 
   - Loop lines 104-120: Catches OperationCanceledException and general Exception
   - **BUT:** If Task.Delay() or TriggerRefreshAsync() throws a non-standard exception (e.g., OutOfMemoryException), it will propagate and terminate the service
   - **Gap:** ProviderRefreshService is a BackgroundService — if ExecuteAsync throws, the service stops silently

### **3. Metadata Persistence Failures (High Risk)**

**Files:**
- AIUsageTracker.Monitor/Services/MonitorInfoPersistence.cs
- AIUsageTracker.Monitor/Program.cs (line 241)

**Risk:** Monitor writes bad metadata on startup or crash, Slim UI connects to wrong port or old process.

**Concrete Issues:**
1. **Monitor.json Partial Write on Crash**
   - Line 241: SaveMonitorInfo() is called **after** pp.StartAsync(), but only once on successful startup
   - If the process crashes after startup, metadata is stale but never updated
   - If the process is killed during File.WriteAllText() (line 48), file is partially written (corrupted JSON)
   - **Missing:** Use atomic write (write to temp file, then move) or fsync

2. **No Verification That Port Actually Bound**
   - Program.cs line 131: uilder.WebHost.UseUrls($"http://localhost:{port}") doesn't guarantee the port is bound
   - Kestrel might fail to bind (e.g., port is ephemeral and taken by another process between check and bind)
   - Metadata would have wrong port
   - **Code:** No post-startup validation that listening socket is actually open

3. **Stale Metadata Isn't Cleaned Up**
   - MonitorLauncherStateResolver.cs lines 207-212: Backup stale files with timestamp suffix
   - No cleanup job — stale files accumulate forever
   - Slow directory listing when checking for metadata paths

### **4. ProviderRefreshService Circuit Breaker False Positives (Medium Risk)**

**Files:** AIUsageTracker.Monitor/Services/ProviderRefreshService.cs (lines 525-560)

**Risk:** Single failure marks provider as "broken" for 30 minutes, even if error is transient.

**Concrete Issues:**
1. **No Distinction Between Error Types**
   - Lines 525-560: UpdateProviderFailureStates() treats all non-success results the same
   - Network timeout, auth failure, API rate limit, and provider API outage all get 3-strike circuit breaker
   - **Gap:** Should have fast-recovery for transient errors (timeout, 429) and slow-recovery for permanent errors (invalid key, 401)

2. **Circuit Breaker Backoff Calculation Is Hardcoded**
   - Lines 42-43: CircuitBreakerBaseBackoff = 1 minute, CircuitBreakerMaxBackoff = 30 minutes
   - If 3 queries fail, circuit opens until now + exponential backoff
   - But if provider API is down for 15 minutes, Monitor won't retry for 30 minutes (missed recovery window)

---

## PART 3: BEST NEXT FIXES/TESTS

### **Tier 1: Critical Stability Improvements** (Do First)

#### **1. Hardened Health Check Endpoint**
**File:** AIUsageTracker.Monitor/Endpoints/MonitorDiagnosticsEndpoints.cs
**Rationale:** Currently returns 200 OK without validating any subsystem. Slim UI believes Monitor is healthy when it's actually stuck.

**Implementation:**
`
app.MapGet(MonitorApiRoutes.Health, async (UsageDatabase db, ProviderRefreshService refresh, ILogger logger) => {
    try {
        // Test database is accessible (5s timeout)
        var testQuery = db.GetLatestHistoryAsync().AsTask();
        if (!await Task.WhenAny(testQuery, Task.Delay(5000)).Equals(testQuery)) {
            return Results.ServiceUnavailable("Database timeout");
        }
        
        // Check refresh service is running (not stuck in semaphore >2 min)
        var telemetry = refresh.GetRefreshTelemetrySnapshot();
        if (telemetry.LastRefreshCompletedUtc.HasValue && 
            (DateTime.UtcNow - telemetry.LastRefreshCompletedUtc.Value).TotalMinutes > 2) {
            return Results.ServiceUnavailable("Refresh service hung");
        }
        
        return Results.Ok(new { status = "healthy", ... });
    } catch (Exception ex) {
        logger.LogError(ex, "Health check failed");
        return Results.ServiceUnavailable();
    }
});
`

**Test:**
- Test: 	est_health_returns_unavailable_when_database_locked — lock database, verify 503
- Test: 	est_health_returns_unavailable_when_refresh_stuck_2_minutes — freeze refresh, verify 503

---

#### **2. Endpoint Exception Handlers (All Endpoints)**
**Files:** AIUsageTracker.Monitor/Endpoints/*.cs
**Rationale:** Several endpoints throw unhandled exceptions on database errors. Slim UI needs proper HTTP status codes for graceful degradation.

**Implementation:**
`
// Pattern for all endpoints:
app.MapGet(route, async (dependency db, ILogger logger) => {
    try {
        var data = await db.SomeQueryAsync();
        return Results.Ok(data);
    } catch (TaskCanceledException) {
        logger.LogWarning("Request timeout");
        return Results.RequestTimeout();
    } catch (SqliteException ex) when (ex.SqliteErrorCode == 5) { // Database is locked
        logger.LogWarning(ex, "Database locked");
        return Results.ServiceUnavailable("Database locked, retry in 1s");
    } catch (Exception ex) {
        logger.LogError(ex, "Unexpected error");
        return Results.InternalServerError();
    }
});
`

**Files to Update:**
- MonitorUsageEndpoints.cs lines 18-32 (GET /api/usage)
- MonitorUsageEndpoints.cs lines 35-46 (GET /api/usage/{providerId})
- MonitorConfigEndpoints.cs lines 17-22 (GET /api/config)

**Tests:**
- Test: 	est_usage_endpoint_returns_500_on_database_error — mock IUsageDatabase.GetLatestHistoryAsync() to throw
- Test: 	est_usage_endpoint_returns_503_on_database_locked

---

#### **3. Global Exception Handler for Background Services**
**Files:** AIUsageTracker.Monitor/Program.cs
**Rationale:** Unobserved task exceptions from fire-and-forget Tasks can crash the process. No global handler currently exists.

**Implementation:**
`csharp
// In Program.Main(), before CreateBuilder():
AppDomain.CurrentDomain.UnhandledException += (sender, e) => {
    logger.LogCritical((Exception)e.ExceptionObject, "Unhandled AppDomain exception");
    MonitorInfoPersistence.ReportError("Crash: " + e.ExceptionObject?.ToString(), pathProvider, logger);
};

TaskScheduler.Current.UnobservedTaskException += (e) => {
    logger.LogCritical(e.Exception, "Unobserved task exception");
    e.SetObserved(); // Prevent process termination
};
`

**Tests:**
- Test: 	est_unobserved_task_exception_is_logged — throw in fire-and-forget task, verify logged
- Test: 	est_appdomain_unhandled_exception_writes_metadata — throw in Console.WriteLine, verify monitor.json has error

---

#### **4. Atomic Metadata Writes**
**File:** AIUsageTracker.Monitor/Services/MonitorInfoPersistence.cs
**Rationale:** Partial writes corrupt monitor.json, Slim UI can't parse it and invalidates metadata.

**Implementation:**
`csharp
public static void SaveMonitorInfo(...) {
    // ... build info ...
    var json = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
    
    foreach (var infoPath in GetMonitorInfoCandidatePaths(pathProvider)) {
        try {
            var dir = Path.GetDirectoryName(infoPath);
            Directory.CreateDirectory(dir);
            
            // Atomic write: temp file + move
            var tempPath = infoPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, infoPath, overwrite: true);
        } catch (Exception ex) {
            logger.LogDebug(ex, "Failed to write monitor.json to {Path}", infoPath);
        }
    }
}
`

**Tests:**
- Test: 	est_metadata_write_survives_process_crash — write metadata, kill process mid-write (simulate), verify file is valid JSON
- Test: 	est_metadata_atomic_write_no_corruption — verify no .tmp files left behind

---

### **Tier 2: Data Integrity Improvements** (Next Phase)

#### **5. Database Query Resilience**
**File:** AIUsageTracker.Monitor/Services/UsageDatabase.cs
**Rationale:** Lock timeouts and connection pool exhaustion cause Monitor to hang. Need smarter retry and circuit breaking.

**Implementation:**
`csharp
private async Task<T> ExecuteWithRetryAsync<T>(
    Func<SqliteConnection, Task<T>> operation,
    string operationName) {
    
    const int maxRetries = 3;
    const int initialDelayMs = 100;
    
    for (int attempt = 0; attempt < maxRetries; attempt++) {
        try {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            return await operation(connection).ConfigureAwait(false);
        } catch (SqliteException ex) when (ex.SqliteErrorCode == 5 && attempt < maxRetries - 1) {
            // Database is locked, retry with exponential backoff
            var delayMs = initialDelayMs * (int)Math.Pow(2, attempt);
            logger.LogWarning("DB locked, retrying {Operation} after {DelayMs}ms", operationName, delayMs);
            await Task.Delay(delayMs).ConfigureAwait(false);
        }
    }
}
`

**Tests:**
- Test: 	est_database_retry_on_lock_succeeds_on_second_attempt — mock 1st call to throw lock, 2nd to succeed
- Test: 	est_database_operation_gives_up_after_3_retries — all calls throw, verify failure after 3 attempts

---

#### **6. Startup Semaphore Timeout Protection**
**File:** AIUsageTracker.Monitor/Program.cs (lines 76-96)
**Rationale:** If first Monitor instance hangs during init, second instance waits 10s (mutex timeout) then proceeds. Conflicts.

**Implementation:**
`csharp
// Extend mutex timeout based on actual initialization completion
var mutexName = @"Global\AIUsageTracker_Monitor_" + Environment.UserName;
bool createdNew;
using var startupMutex = new Mutex(true, mutexName, out createdNew);

if (!createdNew) {
    logger.LogWarning("Another Monitor instance detected. Waiting max 30s...");
    if (!startupMutex.WaitOne(TimeSpan.FromSeconds(30))) {
        logger.LogError("Startup conflict: another Monitor is initializing. Exiting.");
        // Write diagnostic to metadata
        MonitorInfoPersistence.ReportError("Startup conflict: mutex timeout", pathProvider, logger);
        return;
    }
}

try {
    // ... rest of startup ...
    
    // Verify port actually bound before writing metadata
    using (var testClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) }) {
        var health = await testClient.GetAsync($"http://localhost:{port}/api/health");
        if (!health.IsSuccessStatusCode) {
            throw new InvalidOperationException("Health check failed immediately after startup");
        }
    }
    
    MonitorInfoPersistence.SaveMonitorInfo(port, isDebugMode, logger, pathProvider, startupStatus: "running");
} finally {
    startupMutex?.Dispose();
}
`

**Tests:**
- Test: 	est_second_instance_waits_for_first_to_complete — start 2 instances, verify 2nd waits then starts on different port
- Test: 	est_startup_conflict_detected_and_logged — simulate hung first instance, verify diagnostic in metadata

---

#### **7. Metadata File Cleanup Job**
**File:** AIUsageTracker.Monitor/Services/ProviderRefreshService.cs
**Rationale:** Stale .stale.* backup files accumulate, slow down lookups and waste space.

**Implementation:**
`csharp
// In ProviderRefreshService.ExecuteAsync, after first refresh:
_ = Task.Run(async () => {
    try {
        var infoDir = pathProvider.GetAppDataRoot();
        foreach (var file in Directory.GetFiles(infoDir, "monitor.json.stale.*")) {
            try {
                var fileInfo = new FileInfo(file);
                if (fileInfo.LastWriteTime < DateTime.Now.AddDays(-7)) {
                    fileInfo.Delete();
                    logger.LogDebug("Cleaned up old metadata backup: {File}", file);
                }
            } catch (Exception ex) {
                logger.LogDebug(ex, "Failed to clean metadata backup {File}", file);
            }
        }
    } catch { }
}, stoppingToken);
`

**Tests:**
- Test: 	est_stale_metadata_cleanup_removes_files_older_than_7_days — create .stale files, verify cleanup deletes old ones

---

### **Tier 3: Monitoring & Observability** (Best Practices)

#### **8. Add Structured Logging to Metadata Lifecycle**
**File:** AIUsageTracker.Monitor/Services/MonitorInfoPersistence.cs + AIUsageTracker.Core/MonitorClient/MonitorLauncherStateResolver.cs
**Rationale:** Hard to debug Monitor discovery failures without logs showing which metadata was read/invalidated.

**Implementation:**
`csharp
public static void SaveMonitorInfo(...) {
    logger.LogInformation("Writing metadata: Port={Port}, PID={ProcessId}, Debug={DebugMode}", port, Environment.ProcessId, debug);
    foreach (var infoPath in GetMonitorInfoCandidatePaths(pathProvider)) {
        try {
            // ...
            logger.LogDebug("Wrote metadata to {Path}", infoPath);
        } catch (...) {
            logger.LogWarning(ex, "Failed to write metadata to {Path}, trying next location", infoPath);
        }
    }
}

// In MonitorLauncherStateResolver.ReadAgentInfoAsync():
logger.LogDebug("Reading metadata from candidate paths: {Paths}", candidatePaths);
if (info != null) {
    logger.LogInformation("Metadata valid: Port={Port}, PID={ProcessId}, Health={HealthCheck}", 
        info.Port, info.ProcessId, healthOk);
}
`

**Tests:**
- Test: 	est_metadata_lifecycle_is_logged — read/write/invalidate metadata, verify all logged at info level

---

#### **9. Startup Race Detection Tests**
**File:** AIUsageTracker.Monitor.Tests/MonitorStartupTests.cs (new file)
**Rationale:** Concurrent startup scenario is hard to test manually. Need automated tests.

**Implementation:**
`csharp
[Fact]
public async Task ConcurrentStartupAttempts_SecondInstanceDetectsMutex() {
    // Simulate two Monitor instances starting at the same time
    var mutex1Task = Task.Run(() => TryAcquireStartupMutex(timeout: 30));
    var mutex2Task = Task.Run(() => TryAcquireStartupMutex(timeout: 30));
    
    var results = await Task.WhenAll(mutex1Task, mutex2Task);
    
    // One should succeed immediately, one should wait
    Assert.Contains(results, r => r.acquiredImmediately);
    Assert.Contains(results, r => !r.acquiredImmediately && r.acquiredAfterWait);
}

[Fact]
public async Task MonitorStartupVerifiesHealthBeforeWritingMetadata() {
    // Mock Kestrel to fail binding
    var mockKestrel = new Mock<IWebHost>();
    mockKestrel.Setup(m => m.StartAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new SocketException());
    
    // Startup should fail and not write metadata
    Assert.False(await MonitorLauncher.StartAgentAsync());
    Assert.False(File.Exists(monitorJsonPath));
}
`

**Tests:**
- Test: 	est_concurrent_startup_is_serialized_by_mutex — see above
- Test: 	est_startup_failure_does_not_corrupt_metadata — see above

---

## Summary Table: Risks and Fixes

| Risk | Severity | Root Cause | Fix Category | Expected Impact |
|------|----------|-----------|--------------|-----------------|
| Endpoints return 200 OK on partial failure | High | No exception handlers | Tier 1 (#2) | Slim UI stops auto-retrying stale data |
| Health check doesn't validate subsystems | High | Too simple | Tier 1 (#1) | Slim UI correctly detects unavailable Monitor |
| Fire-and-forget tasks crash process | High | No global exception handler | Tier 1 (#3) | Crashes prevented, logged |
| Metadata partial writes corrupt JSON | High | Non-atomic file write | Tier 1 (#4) | No corrupted metadata.json files |
| Database lock timeouts freeze Monitor | High | Single semaphore + no retry | Tier 2 (#5) | Transient locks don't freeze Monitor |
| Port discovery race with old metadata | Medium | TOCTOU race | Tier 1 (#2, partial) | Covered by health check retry logic |
| Startup mutex doesn't prevent conflicts | Medium | 10s timeout too short | Tier 2 (#6) | Second instance waits, then safe fallback |
| Stale metadata accumulates | Medium | No cleanup job | Tier 2 (#7) | Reduced directory listings, cleaner code |
| Circuit breaker can't distinguish error types | Medium | All failures treated equal | Tier 3 | Providers recover faster from transient errors |

---

## Implementation Priority

1. **Week 1:** Tier 1 fixes (#1-4) — Stop crashes and status code corruption
2. **Week 2:** Tier 2 fixes (#5-7) — Prevent hangs and metadata accumulation
3. **Week 3:** Tier 3 fixes (#8-9) — Observability and test coverage

---

## Success Criteria

- [ ] Health check endpoint returns 503 when database locked
- [ ] No unhandled exceptions in Monitor logs
- [ ] monitor.json is never corrupted (validate with test)
- [ ] Concurrent startup attempts are serialized, no port conflicts
- [ ] Slim UI receives HTTP error codes for retriable failures (not 200)
- [ ] Monitor restarts cleanly after crash without stale metadata
- [ ] Database locks don't freeze Monitor (retry + backoff works)
- [ ] All new tests pass, 100% coverage of #1-4 fix code paths

