// <copyright file="ProviderVisualCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows.Media;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

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
        Assert.Equal(expectedCanonicalProviderId, ProviderMetadataCatalog.GetCanonicalProviderId(providerId));
        Assert.Equal(expectedAssetName, ProviderMetadataCatalog.GetIconAssetName(providerId));
    }

    [Theory]
    [InlineData("codex.spark", "AI")]
    [InlineData("claude-code", "C")]
    [InlineData("github-copilot", "GH")]
    [InlineData("unknown-provider", "UN")]
    public void GetBadge_ReturnsStableBadgeInitials(string providerId, string expectedInitial)
    {
        var (_, initial) = ProviderVisualCatalog.GetBadge(providerId, Brushes.Gray);

        Assert.Equal(expectedInitial, initial);
    }
}
