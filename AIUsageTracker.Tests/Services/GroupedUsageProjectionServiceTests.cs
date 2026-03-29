// <copyright file="GroupedUsageProjectionServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;

namespace AIUsageTracker.Tests.Services;

public sealed class GroupedUsageProjectionServiceTests
{
    [Fact]
    public void Build_AntigravityWithFlatCardUsage_ProjectsCardIdAsModel()
    {
        // Antigravity emits flat cards with CardId set; BuildModelsFromFlatCards picks them up.
        var usages = new[]
        {
            new ProviderUsage
            {
                ProviderId = "antigravity",
                CardId = "gemini-3-flash",
                Name = "Gemini 3 Flash",
                ProviderName = "Gemini 3 Flash",
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                UsedPercent = 0,
                RequestsUsed = 0,
                RequestsAvailable = 135,
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

    [Fact]
    public void Build_WhenMostRecentEntryIsError_PrimaryUsageReflectsErrorState()
    {
        // Older successful entry + newer error entry — primary must be the newest so the
        // error reason is surfaced rather than showing stale successful data.
        var old = DateTime.UtcNow.AddHours(-19);
        var now = DateTime.UtcNow;

        var usages = new[]
        {
            new ProviderUsage
            {
                ProviderId = "codex",
                IsAvailable = true,
                UsedPercent = 42,
                Description = "58% remaining",
                FetchedAt = old,
            },
            new ProviderUsage
            {
                ProviderId = "codex",
                IsAvailable = false,
                UsedPercent = 0,
                Description = "HTTP 401: Unauthorized",
                FetchedAt = now,
            },
        };

        var snapshot = GroupedUsageProjectionService.Build(usages);

        var provider = Assert.Single(snapshot.Providers);
        // Description must come from the most recent entry, not the stale successful one.
        Assert.Equal("HTTP 401: Unauthorized", provider.Description);
    }
}
