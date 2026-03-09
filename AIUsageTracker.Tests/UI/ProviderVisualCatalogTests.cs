// <copyright file="ProviderVisualCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.UI
{
    using System.Windows.Media;
    using AIUsageTracker.UI.Slim;

    public class ProviderVisualCatalogTests
    {
        [Theory]
        [InlineData("codex.spark", "codex", "openai")]
        [InlineData("antigravity.claude-opus", "antigravity", "google")]
        [InlineData("minimax-io", "minimax", "minimax")]
        [InlineData("zai-coding-plan", "zai-coding-plan", "zai")]
        public void GetIconAssetName_UsesCanonicalProviderIdentity(
            string providerId,
            string expectedCanonicalProviderId,
            string expectedAssetName)
        {
            Assert.Equal(expectedCanonicalProviderId, ProviderVisualCatalog.GetCanonicalProviderId(providerId));
            Assert.Equal(expectedAssetName, ProviderVisualCatalog.GetIconAssetName(providerId));
        }

        [Theory]
        [InlineData("codex.spark", "AI")]
        [InlineData("claude-code", "C")]
        [InlineData("github-copilot", "GH")]
        [InlineData("unknown-provider", "UN")]
        public void GetFallbackBadge_ReturnsStableBadgeInitials(string providerId, string expectedInitial)
        {
            var (_, initial) = ProviderVisualCatalog.GetFallbackBadge(providerId, Brushes.Gray);

            Assert.Equal(expectedInitial, initial);
        }
    }
}
