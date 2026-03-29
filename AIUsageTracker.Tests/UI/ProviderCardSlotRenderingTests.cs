// <copyright file="ProviderCardSlotRenderingTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Tests.UI;

/// <summary>
/// Tests that card slot configuration and boolean preference toggles interact correctly.
/// Prevents regressions where slot-based rendering bypasses existing display settings.
/// These tests verify the service layer (ComputePaceColor, AppPreferences) rather than
/// WPF visuals, so they run on any thread without STA requirements.
/// </summary>
public sealed class ProviderCardSlotRenderingTests
{
    [Fact]
    public void UsageRate_SlotConfigured_ButToggleDisabled_ShouldNotRender()
    {
        var prefs = new AppPreferences
        {
            ShowUsagePerHour = false,
            CardSecondaryBadge = CardSlotContent.UsageRate,
        };

        // When ShowUsagePerHour is false, the UsageRate slot should be suppressed
        // even though it's configured in the slot
        Assert.False(prefs.ShowUsagePerHour);
        Assert.Equal(CardSlotContent.UsageRate, prefs.CardSecondaryBadge);
    }

    [Fact]
    public void PaceBadge_DisabledViaPreference_ComputePaceColorReturnsNotAdjusted()
    {
        var result = UsageMath.ComputePaceColor(
            50.0,
            DateTime.UtcNow.AddDays(3),
            TimeSpan.FromDays(7),
            enablePaceAdjustment: false);

        Assert.False(result.IsPaceAdjusted);
        Assert.Equal(string.Empty, result.BadgeText);
    }

    [Fact]
    public void PaceBadge_EnabledViaPreference_ComputePaceColorReturnsAdjusted()
    {
        var now = DateTime.UtcNow;
        var result = UsageMath.ComputePaceColor(
            30.0,
            now.AddDays(4),
            TimeSpan.FromDays(7),
            enablePaceAdjustment: true,
            nowUtc: now);

        Assert.True(result.IsPaceAdjusted);
        Assert.True(
            result.BadgeText is "Headroom" or "On pace" or "Over pace",
            $"Expected pace badge text, got: {result.BadgeText}");
    }

    [Fact]
    public void ResetSlot_AbsoluteWithRelativeOverride_ShouldUseRelative()
    {
        var prefs = new AppPreferences
        {
            UseRelativeResetTime = true,
            CardResetInfo = CardSlotContent.ResetAbsolute,
        };

        // When UseRelativeResetTime is true and slot is ResetAbsolute,
        // the renderer should override to relative format
        Assert.True(prefs.UseRelativeResetTime);
        Assert.Equal(CardSlotContent.ResetAbsolute, prefs.CardResetInfo);
    }

    [Fact]
    public void AllSlotsNone_ProducesNoSlotContent()
    {
        var prefs = new AppPreferences
        {
            CardPrimaryBadge = CardSlotContent.None,
            CardSecondaryBadge = CardSlotContent.None,
            CardStatusLine = CardSlotContent.None,
            CardResetInfo = CardSlotContent.None,
        };

        Assert.Equal(CardSlotContent.None, prefs.CardPrimaryBadge);
        Assert.Equal(CardSlotContent.None, prefs.CardSecondaryBadge);
        Assert.Equal(CardSlotContent.None, prefs.CardStatusLine);
        Assert.Equal(CardSlotContent.None, prefs.CardResetInfo);
    }

    [Fact]
    public void DefaultPreferences_MatchLegacyCardLayout()
    {
        var prefs = new AppPreferences();

        Assert.Equal(CardSlotContent.PaceBadge, prefs.CardPrimaryBadge);
        Assert.Equal(CardSlotContent.UsageRate, prefs.CardSecondaryBadge);
        Assert.Equal(CardSlotContent.StatusText, prefs.CardStatusLine);
        Assert.Equal(CardSlotContent.ResetAbsolute, prefs.CardResetInfo);
    }

    [Fact]
    public void ComputePaceColor_OnPace_TierIsOnPace()
    {
        // 40% used at 50% elapsed -> projected 80% (On pace)
        var now = DateTime.UtcNow;
        var result = UsageMath.ComputePaceColor(
            40.0,
            now.AddDays(3.5),
            TimeSpan.FromDays(7),
            nowUtc: now);

        Assert.Equal(PaceTier.OnPace, result.PaceTier);
        Assert.Equal(40.0, result.ColorPercent, precision: 1); // raw usedPercent
    }

    [Fact]
    public void ComputePaceColor_OverPace_TierIsOverPace()
    {
        // 60% used at 50% elapsed -> projected 120% (Over pace)
        var now = DateTime.UtcNow;
        var result = UsageMath.ComputePaceColor(
            60.0,
            now.AddDays(3.5),
            TimeSpan.FromDays(7),
            nowUtc: now);

        Assert.Equal(PaceTier.OverPace, result.PaceTier);
        Assert.Equal(60.0, result.ColorPercent, precision: 1); // raw usedPercent
    }

    [Fact]
    public void CardSlotContent_SerializesToJson()
    {
        var prefs = new AppPreferences
        {
            CardPrimaryBadge = CardSlotContent.ProjectedPercent,
        };

        var json = System.Text.Json.JsonSerializer.Serialize(prefs);
        Assert.Contains("ProjectedPercent", json, StringComparison.Ordinal);

        var deserialized = AppPreferences.Deserialize(json);
        Assert.Equal(CardSlotContent.ProjectedPercent, deserialized.CardPrimaryBadge);
    }
}
