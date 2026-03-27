// <copyright file="DualQuotaSerializationRoundTripTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

/// <summary>
/// Verifies that <see cref="ProviderUsageDetail"/> quota-window fields survive the
/// Monitor → UI JSON round-trip intact, so that TryGetDualQuotaBucketPresentation
/// can read them correctly on the UI side.
/// </summary>
public sealed class DualQuotaSerializationRoundTripTests
{
    // Both sides use the same canonical options — MonitorJsonSerializer is the single source of truth.
    private static readonly JsonSerializerOptions MonitorOptions = MonitorJsonSerializer.DefaultOptions;
    private static readonly JsonSerializerOptions ClientOptions = MonitorJsonSerializer.DefaultOptions;

    [Fact]
    public void ProviderUsageDetail_QuotaWindow_SurvivesJsonRoundTrip()
    {
        var detail = new ProviderUsageDetail
        {
            Name = "5-hour quota",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Burst,
            PercentageValue = 96.0,
            PercentageSemantic = PercentageValueSemantic.Remaining,
        };

        var json = JsonSerializer.Serialize(detail, MonitorOptions);
        var roundTripped = JsonSerializer.Deserialize<ProviderUsageDetail>(json, ClientOptions)!;

        Assert.Equal(ProviderUsageDetailType.QuotaWindow, roundTripped.DetailType);
        Assert.Equal(WindowKind.Burst, roundTripped.QuotaBucketKind);
        Assert.Equal(96.0, roundTripped.PercentageValue);
        Assert.Equal(PercentageValueSemantic.Remaining, roundTripped.PercentageSemantic);

        Assert.True(roundTripped.TryGetPercentageValue(out var pct, out var sem, out _));
        Assert.Equal(96.0, pct, precision: 1);
        Assert.Equal(PercentageValueSemantic.Remaining, sem);
    }

    [Fact]
    public void AgentGroupedProviderUsage_WithQuotaDetails_SurvivesJsonRoundTrip()
    {
        var burstDetail = new ProviderUsageDetail
        {
            Name = "5-hour quota",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Burst,
            PercentageValue = 4.0,
            PercentageSemantic = PercentageValueSemantic.Used,
        };

        var rollingDetail = new ProviderUsageDetail
        {
            Name = "Weekly quota",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Rolling,
            PercentageValue = 51.0,
            PercentageSemantic = PercentageValueSemantic.Used,
        };

        var provider = new AgentGroupedProviderUsage
        {
            ProviderId = "codex",
            IsAvailable = true,
            IsQuotaBased = true,
            UsedPercent = 51,
            ProviderDetails = new[] { burstDetail, rollingDetail },
        };

        var snapshot = new AgentGroupedUsageSnapshot
        {
            Providers = new[] { provider },
        };

        var json = JsonSerializer.Serialize(snapshot, MonitorOptions);
        var roundTripped = JsonSerializer.Deserialize<AgentGroupedUsageSnapshot>(json, ClientOptions)!;

        Assert.Single(roundTripped.Providers);
        var p = roundTripped.Providers[0];
        Assert.Equal(2, p.ProviderDetails.Count);

        var burst = p.ProviderDetails.First(d => d.QuotaBucketKind == WindowKind.Burst);
        Assert.Equal(ProviderUsageDetailType.QuotaWindow, burst.DetailType);
        Assert.Equal(4.0, burst.PercentageValue);
        Assert.Equal(PercentageValueSemantic.Used, burst.PercentageSemantic);

        var rolling = p.ProviderDetails.First(d => d.QuotaBucketKind == WindowKind.Rolling);
        Assert.Equal(ProviderUsageDetailType.QuotaWindow, rolling.DetailType);
        Assert.Equal(51.0, rolling.PercentageValue);
        Assert.Equal(PercentageValueSemantic.Used, rolling.PercentageSemantic);
    }

    [Fact]
    public void FullPipeline_AfterJsonRoundTrip_ProducesDualBucketPresentation()
    {
        // Simulate the full path: provider data → JSON (Monitor) → UI client → Create()
        var burstDetail = new ProviderUsageDetail
        {
            Name = "5-hour quota",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Burst,
            PercentageValue = 96.0,
            PercentageSemantic = PercentageValueSemantic.Remaining,
        };

        var rollingDetail = new ProviderUsageDetail
        {
            Name = "Weekly quota",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Rolling,
            PercentageValue = 49.0,
            PercentageSemantic = PercentageValueSemantic.Remaining,
        };

        var snapshot = new AgentGroupedUsageSnapshot
        {
            Providers = new[]
            {
                new AgentGroupedProviderUsage
                {
                    ProviderId = "codex",
                    IsAvailable = true,
                    IsQuotaBased = true,
                    UsedPercent = 4,
                    ProviderDetails = new[] { burstDetail, rollingDetail },
                },
            },
        };

        // JSON round-trip (Monitor serialize → UI deserialize)
        var json = JsonSerializer.Serialize(snapshot, MonitorOptions);
        var deserialized = JsonSerializer.Deserialize<AgentGroupedUsageSnapshot>(json, ClientOptions)!;

        // UI builds ProviderUsage from snapshot
        var usages = GroupedUsageDisplayAdapter.Expand(deserialized);
        Assert.Single(usages);

        var usage = usages[0];
        Assert.NotNull(usage.Details);
        Assert.Equal(2, usage.Details!.Count);

        // Create() should produce dual buckets
        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: false);

        Assert.True(presentation.HasDualBuckets,
            "HasDualBuckets must be true after JSON round-trip");
        Assert.Equal("5h", presentation.DualBucketPrimaryLabel);
        Assert.Equal("Weekly", presentation.DualBucketSecondaryLabel);
        Assert.True(presentation.SuppressSingleResetTime);
        Assert.Contains("|", presentation.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void FullPipeline_ClaudeCode_AfterJsonRoundTrip_ProducesDualBucketPresentation()
    {
        // Simulate ParseOAuthUsageResponse output → GroupedUsageProjectionService → HTTP JSON → UI
        // Detail names must match ClaudeCodeProvider.StaticDefinition.QuotaWindows DetailName values.
        var sessionDetail = new ProviderUsageDetail
        {
            Name = "Current Session",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Burst,
            PercentageValue = 4.0,
            PercentageSemantic = PercentageValueSemantic.Used,
        };

        var allModelsDetail = new ProviderUsageDetail
        {
            Name = "All Models",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Rolling,
            PercentageValue = 51.0,
            PercentageSemantic = PercentageValueSemantic.Used,
        };

        var snapshot = new AgentGroupedUsageSnapshot
        {
            Providers = new[]
            {
                new AgentGroupedProviderUsage
                {
                    ProviderId = "claude-code",
                    IsAvailable = true,
                    IsQuotaBased = true,
                    UsedPercent = 51,
                    ProviderDetails = new[] { sessionDetail, allModelsDetail },
                },
            },
        };

        var json = JsonSerializer.Serialize(snapshot, MonitorOptions);
        var deserialized = JsonSerializer.Deserialize<AgentGroupedUsageSnapshot>(json, ClientOptions)!;

        var usages = GroupedUsageDisplayAdapter.Expand(deserialized);
        Assert.Single(usages);

        var usage = usages[0];
        Assert.NotNull(usage.Details);
        Assert.Equal(2, usage.Details!.Count);

        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: false);

        Assert.True(presentation.HasDualBuckets,
            "claude-code must produce dual bars: Current Session (5h) + All Models (7-day)");
        Assert.Equal("5h", presentation.DualBucketPrimaryLabel);
        Assert.Equal("7-day", presentation.DualBucketSecondaryLabel);
        Assert.Equal(4.0, presentation.DualBucketPrimaryUsed!.Value, precision: 1);
        Assert.Equal(51.0, presentation.DualBucketSecondaryUsed!.Value, precision: 1);
    }
}
