// <copyright file="DualQuotaSerializationRoundTripTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

/// <summary>
/// Verifies grouped DTO JSON round-trip behavior for both flat models and parent-window details.
/// </summary>
public sealed class DualQuotaSerializationRoundTripTests
{
    // Both sides use the same default options — MonitorJsonSerializer is the single source of truth.
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
    public void AgentGroupedProviderUsage_WithModels_SurvivesJsonRoundTrip()
    {
        var provider = new AgentGroupedProviderUsage
        {
            ProviderId = "codex",
            IsAvailable = true,
            IsQuotaBased = true,
            UsedPercent = 51,
            Models = new[]
            {
                new AgentGroupedModelUsage
                {
                    ModelId = "spark",
                    ModelName = "Spark",
                    UsedPercentage = 51,
                    RemainingPercentage = 49,
                },
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
        var model = Assert.Single(p.Models);
        Assert.Equal("spark", model.ModelId);
        Assert.Equal("Spark", model.ModelName);
        Assert.Equal(51, model.UsedPercentage);
        Assert.Equal(49, model.RemainingPercentage);
    }

    [Fact]
    public void FullPipeline_AfterJsonRoundTrip_WithoutModels_ProducesParentCard()
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
                        new ProviderUsage
                        {
                            ProviderId = "codex",
                            Name = "5h",
                            WindowKind = WindowKind.Burst,
                            UsedPercent = 4,
                        },
                        new ProviderUsage
                        {
                            ProviderId = "codex",
                            Name = "Weekly",
                            WindowKind = WindowKind.Rolling,
                            UsedPercent = 51,
                        },
                    },
                },
            },
        };

        // JSON round-trip (Monitor serialize → UI deserialize)
        var json = JsonSerializer.Serialize(snapshot, MonitorOptions);
        var deserialized = JsonSerializer.Deserialize<AgentGroupedUsageSnapshot>(json, ClientOptions)!;

        // UI builds ProviderUsage from snapshot
        var usages = GroupedUsageDisplayAdapter.Expand(deserialized);
        var usage = Assert.Single(usages);
        Assert.NotNull(usage.WindowCards);
        Assert.Equal(2, usage.WindowCards!.Count);
    }

    [Fact]
    public void FullPipeline_ClaudeCode_AfterJsonRoundTrip_WithoutModels_ProducesParentCard()
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
                        new ProviderUsage
                        {
                            ProviderId = "claude-code",
                            Name = "Current Session",
                            WindowKind = WindowKind.Burst,
                            UsedPercent = 51,
                        },
                        new ProviderUsage
                        {
                            ProviderId = "claude-code",
                            Name = "All Models",
                            WindowKind = WindowKind.Rolling,
                            UsedPercent = 49,
                        },
                    },
                },
            },
        };

        var json = JsonSerializer.Serialize(snapshot, MonitorOptions);
        var deserialized = JsonSerializer.Deserialize<AgentGroupedUsageSnapshot>(json, ClientOptions)!;

        var usages = GroupedUsageDisplayAdapter.Expand(deserialized);
        var usage = Assert.Single(usages);
        Assert.NotNull(usage.WindowCards);
        Assert.Equal(2, usage.WindowCards!.Count);
    }
}
