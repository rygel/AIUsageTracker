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

### Special Case: GitHub Copilot

GitHub Copilot uses the GitHub API `/rate_limit` endpoint to display usage. Although this endpoint technically shows hourly API limits (not monthly Copilot usage), it is treated as a **quota-based provider** to provide consistent UX:

**Implementation Details:**
- Uses `PaymentType.Quota` and `IsQuotaBased = true`
- Calculates **remaining percentage**: `(remaining / limit) * 100`
- Shows full green bar when lots of quota remains
- Shows text: "API Rate Limit: {used}/{limit} Used"

**Example:**
- Rate limit: 5000 requests/hour
- Used: 50 requests
- Remaining: 4950 (99%)
- Bar displays: **~99% full, GREEN** (healthy)

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

**Last Updated:** 2026-02-10
**Version:** 1.0
**Status:** APPROVED - DO NOT MODIFY WITHOUT DEVELOPER APPROVAL
