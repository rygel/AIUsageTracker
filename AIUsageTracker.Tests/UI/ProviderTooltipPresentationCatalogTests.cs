// <copyright file="ProviderTooltipPresentationCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class ProviderTooltipPresentationCatalogTests
{
    [Fact]
    public void BuildContent_ReturnsDetailTooltip_WhenDetailsExist()
    {
        var usage = new ProviderUsage
        {
            IsAvailable = true,
            Description = "Connected",
            Details = new List<ProviderUsageDetail>
            {
                new() { Name = "Weekly", Used = "20% used", DetailType = ProviderUsageDetailType.QuotaWindow, QuotaBucketKind = WindowKind.Secondary },
                new() { Name = "Hourly", Used = "10% used", DetailType = ProviderUsageDetailType.QuotaWindow, QuotaBucketKind = WindowKind.Primary },
            },
        };

        var content = ProviderTooltipPresentationCatalog.BuildContent(usage, "OpenAI");

        Assert.Equal(
            "OpenAI\nStatus: Active\nDescription: Connected\n\nRate Limits:\n  Hourly: 10% used\n  Weekly: 20% used",
            content?.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildContent_OrdersQuotaWindowsBeforeCredits()
    {
        var usage = new ProviderUsage
        {
            IsAvailable = true,
            Details = new List<ProviderUsageDetail>
            {
                new() { Name = "Credits", Used = "0.00", DetailType = ProviderUsageDetailType.Credit },
                new() { Name = "Spark", Used = "0% used", DetailType = ProviderUsageDetailType.QuotaWindow, QuotaBucketKind = WindowKind.Spark },
                new() { Name = "Weekly", Used = "51% used", DetailType = ProviderUsageDetailType.QuotaWindow, QuotaBucketKind = WindowKind.Secondary },
                new() { Name = "5-hour", Used = "4% used", DetailType = ProviderUsageDetailType.QuotaWindow, QuotaBucketKind = WindowKind.Primary },
            },
        };

        var content = ProviderTooltipPresentationCatalog.BuildContent(usage, "Codex");

        Assert.Equal(
            "Codex\nStatus: Active\n\nRate Limits:\n  5-hour: 4% used\n  Weekly: 51% used\n  Spark: 0% used\n  Credits: 0.00",
            content?.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildContent_OrdersUnclassifiedQuotaBucketsBeforeCredits()
    {
        var usage = new ProviderUsage
        {
            IsAvailable = true,
            Details = new List<ProviderUsageDetail>
            {
                new() { Name = "Credits", Used = "0.00", DetailType = ProviderUsageDetailType.Credit },
                new() { Name = "Requests / Day", Used = "35% remaining", DetailType = ProviderUsageDetailType.QuotaWindow, QuotaBucketKind = WindowKind.None },
                new() { Name = "Requests / Hour", Used = "80% remaining", DetailType = ProviderUsageDetailType.QuotaWindow, QuotaBucketKind = WindowKind.None },
            },
        };

        var content = ProviderTooltipPresentationCatalog.BuildContent(usage, "Gemini");

        Assert.Equal(
            "Gemini\nStatus: Active\n\nRate Limits:\n  Requests / Day: 35% remaining\n  Requests / Hour: 80% remaining\n  Credits: 0.00",
            content?.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildContent_ReturnsAuthSourceTooltip_WhenNoDetailsExist()
    {
        var usage = new ProviderUsage
        {
            AuthSource = "local app",
        };

        var content = ProviderTooltipPresentationCatalog.BuildContent(usage, "Copilot");

        Assert.Equal("Copilot\nSource: local app", content);
    }

    [Fact]
    public void BuildContent_ReturnsNull_WhenNoTooltipDataExists()
    {
        var usage = new ProviderUsage();

        var content = ProviderTooltipPresentationCatalog.BuildContent(usage, "OpenCode");

        Assert.Null(content);
    }

    [Fact]
    public void BuildContent_UsesDetailDescription_WhenUsedValueMissing()
    {
        var usage = new ProviderUsage
        {
            IsAvailable = true,
            Details = new List<ProviderUsageDetail>
            {
                new() { Name = "Sessions", Used = string.Empty, Description = "4 sessions", DetailType = ProviderUsageDetailType.Other },
                new() { Name = "Messages", Used = string.Empty, Description = "198 messages", DetailType = ProviderUsageDetailType.Other },
            },
        };

        var content = ProviderTooltipPresentationCatalog.BuildContent(usage, "OpenCode Zen");
        var normalized = content?.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("Sessions: 4 sessions", normalized, StringComparison.Ordinal);
        Assert.Contains("Messages: 198 messages", normalized, StringComparison.Ordinal);
    }
}
