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
        Assert.Equal(1, provider.Models.Count);
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
        Assert.Equal(0, provider.Models.Count);
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
        Assert.Equal(0, provider.Models.Count);
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
        Assert.Equal(2, provider.Models.Count);

        var flashLite = Assert.Single(provider.Models, model => string.Equals(model.ModelId, "gemini-2.5-flash-lite", StringComparison.Ordinal));
        var flashPreview = Assert.Single(provider.Models, model => string.Equals(model.ModelId, "gemini-3-flash-preview", StringComparison.Ordinal));

        var flashLiteBucket = Assert.Single(flashLite.QuotaBuckets);
        var flashPreviewBucket = Assert.Single(flashPreview.QuotaBuckets);

        Assert.Equal("Requests / Minute", flashLiteBucket.BucketName);
        Assert.Equal("Requests / Minute", flashPreviewBucket.BucketName);
        Assert.Equal(95.0, flashLiteBucket.RemainingPercentage!.Value, 1);
        Assert.Equal(70.0, flashPreviewBucket.RemainingPercentage!.Value, 1);
    }

    [Fact]
    public void Build_KimiUsage_PopulatesProviderDetails_FromUsageDetails()
    {
        // Kimi has no Model-type details; only QuotaWindow. The projection must populate
        // ProviderDetails so the UI can render dual bars on the parent card.
        var weeklyDetail = new ProviderUsageDetail
        {
            Name = "Weekly Limit",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Rolling,
        };
        weeklyDetail.SetPercentageValue(25.0, PercentageValueSemantic.Used, decimalPlaces: 1);

        var burstDetail = new ProviderUsageDetail
        {
            Name = "5h Limit",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Burst,
        };
        burstDetail.SetPercentageValue(0.0, PercentageValueSemantic.Used, decimalPlaces: 1);

        var usages = new[]
        {
            new ProviderUsage
            {
                ProviderId = "kimi-for-coding",
                IsAvailable = true,
                IsQuotaBased = true,
                UsedPercent = 25,
                RequestsUsed = 25,
                RequestsAvailable = 100,
                FetchedAt = DateTime.UtcNow,
                Details = new List<ProviderUsageDetail> { weeklyDetail, burstDetail },
            },
        };

        var snapshot = GroupedUsageProjectionService.Build(usages);

        var provider = Assert.Single(snapshot.Providers);
        Assert.Equal(2, provider.ProviderDetails.Count);
        Assert.Contains(provider.ProviderDetails, d => d.QuotaBucketKind == WindowKind.Rolling && string.Equals(d.Name, "Weekly Limit", StringComparison.Ordinal));
        Assert.Contains(provider.ProviderDetails, d => d.QuotaBucketKind == WindowKind.Burst && string.Equals(d.Name, "5h Limit", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_CodexUsageWithModelScopedSparkDetails_CreatesSparkModelWithDualBarBuckets()
    {
        // Regression: CodexProvider emits model-scoped QW details (Burst=Spark5h, Rolling=Weekly)
        // so BuildModelsFromDetails can scope them to the Spark model's QuotaBuckets.
        // Those model-scoped details must NOT appear in ProviderDetails (parent card only shows
        // provider-level windows). The Spark model's QuotaBuckets must carry correct QuotaBucketKind
        // values so the child card can render dual bars.
        const string sparkModelId = "GPT-5.3-Codex-Spark";

        var modelDetail = new ProviderUsageDetail
        {
            Name = sparkModelId,
            ModelName = sparkModelId,
            DetailType = ProviderUsageDetailType.Model,
            QuotaBucketKind = WindowKind.None,
        };
        modelDetail.SetPercentageValue(2.0, PercentageValueSemantic.Remaining); // 98% effective used

        // Model-scoped Burst: Spark's own 5h window just reset (100% remaining)
        var sparkBurstDetail = new ProviderUsageDetail
        {
            Name = "Spark 5h quota",
            ModelName = sparkModelId,
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Burst,
        };
        sparkBurstDetail.SetPercentageValue(100.0, PercentageValueSemantic.Remaining);

        // Model-scoped Rolling: shared weekly window is 98% used
        var sparkRollingDetail = new ProviderUsageDetail
        {
            Name = "Weekly quota",
            ModelName = sparkModelId,
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Rolling,
        };
        sparkRollingDetail.SetPercentageValue(2.0, PercentageValueSemantic.Remaining);

        // Provider-level windows (no ModelName) — these go into ProviderDetails
        var providerBurst = new ProviderUsageDetail
        {
            Name = "5-hour quota",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Burst,
        };
        providerBurst.SetPercentageValue(100.0, PercentageValueSemantic.Remaining);

        var providerRolling = new ProviderUsageDetail
        {
            Name = "Weekly quota",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Rolling,
        };
        providerRolling.SetPercentageValue(2.0, PercentageValueSemantic.Remaining);

        var usages = new[]
        {
            new ProviderUsage
            {
                ProviderId = "codex",
                IsAvailable = true,
                IsQuotaBased = true,
                UsedPercent = 98,
                FetchedAt = DateTime.UtcNow,
                Details = new List<ProviderUsageDetail>
                {
                    modelDetail,
                    sparkBurstDetail,
                    sparkRollingDetail,
                    providerBurst,
                    providerRolling,
                },
            },
        };

        var snapshot = GroupedUsageProjectionService.Build(usages);

        var provider = Assert.Single(snapshot.Providers);
        var spark = Assert.Single(provider.Models);
        Assert.Equal(sparkModelId, spark.ModelId);

        // Child card buckets must carry Burst and Rolling kinds for dual bar rendering
        Assert.Equal(2, spark.QuotaBuckets.Count);
        Assert.Single(spark.QuotaBuckets, b => b.QuotaBucketKind == WindowKind.Burst);
        Assert.Single(spark.QuotaBuckets, b => b.QuotaBucketKind == WindowKind.Rolling);

        // Only provider-level QW entries (no ModelName) appear in ProviderDetails.
        // Model-scoped QW and Model-typed entries are excluded — the parent card dual bar
        // must show only provider-level windows, not per-model windows.
        Assert.Equal(2, provider.ProviderDetails.Count);
        Assert.All(provider.ProviderDetails, d =>
        {
            Assert.Equal(ProviderUsageDetailType.QuotaWindow, d.DetailType);
            Assert.True(string.IsNullOrWhiteSpace(d.ModelName));
        });
    }

    [Fact]
    public void Build_KimiUsage_FiltersToQuotaWindowDetails_InProviderDetails()
    {
        // ProviderDetails contains only non-stale provider-level QuotaWindow entries.
        // Credit-type details are excluded — the UI no longer needs to filter by DetailType.
        var creditDetail = new ProviderUsageDetail
        {
            Name = "Credits",
            DetailType = ProviderUsageDetailType.Credit,
            QuotaBucketKind = WindowKind.None,
        };

        var quotaDetail = new ProviderUsageDetail
        {
            Name = "Weekly Limit",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Rolling,
        };
        quotaDetail.SetPercentageValue(10.0, PercentageValueSemantic.Used);

        var usages = new[]
        {
            new ProviderUsage
            {
                ProviderId = "kimi-for-coding",
                IsAvailable = true,
                IsQuotaBased = true,
                UsedPercent = 10,
                FetchedAt = DateTime.UtcNow,
                Details = new List<ProviderUsageDetail> { creditDetail, quotaDetail },
            },
        };

        var snapshot = GroupedUsageProjectionService.Build(usages);

        var provider = Assert.Single(snapshot.Providers);
        Assert.Single(provider.ProviderDetails); // only QuotaWindow passes through; Credit is excluded
    }
}
