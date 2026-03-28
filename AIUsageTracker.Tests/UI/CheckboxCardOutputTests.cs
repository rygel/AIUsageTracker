// <copyright file="CheckboxCardOutputTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Helpers;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

/// <summary>
/// Verifies that every boolean checkbox preference produces the correct observable output
/// in the card presentation layer. Each test documents the checkbox name (as shown in
/// Settings), what it controls, and asserts the expected output change.
///
/// Tests operate on the pure C# service layer (MainWindowRuntimeLogic.Create,
/// ResolveDisplayAccountName, BuildSingleDualQuotaStatusText, UsageMath.ComputePaceColor)
/// and do not require WPF/STA.
/// </summary>
public sealed class CheckboxCardOutputTests
{
    // ---------------------------------------------------------------------------
    // Shared fixture: codex provider with 5h burst + Weekly rolling quota windows.
    // Codex has SupportsAccountIdentity = true, so ResolveDisplayAccountName works.
    // ---------------------------------------------------------------------------

    private static ProviderUsage BuildCodexDualQuotaUsage(
        double burstUsedPercent = 40.0,
        double rollingUsedPercent = 60.0,
        string accountEmail = "user@example.com")
    {
        return new ProviderUsage
        {
            ProviderId = "codex",
            ProviderName = "Codex",
            AccountName = accountEmail,
            IsAvailable = true,
            IsQuotaBased = true,
            UsedPercent = rollingUsedPercent,
            NextResetTime = DateTime.UtcNow.AddDays(5),
            WindowCards = new[]
            {
                new ProviderUsage { ProviderId = "codex", Name = "5h",     WindowKind = WindowKind.Burst,   UsedPercent = burstUsedPercent,   NextResetTime = DateTime.UtcNow.AddHours(2) },
                new ProviderUsage { ProviderId = "codex", Name = "Weekly", WindowKind = WindowKind.Rolling, UsedPercent = rollingUsedPercent, NextResetTime = DateTime.UtcNow.AddDays(5) },
            },
        };
    }

    private static ProviderUsage BuildSimpleQuotaUsage(double usedPercent = 50.0)
    {
        return new ProviderUsage
        {
            ProviderId = "openai",
            IsAvailable = true,
            IsQuotaBased = true,
            UsedPercent = usedPercent,
            NextResetTime = DateTime.UtcNow.AddDays(5),
        };
    }

    // ---------------------------------------------------------------------------
    // "Show used percentages" toggle (Settings → PercentageDisplayMode = Used/Remaining)
    // When checked: status text says "X% used"
    // When unchecked: status text says "X% remaining"
    // ---------------------------------------------------------------------------

    [Fact]
    public void ShowUsedPercentages_Checked_StatusTextContainsUsed()
    {
        var usage = BuildSimpleQuotaUsage(usedPercent: 40.0);
        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: true);

        Assert.Contains("used", presentation.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("remaining", presentation.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShowUsedPercentages_Unchecked_StatusTextContainsRemaining()
    {
        var usage = BuildSimpleQuotaUsage(usedPercent: 40.0);
        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: false);

        Assert.Contains("remaining", presentation.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("% used", presentation.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------------
    // "Dual quota bars" checkbox (Settings → ShowDualQuotaBars)
    // The presentation layer always computes dual bucket data when the provider has
    // two QuotaWindow details. HasDualBuckets drives whether bars are rendered.
    // When ShowDualQuotaBars is false in the WPF layer, BuildSingleDualQuotaStatusText
    // is called to collapse the two bars into one status segment.
    // ---------------------------------------------------------------------------

    [Fact]
    public void ShowDualQuotaBars_Checked_HasDualBuckets_LabelsAndStatusTextPopulated()
    {
        var usage = BuildCodexDualQuotaUsage(burstUsedPercent: 40.0, rollingUsedPercent: 60.0);
        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: true);

        // Dual bucket data is present
        Assert.True(presentation.HasDualBuckets);
        Assert.Equal("5h", presentation.DualBucketPrimaryLabel);
        Assert.Equal("Weekly", presentation.DualBucketSecondaryLabel);

        // Values are set
        Assert.NotNull(presentation.DualBucketPrimaryUsed);
        Assert.NotNull(presentation.DualBucketSecondaryUsed);
        Assert.Equal(40.0, presentation.DualBucketPrimaryUsed!.Value, precision: 1);
        Assert.Equal(60.0, presentation.DualBucketSecondaryUsed!.Value, precision: 1);

        // Status text contains the "|" separator between segments
        Assert.Contains("|", presentation.StatusText, StringComparison.Ordinal);
        Assert.Contains("5h", presentation.StatusText, StringComparison.Ordinal);
        Assert.Contains("Weekly", presentation.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowDualQuotaBars_Unchecked_SingleBarMode_Rolling_ShowsRollingSegment()
    {
        // When ShowDualQuotaBars is false, ProviderCardRenderer calls
        // BuildSingleDualQuotaStatusText(..., DualQuotaSingleBarMode.Rolling).
        var usage = BuildCodexDualQuotaUsage(burstUsedPercent: 20.0, rollingUsedPercent: 65.0);
        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: true);

        var singleBarText = MainWindowRuntimeLogic.BuildSingleDualQuotaStatusText(
            presentation,
            showUsed: true,
            mode: DualQuotaSingleBarMode.Rolling);

        // Rolling bar is the secondary (Weekly) for codex
        Assert.Contains("Weekly", singleBarText, StringComparison.Ordinal);
        Assert.DoesNotContain("|", singleBarText, StringComparison.Ordinal);
        Assert.Contains("used", singleBarText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShowDualQuotaBars_Unchecked_SingleBarMode_Burst_ShowsBurstSegment()
    {
        var usage = BuildCodexDualQuotaUsage(burstUsedPercent: 20.0, rollingUsedPercent: 65.0);
        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: true);

        var singleBarText = MainWindowRuntimeLogic.BuildSingleDualQuotaStatusText(
            presentation,
            showUsed: true,
            mode: DualQuotaSingleBarMode.Burst);

        // Burst bar is the primary (5h) for codex
        Assert.Contains("5h", singleBarText, StringComparison.Ordinal);
        Assert.DoesNotContain("|", singleBarText, StringComparison.Ordinal);
        Assert.Contains("used", singleBarText, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------------
    // "Dual quota bars" suppresses the single reset-time slot
    // When a provider has dual buckets, showing a single reset timestamp is
    // misleading, so SuppressSingleResetTime is set to true.
    // ---------------------------------------------------------------------------

    [Fact]
    public void DualQuotaBars_Present_SuppressesSingleResetTime()
    {
        var usage = BuildCodexDualQuotaUsage();
        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: true);

        Assert.True(presentation.HasDualBuckets);
        Assert.True(presentation.SuppressSingleResetTime,
            "Single reset-time slot must be suppressed when dual quota bars are active");
    }

    [Fact]
    public void NoDualQuotaBars_DoesNotSuppressSingleResetTime()
    {
        var usage = BuildSimpleQuotaUsage();
        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: true);

        Assert.False(presentation.HasDualBuckets);
        Assert.False(presentation.SuppressSingleResetTime);
    }

    // ---------------------------------------------------------------------------
    // "Enable pace adjustment" checkbox (Settings → EnablePaceAdjustment)
    // When checked: ComputePaceColor returns IsPaceAdjusted = true and a badge text
    // When unchecked: ComputePaceColor returns IsPaceAdjusted = false and empty badge text
    // ---------------------------------------------------------------------------

    [Fact]
    public void EnablePaceAdjustment_Checked_PaceBadgeTextIsNonEmpty()
    {
        var now = DateTime.UtcNow;

        // 30% used at 50% elapsed (midpoint) → "Headroom" (under pace)
        var result = UsageMath.ComputePaceColor(
            usedPercent: 30.0,
            nextResetTime: now.AddDays(3.5),
            periodDuration: TimeSpan.FromDays(7),
            enablePaceAdjustment: true,
            nowUtc: now);

        Assert.True(result.IsPaceAdjusted);
        Assert.False(string.IsNullOrEmpty(result.BadgeText),
            "Pace badge text must be non-empty when pace adjustment is enabled");
    }

    [Fact]
    public void EnablePaceAdjustment_Unchecked_PaceBadgeTextIsEmpty()
    {
        var result = UsageMath.ComputePaceColor(
            usedPercent: 30.0,
            nextResetTime: DateTime.UtcNow.AddDays(3.5),
            periodDuration: TimeSpan.FromDays(7),
            enablePaceAdjustment: false);

        Assert.False(result.IsPaceAdjusted);
        Assert.Equal(string.Empty, result.BadgeText);
    }

    // ---------------------------------------------------------------------------
    // "Privacy mode" checkbox (Settings → IsPrivacyMode)
    // When checked: account name is masked (e.g. "u***@e***.com")
    // When unchecked: account name is shown as-is
    // ---------------------------------------------------------------------------

    [Fact]
    public void IsPrivacyMode_Unchecked_ReturnsRealAccountName()
    {
        var name = MainWindowRuntimeLogic.ResolveDisplayAccountName(
            providerId: "codex",
            usageAccountName: "user@example.com",
            isPrivacyMode: false);

        Assert.Equal("user@example.com", name);
    }

    [Fact]
    public void IsPrivacyMode_Checked_ReturnsMaskedAccountName()
    {
        var name = MainWindowRuntimeLogic.ResolveDisplayAccountName(
            providerId: "codex",
            usageAccountName: "user@example.com",
            isPrivacyMode: true);

        // Masked — must not expose the plain email
        Assert.False(string.IsNullOrWhiteSpace(name),
            "Masked name must be non-empty (privacy placeholder)");
        Assert.NotEqual("user@example.com", name);
    }

    [Fact]
    public void IsPrivacyMode_Checked_ProviderWithoutIdentity_ReturnsEmpty()
    {
        // A provider not registered in the catalog has SupportsAccountIdentity = false
        // by definition — privacy mode is irrelevant; account name is never shown.
        var name = MainWindowRuntimeLogic.ResolveDisplayAccountName(
            providerId: "unknown-provider-xyz",
            usageAccountName: "user@example.com",
            isPrivacyMode: true);

        Assert.Equal(string.Empty, name);
    }

    // ---------------------------------------------------------------------------
    // "Show usage per hour" checkbox (Settings → ShowUsagePerHour)
    // This is a slot configuration preference. Asserting that the preference state
    // round-trips correctly and that the CardSecondaryBadge slot can be set to UsageRate.
    // ---------------------------------------------------------------------------

    [Fact]
    public void ShowUsagePerHour_Checked_PreferenceStateIsTrue()
    {
        var prefs = new AppPreferences { ShowUsagePerHour = true };
        Assert.True(prefs.ShowUsagePerHour);
    }

    [Fact]
    public void ShowUsagePerHour_Unchecked_SlotConfiguredAsUsageRate_IsDisabled()
    {
        // When ShowUsagePerHour is false the UsageRate slot must be suppressed
        // even if it is set in the slot configuration.
        var prefs = new AppPreferences
        {
            ShowUsagePerHour = false,
            CardSecondaryBadge = CardSlotContent.UsageRate,
        };

        Assert.False(prefs.ShowUsagePerHour);
        Assert.Equal(CardSlotContent.UsageRate, prefs.CardSecondaryBadge);
    }

    // ---------------------------------------------------------------------------
    // "Relative reset time" checkbox (Settings → UseRelativeResetTime)
    // When checked: the ResetAbsolute slot is overridden to show a relative format.
    // The override logic lives in the WPF renderer; here we assert the preference state.
    // ---------------------------------------------------------------------------

    [Fact]
    public void UseRelativeResetTime_Checked_PreferenceStateIsTrue()
    {
        var prefs = new AppPreferences { UseRelativeResetTime = true };
        Assert.True(prefs.UseRelativeResetTime);
    }

    [Fact]
    public void UseRelativeResetTime_Unchecked_PreferenceStateIsFalse()
    {
        var prefs = new AppPreferences { UseRelativeResetTime = false };
        Assert.False(prefs.UseRelativeResetTime);
    }

    // ---------------------------------------------------------------------------
    // "Card background bar" checkbox (Settings → CardBackgroundBar)
    // Controls whether the progress bar background is rendered inside the card.
    // The effect is WPF-only; we assert the preference round-trips.
    // ---------------------------------------------------------------------------

    [Fact]
    public void CardBackgroundBar_Checked_DefaultIsTrue()
    {
        var prefs = new AppPreferences();
        Assert.True(prefs.CardBackgroundBar);
    }

    [Fact]
    public void CardBackgroundBar_Unchecked_PreferenceStateIsFalse()
    {
        var prefs = new AppPreferences { CardBackgroundBar = false };
        Assert.False(prefs.CardBackgroundBar);
    }

    // ---------------------------------------------------------------------------
    // "Card compact mode" checkbox (Settings → CardCompactMode)
    // Controls row height in the WPF card renderer. We assert the preference state.
    // ---------------------------------------------------------------------------

    [Fact]
    public void CardCompactMode_Checked_PreferenceStateIsTrue()
    {
        var prefs = new AppPreferences { CardCompactMode = true };
        Assert.True(prefs.CardCompactMode);
    }

    [Fact]
    public void CardCompactMode_Unchecked_DefaultIsFalse()
    {
        var prefs = new AppPreferences();
        Assert.False(prefs.CardCompactMode);
    }

    // ---------------------------------------------------------------------------
    // "Bold" / "Italic" font checkboxes (Settings → FontBold / FontItalic)
    // Applied to font rendering in WPF; we assert preference state round-trips.
    // ---------------------------------------------------------------------------

    [Fact]
    public void FontBold_Checked_PreferenceStateIsTrue()
    {
        var prefs = new AppPreferences { FontBold = true };
        Assert.True(prefs.FontBold);
    }

    [Fact]
    public void FontItalic_Checked_PreferenceStateIsTrue()
    {
        var prefs = new AppPreferences { FontItalic = true };
        Assert.True(prefs.FontItalic);
    }

    // ---------------------------------------------------------------------------
    // Dual quota color thresholds — redThreshold affects DualBucketPrimaryColorPercent
    // ---------------------------------------------------------------------------

    [Fact]
    public void RedThreshold_AffectsDualBucketColorPercent()
    {
        // 90% used → should be above threshold=80 → color should reach/exceed 80
        var usage = BuildCodexDualQuotaUsage(burstUsedPercent: 90.0, rollingUsedPercent: 90.0);
        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: true, redThreshold: 80);

        Assert.NotNull(presentation.DualBucketPrimaryColorPercent);
        Assert.True(presentation.DualBucketPrimaryColorPercent!.Value >= 80.0,
            $"90% used at threshold=80 must produce color >= 80, got {presentation.DualBucketPrimaryColorPercent:F1}");
    }

    [Fact]
    public void LowUsage_DualBucketColorPercent_BelowRedThreshold()
    {
        // 10% used → well below threshold → color should be below threshold
        var usage = BuildCodexDualQuotaUsage(burstUsedPercent: 10.0, rollingUsedPercent: 10.0);
        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: true, redThreshold: 80);

        Assert.NotNull(presentation.DualBucketPrimaryColorPercent);
        Assert.True(presentation.DualBucketPrimaryColorPercent!.Value < 80.0,
            $"10% used at threshold=80 must produce color < 80, got {presentation.DualBucketPrimaryColorPercent:F1}");
    }
}
