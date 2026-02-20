# Progress Bar Design Document

**WARNING: CRITICAL DESIGN RULES - DO NOT MODIFY WITHOUT DEVELOPER APPROVAL**

**IMPORTANT NOTICE FOR AI ASSISTANTS:** The rules and logic documented in this file represent established design decisions that have been carefully validated with the developer. **These rules should NEVER be changed, modified, or "fixed" by an AI assistant without explicit approval from the developer.** If you believe there's an issue or have suggestions for improvement, you must ask the developer first before making any changes.

---

## Overview

This document describes the design and implementation of progress bars in the AI Consumption Tracker application.

## Payment Type-Based Progress Bar Behavior

The application supports two distinct progress bar visualization modes based on the provider's payment type:

### 1. Quota-Based Providers (e.g., Synthetic, Z.AI, Antigravity)

**Concept:** Visualize remaining quota like a fuel gauge

**Backend Calculation:**
```csharp
var utilization = (total - used) / total * 100.0;
```

**Visual Behavior:**

| Used | Total | Bar Fill | Meaning |
|------|-------|----------|---------|
| 0 | 135 | **100% full** | All credits available |
| 67 | 135 | **50% filled** | Half credits remaining |
| 135 | 135 | **0% empty** | No credits remaining |

**Color Thresholds (User-Configurable):**

Default user settings: Red=80, Yellow=60 (these are USED % thresholds, inverted for quota)

| Remaining % | Inverted Threshold Check | Color | Meaning |
|-------------|-------------------------|-------|---------|
| > 40% | remaining > (100 - 60) | **Green** | Healthy - lots of quota remaining |
| 20-40% | (100 - 80) < remaining <= (100 - 60) | **Yellow** | Warning - moderate quota remaining |
| < 20% | remaining < (100 - 80) | **Red** | Critical - quota nearly depleted |

**Example:**
- User has used 14 out of 135 tokens (10.4% used)
- Remaining percentage: 89.6%
- Bar displays: **~90% full, GREEN** (healthy, above 40% threshold)

**Rationale:** Users expect to see a full green bar when they have all their quota available. The bar depletes and turns red as they consume credits, providing intuitive feedback similar to a fuel gauge.

---

### 2. Credits-Based Providers (e.g., OpenCode)

**Concept:** Visualize spending accumulation as a warning indicator

**Backend Calculation:**
```csharp
var utilization = used / total * 100.0;
```

**Visual Behavior:**

| Used | Total | Bar Fill | Meaning |
|------|-------|----------|---------|
| 0 | 100 | **0% empty** | No spending yet |
| 50 | 100 | **50% filled** | Moderate spending |
| 100 | 100 | **100% full** | Budget exhausted |

**Color Thresholds (User-Configurable):**

Default user settings: Red=80, Yellow=60

| Used % | Threshold Check | Color | Meaning |
|--------|----------------|-------|---------|
| < 60% | used <= 60 | **Green** | Safe - low spending |
| 60-80% | 60 < used <= 80 | **Yellow** | Caution - moderate spending |
| > 80% | used > 80 | **Red** | Warning - high spending |

**Example:**
- User has spent $30 out of $100 budget (30% used)
- Bar displays: **30% full, GREEN** (safe spending level)

**Rationale:** For pay-as-you-go providers, users want to see spending accumulate. The bar fills up and turns red as they approach their budget limit, acting as a spending warning indicator.

---

## Bar Calculation Formulas

### Step-by-Step Calculation

#### For Quota-Based Providers

**Given:**
- `used` = amount consumed (e.g., 14 tokens)
- `total` = total quota/limit (e.g., 135 tokens)

**Calculation:**
```
remaining = total - used
remaining_percentage = (remaining / total) × 100
bar_width = remaining_percentage (capped at 100)
```

**Example with 14 used / 135 total:**
```
remaining = 135 - 14 = 121
remaining_percentage = (121 / 135) × 100 = 89.629...%
bar_width = 89.6% (approximately 90% full)
```

**Edge Cases:**
- If `total = 0`: Bar shows 100% (assume unlimited/no limit)
- If `used > total`: Bar shows 0% (over limit)
- If `used = 0`: Bar shows 100% (full quota available)

#### For Credits-Based Providers

**Given:**
- `used` = amount spent (e.g., $30)
- `total` = total budget/credits (e.g., $100)

**Calculation:**
```
used_percentage = (used / total) × 100
bar_width = used_percentage (capped at 100)
```

**Example with $30 spent / $100 budget:**
```
used_percentage = (30 / 100) × 100 = 30%
bar_width = 30% (30% full)
```

**Edge Cases:**
- If `total = 0`: Bar shows 0% (no budget set)
- If `used > total`: Bar shows 100% (over budget)
- If `used = 0`: Bar shows 0% (no spending yet)

---

## Color Determination

**AI NOTICE: This is the centralized color logic. DO NOT modify without developer approval.**

Color logic is centralized in `MainWindow.GetProgressBarColor(percentage, isQuota)`:

### Quota-Based Color Logic (Inverted Thresholds)

**RULE: For quota providers, the bar shows REMAINING percentage.**
- **Full bar = 100% remaining = GREEN**
- **Empty bar = 0% remaining = RED**
- **Color thresholds are INVERTED:** `(100 - userThreshold)`

```csharp
// For quota: percentage represents REMAINING
// Full bar (100% remaining) = GREEN
// Empty bar (0% remaining) = RED
IF remaining_percentage < (100 - ColorThresholdRed):     // e.g., < 20% when Red=80
    color = RED
ELSE IF remaining_percentage < (100 - ColorThresholdYellow):  // e.g., < 40% when Yellow=60
    color = YELLOW
ELSE:
    color = GREEN
```

**Examples with default settings (Red=80, Yellow=60):**
- 89.6% remaining → **GREEN** (above 40%)
- 30% remaining → **YELLOW** (between 20-40%)
- 15% remaining → **RED** (below 20%)

### Credits-Based Color Logic (Standard Thresholds)

**RULE: For usage-based providers, the bar shows USED percentage.**
- **Empty bar = 0% used = GREEN**
- **Full bar = 100% used = RED**
- **Color thresholds are used directly**

```csharp
// For usage-based: percentage represents USED
// Empty bar (0% used) = GREEN
// Full bar (100% used) = RED
IF used_percentage > ColorThresholdRed:     // e.g., > 80%
    color = RED
ELSE IF used_percentage > ColorThresholdYellow:  // e.g., > 60%
    color = YELLOW
ELSE:
    color = GREEN
```

**Examples with default settings (Red=80, Yellow=60):**
- 30% used → **GREEN** (below 60%)
- 70% used → **YELLOW** (between 60-80%)
- 90% used → **RED** (above 80%)

### Inverted Flag Behavior

**RULE: The `InvertProgressBar` setting affects ONLY bar direction, NEVER color logic.**

The inverted flag reverses the visual direction of the bar (fills from right to left or bottom to top), but the color determination remains based on the underlying percentage value:

**With Inverted Flag + Quota Provider:**
- Bar fills from right to left (or bottom to top for tray icons)
- Full bar = 100% remaining = **GREEN**
- Empty bar = 0% remaining = **RED**
- Color logic remains based on remaining percentage (not affected by inverted flag)

**Visual Examples (Inverted + Quota with 29/135 used):**
```
78.5% remaining (non-inverted):
[████████░░░░]  →  (bar 78.5% full from left, GREEN)

78.5% remaining (inverted):
[░░░░████████]  →  (bar 78.5% full from right, GREEN)

30% remaining (inverted):
[░░░░░░░░████]  →  (bar 30% full from right, YELLOW)

10% remaining (inverted):
[░░░░░░░░░░██]  →  (bar 10% full from right, RED)
```

**Key Point:** The color is determined by the remaining percentage value (78.5%, 30%, 10%), not by the visual direction of the bar.

---

## Implementation Details

### Backend (Provider Classes)

**AI NOTICE: Backend providers must return REMAINING percentage for quota-based providers.**

**GenericPayAsYouGoProvider.cs:**
```csharp
// For quota-based providers, show remaining percentage (full bar = lots remaining)
// For other providers, show used percentage (full bar = high usage)
var utilization = paymentType == PaymentType.Quota
    ? (total > 0 ? ((total - used) / total) * 100.0 : 100)  // Remaining % for quota
    : (total > 0 ? (used / total) * 100.0 : 0);              // Used % for others
```

**ZaiProvider.cs:**
```csharp
// Calculate remaining percentage for quota-based display
var remainingPercent = total > 0 
    ? ((total - used) / total) * 100.0 
    : 100;

return new ProviderUsage
{
    UsagePercentage = Math.Min(remainingPercent, 100),
    PaymentType = PaymentType.Quota,
    IsQuotaBased = true,
    // ...
};
```

**AntigravityProvider.cs:**
```csharp
// Return REMAINING percentage in UsagePercentage for quota-based providers
// Do NOT invert to used percentage - let the UI handle it
var remainingPct = CalculateRemainingPercentage();

return new ProviderUsage
{
    UsagePercentage = remainingPercent,  // REMAINING %
    PaymentType = PaymentType.Quota,
    IsQuotaBased = true,
    // ...
};
```

**GitHubCopilotProvider.cs:**
```csharp
// Uses GitHub API /rate_limit endpoint
// Shows remaining percentage like quota providers
int limit = core.GetProperty("limit").GetInt32();
int remaining = core.GetProperty("remaining").GetInt32();
int used = limit - remaining;

// Calculate REMAINING percentage (not used percentage)
double percentage = limit > 0 ? ((double)remaining / limit) * 100 : 100;

return new ProviderUsage
{
    UsagePercentage = percentage,  // REMAINING %
    CostLimit = limit,
    CostUsed = used,
    PaymentType = PaymentType.Quota,
    IsQuotaBased = true,
    // ...
};
```

### Frontend (UI - MainWindow.xaml.cs)

**Centralized Color Logic:**
```csharp
public Brush GetProgressBarColor(double percentage, bool isQuota)
{
    // DO NOT MODIFY THIS LOGIC WITHOUT DEVELOPER APPROVAL
    if (isQuota)
    {
        // Inverted thresholds for quota (remaining %)
        if (percentage < (100 - ColorThresholdRed))
            return Brushes.Crimson;
        else if (percentage < (100 - ColorThresholdYellow))
            return Brushes.Gold;
        else
            return Brushes.MediumSeaGreen;
    }
    else
    {
        // Standard thresholds for usage-based (used %)
        if (percentage > ColorThresholdRed)
            return Brushes.Crimson;
        else if (percentage > ColorThresholdYellow)
            return Brushes.Gold;
        else
            return Brushes.MediumSeaGreen;
    }
}

// Usage:
var isQuota = usage.IsQuotaBased || usage.PaymentType == PaymentType.Quota;
var brush = GetProgressBarColor(usage.UsagePercentage, isQuota);
```

**Progress Bar Width:**
```csharp
var indicatorWidth = Math.Min(usage.UsagePercentage, 100);
if (_preferences.InvertProgressBar) 
    indicatorWidth = Math.Max(0, 100 - indicatorWidth);
```

---

## Provider Classification

**AI NOTICE: When adding new providers, classify them correctly:**

| Provider | Payment Type | Progress Bar Mode | UsagePercentage Value |
|----------|--------------|-------------------|----------------------|
| Synthetic | Quota | Remaining percentage | % remaining |
| Z.AI | Quota | Remaining percentage | % remaining |
| Antigravity | Quota | Remaining percentage | % remaining |
| GitHub Copilot | Quota | Remaining percentage | % remaining (from rate limit) |
| OpenCode | Credits | Used percentage | % used |
| OpenAI | Usage-Based | Status only (no bar) | N/A |
| Anthropic | Usage-Based | Status only (no bar) | N/A |

### Classification Contract (MANDATORY)

Provider classification drives Slim/Desktop grouping and Antigravity sub-provider rendering.

Source-of-truth implementation points:

- `AIConsumptionTracker.Core/Models/ProviderPlanClassifier.cs`
- `AIConsumptionTracker.Infrastructure/Configuration/TokenDiscoveryService.cs` (default config classification)
- `AIConsumptionTracker.Agent/Services/UsageDatabase.cs` (`/api/usage` response normalization)

When classification changes for any provider, all three locations must be updated in the same PR.

### Special Case: GitHub Copilot

GitHub Copilot is treated as a **quota-based provider**. The provider prefers Copilot-specific quota data from `/copilot_internal/user` and falls back to GitHub `/rate_limit` only when that data is unavailable.

**Implementation Details:**
- Uses `PaymentType.Quota` and `IsQuotaBased = true`
- Primary data source mirrors `opencode-bar/scripts/query-copilot.sh`: `GET /copilot_internal/user` with Copilot-specific headers
- Calculates **remaining percentage**: `(remaining / entitlement) * 100` from `quota_snapshots.premium_interactions`
- Uses `quota_reset_date` for `NextResetTime` when available (fallback: next 1st day 00:00 UTC)
- Shows full green bar when lots of quota remains
- Shows text: "Premium Requests: {remaining}/{entitlement} Remaining" (fallback: "API Rate Limit: {remaining}/{limit} Remaining")

**Example:**
- Premium entitlement: 300 requests/month
- Used: 60 requests
- Remaining: 240 (80%)
- Bar displays: **80% full, GREEN** (healthy)

**Rationale:** Even though it's an hourly limit, users expect the same fuel-gauge metaphor as other quota providers. The bar depletes as they approach the limit, giving visual feedback on available capacity.

---

## User Preferences

Users can customize the visual thresholds via settings:

- **ColorThresholdRed**: Percentage at which the bar turns red (default: 80 for used % threshold)
  - For quota: triggers when remaining < 20%
  - For credits: triggers when used > 80%
  
- **ColorThresholdYellow**: Percentage at which the bar turns yellow (default: 60 for used % threshold)
  - For quota: triggers when remaining < 40%
  - For credits: triggers when used > 60%

- **InvertProgressBar**: Whether to invert the bar direction only (does NOT affect color logic)

---

## Design Principles

**AI NOTICE: These principles are fundamental to the user experience. DO NOT violate them.**

1. **Intuitive Mapping**: The progress bar should map to the user's mental model of the resource
   - Quota = "How much do I have left?" (fuel gauge metaphor)
   - Credits = "How much have I spent?" (budget tracker metaphor)

2. **Color Semantics** (Universal across all providers):
   - **Green** always means "good/healthy"
   - **Yellow** always means "caution/attention needed"
   - **Red** always means "critical/action required"

3. **Consistency**: All quota-based providers behave the same way, all credit-based providers behave the same way

4. **Zero State**:
   - Quota providers show **full green bar** (100% remaining = healthy)
   - Credit providers show **empty green bar** (0% used = healthy)

5. **Inverted Flag**: Only affects visual bar direction, NEVER color determination

6. **Use Real API Data**: 
   - **NEVER** hardcode assumptions about provider behavior (e.g., reset times, billing cycles)
   - **ALWAYS** use actual data returned by the provider's API
   - If the API does not provide certain information (e.g., reset time), set `NextResetTime = null` and do not display it
   - Examples of what NOT to do:
     - Do NOT assume all providers reset at UTC midnight
     - Do NOT assume fixed billing cycles (e.g., monthly from signup)
     - Do NOT make up data that the API doesn't provide
   - When in doubt, ask the developer what the real API behavior is

---

## Change Control Policy

**CRITICAL: This section defines rules that must never be violated.**

### Rules for AI Assistants

1. **NEVER modify** the color logic in `GetProgressBarColor()` without explicit developer approval
2. **NEVER change** the threshold calculation (standard vs inverted) without explicit developer approval
3. **NEVER modify** the relationship between inverted flag and color logic without explicit developer approval
4. **NEVER change** whether quota providers show remaining vs used percentage without explicit developer approval
5. **ALWAYS ask** the developer before making any changes to:
   - Color threshold calculations
   - Inverted flag behavior
   - Progress bar direction logic
   - Provider payment type classifications
   - Any logic documented in this file

### Rules for Developers

Before modifying any logic in this document:
1. Consider the impact on user experience
2. Test with both inverted and non-inverted modes
3. Verify all provider types still work correctly
4. Update this documentation to reflect changes
5. Ensure tests pass for all scenarios

---

## Provider API Response Formats

**AI NOTICE: These are the actual API response structures. Use real data from these fields.**

### Z.AI (api.z.ai)

**Endpoint:** `GET https://api.z.ai/api/monitor/usage/quota/limit`

**Response Structure:**
```json
{
  "data": {
    "limits": [
      {
        "type": "TOKENS_LIMIT",
        "percentage": null,
        "currentValue": 0,
        "usage": 135000000,
        "remaining": 135000000,
        "nextResetTime": 1739232000
      }
    ]
  }
}
```

**Key Fields:**
- `type`: "TOKENS_LIMIT" for coding plan, "TIME_LIMIT" for MCP usage
- `usage`: Total quota limit (mapped to `Total` property)
- `currentValue`: Amount used
- `remaining`: Amount remaining
- `nextResetTime`: **Unix timestamp in milliseconds** (NOT seconds, NOT ISO 8601 string)

**Accessing Reset Time:**
```csharp
[JsonPropertyName("nextResetTime")]
public long? NextResetTime { get; set; }  // Unix timestamp in milliseconds

var limitWithReset = limits.FirstOrDefault(l => l.NextResetTime.HasValue && l.NextResetTime.Value > 0);
if (limitWithReset != null)
{
    nextResetTime = DateTimeOffset.FromUnixTimeMilliseconds(limitWithReset.NextResetTime!.Value).LocalDateTime;
}
```

**Display Format:**
The reset time is displayed in two ways:
1. **Description field**: Shows inline text like `"10.5% Used of 135M tokens limit (Resets: Feb 11 00:00)"`
2. **NextResetTime property**: Set for UI components to use (e.g., tray icon tooltips, detailed views)

---

### Provider Payment Type Requirements

**CRITICAL RULE: All provider return paths MUST set PaymentType and IsQuotaBased**

Every provider that implements `IProviderService` must ensure that **ALL** return paths set the `PaymentType` and `IsQuotaBased` properties on `ProviderUsage`. This is required for the UI to correctly categorize providers into "Plans & Quotas" vs "Pay As You Go" sections.

**Required Properties:**
- **Quota-based providers** (e.g., Antigravity, Z.AI, GitHub Copilot): 
  - `PaymentType = PaymentType.Quota`
  - `IsQuotaBased = true`
- **Usage-based providers** (e.g., OpenCode):
  - `PaymentType = PaymentType.UsageBased` (or leave as default)
  - `IsQuotaBased = false` (or leave as default)

**Common Mistake:**
```csharp
// WRONG - Missing PaymentType in error case
return new[] { new ProviderUsage
{
    ProviderId = ProviderId,
    ProviderName = "Antigravity",
    Description = "Application not running"
    // Missing: IsQuotaBased and PaymentType!
}};

// CORRECT - All properties set
return new[] { new ProviderUsage
{
    ProviderId = ProviderId,
    ProviderName = "Antigravity",
    Description = "Application not running",
    IsQuotaBased = true,
    PaymentType = PaymentType.Quota
}};
```

**Implementation Checklist for Providers:**
- [ ] Success path sets correct PaymentType
- [ ] Error/exception catch blocks set correct PaymentType
- [ ] "Not running" / unavailable states set correct PaymentType
- [ ] All return statements in the method set PaymentType
- [ ] Unit test verifies PaymentType in all scenarios

---

### API Key Discovery Requirements

**CRITICAL RULE: API keys discovered from configuration files MUST be properly extracted and passed to providers**

When implementing token discovery from configuration files (e.g., `providers.json`, `auth.json`), the actual API key value must be extracted and passed to the provider, not an empty string or placeholder.

**Common Mistake:**
```csharp
// WRONG - Passing empty string instead of actual API key
foreach (var id in known.Keys)
{
    AddIfNotExists(configs, id, "", "Discovered in providers.json", "Config: providers.json");
}

// CORRECT - Passing the actual API key value
foreach (var id in known.Keys)
{
    AddIfNotExists(configs, id, known[id], "Discovered in providers.json", "Config: providers.json");
}
```

**Discovery Implementation Checklist:**
- [ ] Read the configuration file correctly
- [ ] Parse the JSON/file format properly
- [ ] Extract the actual API key value (not just provider ID)
- [ ] Pass the API key to `AddOrUpdate` or `AddIfNotExists`
- [ ] Test that discovered providers work without manual key entry
- [ ] Verify the key is actually sent in API requests (check logs)

**Example Implementation:**
```csharp
string resetStr = "";
DateTime? nextResetTime = null;
if (limitWithReset != null)
{
    nextResetTime = DateTimeOffset.FromUnixTimeMilliseconds(limitWithReset.NextResetTime!.Value).LocalDateTime;
    resetStr = $" (Resets: {nextResetTime:MMM dd HH:mm})";
}

return new ProviderUsage
{
    // ... other properties ...
    Description = detailInfo + resetStr,  // Shows in main UI
    NextResetTime = nextResetTime         // Used by UI components
};
```

---

## Validation Checklist

When making changes to progress bar logic, verify:

- [ ] Quota providers show **remaining** percentage (full bar = good)
- [ ] Credits providers show **used** percentage (empty bar = good)
- [ ] Color thresholds are **inverted** for quota providers: `(100 - threshold)`
- [ ] Color thresholds are **standard** for credits providers: `threshold`
- [ ] Inverted flag affects **only bar direction**, not color
- [ ] Full bar + Quota = Green (healthy)
- [ ] Empty bar + Quota = Red (critical)
- [ ] Empty bar + Credits = Green (healthy)
- [ ] Full bar + Credits = Red (critical)
- [ ] All tests pass
- [ ] This documentation is updated

---

## Summary of Current Behavior

**Verified Working (Non-Inverted Mode):**
- 29/135 tokens (78.5% remaining) = ~79% full bar, GREEN
- Color uses inverted thresholds: (100 - userThreshold)
- With defaults: Green > 40%, Yellow 20-40%, Red < 20%

**Inverted Mode:**
- Bar fills from right to left (or bottom to top)
- Color logic remains the same (based on remaining %)
- Full bar = Green, Empty bar = Red

---

---

## Locale Independence Requirements

**CRITICAL RULE: All API parsing and number formatting MUST be locale-independent**

All providers MUST use `CultureInfo.InvariantCulture` when formatting numbers to strings. This ensures consistent behavior across all system locales (e.g., US uses "." for decimals, EU uses ",").

### Required Pattern for Number Formatting

```csharp
using System.Globalization;

// CORRECT - Using invariant culture with explicit format
Description = $"{used.ToString("F2", CultureInfo.InvariantCulture)} / {total.ToString("F2", CultureInfo.InvariantCulture)} credits";

// WRONG - Using current culture (may use comma for decimals)
Description = $"{used:F2} / {total:F2} credits";

// WRONG - Implicit ToString() without culture
Description = $"{used} / {total} credits";
```

### Required Pattern for Number Parsing

```csharp
using System.Globalization;

// CORRECT - Using invariant culture when parsing
if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))

// CORRECT - Direct conversion from invariant
var result = Convert.ToDouble(value, CultureInfo.InvariantCulture);

// WRONG - Using current culture
if (double.TryParse(value, out var result))
```

### Affected Output Fields

All providers MUST ensure these fields use invariant culture formatting:

| Field | Example Output | Format Required |
|-------|---------------|-----------------|
| `Description` (credits format) | "500.00 / 50000.00 credits" | `ToString("F2", CultureInfo.InvariantCulture)` |
| `Description` (currency format) | "$30.00 used of $100.00 limit" | `ToString("F2", CultureInfo.InvariantCulture)` |
| Any dollar amounts | "$30.00" | `ToString("F2", CultureInfo.InvariantCulture)` |

### Testing Requirement

All providers that format numbers MUST have unit tests that verify output is independent of system locale. Tests should use `CultureInfo.InvariantCulture` comparison:

```csharp
// Test verifies output uses period (.) for decimals, not comma (,)
var usage = await provider.GetUsageAsync(config);
Assert.Equal("500.00 / 50000.00 credits", usage[0].Description);
```

### Common Mistakes

```csharp
// WRONG - Log messages use current culture formatting
_logger.LogInformation("Cost: ${Cost:F2}", totalCost);
// May output: "Cost: $30,50" on EU systems

// CORRECT - Log messages use invariant culture
_logger.LogInformation("Cost: ${Cost}", totalCost);
// Outputs: "Cost: $30.50" on all systems

// WRONG - Description uses format string without culture
Description = $"${totalCost:F2} (7 days)";

// CORRECT - Description uses invariant culture
Description = $"${totalCost.ToString("F2", CultureInfo.InvariantCulture)} (7 days)";
```

### Provider Implementation Checklist

- [ ] All `Description` fields use `ToString("F2", CultureInfo.InvariantCulture)`
- [ ] All `double` to string conversions use `CultureInfo.InvariantCulture`
- [ ] All number parsing uses `CultureInfo.InvariantCulture`
- [ ] Unit tests verify locale-independent output
- [ ] Tests pass on systems with different decimal separators

---

## Core System Behavior

### Resume from Hibernate/Sleep - MANDATORY

**CRITICAL RULE: The application MUST immediately refresh all provider data when the system resumes from hibernate or sleep mode**

This is a **core functionality requirement** that ensures users always see current data after their system has been suspended. When a computer wakes from sleep or hibernate, network connections may have been interrupted, cached data may be stale, and provider rate limits or quotas may have changed.

**Implementation Requirements:**

1. **Event Subscription**: Subscribe to `SystemEvents.PowerModeChanged` in the MainWindow
2. **Resume Detection**: Check for `PowerModes.Resume` event
3. **Immediate Refresh**: Trigger data refresh immediately upon resume
4. **Thread Safety**: Use Dispatcher to marshal UI updates to the main thread
5. **Resource Cleanup**: Unsubscribe from events when window closes

**Implementation Pattern:**

```csharp
using Microsoft.Win32;

public MainWindow(...)
{
    InitializeComponent();
    
    // Subscribe to power mode changes
    SystemEvents.PowerModeChanged += OnPowerModeChanged;
}

private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
{
    if (e.Mode == PowerModes.Resume)
    {
        // System resumed from sleep/hibernate
        Dispatcher.Invoke(async () =>
        {
            _logger?.LogInformation("System resumed from sleep/hibernate, refreshing data...");
            await RefreshDataAsync();
        });
    }
}

protected override void OnClosed(EventArgs e)
{
    // Critical: Unsubscribe to prevent memory leaks
    SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    base.OnClosed(e);
}
```

**PowerModes Values:**
- `Resume` - System waking from sleep or hibernate (**refresh data**)
- `Suspend` - System entering sleep or hibernate
- `StatusChange` - Power status changed (battery/AC)

**Why This Matters:**
- API rate limits may have reset during sleep
- OAuth tokens may have expired
- Quota periods may have rolled over
- Network state may have changed
- Cached data is potentially stale

**Testing Requirements:**
- [ ] Application refreshes data after waking from sleep
- [ ] Application refreshes data after waking from hibernate
- [ ] No memory leaks from event handlers
- [ ] Thread-safe UI updates work correctly
- [ ] Handles cases where network is not yet available after resume

---

**Last Updated:** 2026-02-12
**Version:** 1.2
**Status:** APPROVED - DO NOT MODIFY WITHOUT DEVELOPER APPROVAL

---

## Agent Refresh Behavior

**CRITICAL RULE: The Agent MUST NOT perform an immediate refresh on startup. It should only refresh on the configured interval.**

### Design Rationale

The Agent uses a **cached data model** where:
1. **On startup**: Agent serves cached data from the database immediately
2. **On interval**: Agent refreshes provider data every 5 minutes (configurable)
3. **On manual refresh**: UI can trigger refresh via `/api/refresh` endpoint

This design ensures:
- **Fast startup**: No waiting for API calls on Agent restart
- **Reduced API load**: Providers are only queried on the configured interval
- **Consistent behavior**: Restarting the Agent doesn't cause unnecessary API hits
- **Offline capability**: Cached data is available even if providers are temporarily unreachable

### Implementation Details

**ProviderRefreshService.cs:**
```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    _logger.LogInformation("Provider Refresh Service starting...");

    InitializeProviders();

    // No immediate refresh on startup - use cached data from database
    // Refresh only happens on the configured interval

    while (!stoppingToken.IsCancellationRequested)
    {
        try
        {
            await Task.Delay(_refreshInterval, stoppingToken);
            await TriggerRefreshAsync();
        }
        catch (OperationCanceledException)
        {
            break;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during scheduled refresh");
        }
    }

    _logger.LogInformation("Provider Refresh Service stopping...");
}
```

**Default Refresh Interval:**
- `_refreshInterval = TimeSpan.FromMinutes(5)`
- This means providers are queried once every 5 minutes
- Users can manually trigger refresh via Slim UI if needed

**API Endpoints:**
- `GET /api/usage` - Returns cached data from database (fast, no API calls)
- `POST /api/refresh` - Triggers manual refresh (queries all providers)

**Slim UI Behavior:**
- On startup: Fetches cached data immediately from `/api/usage`
- Status shows last refresh time (e.g., "14:32:15")
- Refresh button triggers `/api/refresh` and updates display
- On startup (non-blocking): starts a NetSparkle (`NetSparkleUpdater.SparkleUpdater`) update check against architecture-specific appcast feeds
- Periodic update check: re-checks for updates every 15 minutes while the app is running
- When update is available: shows `UpdateNotificationBanner` with the target version
- Download action uses framework-driven installer handoff (`DownloadAndInstallUpdateAsync`), then exits app after successful installer launch
- If no framework update payload is available, update action may fall back to opening the GitHub releases page

### Testing Requirements

- [ ] Agent startup does NOT hit provider APIs
- [ ] Agent serves cached data immediately on startup
- [ ] First refresh happens only after the configured interval
- [ ] Manual refresh via `/api/refresh` works correctly
- [ ] Database retains data across Agent restarts
- [ ] Slim startup and periodic update checks do not block usage loading
- [ ] Update banner appears only when newer version is detected

---

## Agent API Key Filtering

**CRITICAL RULE: The Agent MUST NOT query upstream providers that don't have API keys configured.**

### Design Principle

The Agent implements a **"no key, no query"** policy:
- Providers without API keys are completely skipped
- No upstream API calls are made to providers without keys
- Providers appear in UI but show "Not Configured" status
- This prevents unnecessary errors and rate limiting

### Implementation

**ProviderRefreshService.TriggerRefreshAsync():**

```csharp
// Get all provider configurations
var configs = await _configService.GetConfigsAsync();

// Filter to only providers with API keys
var activeConfigs = configs.Where(c => !string.IsNullOrEmpty(c.ApiKey)).ToList();
var skippedCount = configs.Count - activeConfigs.Count;

if (skippedCount > 0)
{
    _logger.LogInformation("Skipping {Count} providers without API keys", skippedCount);
}

// Only query providers with API keys
if (activeConfigs.Count > 0)
{
    var usages = await _providerManager.GetAllUsageAsync(forceRefresh: true);
    
    // Filter to only include providers that have configs with API keys
    var activeProviderIds = activeConfigs.Select(c => c.ProviderId).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var filteredUsages = usages.Where(u => activeProviderIds.Contains(u.ProviderId)).ToList();
    
    await _database.StoreHistoryAsync(filteredUsages);
}
```

### API Key Discovery

**Manual Scanning:**
- `POST /api/scan-keys` - Scans system for API keys
- Only runs when user explicitly requests it
- Updates configs with discovered keys
- Next refresh will query newly configured providers

**No Automatic Discovery:**
- Agent does NOT scan for keys on startup
- Agent does NOT scan for keys on interval
- Keys must be added manually or via explicit scan

### Behavior Examples

**Scenario 1: Fresh Install**
1. Agent starts with no API keys configured
2. Providers list shows all supported providers
3. All providers show "Not Configured" status
4. Agent does NOT make any upstream API calls
5. User clicks "Scan for Keys" → Keys discovered
6. Next refresh queries only providers with keys

**Scenario 2: Key Removed**
1. Provider had API key, was being queried
2. User removes API key from config
3. Next refresh skips this provider
4. Provider still shows in UI as "Not Configured"
5. No upstream calls made

**Scenario 3: Key Added**
1. Provider had no key, was skipped
2. User adds API key via Settings
3. Next refresh includes this provider
4. Upstream API call made to fetch usage
5. Usage data stored in database

### Benefits

1. **No Unnecessary API Calls** - Prevents hitting rate limits
2. **No Error Spam** - No "API Key missing" errors in logs
3. **Clear Status** - UI clearly shows which providers are configured
4. **User Control** - User decides which providers to monitor
5. **Privacy** - No data sent to providers user doesn't use

---

## Agent API Contract (OpenAPI)

The Agent HTTP API contract is defined in:

- `AIConsumptionTracker.Agent/openapi.yaml`

This OpenAPI document is the contract between the Agent and all consuming applications (Slim UI, Desktop UI, Web UI, and CLI).

### Contract Maintenance Rule (MANDATORY)

Whenever any Agent API change is made, the same PR **must** update `AIConsumptionTracker.Agent/openapi.yaml`, including:

1. Endpoint paths and HTTP methods
2. Request parameters and request bodies
3. Response schemas and status codes
4. Added/removed/renamed fields

Changes to Agent endpoints are considered incomplete unless this contract file is updated.

---

## Agent Status Detection

### Port Configuration

The Agent supports dynamic port allocation to handle conflicts:

**Port Selection Priority:**
1. Try preferred port (5000)
2. If in use, try ports 5001-5010
3. If all in use, use random available port

**Port Persistence:**
- Port saved to `%LOCALAPPDATA%\AIConsumptionTracker\Agent\agent.port`
- JSON info saved to `agent.info`:
  ```json
  {
    "Port": 5000,
    "StartedAt": "2024-01-15T10:30:00Z",
    "ProcessId": 12345
  }
  ```

### Status Checking

**Health Endpoint:**
```
GET /api/health
```

Returns:
```json
{
  "status": "healthy",
  "timestamp": "2024-01-15T10:30:00Z",
  "port": 5000
}
```

**UI Status Detection:**

The Settings dialog checks agent status on load:

```csharp
private async Task UpdateAgentStatusAsync()
{
    // Check if agent is running
    var isRunning = await AgentLauncher.IsAgentRunningAsync();
    
    // Get the actual port from the agent
    int port = await GetAgentPortAsync();
    
    // Update UI
    AgentStatusText.Text = isRunning ? "Running" : "Not Running";
    AgentPortText.Text = port.ToString();
}

private async Task<int> GetAgentPortAsync()
{
    // Try to read port from agent info file
    var portFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIConsumptionTracker", "Agent", "agent.port"
    );
    
    if (File.Exists(portFile))
    {
        var portStr = await File.ReadAllTextAsync(portFile);
        if (int.TryParse(portStr, out int port))
        {
            return port;
        }
    }
    
    // Default to 5000 if file doesn't exist
    return 5000;
}
```

### UI Display

**Settings Dialog - Agent Tab:**
- **Status:** Shows "Running", "Not Running", or "Error"
- **Port:** Shows actual port (e.g., "5000" or "5001")
- **Auto-Start:** Checkbox to auto-start Agent with UI
- **Actions:** "Restart Agent", "Check Health" buttons

**Status Indicators:**
- 🟢 **Running** - Agent responding on expected port
- 🔴 **Not Running** - Agent not reachable
- 🟡 **Error** - Agent process exists but not responding

### Port Conflict Handling

**When Port 5000 is in use:**
1. Agent detects port conflict
2. Logs message: "Port 5000 was in use, using port 5001 instead"
3. Saves new port to `agent.port` file
4. UI reads port from file on next status check
5. UI displays updated port number

**Communication:**
- Slim UI reads `agent.port` file to discover actual port
- Falls back to 5000 if file doesn't exist
- Updates display dynamically
- No hardcoded port in UI

### Troubleshooting

**Agent not detected:**
1. Check if `agent.port` file exists
2. Verify file contains valid port number
3. Check if process is running on that port
4. Try restarting Agent

**Port in use:**
1. Stop other application using port 5000
2. Or let Agent auto-select different port
3. UI will automatically detect new port

**File permissions:**
- `agent.port` and `agent.info` saved to user's LocalApplicationData
- No admin privileges required
- User must have write access to folder

---

## Sensitive Data Handling - MANDATORY

**CRITICAL RULE: API keys and base URLs MUST NEVER be stored in the database.**

### Design Principle

The application follows a strict separation between:
1. **Configuration storage** (`auth.json`) - Contains sensitive credentials
2. **Database storage** (SQLite) - Contains only usage data and metadata

### What Goes Where

| Data | Storage Location | Reason |
|------|------------------|--------|
| API Keys | `auth.json` only | Sensitive credential |
| Base URLs | `auth.json` only | May contain embedded credentials |
| Provider ID/Name | SQLite database | Non-sensitive metadata |
| Usage percentages | SQLite database | Non-sensitive metrics |
| Cost data | SQLite database | Non-sensitive metrics |
| Auth source | SQLite database | Non-sensitive metadata (e.g., "manual", "env") |

### Username Privacy Contract

- `account_name` (e.g., GitHub username, Antigravity email) is part of the Agent API contract and should be returned by `/api/usage` whenever available.
- UI clients must show `account_name` in normal mode and mask it in privacy mode.
- Privacy masking must only redact the account identifier (e.g., email local-part/username), not provider/status titles, and this rule applies consistently in main dashboards and Settings dialogs (including Antigravity and GitHub Copilot rows).
- Privacy mode is a single global UI state: toggling it in MainWindow or SettingsWindow must update the other immediately via `App.SetPrivacyMode` / `App.PrivacyChanged`, and both toggles must use the same lock/unlock icon semantics.
- Agent changes must not drop `account_name` from API responses unless the UI contract is updated in the same PR.

### Database Schema (V2+)

The `providers` table MUST NOT contain:
- `api_key` column
- `base_url` column

Any migration or schema change that adds these columns is **prohibited**.

### config_json Field

The `config_json` field in the `providers` table MUST NOT contain:
- API keys
- Base URLs
- Any other sensitive credentials

When storing provider configuration, create a safe subset:
```csharp
var safeConfig = new
{
    config.ProviderId,
    config.Type,
    config.AuthSource
};
ConfigJson = JsonSerializer.Serialize(safeConfig)
```

### Code Review Checklist

Before merging any database-related code, verify:
- [ ] No API key stored in SQLite
- [ ] No base URL stored in SQLite
- [ ] `config_json` field excludes sensitive data
- [ ] Migrations do not add sensitive columns
- [ ] Logs do not expose API keys

### Rationale

1. **Security**: Database files may be copied, backed up, or accessed by other processes
2. **Compliance**: Reduces attack surface for credential theft
3. **Separation of concerns**: Credentials managed separately from usage data
4. **Recovery**: Database can be shared/deleted without exposing credentials

### Implementation

**CRITICAL: This section defines rules that must never be violated.**

### Rules for AI Assistants

1. **NEVER store** API keys in the database
2. **NEVER store** base URLs in the database
3. **NEVER serialize** full `ProviderConfig` objects to `config_json` - always exclude sensitive fields
4. **ALWAYS ask** the developer before adding any column that could contain sensitive data
5. **ALWAYS** create a safe anonymous object when serializing config for database storage
