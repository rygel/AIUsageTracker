// <copyright file="ProviderVisualCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Tests.UI;

public class ProviderVisualCatalogTests
{
    [Theory]
    [InlineData("codex.spark", "codex.spark", "openai")]
    [InlineData("antigravity.claude-opus", "antigravity", "google")]
    [InlineData("minimax-io", "minimax-io", "minimax")]
    [InlineData("zai-coding-plan", "zai-coding-plan", "zai")]
    public void GetIconAssetName_UsesProviderOwnerIdentity(
        string providerId,
        string expectedOwnerProviderId,
        string expectedAssetName)
    {
        Assert.Equal(expectedOwnerProviderId, ProviderMetadataCatalog.GetProviderOwnerId(providerId));
        Assert.Equal(expectedAssetName, ProviderMetadataCatalog.GetIconAssetName(providerId));
    }
}
