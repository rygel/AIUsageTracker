# Performance & Security Fixes — Design Spec

**Date:** 2026-04-09  
**Scope:** Five targeted fixes across CLI, Monitor, and UI.Slim. No architectural refactors; no DPAPI key encryption.

---

## A — CLI: API Key via Stdin Instead of Command-Line Argument

**File:** `AIUsageTracker.CLI/Program.cs` — `SetKeyAsync` / `RunAsync`

Command-line arguments are visible in process listings, Windows event logs (process creation audit), and monitoring software. The current `act set-key <provider-id> <api-key>` pattern exposes the key.

**Change:** When `set-key` is called with only the provider-id and no key argument, prompt for the key via `Console.ReadLine()`. When called with three args (the current form), still accept it for scripting/piping. This preserves backward-compatibility for automated use while making interactive use safer.

```
act set-key anthropic         → prompts "Enter API key: "
act set-key anthropic sk-...  → accepted as before (piped/scripted)
```

No new dependencies. Change is confined to the `RunAsync` switch case and `SetKeyAsync`.

---

## B — CORS: Restrict Methods and Headers

**File:** `AIUsageTracker.Monitor/Program.cs` — `AddCors` block (lines 174–182)

`AllowAnyMethod()` permits DELETE, PUT, PATCH on the Monitor's REST surface. `AllowAnyHeader()` allows arbitrary headers. Though the Monitor is localhost-only, there is no reason to grant more than what the UI actually uses.

**Change:**
- Replace `AllowAnyMethod()` with `.WithMethods("GET", "POST")`
- Replace `AllowAnyHeader()` with `.WithHeaders("Content-Type", "Authorization", "X-Requested-With")`
- Keep `AllowCredentials()` (required for SignalR)
- Keep the two explicit origin strings (no wildcard)

---

## C — Config Save: ProviderId Validation

**File:** `AIUsageTracker.Monitor/Services/ConfigService.cs` — `SaveConfigAsync`

`SaveConfigAsync` accepts any `ProviderConfig` without checking whether the `ProviderId` is known to `ProviderMetadataCatalog`. A malformed or injected ID could create orphan config entries.

**Change:** At the top of `SaveConfigAsync`, after the null check, validate:
1. `config.ProviderId` is non-null and non-whitespace (already enforced by model, but explicit)
2. `ProviderMetadataCatalog.TryGet(config.ProviderId, out _)` returns true

Throw `ArgumentException` with a descriptive message if validation fails. `TryGet` already handles family-based lookups (e.g., sub-provider IDs). This is a server-side guard; the UI always sends valid IDs, so no UI changes are needed.

---

## D — Fire-and-Forget: Log Exceptions from CheckForUpdatesAsync

**File:** `AIUsageTracker.UI.Slim/MainWindow.xaml.cs` — `Loaded` handler (line 206)

`_ = this.CheckForUpdatesAsync()` discards the returned Task. If the method throws after its first `await`, the exception is silently swallowed and never logged. The method should have its own internal try/catch, but defense-in-depth is warranted.

**Change:** Replace:
```csharp
_ = this.CheckForUpdatesAsync();
```
with:
```csharp
_ = this.CheckForUpdatesAsync().ContinueWith(
    t => this._logger.LogError(t.Exception, "CheckForUpdatesAsync failed unhandled"),
    CancellationToken.None,
    TaskContinuationOptions.OnlyOnFaulted,
    TaskScheduler.Default);
```

This does not change execution semantics; it only ensures any unhandled fault is logged.

---

## E — Polling: Remove Redundant 1-Second Delay

**File:** `AIUsageTracker.UI.Slim/MainWindow.Polling.cs` — timer tick lambda (line ~136)

When a poll returns empty, the code:
1. Optionally triggers a monitor refresh
2. Waits `Task.Delay(1000)`
3. Calls `GetUsageForDisplayAsync()` a second time

The second fetch after a 1-second delay is redundant: the monitor refresh is asynchronous and rarely completes within 1 second. On the next normal tick (2s startup interval or 60s normal interval), the data will be present if the refresh succeeded.

**Change:** Remove the `await Task.Delay(1000)` and the second `GetUsageForDisplayAsync()` call and its surrounding `if/ApplyFetchedUsages` block. Leave the refresh-trigger logic intact. The poll loop already handles the follow-up on subsequent ticks.

---

## Out of Scope

- DPAPI encryption of stored API keys — deferred
- MainWindow SRP refactor — separate initiative
- Static `PrivacyChanged` event — separate initiative
- HttpClient in `MonitorLauncher` — singleton lifetime; not a real leak
- `CachedGroupedUsageProjectionService` locking — pattern is correct; no change needed

---

## Test Impact

- **A**: Update any existing CLI `set-key` tests to either pass 3 args or mock stdin.
- **B**: CORS change is config-only; no unit test needed, but existing integration tests should still pass.
- **C**: Add unit test to `ConfigService` asserting `SaveConfigAsync` throws `ArgumentException` for an unknown provider ID.
- **D**: No test change; `CheckForUpdatesAsync` tests are unaffected.
- **E**: Update polling logic tests that assert the second fetch occurs after the delay.
