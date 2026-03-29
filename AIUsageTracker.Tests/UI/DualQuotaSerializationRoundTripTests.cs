// <copyright file="DualQuotaSerializationRoundTripTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

/// <summary>
/// Verifies that <see cref="ProviderUsage"/> quota-window flat cards survive the
/// Monitor → UI JSON round-trip intact, so that TryGetDualQuotaBucketPresentation
/// can read them correctly on the UI side.
/// </summary>
public sealed class DualQuotaSerializationRoundTripTests
{
    // Both sides use the same canonical options — MonitorJsonSerializer is the single source of truth.
    private static readonly JsonSerializerOptions MonitorOptions = MonitorJsonSerializer.DefaultOptions;
    private static readonly JsonSerializerOptions ClientOptions = MonitorJsonSerializer.DefaultOptions;

    [Fact]
    public void ProviderUsage_WindowCard_SurvivesJsonRoundTrip()
    {
        var card = new ProviderUsage
        {
            ProviderId = "codex",
            Name = "5h",
            WindowKind = WindowKind.Burst,
            UsedPercent = 4.0,
        };

        var json = JsonSerializer.Serialize(card, MonitorOptions);
        var roundTripped = JsonSerializer.Deserialize<ProviderUsage>(json, ClientOptions)!;

        Assert.Equal(WindowKind.Burst, roundTripped.WindowKind);
        Assert.Equal("5h", roundTripped.Name);
        Assert.Equal(4.0, roundTripped.UsedPercent, precision: 1);
    }

    [Fact]
    public void AgentGroupedProviderUsage_WithQuotaWindowCards_SurvivesJsonRoundTrip()
    {
        var provider = new AgentGroupedProviderUsage
        {
            ProviderId = "codex",
            IsAvailable = true,
            IsQuotaBased = true,
            UsedPercent = 51,
            ProviderDetails = new[]
            {
                new ProviderUsage { ProviderId = "codex", Name = "5h",     WindowKind = WindowKind.Burst,   UsedPercent = 4.0  },
                new ProviderUsage { ProviderId = "codex", Name = "Weekly", WindowKind = WindowKind.Rolling, UsedPercent = 51.0 },
            },
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

        var burst = p.ProviderDetails.First(d => d.WindowKind == WindowKind.Burst);
        Assert.Equal("5h", burst.Name);
        Assert.Equal(4.0, burst.UsedPercent, precision: 1);

        var rolling = p.ProviderDetails.First(d => d.WindowKind == WindowKind.Rolling);
        Assert.Equal("Weekly", rolling.Name);
        Assert.Equal(51.0, rolling.UsedPercent, precision: 1);
    }

    [Fact]
    public void FullPipeline_AfterJsonRoundTrip_ProducesDualBucketPresentation()
    {
        // Simulate the full path: provider data → JSON (Monitor) → UI client → Create()
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
                    ProviderDetails = new[]
                    {
                        new ProviderUsage { ProviderId = "codex", Name = "5h",     WindowKind = WindowKind.Burst,   UsedPercent = 4.0  },
                        new ProviderUsage { ProviderId = "codex", Name = "Weekly", WindowKind = WindowKind.Rolling, UsedPercent = 51.0 },
                    },
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
        Assert.NotNull(usage.WindowCards);
        Assert.Equal(2, usage.WindowCards!.Count);

        // Create() should produce dual buckets
        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: false);

        Assert.True(presentation.HasDualBuckets,
            "HasDualBuckets must be true after JSON round-trip");
        Assert.Equal("5h", presentation.DualBar!.Primary.Label);
        Assert.Equal("Weekly", presentation.DualBar.Secondary.Label);
        Assert.True(presentation.SuppressSingleResetTime);
        Assert.Contains("|", presentation.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void FullPipeline_ClaudeCode_AfterJsonRoundTrip_ProducesDualBucketPresentation()
    {
        // Simulate ClaudeCodeProvider output → GroupedUsageProjectionService → HTTP JSON → UI
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
                    ProviderDetails = new[]
                    {
                        new ProviderUsage { ProviderId = "claude-code", Name = "Current Session", WindowKind = WindowKind.Burst,   UsedPercent = 4.0  },
                        new ProviderUsage { ProviderId = "claude-code", Name = "All Models",      WindowKind = WindowKind.Rolling, UsedPercent = 51.0 },
                    },
                },
            },
        };

        var json = JsonSerializer.Serialize(snapshot, MonitorOptions);
        var deserialized = JsonSerializer.Deserialize<AgentGroupedUsageSnapshot>(json, ClientOptions)!;

        var usages = GroupedUsageDisplayAdapter.Expand(deserialized);
        Assert.Single(usages);

        var usage = usages[0];
        Assert.NotNull(usage.WindowCards);
        Assert.Equal(2, usage.WindowCards!.Count);

        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: false);

        Assert.True(presentation.HasDualBuckets,
            "claude-code must produce dual bars: Current Session (Burst) + All Models (Rolling)");
        Assert.Equal("Current Session", presentation.DualBar!.Primary.Label);
        Assert.Equal("All Models", presentation.DualBar.Secondary.Label);
        Assert.Equal(4.0, presentation.DualBar.Primary.UsedPercent, precision: 1);
        Assert.Equal(51.0, presentation.DualBar.Secondary.UsedPercent, precision: 1);
    }
}
