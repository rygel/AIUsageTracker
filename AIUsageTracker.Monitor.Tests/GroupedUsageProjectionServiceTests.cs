// <copyright file="GroupedUsageProjectionServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;

namespace AIUsageTracker.Monitor.Tests;

public sealed class GroupedUsageProjectionServiceTests
{
    [Fact]
    public void Build_ProjectsSingleProviderWithModelArray_FromModelDetails()
    {
        var modelDetail = new ProviderUsageDetail
        {
            Name = "Gemini 2.5 Flash Lite",
            ModelName = "gemini-2.5-flash-lite",
            DetailType = ProviderUsageDetailType.Model,
            QuotaBucketKind = WindowKind.None,
        };
        modelDetail.SetPercentageValue(96.7, PercentageValueSemantic.Remaining);

        var quotaDetail = new ProviderUsageDetail
        {
            Name = "Quota Bucket (Primary)",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Burst,
        };
        quotaDetail.SetPercentageValue(65.7, PercentageValueSemantic.Remaining);

        var usages = new[]
        {
            new ProviderUsage
            {
                ProviderId = "gemini-cli",
                ProviderName = "Google Gemini",
                IsAvailable = true,
                IsQuotaBased = true,
                RequestsUsed = 35,
                RequestsAvailable = 100,
                UsedPercent = 35,
                FetchedAt = DateTime.UtcNow,
                Details = new List<ProviderUsageDetail> { modelDetail, quotaDetail },
            },
            new ProviderUsage
            {
                ProviderId = "gemini-cli.primary",
                ProviderName = "Gemini CLI (Primary)",
                IsAvailable = true,
                IsQuotaBased = true,
                RequestsUsed = 93,
                RequestsAvailable = 100,
                UsedPercent = 93,
                FetchedAt = DateTime.UtcNow,
            },
        };

        var snapshot = GroupedUsageProjectionService.Build(usages);

        var provider = Assert.Single(snapshot.Providers);
        Assert.Equal("gemini-cli", provider.ProviderId);
        Assert.Equal("Google Gemini", provider.ProviderName);
        Assert.Equal(1, provider.ModelCount);
        var model = Assert.Single(provider.Models);
        Assert.Equal("gemini-2.5-flash-lite", model.ModelId);
        Assert.Equal("Gemini 2.5 Flash Lite", model.ModelName);
        var quotaBucket = Assert.Single(model.QuotaBuckets);
        Assert.Equal("effective", quotaBucket.BucketId);
        Assert.Equal("Effective Quota", quotaBucket.BucketName);
        Assert.NotNull(quotaBucket.RemainingPercentage);
        Assert.Equal(96.7, quotaBucket.RemainingPercentage!.Value, 1);
        Assert.Equal(96.7, model.EffectiveRemainingPercentage!.Value, 1);
        Assert.Equal(3.3, model.EffectiveUsedPercentage!.Value, 1);
        Assert.Equal("96.7% Remaining", model.EffectiveDescription);
    }

    [Fact]
    public void Build_DoesNotUseDerivedRowsAsModelFallback_WhenModelDetailsAreMissing()
    {
        var now = DateTime.UtcNow;
        var usages = new[]
        {
            new ProviderUsage
            {
                ProviderId = "codex",
                ProviderName = "OpenAI (Codex)",
                IsAvailable = true,
                IsQuotaBased = true,
                RequestsUsed = 50,
                RequestsAvailable = 100,
                UsedPercent = 50,
                FetchedAt = now,
                Details = null,
            },
            new ProviderUsage
            {
                ProviderId = "codex.spark",
                ProviderName = "OpenAI (GPT-5.3-Codex-Spark)",
                IsAvailable = true,
                IsQuotaBased = true,
                RequestsUsed = 10,
                RequestsAvailable = 100,
                UsedPercent = 10,
                FetchedAt = now,
            },
        };

        var snapshot = GroupedUsageProjectionService.Build(usages);

        var provider = Assert.Single(snapshot.Providers);
        Assert.Equal("codex", provider.ProviderId);
        Assert.Equal(0, provider.ModelCount);
        Assert.Empty(provider.Models);
    }

    [Fact]
    public void Build_KeepsProviderWithEmptyModelArray_WhenNoModelDataExists()
    {
        var weeklyDetail = new ProviderUsageDetail
        {
            Name = "Weekly Quota",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Rolling,
        };
        weeklyDetail.SetPercentageValue(14.0, PercentageValueSemantic.Used);

        var usages = new[]
        {
            new ProviderUsage
            {
                ProviderId = "github-copilot",
                ProviderName = "GitHub Copilot",
                IsAvailable = false,
                IsQuotaBased = true,
                RequestsUsed = 0,
                RequestsAvailable = 0,
                UsedPercent = 0,
                Description = "Not authenticated",
                FetchedAt = DateTime.UtcNow,
                Details = new List<ProviderUsageDetail> { weeklyDetail },
            },
        };

        var snapshot = GroupedUsageProjectionService.Build(usages);

        var provider = Assert.Single(snapshot.Providers);
        Assert.Equal("github-copilot", provider.ProviderId);
        Assert.Empty(provider.Models);
        Assert.Equal(0, provider.ModelCount);
    }

    [Fact]
    public void Build_MapsModelScopedQuotaBuckets_ToMatchingModels()
    {
        var flashLiteModel = new ProviderUsageDetail
        {
            Name = "Gemini 2.5 Flash Lite",
            ModelName = "gemini-2.5-flash-lite",
            DetailType = ProviderUsageDetailType.Model,
            QuotaBucketKind = WindowKind.None,
        };
        flashLiteModel.SetPercentageValue(90.0, PercentageValueSemantic.Remaining);

        var flashPreviewModel = new ProviderUsageDetail
        {
            Name = "Gemini 3 Flash Preview",
            ModelName = "gemini-3-flash-preview",
            DetailType = ProviderUsageDetailType.Model,
            QuotaBucketKind = WindowKind.None,
        };
        flashPreviewModel.SetPercentageValue(60.0, PercentageValueSemantic.Remaining);

        var flashLiteQuota = new ProviderUsageDetail
        {
            Name = "Requests / Minute",
            ModelName = "gemini-2.5-flash-lite",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Burst,
        };
        flashLiteQuota.SetPercentageValue(95.0, PercentageValueSemantic.Remaining);

        var flashPreviewQuota = new ProviderUsageDetail
        {
            Name = "Requests / Minute",
            ModelName = "gemini-3-flash-preview",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Burst,
        };
        flashPreviewQuota.SetPercentageValue(70.0, PercentageValueSemantic.Remaining);

        var usages = new[]
        {
            new ProviderUsage
            {
                ProviderId = "gemini-cli",
                ProviderName = "Google Gemini",
                IsAvailable = true,
                IsQuotaBased = true,
                RequestsUsed = 20,
                RequestsAvailable = 100,
                UsedPercent = 20,
                FetchedAt = DateTime.UtcNow,
                Details = new List<ProviderUsageDetail>
                {
                    flashLiteModel,
                    flashPreviewModel,
                    flashLiteQuota,
                    flashPreviewQuota,
                },
            },
        };

        var snapshot = GroupedUsageProjectionService.Build(usages);

        var provider = Assert.Single(snapshot.Providers);
        Assert.Equal(2, provider.ModelCount);

        var flashLite = Assert.Single(provider.Models, model => string.Equals(model.ModelId, "gemini-2.5-flash-lite", StringComparison.Ordinal));
        var flashPreview = Assert.Single(provider.Models, model => string.Equals(model.ModelId, "gemini-3-flash-preview", StringComparison.Ordinal));

        var flashLiteBucket = Assert.Single(flashLite.QuotaBuckets);
        var flashPreviewBucket = Assert.Single(flashPreview.QuotaBuckets);

        Assert.Equal("Requests / Minute", flashLiteBucket.BucketName);
        Assert.Equal("Requests / Minute", flashPreviewBucket.BucketName);
        Assert.Equal(95.0, flashLiteBucket.RemainingPercentage!.Value, 1);
        Assert.Equal(70.0, flashPreviewBucket.RemainingPercentage!.Value, 1);
    }
}
