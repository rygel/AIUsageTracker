// <copyright file="ProviderStatusPresentationCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;
public sealed class ProviderStatusPresentationCatalogTests
{
    [Fact]
    public void Create_ReturnsDerivedPresentation_WithResetLine()
    {
        var config = new ProviderConfig { ProviderId = "codex.spark" };
        var usage = new ProviderUsage
        {
            IsAvailable = true,
            NextResetTime = new DateTime(2026, 3, 7, 12, 0, 0),
        };

        var presentation = ProviderStatusPresentationCatalog.Create(
            config,
            usage,
            ProviderInputMode.DerivedReadOnly,
            isPrivacyMode: false);

        Assert.Equal("Derived from Codex usage (read-only)", presentation.PrimaryText);
        Assert.Single(presentation.SecondaryLines);
        Assert.StartsWith("Next reset:", presentation.SecondaryLines[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_ReturnsAntigravityPresentation_WithWrappedModelsLine()
    {
        var config = new ProviderConfig { ProviderId = "antigravity" };
        var usage = new ProviderUsage
        {
            IsAvailable = true,
            AccountName = "user@example.com",
            Details = new List<ProviderUsageDetail>
            {
                new() { Name = "Gemini 2.5 Pro" },
                new() { Name = "Gemini 2.5 Flash" },
                new() { Name = "[internal]" },
            },
        };

        var presentation = ProviderStatusPresentationCatalog.Create(
            config,
            usage,
            ProviderInputMode.AntigravityAutoDetected,
            isPrivacyMode: true);

        Assert.Equal("Auto-Detected (u**r@*******.***)", presentation.PrimaryText);
        Assert.Single(presentation.SecondaryLines);
        Assert.Equal("Models: Gemini 2.5 Flash, Gemini 2.5 Pro", presentation.SecondaryLines[0].Text);
        Assert.True(presentation.SecondaryLines[0].Wrap);
    }

    [Fact]
    public void Create_ReturnsAntigravityPresentation_UsesUnknownWhenUsageMissingAccount()
    {
        var config = new ProviderConfig { ProviderId = "antigravity" };
        var usage = new ProviderUsage
        {
            IsAvailable = true,
            AccountName = string.Empty,
            Details = new List<ProviderUsageDetail>(),
        };

        var presentation = ProviderStatusPresentationCatalog.Create(
            config,
            usage,
            ProviderInputMode.AntigravityAutoDetected,
            isPrivacyMode: false);

        Assert.Equal("Auto-Detected (Unknown)", presentation.PrimaryText);
    }

    [Fact]
    public void Create_ReturnsGitHubPresentation_AuthenticatedWithoutUsername_WhenUsageMissingAccount()
    {
        var config = new ProviderConfig { ProviderId = "github-copilot", ApiKey = string.Empty };

        var presentation = ProviderStatusPresentationCatalog.Create(
            config,
            usage: null,
            ProviderInputMode.GitHubCopilotAuthStatus,
            isPrivacyMode: false);

        Assert.True(presentation.UseHorizontalLayout);
        Assert.Equal("Not Authenticated", presentation.PrimaryText);
        Assert.Empty(presentation.SecondaryLines);
    }

    [Fact]
    public void Create_ReturnsGitHubPresentation_FromUsageAccount_WhenAuthIdentityUnavailable()
    {
        var config = new ProviderConfig { ProviderId = "github-copilot", ApiKey = string.Empty };
        var usage = new ProviderUsage
        {
            IsAvailable = true,
            AccountName = "octocat-from-usage",
        };

        var presentation = ProviderStatusPresentationCatalog.Create(
            config,
            usage,
            ProviderInputMode.GitHubCopilotAuthStatus,
            isPrivacyMode: false);

        Assert.True(presentation.UseHorizontalLayout);
        Assert.Equal("Authenticated (octocat-from-usage)", presentation.PrimaryText);
        Assert.Empty(presentation.SecondaryLines);
    }

    [Fact]
    public void Create_ReturnsOpenAiPresentation_WithoutIdentity_AndLoadingReset()
    {
        var config = new ProviderConfig { ProviderId = "codex", ApiKey = "sess-token" };

        var presentation = ProviderStatusPresentationCatalog.Create(
            config,
            usage: null,
            ProviderInputMode.OpenAiSessionStatus,
            isPrivacyMode: true);

        Assert.Equal("Authenticated via OpenAI Codex - refresh to load quota", presentation.PrimaryText);
        Assert.Single(presentation.SecondaryLines);
        Assert.Equal("Next reset: loading...", presentation.SecondaryLines[0].Text);
    }

    [Fact]
    public void Create_ReturnsOpenAiPresentation_FromUsageIdentity()
    {
        var config = new ProviderConfig { ProviderId = "openai", ApiKey = "sess-token" };
        var usage = new ProviderUsage
        {
            IsAvailable = true,
            AccountName = "openai-user@example.com",
        };

        var presentation = ProviderStatusPresentationCatalog.Create(
            config,
            usage,
            ProviderInputMode.OpenAiSessionStatus,
            isPrivacyMode: false);

        Assert.Equal("Authenticated (openai-user@example.com)", presentation.PrimaryText);
        Assert.Single(presentation.SecondaryLines);
        Assert.Equal("Next reset: loading...", presentation.SecondaryLines[0].Text);
    }

    [Fact]
    public void MaskAccountIdentifier_MasksEmailAddress()
    {
        var masked = ProviderStatusPresentationCatalog.MaskAccountIdentifier("person@example.com");

        Assert.Equal("p****n@*******.***", masked);
    }
}
