# WPF Async/Await Best Practices

This document outlines critical async/await patterns for WPF applications to prevent UI blocking, deadlocks, and crashes.

## Critical Anti-Patterns to Avoid

### 1. ❌ Never Use `.GetAwaiter().GetResult()` or `.Result` on UI Thread

**Why:** These block the calling thread synchronously, causing the UI to freeze.

**BAD:**
```csharp
private void StartWebService()
{
    var response = client.GetAsync("http://localhost:5100").GetAwaiter().GetResult(); // BLOCKS UI!
}
```

**GOOD:**
```csharp
private async Task StartWebServiceAsync()
{
    var response = await client.GetAsync("http://localhost:5100"); // Non-blocking
}
```

**Real Issue Found:**
- `MainWindow.xaml.cs:2534` - Web service health check blocked UI for 1-30 seconds
- `MonitorLauncher.cs:171, 268` - Sync wrappers caused deadlocks when called from UI thread

### 2. ❌ Never Create Sync Wrappers for Async Methods

**Why:** `.GetAwaiter().GetResult()` causes deadlocks in WPF because it blocks the dispatcher thread while waiting for the continuation to complete on the same thread.

**BAD:**
```csharp
public static bool StartAgent()
{
    return StartAgentAsync().GetAwaiter().GetResult(); // DEADLOCK!
}
```

**GOOD:**
```csharp
// Remove sync wrapper - use async version only
public static async Task<bool> StartAgentAsync()
{
    // Implementation
}
```

**Real Issue Found:**
- `MonitorLauncher.cs:169-172` and `266-269` - Removed these dangerous wrappers

### 3. ❌ Never Use `await` in DispatcherTimer.Tick Without Fire-and-Forget for Non-Critical Work

**Why:** Timer ticks on UI thread. Long async operations block the UI.

**BAD:**
```csharp
_pollingTimer.Tick += async (s, e) =>
{
    await UpdateTrayIconsAsync(); // Blocks if this takes time
};
```

**GOOD:**
```csharp
_pollingTimer.Tick += async (s, e) =>
{
    // Critical UI updates first
    RenderProviders();
    _lastMonitorUpdate = DateTime.Now;
    
    // Non-critical work as fire-and-forget
    _ = UpdateTrayIconsAsync();
};
```

**Real Issue Fixed:**
- `MainWindow.xaml.cs` polling timer - Tray icon updates now fire-and-forget

### 4. ❌ Never Use `.Result` After Task.WhenAll (Still Risky)

**Why:** Even after `WhenAll`, `.Result` can deadlock if called on UI thread.

**BAD:**
```csharp
var configsTask = _monitorService.GetConfigsAsync();
var usagesTask = _monitorService.GetUsageAsync();
await Task.WhenAll(configsTask, usagesTask);
_configs = configsTask.Result; // Risky on UI thread
```

**GOOD:**
```csharp
var configsTask = _monitorService.GetConfigsAsync();
var usagesTask = _monitorService.GetUsageAsync();
var (configs, usages) = await Task.WhenAll(configsTask, usagesTask);
// Or better yet:
_configs = await _monitorService.GetConfigsAsync();
_usages = await _monitorService.GetUsageAsync();
```

### 5. ✅ Async Void Event Handlers: Handle Exceptions

**Why:** Async void exceptions crash the app silently.

**BAD:**
```csharp
private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
{
    await _monitorService.TriggerRefreshAsync(); // Exception = crash
}
```

**GOOD:**
```csharp
private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
{
    try
    {
        await _monitorService.TriggerRefreshAsync();
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"Refresh failed: {ex.Message}");
        ShowStatus("Refresh failed", StatusType.Error);
    }
}
```

### 6. ✅ Always Add Timeouts to HTTP Calls

**Why:** Default HttpClient timeout is 100 seconds - way too long for UI.

**GOOD:**
```csharp
// In constructor
_httpClient.Timeout = TimeSpan.FromSeconds(12); // Reasonable default

// Or per-request with CancellationToken
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
var response = await _httpClient.GetAsync(url, cts.Token);
```

**Real Fix:**
- `MonitorService.cs` - Added 8s timeout for usage, 3s for config

### 7. ✅ Use ConfigureAwait(false) in Library Code

**Why:** Prevents capturing dispatcher context in non-UI code.

**GOOD:**
```csharp
public async Task<List<ProviderUsage>> GetUsageAsync()
{
    var response = await _httpClient.GetAsync(url).ConfigureAwait(false);
    // ...
}
```

**Note:** WPF UI code should NOT use `ConfigureAwait(false)` - it needs the dispatcher context.

### 8. ✅ Add Guards Against Concurrent Execution

**Why:** Rapid clicks or timer ticks can start multiple overlapping operations.

**GOOD:**
```csharp
private bool _isTrayIconUpdateInProgress;

private async Task UpdateTrayIconsAsync()
{
    if (_isTrayIconUpdateInProgress)
        return;
    
    _isTrayIconUpdateInProgress = true;
    try
    {
        // Do work
    }
    finally
    {
        _isTrayIconUpdateInProgress = false;
    }
}
```

**Real Fix:**
- `MainWindow.xaml.cs:UpdateTrayIconsAsync()` - Added `_isTrayIconUpdateInProgress` guard

## Quick Reference: Async Patterns by Context

### In WPF Event Handlers (UI Thread)
```csharp
// ❌ BAD - Blocks UI
void Button_Click() { _service.GetDataAsync().GetAwaiter().GetResult(); }

// ✅ GOOD - Non-blocking
async void Button_Click() 
{ 
    try { await _service.GetDataAsync(); }
    catch (Exception ex) { /* handle */ }
}
```

### In DispatcherTimer
```csharp
// ❌ BAD - Blocks if slow
timer.Tick += async (s, e) => { await SlowOperationAsync(); };

// ✅ GOOD - Fire-and-forget for non-critical work
timer.Tick += async (s, e) => 
{ 
    CriticalUIUpdate(); // Synchronous
    _ = SlowOperationAsync(); // Fire-and-forget
};
```

### In Library Code (Non-UI)
```csharp
// ✅ Always use ConfigureAwait(false)
public async Task<T> LibraryMethodAsync()
{
    var result = await _httpClient.GetAsync(url).ConfigureAwait(false);
    return await result.Content.ReadAsAsync<T>().ConfigureAwait(false);
}
```

## Testing Async Code

1. **Test for blocking:** Use `Task.WhenAny` with timeout
   ```csharp
   var task = SlowOperationAsync();
   var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
   Assert.Same(task, completed); // Fails if operation blocked too long
   ```

2. **Test for deadlocks:** Run from STA thread context
   ```csharp
   [StaFact]
   public void ShouldNotDeadlock()
   {
       // Test code that might deadlock
   }
   ```

## Summary Checklist

- [ ] No `.GetAwaiter().GetResult()` in UI code
- [ ] No `.Result` on UI thread
- [ ] No sync wrappers for async methods
- [ ] Async void handlers have try-catch
- [ ] HTTP calls have reasonable timeouts (< 30s)
- [ ] Concurrent operation guards in place
- [ ] Library code uses `ConfigureAwait(false)`
- [ ] Non-critical async work uses fire-and-forget (`_ =`)

## Related Documents

- [AGENTS.md](AGENTS.md) - Development guidelines
- [Architecture](architecture.md) - Project structure
