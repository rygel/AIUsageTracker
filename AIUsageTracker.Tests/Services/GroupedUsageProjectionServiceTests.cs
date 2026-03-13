// <copyright file="GroupedUsageProjectionServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;

namespace AIUsageTracker.Tests.Services;

public sealed class GroupedUsageProjectionServiceTests
{
    [Fact]
    public void Build_AntigravityWithExplicitChildRows_UsesProviderChildRowsAsModelSource()
    {
        var usages = new[]
        {
            new ProviderUsage
            {
                ProviderId = "antigravity",
                ProviderName = "Antigravity",
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                RequestsPercentage = 40,
                Description = "40% Remaining",
                Details = new List<ProviderUsageDetail>
                {
                    new()
                    {
                        Name = "Stale Detail",
                        Used = "10% remaining",
                        DetailType = ProviderUsageDetailType.Model,
                    },
                },
            },
            new ProviderUsage
            {
                ProviderId = "antigravity.gemini-3-flash",
                ProviderName = "Gemini 3 Flash",
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                RequestsPercentage = 100,
                RequestsUsed = 0,
                RequestsAvailable = 135,
                UsageUnit = "Tokens",
                DisplayAsFraction = true,
                Description = "100% Remaining",
            },
        };

        var snapshot = GroupedUsageProjectionService.Build(usages);

        var provider = Assert.Single(snapshot.Providers);
        Assert.Equal("Google Antigravity", provider.ProviderName);
        var model = Assert.Single(provider.Models);
        Assert.Equal("gemini-3-flash", model.ModelId);
        Assert.Equal("Gemini 3 Flash", model.ModelName);
        Assert.Equal(100, model.RemainingPercentage);
        Assert.Equal(0, model.UsedPercentage);
        Assert.Equal("100% Remaining", model.Description);
    }
}
