# Performance & Security Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement five targeted fixes — CLI key via stdin, CORS tightening, ConfigService save validation, fire-and-forget exception logging, and removal of a redundant 1-second polling delay.

**Architecture:** All fixes are surgical: one or two files each, no new abstractions. Task C is the only one that needs a new test file. Tasks A, B, D, E are direct edits with no structural changes.

**Tech Stack:** C# / .NET 8, xUnit, Moq, `NullLogger<T>.Instance`, `Microsoft.Extensions.Logging.Abstractions`

---

## File Map

| Task | File(s) touched |
|------|----------------|
| A — CLI stdin | `AIUsageTracker.CLI/Program.cs` |
| B — CORS | `AIUsageTracker.Monitor/Program.cs` |
| C — Config validation | `AIUsageTracker.Monitor/Services/ConfigService.cs` + new `AIUsageTracker.Tests/Services/ConfigServiceSaveValidationTests.cs` |
| D — Fire-and-forget logging | `AIUsageTracker.UI.Slim/MainWindow.xaml.cs` |
| E — Polling delay | `AIUsageTracker.UI.Slim/MainWindow.Polling.cs` |

---

## Task A: CLI — Accept API Key via Stdin When Omitted from Args

**Files:**
- Modify: `AIUsageTracker.CLI/Program.cs` — `RunAsync` switch case for `set-key`, and `SetKeyAsync`

- [ ] **Step 1: Update the `set-key` case in `RunAsync` to allow 2-arg form**

In `RunAsync`, find the `case "set-key":` block (lines 117–125). Replace it with:

```csharp
case "set-key":
    if (args.Length < 2)
    {
        Console.WriteLine("Usage: act set-key <provider-id> [api-key]");
        Console.WriteLine("  If api-key is omitted, you will be prompted to enter it.");
        return;
    }

    string apiKeyArg;
    if (args.Length >= 3)
    {
        apiKeyArg = args[2];
    }
    else
    {
        Console.Write($"Enter API key for '{args[1]}': ");
        apiKeyArg = Console.ReadLine() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(apiKeyArg))
        {
            Console.WriteLine("No key entered. Aborting.");
            return;
        }
    }

    await SetKeyAsync(agentService, args[1], apiKeyArg).ConfigureAwait(false);
    break;
```

- [ ] **Step 2: Build to verify no compile errors**

```bash
dotnet build AIUsageTracker.CLI/AIUsageTracker.CLI.csproj
```

Expected: Build succeeded, 0 error(s).

- [ ] **Step 3: Commit**

```bash
git add AIUsageTracker.CLI/Program.cs
git commit -m "fix: read API key from stdin when omitted from set-key args"
```

---

## Task B: CORS — Restrict to Explicit Methods and Headers

**Files:**
- Modify: `AIUsageTracker.Monitor/Program.cs` — `AddCors` block (lines 174–183)

- [ ] **Step 1: Replace `AllowAnyMethod` and `AllowAnyHeader`**

Find the `AddCors` block:
```csharp
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5100", "http://localhost:5000") // Explicit origins for SignalR/CORS safety
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials(); // Required for SignalR with WebSockets/Long Polling
    });
});
```

Replace with:
```csharp
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5100", "http://localhost:5000") // Explicit origins for SignalR/CORS safety
              .WithMethods("GET", "POST")
              .WithHeaders("Content-Type", "Authorization", "X-Requested-With")
              .AllowCredentials(); // Required for SignalR with WebSockets/Long Polling
    });
});
```

- [ ] **Step 2: Build Monitor**

```bash
dotnet build AIUsageTracker.Monitor/AIUsageTracker.Monitor.csproj
```

Expected: Build succeeded, 0 error(s).

- [ ] **Step 3: Commit**

```bash
git add AIUsageTracker.Monitor/Program.cs
git commit -m "fix: restrict CORS to explicit methods (GET, POST) and headers"
```

---

## Task C: ConfigService — Validate ProviderId on Save

**Files:**
- Modify: `AIUsageTracker.Monitor/Services/ConfigService.cs` — `SaveConfigAsync` method
- Create: `AIUsageTracker.Tests/Services/ConfigServiceSaveValidationTests.cs`

- [ ] **Step 1: Write the failing test**

Create `AIUsageTracker.Tests/Services/ConfigServiceSaveValidationTests.cs`:

```csharp
// <copyright file="ConfigServiceSaveValidationTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;
using AIUsageTracker.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AIUsageTracker.Tests.Services;

public sealed class ConfigServiceSaveValidationTests : IntegrationTestBase
{
    private ConfigService CreateConfigService()
    {
        var authPath = this.CreateFile("config/auth.json", "{}");
        var providersPath = this.CreateFile("config/providers.json", "{}");
        var prefsPath = this.CreateFile("preferences.json", "{}");

        var mockPathProvider = new Mock<IAppPathProvider>();
        mockPathProvider.Setup(p => p.GetAuthFilePath()).Returns(authPath);
        mockPathProvider.Setup(p => p.GetProviderConfigFilePath()).Returns(providersPath);
        mockPathProvider.Setup(p => p.GetPreferencesFilePath()).Returns(prefsPath);
        mockPathProvider.Setup(p => p.GetUserProfileRoot()).Returns(this.TestRootPath);
        mockPathProvider.Setup(p => p.GetAppDataRoot()).Returns(this.TestRootPath);
        mockPathProvider.Setup(p => p.GetDatabasePath()).Returns(Path.Combine(this.TestRootPath, "usage.db"));
        mockPathProvider.Setup(p => p.GetLogDirectory()).Returns(Path.Combine(this.TestRootPath, "logs"));

        return new ConfigService(
            NullLogger<ConfigService>.Instance,
            NullLoggerFactory.Instance,
            mockPathProvider.Object);
    }

    [Fact]
    public async Task SaveConfigAsync_UnknownProviderId_ThrowsArgumentExceptionAsync()
    {
        var service = this.CreateConfigService();
        var config = new ProviderConfig { ProviderId = "totally-unknown-provider-xyz-99999" };

        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveConfigAsync(config));
    }

    [Fact]
    public async Task SaveConfigAsync_KnownProviderId_DoesNotThrowAsync()
    {
        var service = this.CreateConfigService();
        // "claude-code" is a well-known provider in ProviderMetadataCatalog
        var config = new ProviderConfig { ProviderId = "claude-code", ApiKey = "sk-test" };

        // Should complete without throwing
        await service.SaveConfigAsync(config);
    }

    [Fact]
    public async Task SaveConfigAsync_NullConfig_ThrowsArgumentNullExceptionAsync()
    {
        var service = this.CreateConfigService();

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.SaveConfigAsync(null!));
    }
}
```

- [ ] **Step 2: Run to confirm the unknown-provider test fails (no validation yet)**

```bash
dotnet test AIUsageTracker.Tests/AIUsageTracker.Tests.csproj --filter "FullyQualifiedName~ConfigServiceSaveValidationTests.SaveConfigAsync_UnknownProviderId" -T 4
```

Expected: FAIL — `SaveConfigAsync` currently accepts the unknown ID without throwing.

- [ ] **Step 3: Add validation to `SaveConfigAsync` in ConfigService**

In `AIUsageTracker.Monitor/Services/ConfigService.cs`, find `SaveConfigAsync` (line 79). After `ArgumentNullException.ThrowIfNull(config);`, add:

```csharp
if (!AIUsageTracker.Infrastructure.Providers.ProviderMetadataCatalog.TryGet(config.ProviderId, out _))
{
    throw new ArgumentException(
        $"Unknown provider ID '{config.ProviderId}'. Only catalog-registered providers may be saved.",
        nameof(config));
}
```

The full updated method signature area becomes:
```csharp
public async Task SaveConfigAsync(ProviderConfig config)
{
    ArgumentNullException.ThrowIfNull(config);
    if (!AIUsageTracker.Infrastructure.Providers.ProviderMetadataCatalog.TryGet(config.ProviderId, out _))
    {
        throw new ArgumentException(
            $"Unknown provider ID '{config.ProviderId}'. Only catalog-registered providers may be saved.",
            nameof(config));
    }
    try
    {
        // ... rest unchanged
```

- [ ] **Step 4: Check the using statements — add if missing**

At the top of `ConfigService.cs`, `ProviderMetadataCatalog` is in `AIUsageTracker.Infrastructure.Providers`. The existing `using AIUsageTracker.Infrastructure.Providers;` statement is already present (line 8). No change needed.

- [ ] **Step 5: Run all three tests**

```bash
dotnet test AIUsageTracker.Tests/AIUsageTracker.Tests.csproj --filter "FullyQualifiedName~ConfigServiceSaveValidationTests" -T 4
```

Expected: All 3 PASS.

- [ ] **Step 6: Run full test suite to check for regressions**

```bash
dotnet test AIUsageTracker.Tests/AIUsageTracker.Tests.csproj -T 4
```

Expected: All tests pass. (If any test calls `SaveConfigAsync` with an unknown provider ID, it will now fail — fix those by using a real catalog ID.)

- [ ] **Step 7: Commit**

```bash
git add AIUsageTracker.Monitor/Services/ConfigService.cs
git add AIUsageTracker.Tests/Services/ConfigServiceSaveValidationTests.cs
git commit -m "fix: validate provider ID against catalog in SaveConfigAsync"
```

---

## Task D: Fire-and-Forget — Log Exceptions from CheckForUpdatesAsync

**Files:**
- Modify: `AIUsageTracker.UI.Slim/MainWindow.xaml.cs` — `Loaded` handler

- [ ] **Step 1: Replace the discard pattern with a faulted continuation**

In `MainWindow.xaml.cs`, find the `Loaded` handler (around line 201). The current line:
```csharp
_ = this.CheckForUpdatesAsync();
```

Replace with:
```csharp
_ = this.CheckForUpdatesAsync().ContinueWith(
    t => this._logger.LogError(t.Exception, "CheckForUpdatesAsync failed unhandled"),
    CancellationToken.None,
    TaskContinuationOptions.OnlyOnFaulted,
    TaskScheduler.Default);
```

- [ ] **Step 2: Build UI.Slim**

```bash
dotnet build AIUsageTracker.UI.Slim/AIUsageTracker.UI.Slim.csproj
```

Expected: Build succeeded, 0 error(s).

- [ ] **Step 3: Commit**

```bash
git add AIUsageTracker.UI.Slim/MainWindow.xaml.cs
git commit -m "fix: log unhandled exceptions from fire-and-forget CheckForUpdatesAsync"
```

---

## Task E: Polling — Remove Redundant 1-Second Delay and Second Fetch

**Files:**
- Modify: `AIUsageTracker.UI.Slim/MainWindow.Polling.cs` — timer tick lambda

- [ ] **Step 1: Remove the delay + second fetch block**

In `MainWindow.Polling.cs`, inside the `_pollingTimer.Tick` async lambda, find this block (around lines 136–175):

```csharp
                    await Task.Delay(1000).ConfigureAwait(true);
                    var refreshedUsages = await this.GetUsageForDisplayAsync().ConfigureAwait(true);
                    if (refreshedUsages.Any())
                    {
                        this.ApplyFetchedUsages(refreshedUsages, DateTime.Now, " (refreshed)");
                    }

                    bool hasCurrentUsages;
                    lock (this._dataLock)
                    {
                        hasCurrentUsages = this._usages.Any();
                    }

                    var now = DateTime.Now;
                    string? noDataMessage = null;
                    StatusType? noDataStatusType = null;
                    var switchToStartupInterval = false;
                    if (!hasCurrentUsages)
                    {
                        noDataMessage = "No data - waiting for Monitor";
                        noDataStatusType = StatusType.Warning;
                        switchToStartupInterval = true;
                    }
                    else if ((now - this._lastMonitorUpdate).TotalMinutes > 5)
                    {
                        noDataMessage = MainWindowRuntimeLogic.FormatMonitorOfflineStatus(this._lastMonitorUpdate, now);
                        noDataStatusType = StatusType.Warning;
                    }

                    if (noDataMessage != null && noDataStatusType.HasValue)
                    {
                        this.ShowStatus(noDataMessage, noDataStatusType.Value);
                    }

                    if (switchToStartupInterval &&
                        this._pollingTimer != null &&
                        this._pollingTimer.Interval != StartupPollingInterval)
                    {
                        this._pollingTimer.Interval = StartupPollingInterval;
                    }
```

Replace the entire `else` branch (the one entered when `usages.Any()` is false, after the refresh-trigger decision) with this slimmer version:

```csharp
                    bool hasCurrentUsages;
                    lock (this._dataLock)
                    {
                        hasCurrentUsages = this._usages.Any();
                    }

                    var now = DateTime.Now;
                    string? noDataMessage = null;
                    StatusType? noDataStatusType = null;
                    var switchToStartupInterval = false;
                    if (!hasCurrentUsages)
                    {
                        noDataMessage = "No data - waiting for Monitor";
                        noDataStatusType = StatusType.Warning;
                        switchToStartupInterval = true;
                    }
                    else if ((now - this._lastMonitorUpdate).TotalMinutes > 5)
                    {
                        noDataMessage = MainWindowRuntimeLogic.FormatMonitorOfflineStatus(this._lastMonitorUpdate, now);
                        noDataStatusType = StatusType.Warning;
                    }

                    if (noDataMessage != null && noDataStatusType.HasValue)
                    {
                        this.ShowStatus(noDataMessage, noDataStatusType.Value);
                    }

                    if (switchToStartupInterval &&
                        this._pollingTimer != null &&
                        this._pollingTimer.Interval != StartupPollingInterval)
                    {
                        this._pollingTimer.Interval = StartupPollingInterval;
                    }
```

The only lines removed are `await Task.Delay(1000).ConfigureAwait(true);` and the subsequent `GetUsageForDisplayAsync` call with its `if/ApplyFetchedUsages` block. Everything else stays identical.

- [ ] **Step 2: Build UI.Slim**

```bash
dotnet build AIUsageTracker.UI.Slim/AIUsageTracker.UI.Slim.csproj
```

Expected: Build succeeded, 0 error(s).

- [ ] **Step 3: Run tests**

```bash
dotnet test AIUsageTracker.Tests/AIUsageTracker.Tests.csproj -T 4
```

Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add AIUsageTracker.UI.Slim/MainWindow.Polling.cs
git commit -m "perf: remove redundant 1s delay and second fetch in polling tick"
```

---

## Final Step: Open PR

- [ ] **Create feature branch and push**

```bash
git checkout -b fix/performance-security-improvements
git push -u origin fix/performance-security-improvements
```

- [ ] **Open PR targeting `develop`**

```bash
gh pr create \
  --base develop \
  --title "fix: performance and security improvements" \
  --body "$(cat <<'EOF'
## Summary
- CLI: API key can now be entered via stdin when omitted from `set-key` args
- CORS: Restricted to explicit methods (GET, POST) and headers
- ConfigService: Validates provider ID against catalog on save
- UI: Unhandled exceptions from fire-and-forget `CheckForUpdatesAsync` are now logged
- UI: Removed redundant 1-second delay and second fetch in polling tick

## Test plan
- [ ] All existing tests pass
- [ ] `ConfigServiceSaveValidationTests` (3 new tests) pass
- [ ] `act set-key anthropic` (no key arg) prompts for input
- [ ] `act set-key anthropic sk-xxx` (with key arg) works as before

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```
