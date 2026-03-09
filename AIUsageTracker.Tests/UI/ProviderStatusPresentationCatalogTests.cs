// <copyright file="ProviderStatusPresentationCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.UI
{
    using AIUsageTracker.Core.Models;
    using AIUsageTracker.UI.Slim;

    public sealed class ProviderStatusPresentationCatalogTests
    {
        [Fact]
        public void Create_ReturnsDerivedPresentation_WithResetLine()
        {
            var config = new ProviderConfig { ProviderId = "codex.spark" };
            var usage = new ProviderUsage
            {
                IsAvailable = true,
                NextResetTime = new DateTime(2026, 3, 7, 12, 0, 0)
            };

            var presentation = ProviderStatusPresentationCatalog.Create(
                config,
                usage,
                ProviderInputMode.DerivedReadOnly,
                isPrivacyMode: false,
                new ProviderAuthIdentities(null, null, null));

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
                    new() { Name = "[internal]" }
                }
            };

            var presentation = ProviderStatusPresentationCatalog.Create(
                config,
                usage,
                ProviderInputMode.AntigravityAutoDetected,
                isPrivacyMode: true,
                new ProviderAuthIdentities(null, null, null));

            Assert.Equal("Auto-Detected (u**r@*******.***)", presentation.PrimaryText);
            Assert.Single(presentation.SecondaryLines);
            Assert.Equal("Models: Gemini 2.5 Flash, Gemini 2.5 Pro", presentation.SecondaryLines[0].Text);
            Assert.True(presentation.SecondaryLines[0].Wrap);
        }

        [Fact]
        public void Create_ReturnsGitHubPresentation_FromAuthIdentity()
        {
            var config = new ProviderConfig { ProviderId = "github-copilot", ApiKey = string.Empty };

            var presentation = ProviderStatusPresentationCatalog.Create(
                config,
                usage: null,
                ProviderInputMode.GitHubCopilotAuthStatus,
                isPrivacyMode: false,
                new ProviderAuthIdentities("octocat", null, null));

            Assert.True(presentation.UseHorizontalLayout);
            Assert.Equal("Authenticated (octocat)", presentation.PrimaryText);
            Assert.Empty(presentation.SecondaryLines);
        }

        [Fact]
        public void Create_ReturnsOpenAiPresentation_FromCodexIdentity_AndLoadingReset()
        {
            var config = new ProviderConfig { ProviderId = "codex", ApiKey = "sess-token" };

            var presentation = ProviderStatusPresentationCatalog.Create(
                config,
                usage: null,
                ProviderInputMode.OpenAiSessionStatus,
                isPrivacyMode: true,
                new ProviderAuthIdentities(null, "fallback@example.com", "codex@example.com"));

            Assert.Equal("Authenticated (c***x@*******.***)", presentation.PrimaryText);
            Assert.Single(presentation.SecondaryLines);
            Assert.Equal("Next reset: loading...", presentation.SecondaryLines[0].Text);
        }

        [Fact]
        public void Create_ReturnsOpenAiPresentation_FromOpenAiIdentity()
        {
            var config = new ProviderConfig { ProviderId = "openai", ApiKey = "sess-token" };

            var presentation = ProviderStatusPresentationCatalog.Create(
                config,
                usage: null,
                ProviderInputMode.OpenAiSessionStatus,
                isPrivacyMode: false,
                new ProviderAuthIdentities(null, "openai-user@example.com", "codex-user@example.com"));

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
}
