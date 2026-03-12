// <copyright file="AgentGroupedUsageValueResolverTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.MonitorClient;

namespace AIUsageTracker.Tests.Core;

public class AgentGroupedUsageValueResolverTests
{
    [Fact]
    public void ResolveModelEffectiveState_PrefersEffectiveFields_WhenProvided()
    {
        var expectedReset = DateTime.UtcNow.AddHours(3);
        var model = new AgentGroupedModelUsage
        {
            ModelId = "gemini-2.5-flash-lite",
            ModelName = "Gemini 2.5 Flash Lite",
            UsedPercentage = 80,
            RemainingPercentage = 20,
            Description = "20% Remaining",
            NextResetTime = DateTime.UtcNow.AddHours(1),
            EffectiveUsedPercentage = 23,
            EffectiveRemainingPercentage = 77,
            EffectiveDescription = "77.0% Remaining",
            EffectiveNextResetTime = expectedReset,
            QuotaBuckets = new[]
            {
                new AgentGroupedQuotaBucketUsage
                {
                    BucketId = "hourly",
                    BucketName = "Requests / Hour",
                    RemainingPercentage = 10,
                    UsedPercentage = 90,
                    Description = "10% remaining",
                },
            },
        };

        var result = AgentGroupedUsageValueResolver.ResolveModelEffectiveState(model, parentIsQuotaBased: true);

        Assert.Equal(23, result.UsedPercentage, 3);
        Assert.Equal(77, result.RemainingPercentage, 3);
        Assert.Equal("77.0% Remaining", result.Description);
        Assert.Equal(expectedReset, result.NextResetTime);
    }

    [Fact]
    public void ResolveModelEffectiveState_DerivesEffectiveUsed_WhenOnlyEffectiveRemainingProvided()
    {
        var model = new AgentGroupedModelUsage
        {
            ModelId = "gemini-3-flash-preview",
            ModelName = "Gemini 3 Flash Preview",
            RemainingPercentage = 40,
            UsedPercentage = 60,
            EffectiveRemainingPercentage = 88,
        };

        var result = AgentGroupedUsageValueResolver.ResolveModelEffectiveState(model, parentIsQuotaBased: true);

        Assert.Equal(12, result.UsedPercentage, 3);
        Assert.Equal(88, result.RemainingPercentage, 3);
    }

    [Fact]
    public void ResolveModelEffectiveState_UsesQuotaBucket_WhenModelPercentagesMissing()
    {
        var model = new AgentGroupedModelUsage
        {
            ModelId = "gemini-3-pro",
            ModelName = "Gemini 3 Pro",
            QuotaBuckets = new[]
            {
                new AgentGroupedQuotaBucketUsage
                {
                    BucketId = "daily",
                    BucketName = "Requests / Day",
                    RemainingPercentage = 35,
                    Description = "35% remaining",
                },
            },
        };

        var result = AgentGroupedUsageValueResolver.ResolveModelEffectiveState(model, parentIsQuotaBased: true);

        Assert.Equal(65, result.UsedPercentage, 3);
        Assert.Equal(35, result.RemainingPercentage, 3);
        Assert.Equal("35% remaining", result.Description);
    }
}
