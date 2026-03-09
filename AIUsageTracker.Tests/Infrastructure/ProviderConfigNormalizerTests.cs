// <copyright file="ProviderConfigNormalizerTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Tests.Infrastructure;

public class ProviderMetadataCatalogCanonicalizationTests
{
    [Fact]
    public void NormalizeCanonicalConfigurations_MigratesOpenAiSessionTokenToCodex()
    {
        var configs = new List<ProviderConfig>
        {
            new()
            {
                ProviderId = "openai",
                ApiKey = "session-token",
                AuthSource = "OpenCode",
                Description = "session"
            },
        };

        ProviderMetadataCatalog.NormalizeCanonicalConfigurations(configs);

        var codex = Assert.Single(configs);
        Assert.Equal("codex", codex.ProviderId);
        Assert.Equal("session-token", codex.ApiKey);
        Assert.Equal(PlanType.Coding, codex.PlanType);
        Assert.Equal("quota-based", codex.Type);
        Assert.Equal("OpenCode", codex.AuthSource);
        Assert.Equal("Migrated from OpenAI session config", codex.Description);
    }

    [Fact]
    public void NormalizeCanonicalConfigurations_MergesCodexSparkIntoCodex()
    {
        var configs = new List<ProviderConfig>
        {
            new()
            {
                ProviderId = "codex.spark",
                ApiKey = "spark-token",
                AuthSource = "Spark",
                Description = "spark",
                BaseUrl = "https://example.invalid",
                ShowInTray = true,
                EnableNotifications = true
            },
        };

        ProviderMetadataCatalog.NormalizeCanonicalConfigurations(configs);

        var codex = Assert.Single(configs);
        Assert.Equal("codex", codex.ProviderId);
        Assert.Equal("spark-token", codex.ApiKey);
        Assert.Equal("Spark", codex.AuthSource);
        Assert.Equal("spark", codex.Description);
        Assert.Equal("https://example.invalid", codex.BaseUrl);
        Assert.True(codex.ShowInTray);
        Assert.True(codex.EnableNotifications);
        Assert.Equal(PlanType.Coding, codex.PlanType);
        Assert.Equal("quota-based", codex.Type);
    }

    [Fact]
    public void ShouldSuppressOpenAiSession_ReturnsTrue_WhenCodexHasKeyAndOpenAiIsSessionOnly()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "codex", ApiKey = "codex-session" },
            new() { ProviderId = "openai", ApiKey = "legacy-session-token" },
        };

        var result = ProviderMetadataCatalog.ShouldSuppressOpenAiSession(configs);

        Assert.True(result);
    }

    [Fact]
    public void ShouldSuppressOpenAiSession_ReturnsFalse_WhenOpenAiHasExplicitApiKey()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "codex", ApiKey = "codex-session" },
            new() { ProviderId = "openai", ApiKey = "sk-openai-live" },
        };

        var result = ProviderMetadataCatalog.ShouldSuppressOpenAiSession(configs);

        Assert.False(result);
    }
}
