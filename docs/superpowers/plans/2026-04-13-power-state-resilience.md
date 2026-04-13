# Power State Resilience Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Monitor process survive Windows sleep/wake transitions and add a watchdog to the UI Slim that relaunches the Monitor when it dies.

**Architecture:** Two layers — (1) a `PowerStateListener` hosted service in the Monitor that pauses jobs on suspend and resumes on wake, and (2) an adaptive watchdog loop in `MonitorStartupOrchestrator` that periodically health-checks the Monitor and relaunches it on failure, with immediate wake-triggered checks.

**Tech Stack:** .NET 8, ASP.NET Core hosted services, `Microsoft.Win32.SystemEvents.PowerModeChanged`, xUnit + Moq

---

## File Structure

| Action | File | Responsibility |
|--------|------|----------------|
| Create | `AIUsageTracker.Monitor/Services/PowerStateListener.cs` | Hosted service subscribing to Windows power events, pausing/resuming scheduler and refresh |
| Create | `AIUsageTracker.Monitor.Tests/PowerStateListenerTests.cs` | Tests for power state listener |
| Modify | `AIUsageTracker.Monitor/Services/IMonitorJobScheduler.cs` | Add `PauseAsync()` / `ResumeAsync()` to interface |
| Modify | `AIUsageTracker.Monitor/Services/MonitorJobScheduler.cs` | Implement pause/resume: block dispatch, stop/restart recurring timers |
| Modify | `AIUsageTracker.Monitor/Services/MonitorJobSchedulerSnapshot.cs` | Add `IsPaused` property |
| Modify | `AIUsageTracker.Monitor/Program.cs:202-230` | Register `PowerStateListener` (Windows-only) |
| Modify | `AIUsageTracker.Monitor.Tests/MonitorJobSchedulerTests.cs` | Add pause/resume tests |
| Modify | `AIUsageTracker.UI.Slim/Services/MonitorStartupOrchestrator.cs` | Add watchdog loop with power-aware fast path |
| Modify | `AIUsageTracker.Tests/Core/MonitorStartupOrchestratorTests.cs` or create new test file | Add watchdog tests |

---

### Task 1: Add PauseAsync/ResumeAsync to MonitorJobScheduler

**Files:**
- Modify: `AIUsageTracker.Monitor/Services/IMonitorJobScheduler.cs`
- Modify: `AIUsageTracker.Monitor/Services/MonitorJobScheduler.cs:29-48` (fields), `171-276` (ExecuteAsync), `285-324` (StartRecurringLoopAsync)
- Modify: `AIUsageTracker.Monitor/Services/MonitorJobSchedulerSnapshot.cs`
- Modify: `AIUsageTracker.Monitor.Tests/MonitorJobSchedulerTests.cs`

- [ ] **Step 1: Write the failing test — PauseAsync blocks new job dispatch**

Add to `AIUsageTracker.Monitor.Tests/MonitorJobSchedulerTests.cs`:

```csharp
[Fact]
public async Task PauseAsync_BlocksNewJobDispatchWhileInFlightCompletesAsync()
{
    var logger = new Mock<ILogger<MonitorJobScheduler>>();
    var scheduler = new MonitorJobScheduler(logger.Object);
    var inFlightStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    var inFlightGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    var blockedJobExecuted = false;

    await scheduler.StartAsync(CancellationToken.None);
    try
    {
        // Enqueue a job that will be in-flight when we pause
        _ = scheduler.Enqueue(
            "in-flight-job",
            async _ =>
            {
                inFlightStarted.TrySetResult(true);
                await inFlightGate.Task;
            },
            MonitorJobPriority.High);

        // Wait for the in-flight job to start executing
        Assert.True(await inFlightStarted.Task.WaitAsync(TimeSpan.FromSeconds(2)));

        // Pause while a job is in-flight
        scheduler.PauseAsync();

        // Enqueue a job that should be blocked
        _ = scheduler.Enqueue(
            "blocked-job",
            _ =>
            {
                blockedJobExecuted = true;
                return Task.CompletedTask;
            },
            MonitorJobPriority.High);

        // Let the in-flight job complete
        inFlightGate.TrySetResult(true);
        await Task.Delay(200);

        // The blocked job should NOT have executed while paused
        Assert.False(blockedJobExecuted);

        var snapshot = scheduler.GetSnapshot();
        Assert.True(snapshot.IsPaused);
    }
    finally
    {
        scheduler.ResumeAsync();
        await scheduler.StopAsync(CancellationToken.None);
    }
}
```

- [ ] **Step 2: Write the failing test — ResumeAsync restores dispatch**

Add to `AIUsageTracker.Monitor.Tests/MonitorJobSchedulerTests.cs`:

```csharp
[Fact]
public async Task ResumeAsync_RestoresDispatchAfterPauseAsync()
{
    var logger = new Mock<ILogger<MonitorJobScheduler>>();
    var scheduler = new MonitorJobScheduler(logger.Object);
    var jobCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    await scheduler.StartAsync(CancellationToken.None);
    try
    {
        scheduler.PauseAsync();

        _ = scheduler.Enqueue(
            "queued-while-paused",
            _ =>
            {
                jobCompleted.TrySetResult(true);
                return Task.CompletedTask;
            },
            MonitorJobPriority.High);

        // Job should not run while paused
        await Task.Delay(200);
        Assert.False(jobCompleted.Task.IsCompleted);

        // Resume should allow the queued job to run
        scheduler.ResumeAsync();
        Assert.True(await jobCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2)));

        var snapshot = scheduler.GetSnapshot();
        Assert.False(snapshot.IsPaused);
    }
    finally
    {
        await scheduler.StopAsync(CancellationToken.None);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test AIUsageTracker.Monitor.Tests --filter "PauseAsync_BlocksNewJobDispatchWhileInFlightCompletesAsync|ResumeAsync_RestoresDispatchAfterPauseAsync" --no-restore -v minimal`
Expected: Compile error — `PauseAsync`, `ResumeAsync`, `IsPaused` do not exist yet.

- [ ] **Step 4: Add PauseAsync/ResumeAsync to IMonitorJobScheduler**

In `AIUsageTracker.Monitor/Services/IMonitorJobScheduler.cs`, add after the `GetSnapshot()` method:

```csharp
void PauseAsync();

void ResumeAsync();
```

- [ ] **Step 5: Add IsPaused to MonitorJobSchedulerSnapshot**

In `AIUsageTracker.Monitor/Services/MonitorJobSchedulerSnapshot.cs`, add:

```csharp
public bool IsPaused { get; init; }
```

- [ ] **Step 6: Implement PauseAsync/ResumeAsync in MonitorJobScheduler**

In `AIUsageTracker.Monitor/Services/MonitorJobScheduler.cs`:

Add new fields after line 48 (`private CancellationToken _schedulerToken`):

```csharp
private volatile bool _paused;
private readonly SemaphoreSlim _pauseGate = new(1, 1);
```

Add the two methods after `GetSnapshot()` (after line 169):

```csharp
public void PauseAsync()
{
    if (this._paused)
    {
        return;
    }

    this._paused = true;
    this._pauseGate.Wait();
    this._logger.LogInformation("Job scheduler paused");
}

public void ResumeAsync()
{
    if (!this._paused)
    {
        return;
    }

    this._paused = false;
    this._pauseGate.Release();
    this._logger.LogInformation("Job scheduler resumed");
}
```

Modify `ExecuteAsync` — in the dispatch loop, after the `WaitAsync` on `_queuedItemsSignal` (line 189) and before `TryDequeueNext` (line 190), add a pause gate check:

```csharp
// Wait for pause gate — blocks dispatch when paused
await this._pauseGate.WaitAsync(stoppingToken).ConfigureAwait(false);
this._pauseGate.Release();
```

Add `IsPaused` to the `GetSnapshot()` method return:

```csharp
IsPaused = this._paused,
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test AIUsageTracker.Monitor.Tests --filter "PauseAsync_BlocksNewJobDispatchWhileInFlightCompletesAsync|ResumeAsync_RestoresDispatchAfterPauseAsync" --no-restore -v minimal`
Expected: Both PASS.

- [ ] **Step 8: Run all existing scheduler tests to verify no regressions**

Run: `dotnet test AIUsageTracker.Monitor.Tests --filter "MonitorJobSchedulerTests" --no-restore -v minimal`
Expected: All existing tests still pass.

- [ ] **Step 9: Commit**

```bash
git add AIUsageTracker.Monitor/Services/IMonitorJobScheduler.cs AIUsageTracker.Monitor/Services/MonitorJobScheduler.cs AIUsageTracker.Monitor/Services/MonitorJobSchedulerSnapshot.cs AIUsageTracker.Monitor.Tests/MonitorJobSchedulerTests.cs
git commit -m "feat: add PauseAsync/ResumeAsync to MonitorJobScheduler for power state resilience"
```

---

### Task 2: Add CancelActiveRefresh to ProviderRefreshService

**Files:**
- Modify: `AIUsageTracker.Monitor/Services/ProviderRefreshService.cs:34` (fields), `160-261` (TriggerRefreshAsync)
- Modify: `AIUsageTracker.Monitor.Tests/ProviderRefreshServiceTests.cs`

- [ ] **Step 1: Write the failing test — CancelActiveRefresh cancels current cycle**

Add to `AIUsageTracker.Monitor.Tests/ProviderRefreshServiceTests.cs`:

```csharp
[Fact]
public void CancelActiveRefresh_WhenNoRefreshActive_DoesNotThrow()
{
    this._service.CancelActiveRefresh();
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test AIUsageTracker.Monitor.Tests --filter "CancelActiveRefresh_WhenNoRefreshActive_DoesNotThrow" --no-restore -v minimal`
Expected: Compile error — `CancelActiveRefresh` does not exist.

- [ ] **Step 3: Implement CancelActiveRefresh**

In `AIUsageTracker.Monitor/Services/ProviderRefreshService.cs`:

Add a new field after line 34 (`private readonly SemaphoreSlim _refreshSemaphore`):

```csharp
private CancellationTokenSource? _activeRefreshCts;
```

Add the public method after `QueueForceRefresh` (after line 93):

```csharp
public void CancelActiveRefresh()
{
    var cts = this._activeRefreshCts;
    if (cts != null && !cts.IsCancellationRequested)
    {
        this._logger.LogInformation("Cancelling active refresh cycle (power state transition)");
        cts.Cancel();
    }
}
```

In `TriggerRefreshAsync` (line 160), after the method signature and before `using var refreshActivity`, add CTS management:

```csharp
using var refreshCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
this._activeRefreshCts = refreshCts;
var effectiveToken = refreshCts.Token;
```

Replace all uses of `cancellationToken` inside `TriggerRefreshAsync` and `RefreshAndStoreProviderDataAsync` with `effectiveToken` — specifically:
- Line 181: `await this._refreshSemaphore.WaitAsync(cancellationToken)` → `await this._refreshSemaphore.WaitAsync(effectiveToken)`

In the `finally` block of `TriggerRefreshAsync` (after line 259), add:

```csharp
this._activeRefreshCts = null;
```

Also pass `effectiveToken` through to `RefreshAndStoreProviderDataAsync` on line 223-229.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test AIUsageTracker.Monitor.Tests --filter "CancelActiveRefresh_WhenNoRefreshActive_DoesNotThrow" --no-restore -v minimal`
Expected: PASS.

- [ ] **Step 5: Run all existing refresh service tests**

Run: `dotnet test AIUsageTracker.Monitor.Tests --filter "ProviderRefreshServiceTests" --no-restore -v minimal`
Expected: All existing tests still pass.

- [ ] **Step 6: Commit**

```bash
git add AIUsageTracker.Monitor/Services/ProviderRefreshService.cs AIUsageTracker.Monitor.Tests/ProviderRefreshServiceTests.cs
git commit -m "feat: add CancelActiveRefresh to ProviderRefreshService for power state transitions"
```

---

### Task 3: Create PowerStateListener hosted service

**Files:**
- Create: `AIUsageTracker.Monitor/Services/PowerStateListener.cs`
- Create: `AIUsageTracker.Monitor.Tests/PowerStateListenerTests.cs`
- Modify: `AIUsageTracker.Monitor/Program.cs:218-220` (service registration)

- [ ] **Step 1: Write the failing tests**

Create `AIUsageTracker.Monitor.Tests/PowerStateListenerTests.cs`:

```csharp
// <copyright file="PowerStateListenerTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Monitor.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AIUsageTracker.Monitor.Tests;

public class PowerStateListenerTests
{
    private readonly Mock<ILogger<PowerStateListener>> _mockLogger;
    private readonly Mock<MonitorJobScheduler> _mockScheduler;
    private readonly Mock<ProviderRefreshService> _mockRefreshService;
    private readonly Mock<IAppPathProvider> _mockPathProvider;

    public PowerStateListenerTests()
    {
        this._mockLogger = new Mock<ILogger<PowerStateListener>>();
        this._mockScheduler = new Mock<MonitorJobScheduler>(new Mock<ILogger<MonitorJobScheduler>>().Object) { CallBase = false };
        this._mockRefreshService = new Mock<ProviderRefreshService>() { CallBase = false };
        this._mockPathProvider = new Mock<IAppPathProvider>();
    }

    [Fact]
    public void OnSuspend_PausesSchedulerAndCancelsRefresh()
    {
        var scheduler = new MonitorJobScheduler(new Mock<ILogger<MonitorJobScheduler>>().Object);
        var listener = new PowerStateListener(
            this._mockLogger.Object,
            scheduler,
            this._mockPathProvider.Object,
            onSuspend: () =>
            {
                scheduler.PauseAsync();
            },
            onResume: () =>
            {
                scheduler.ResumeAsync();
            });

        listener.SimulateSuspend();

        var snapshot = scheduler.GetSnapshot();
        Assert.True(snapshot.IsPaused);
    }

    [Fact]
    public void OnResume_ResumesScheduler()
    {
        var scheduler = new MonitorJobScheduler(new Mock<ILogger<MonitorJobScheduler>>().Object);
        var listener = new PowerStateListener(
            this._mockLogger.Object,
            scheduler,
            this._mockPathProvider.Object,
            onSuspend: () =>
            {
                scheduler.PauseAsync();
            },
            onResume: () =>
            {
                scheduler.ResumeAsync();
            });

        listener.SimulateSuspend();
        Assert.True(scheduler.GetSnapshot().IsPaused);

        listener.SimulateResume();
        Assert.False(scheduler.GetSnapshot().IsPaused);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test AIUsageTracker.Monitor.Tests --filter "PowerStateListenerTests" --no-restore -v minimal`
Expected: Compile error — `PowerStateListener` does not exist.

- [ ] **Step 3: Create PowerStateListener**

Create `AIUsageTracker.Monitor/Services/PowerStateListener.cs`:

```csharp
// <copyright file="PowerStateListener.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Monitor.Services;

public sealed class PowerStateListener : IHostedService, IDisposable
{
    private readonly ILogger<PowerStateListener> _logger;
    private readonly IAppPathProvider _pathProvider;
    private readonly Action _onSuspend;
    private readonly Action _onResume;
    private bool _subscribed;

    public PowerStateListener(
        ILogger<PowerStateListener> logger,
        MonitorJobScheduler scheduler,
        IAppPathProvider pathProvider,
        Action? onSuspend = null,
        Action? onResume = null)
    {
        this._logger = logger;
        this._pathProvider = pathProvider;

        this._onSuspend = onSuspend ?? (() =>
        {
            scheduler.PauseAsync();
        });

        this._onResume = onResume ?? (() =>
        {
            scheduler.ResumeAsync();
            scheduler.Enqueue(
                "post-resume-refresh",
                _ => Task.CompletedTask,
                MonitorJobPriority.High);
        });
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            this._logger.LogDebug("Power state listener skipped — not running on Windows");
            return Task.CompletedTask;
        }

        SubscribePowerEvents();
        this._logger.LogInformation("Power state listener started");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (this._subscribed)
        {
            UnsubscribePowerEvents();
        }

        return Task.CompletedTask;
    }

    public void SimulateSuspend()
    {
        this.HandleSuspend();
    }

    public void SimulateResume()
    {
        this.HandleResume();
    }

    public void Dispose()
    {
        if (this._subscribed)
        {
            UnsubscribePowerEvents();
        }
    }

    private void HandleSuspend()
    {
        this._logger.LogInformation("System entering suspend — pausing scheduled jobs");
        try
        {
            this._onSuspend();
            MonitorInfoPersistence.SaveMonitorInfo(0, false, this._logger, this._pathProvider, startupStatus: "suspended");
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Error handling suspend event");
        }
    }

    private void HandleResume()
    {
        this._logger.LogInformation("System resuming — restarting scheduled jobs");
        try
        {
            this._onResume();
            MonitorInfoPersistence.SaveMonitorInfo(0, false, this._logger, this._pathProvider, startupStatus: "running");
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Error handling resume event");
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void SubscribePowerEvents()
    {
        Microsoft.Win32.SystemEvents.PowerModeChanged += this.OnPowerModeChanged;
        this._subscribed = true;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void UnsubscribePowerEvents()
    {
        Microsoft.Win32.SystemEvents.PowerModeChanged -= this.OnPowerModeChanged;
        this._subscribed = false;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case Microsoft.Win32.PowerModes.Suspend:
                this.HandleSuspend();
                break;
            case Microsoft.Win32.PowerModes.Resume:
                this.HandleResume();
                break;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test AIUsageTracker.Monitor.Tests --filter "PowerStateListenerTests" --no-restore -v minimal`
Expected: Both tests PASS.

- [ ] **Step 5: Register PowerStateListener in Program.cs**

In `AIUsageTracker.Monitor/Program.cs`, after the line `builder.Services.AddHostedService(sp => sp.GetRequiredService<ProviderRefreshService>());` (line 230), add:

```csharp
if (OperatingSystem.IsWindows())
{
    builder.Services.AddSingleton<PowerStateListener>(sp =>
        new PowerStateListener(
            sp.GetRequiredService<ILogger<PowerStateListener>>(),
            sp.GetRequiredService<MonitorJobScheduler>(),
            sp.GetRequiredService<IAppPathProvider>(),
            onSuspend: () =>
            {
                sp.GetRequiredService<MonitorJobScheduler>().PauseAsync();
                sp.GetRequiredService<ProviderRefreshService>().CancelActiveRefresh();
            },
            onResume: () =>
            {
                var scheduler = sp.GetRequiredService<MonitorJobScheduler>();
                scheduler.ResumeAsync();
                sp.GetRequiredService<ProviderRefreshService>().QueueManualRefresh(forceAll: true);
            }));
    builder.Services.AddHostedService(sp => sp.GetRequiredService<PowerStateListener>());
}
```

- [ ] **Step 6: Run all Monitor tests**

Run: `dotnet test AIUsageTracker.Monitor.Tests --no-restore -v minimal`
Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
git add AIUsageTracker.Monitor/Services/PowerStateListener.cs AIUsageTracker.Monitor.Tests/PowerStateListenerTests.cs AIUsageTracker.Monitor/Program.cs
git commit -m "feat: add PowerStateListener to pause/resume Monitor on sleep/wake"
```

---

### Task 4: Add adaptive watchdog to MonitorStartupOrchestrator

**Files:**
- Modify: `AIUsageTracker.UI.Slim/Services/MonitorStartupOrchestrator.cs`
- Create: `AIUsageTracker.Tests/Core/MonitorStartupOrchestratorWatchdogTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `AIUsageTracker.Tests/Core/MonitorStartupOrchestratorWatchdogTests.cs`:

```csharp
// <copyright file="MonitorStartupOrchestratorWatchdogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Infrastructure.Services;
using AIUsageTracker.UI.Slim.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AIUsageTracker.Tests.Core;

public class MonitorStartupOrchestratorWatchdogTests
{
    private readonly Mock<IMonitorService> _mockMonitorService;
    private readonly Mock<MonitorLifecycleService> _mockLifecycle;
    private readonly Mock<ILogger<MonitorStartupOrchestrator>> _mockLogger;

    public MonitorStartupOrchestratorWatchdogTests()
    {
        this._mockMonitorService = new Mock<IMonitorService>();
        var launcherLogger = new Mock<ILogger<MonitorLauncher>>();
        var launcher = new MonitorLauncher(launcherLogger.Object);
        this._mockLifecycle = new Mock<MonitorLifecycleService>(launcher) { CallBase = false };
        this._mockLogger = new Mock<ILogger<MonitorStartupOrchestrator>>();
    }

    [Fact]
    public async Task Watchdog_WhenHealthCheckFails_CallsEnsureAgentRunningAsync()
    {
        this._mockMonitorService.Setup(m => m.CheckHealthAsync(It.IsAny<TimeSpan>())).ReturnsAsync(false);
        this._mockLifecycle.Setup(m => m.EnsureAgentRunningAsync()).ReturnsAsync(true);
        this._mockMonitorService.Setup(m => m.RefreshPortAsync()).Returns(Task.CompletedTask);

        var orchestrator = new MonitorStartupOrchestrator(
            this._mockMonitorService.Object,
            this._mockLifecycle.Object,
            this._mockLogger.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        // Trigger a single watchdog tick
        await orchestrator.RunWatchdogTickAsync();

        this._mockLifecycle.Verify(m => m.EnsureAgentRunningAsync(), Times.Once);
    }

    [Fact]
    public async Task Watchdog_WhenHealthCheckSucceeds_DoesNotRelaunch()
    {
        this._mockMonitorService.Setup(m => m.CheckHealthAsync(It.IsAny<TimeSpan>())).ReturnsAsync(true);

        var orchestrator = new MonitorStartupOrchestrator(
            this._mockMonitorService.Object,
            this._mockLifecycle.Object,
            this._mockLogger.Object);

        await orchestrator.RunWatchdogTickAsync();

        this._mockLifecycle.Verify(m => m.EnsureAgentRunningAsync(), Times.Never);
    }

    [Fact]
    public async Task Watchdog_AfterRepeatedFailures_IncreasesInterval()
    {
        this._mockMonitorService.Setup(m => m.CheckHealthAsync(It.IsAny<TimeSpan>())).ReturnsAsync(false);
        this._mockLifecycle.Setup(m => m.EnsureAgentRunningAsync()).ReturnsAsync(false);

        var orchestrator = new MonitorStartupOrchestrator(
            this._mockMonitorService.Object,
            this._mockLifecycle.Object,
            this._mockLogger.Object);

        // First failure: interval should increase from 60s
        await orchestrator.RunWatchdogTickAsync();
        var firstInterval = orchestrator.CurrentWatchdogInterval;

        await orchestrator.RunWatchdogTickAsync();
        var secondInterval = orchestrator.CurrentWatchdogInterval;

        Assert.True(secondInterval > firstInterval);
    }

    [Fact]
    public async Task Watchdog_AfterRecovery_ResetsInterval()
    {
        this._mockMonitorService.Setup(m => m.CheckHealthAsync(It.IsAny<TimeSpan>())).ReturnsAsync(false);
        this._mockLifecycle.Setup(m => m.EnsureAgentRunningAsync()).ReturnsAsync(false);
        this._mockMonitorService.Setup(m => m.RefreshPortAsync()).Returns(Task.CompletedTask);

        var orchestrator = new MonitorStartupOrchestrator(
            this._mockMonitorService.Object,
            this._mockLifecycle.Object,
            this._mockLogger.Object);

        // Fail a few times to increase interval
        await orchestrator.RunWatchdogTickAsync();
        await orchestrator.RunWatchdogTickAsync();
        Assert.True(orchestrator.CurrentWatchdogInterval > TimeSpan.FromSeconds(60));

        // Recover
        this._mockMonitorService.Setup(m => m.CheckHealthAsync(It.IsAny<TimeSpan>())).ReturnsAsync(true);
        await orchestrator.RunWatchdogTickAsync();
        Assert.Equal(TimeSpan.FromSeconds(60), orchestrator.CurrentWatchdogInterval);
    }

    [Fact]
    public async Task NotifyResumed_TriggersImmediateWatchdogCheck()
    {
        var healthCheckCount = 0;
        this._mockMonitorService
            .Setup(m => m.CheckHealthAsync(It.IsAny<TimeSpan>()))
            .ReturnsAsync(true)
            .Callback(() => Interlocked.Increment(ref healthCheckCount));

        var orchestrator = new MonitorStartupOrchestrator(
            this._mockMonitorService.Object,
            this._mockLifecycle.Object,
            this._mockLogger.Object);

        // Signal resume — the next watchdog tick should fire immediately
        orchestrator.NotifyResumed();

        // RunWatchdogTickAsync should check health
        await orchestrator.RunWatchdogTickAsync();
        Assert.True(healthCheckCount >= 1);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test AIUsageTracker.Tests --filter "MonitorStartupOrchestratorWatchdogTests" --no-restore -v minimal`
Expected: Compile error — `RunWatchdogTickAsync`, `CurrentWatchdogInterval`, `NotifyResumed` do not exist.

- [ ] **Step 3: Implement watchdog in MonitorStartupOrchestrator**

Modify `AIUsageTracker.UI.Slim/Services/MonitorStartupOrchestrator.cs`. Replace the entire file:

```csharp
// <copyright file="MonitorStartupOrchestrator.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim.Services;

public sealed class MonitorStartupOrchestrator
{
    private static readonly TimeSpan BaseInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaxInterval = TimeSpan.FromSeconds(300);
    private static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromMilliseconds(2000);

    private readonly IMonitorService _monitorService;
    private readonly MonitorLifecycleService _monitorLifecycleService;
    private readonly ILogger<MonitorStartupOrchestrator> _logger;
    private readonly TaskCompletionSource<bool> _resumeSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource<bool>? _activeResumeSignal;
    private int _consecutiveFailures;

    public MonitorStartupOrchestrator(
        IMonitorService monitorService,
        MonitorLifecycleService monitorLifecycleService,
        ILogger<MonitorStartupOrchestrator> logger)
    {
        this._monitorService = monitorService;
        this._monitorLifecycleService = monitorLifecycleService;
        this._logger = logger;
        this.CurrentWatchdogInterval = BaseInterval;
    }

    public TimeSpan CurrentWatchdogInterval { get; private set; }

    public async Task<MonitorStartupOrchestrationResult> EnsureMonitorReadyAsync(
        Func<string, StatusType, Task> reportStatusAsync,
        bool skipInitialHealthCheck = false)
    {
        ArgumentNullException.ThrowIfNull(reportStatusAsync);

        try
        {
            // If caller already checked health and it failed, skip the redundant check
            // to avoid wasting another 6+ seconds on a TCP timeout.
            var isRunning = false;
            if (!skipInitialHealthCheck)
            {
                await this._monitorService.RefreshPortAsync().ConfigureAwait(false);
                isRunning = await this._monitorService.CheckHealthAsync().ConfigureAwait(false);
            }

            if (!isRunning)
            {
                await reportStatusAsync("Starting monitor...", StatusType.Warning).ConfigureAwait(false);

                var monitorReady = await this._monitorLifecycleService.EnsureAgentRunningAsync().ConfigureAwait(false);
                if (!monitorReady)
                {
                    await this._monitorService.RefreshAgentInfoAsync().ConfigureAwait(false);
                    return new MonitorStartupOrchestrationResult(IsSuccess: false, IsLaunchFailure: true);
                }

                await this._monitorService.RefreshPortAsync().ConfigureAwait(false);
            }
            else if (await this.TryRestartMonitorForVersionMismatchAsync(reportStatusAsync).ConfigureAwait(false))
            {
                await this._monitorService.RefreshPortAsync().ConfigureAwait(false);
            }

            return new MonitorStartupOrchestrationResult(IsSuccess: true, IsLaunchFailure: false);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Startup orchestration failed");
            return new MonitorStartupOrchestrationResult(IsSuccess: false, IsLaunchFailure: false);
        }
    }

    public async Task RunWatchdogLoopAsync(CancellationToken cancellationToken)
    {
        this._logger.LogDebug("Monitor watchdog started (interval: {Interval}s)", BaseInterval.TotalSeconds);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Wait for the current interval OR resume signal, whichever comes first
                this._activeResumeSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var delayTask = Task.Delay(this.CurrentWatchdogInterval, cancellationToken);
                var resumeTask = this._activeResumeSignal.Task;

                await Task.WhenAny(delayTask, resumeTask).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                await this.RunWatchdogTickAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "Watchdog tick failed");
            }
        }

        this._logger.LogDebug("Monitor watchdog stopped");
    }

    public async Task RunWatchdogTickAsync()
    {
        try
        {
            var isHealthy = await this._monitorService.CheckHealthAsync(HealthCheckTimeout).ConfigureAwait(false);

            if (isHealthy)
            {
                if (this._consecutiveFailures > 0)
                {
                    this._logger.LogInformation("Monitor recovered after {Failures} failed health checks", this._consecutiveFailures);
                }

                this._consecutiveFailures = 0;
                this.CurrentWatchdogInterval = BaseInterval;
                return;
            }

            this._consecutiveFailures++;
            this._logger.LogWarning("Monitor health check failed (consecutive failures: {Count})", this._consecutiveFailures);

            await this._monitorService.RefreshPortAsync().ConfigureAwait(false);
            var restarted = await this._monitorLifecycleService.EnsureAgentRunningAsync().ConfigureAwait(false);

            if (restarted)
            {
                this._logger.LogInformation("Monitor relaunched by watchdog after health check failure");
                await this._monitorService.RefreshPortAsync().ConfigureAwait(false);
                this._consecutiveFailures = 0;
                this.CurrentWatchdogInterval = BaseInterval;
            }
            else
            {
                // Exponential backoff: 60 -> 120 -> 240 -> 300 (capped)
                var backoffSeconds = Math.Min(
                    BaseInterval.TotalSeconds * Math.Pow(2, this._consecutiveFailures - 1),
                    MaxInterval.TotalSeconds);
                this.CurrentWatchdogInterval = TimeSpan.FromSeconds(backoffSeconds);
                this._logger.LogWarning(
                    "Monitor relaunch failed. Next check in {Seconds}s",
                    this.CurrentWatchdogInterval.TotalSeconds);
            }
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Error during watchdog health check");
            this._consecutiveFailures++;
            var backoffSeconds = Math.Min(
                BaseInterval.TotalSeconds * Math.Pow(2, this._consecutiveFailures - 1),
                MaxInterval.TotalSeconds);
            this.CurrentWatchdogInterval = TimeSpan.FromSeconds(backoffSeconds);
        }
    }

    public void NotifyResumed()
    {
        this._activeResumeSignal?.TrySetResult(true);
    }

    private async Task<bool> TryRestartMonitorForVersionMismatchAsync(Func<string, StatusType, Task> reportStatusAsync)
    {
        try
        {
            var healthSnapshot = await this._monitorService.GetHealthSnapshotAsync().ConfigureAwait(false);
            if (healthSnapshot == null)
            {
                return false;
            }

            var monitorVersion = healthSnapshot.AgentVersion;
            var uiVersion = typeof(App).Assembly.GetName().Version?.ToString();
            if (string.IsNullOrEmpty(monitorVersion) || string.IsNullOrEmpty(uiVersion))
            {
                return false;
            }

            if (string.Equals(monitorVersion, uiVersion, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            MonitorService.LogDiagnostic(
                $"Monitor version mismatch (monitor: {monitorVersion}, ui: {uiVersion}). Restarting monitor...");
            this._logger.LogWarning(
                "Monitor version mismatch (monitor: {MonitorVersion}, ui: {UiVersion}). Restarting monitor...",
                monitorVersion,
                uiVersion);

            await reportStatusAsync("Restarting monitor (version mismatch)...", StatusType.Warning).ConfigureAwait(false);

            await this._monitorLifecycleService.StopAgentAsync().ConfigureAwait(false);
            var started = await this._monitorLifecycleService.EnsureAgentRunningAsync().ConfigureAwait(false);
            if (!started)
            {
                MonitorService.LogDiagnostic("Failed to restart monitor after version mismatch.");
                this._logger.LogError("Failed to restart monitor after version mismatch");
                return false;
            }

            MonitorService.LogDiagnostic("Monitor restarted successfully after version mismatch.");
            return true;
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Error during monitor version mismatch restart");
            return false;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test AIUsageTracker.Tests --filter "MonitorStartupOrchestratorWatchdogTests" --no-restore -v minimal`
Expected: All 5 tests PASS.

- [ ] **Step 5: Run full test suite to check for regressions**

Run: `dotnet test AIUsageTracker.Tests --no-restore -v minimal -T 4`
Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add AIUsageTracker.UI.Slim/Services/MonitorStartupOrchestrator.cs AIUsageTracker.Tests/Core/MonitorStartupOrchestratorWatchdogTests.cs
git commit -m "feat: add adaptive watchdog to MonitorStartupOrchestrator with power-aware fast path"
```

---

### Task 5: Wire watchdog into UI Slim startup and power events

**Files:**
- Modify: `AIUsageTracker.UI.Slim/MainWindow.xaml.cs` (start watchdog after initial startup, subscribe to power events)

- [ ] **Step 1: Find where EnsureMonitorReadyAsync is first called in MainWindow**

Read `AIUsageTracker.UI.Slim/MainWindow.xaml.cs` and locate the initial `EnsureMonitorReadyAsync` call. This is where we'll start the watchdog loop after success.

- [ ] **Step 2: Add watchdog startup and power event subscription**

After the first successful `EnsureMonitorReadyAsync()` call in `MainWindow.xaml.cs`, add:

```csharp
// Start the watchdog loop in the background
_ = Task.Run(() => this._monitorStartupOrchestrator.RunWatchdogLoopAsync(this._appCts.Token));
```

Where `_appCts` is the application-level `CancellationTokenSource` (look for existing one or add as field).

In the `MainWindow` constructor or initialization, subscribe to power events for the fast-path wake signal:

```csharp
if (OperatingSystem.IsWindows())
{
    Microsoft.Win32.SystemEvents.PowerModeChanged += this.OnPowerModeChanged;
}
```

Add the handler:

```csharp
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
{
    if (e.Mode == Microsoft.Win32.PowerModes.Resume)
    {
        this._logger.LogInformation("System resumed — triggering immediate watchdog check");
        this._monitorStartupOrchestrator.NotifyResumed();
    }
}
```

Unsubscribe in the window closing / dispose path:

```csharp
if (OperatingSystem.IsWindows())
{
    Microsoft.Win32.SystemEvents.PowerModeChanged -= this.OnPowerModeChanged;
}
```

- [ ] **Step 3: Build the UI.Slim project to verify compilation**

Run: `dotnet build AIUsageTracker.UI.Slim --no-restore -v minimal`
Expected: Build succeeds.

- [ ] **Step 4: Run full test suite**

Run: `dotnet test --no-restore -v minimal -T 4`
Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add AIUsageTracker.UI.Slim/MainWindow.xaml.cs
git commit -m "feat: wire watchdog loop and power event subscription into UI Slim MainWindow"
```

---

### Task 6: Full integration verification

- [ ] **Step 1: Build entire solution**

Run: `dotnet build --no-restore -v minimal`
Expected: Clean build, no warnings related to new code.

- [ ] **Step 2: Run full test suite**

Run: `dotnet test --no-restore -v minimal -T 4`
Expected: All tests pass.

- [ ] **Step 3: Verify no analyzer violations**

Run: `dotnet build --no-restore -v minimal /p:TreatWarningsAsErrors=true` (if configured)
Expected: No new warnings.

- [ ] **Step 4: Final commit if any fixups needed**

Only if Steps 1-3 revealed issues that needed fixing.
