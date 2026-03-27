// <copyright file="ProviderMetadataCatalogCanonicalizationTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Tests.Infrastructure;

public class ProviderMetadataCatalogCanonicalizationTests
{
    private static readonly string TestApiKey1 = Guid.NewGuid().ToString();
    private static readonly string TestApiKey2 = Guid.NewGuid().ToString();
    private static readonly string TestApiKey3 = Guid.NewGuid().ToString();
    private static readonly string TestApiKey4 = Guid.NewGuid().ToString();

    [Fact]
    public void NormalizeCanonicalConfigurations_MigratesOpenAiSessionTokenToCodex()
    {
        var configs = new List<ProviderConfig>
        {
            new()
            {
                ProviderId = "openai",
                ApiKey = TestApiKey1,
                AuthSource = "OpenCode",
                Description = "session",
            },
        };

        ProviderMetadataCatalog.NormalizeCanonicalConfigurations(configs);

        var codex = Assert.Single(configs);
        Assert.Equal("codex", codex.ProviderId);
        Assert.Equal(TestApiKey1, codex.ApiKey);
        Assert.Equal(PlanType.Coding, codex.PlanType);
        Assert.Equal("quota-based", codex.Type);
        Assert.Equal("OpenCode", codex.AuthSource);
        Assert.Equal("Migrated from OpenAI session config", codex.Description);
    }

    [Fact]
    public void NormalizeCanonicalConfigurations_KeepsCodexSparkAsDedicatedProvider()
    {
        var configs = new List<ProviderConfig>
        {
            new()
            {
                ProviderId = "codex.spark",
                ApiKey = TestApiKey2,
                AuthSource = "Spark",
                Description = "spark",
                Type = "quota-based",
                PlanType = PlanType.Coding,
                BaseUrl = "https://example.invalid",
                ShowInTray = true,
                EnableNotifications = true,
            },
        };

        ProviderMetadataCatalog.NormalizeCanonicalConfigurations(configs);

        var spark = Assert.Single(configs);
        Assert.Equal("codex.spark", spark.ProviderId);
        Assert.Equal(TestApiKey2, spark.ApiKey);
        Assert.Equal("Spark", spark.AuthSource);
        Assert.Equal("spark", spark.Description);
        Assert.Equal("https://example.invalid", spark.BaseUrl);
        Assert.True(spark.ShowInTray);
        Assert.True(spark.EnableNotifications);
        Assert.Equal(PlanType.Coding, spark.PlanType);
        Assert.Equal("quota-based", spark.Type);
    }

    [Fact]
    public void NormalizeCanonicalConfigurations_RemovesOpenAiAlias_WhenCodexExists()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "codex", ApiKey = TestApiKey3 },
            new() { ProviderId = "openai", ApiKey = TestApiKey4 },
        };

        ProviderMetadataCatalog.NormalizeCanonicalConfigurations(configs);

        var codex = Assert.Single(configs);
        Assert.Equal("codex", codex.ProviderId);
        Assert.Equal(TestApiKey3, codex.ApiKey);
    }
}
