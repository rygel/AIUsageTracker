// <copyright file="ProviderApiKeyPresentationCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class ProviderApiKeyPresentationCatalogTests
{
    [Theory]
    [InlineData(null, false, "")]
    [InlineData("", false, "")]
    [InlineData("sk-live-1234", false, "sk-live-1234")]
    [InlineData("sk-live-1234", true, "sk-l****1234")]
    [InlineData("short", true, "****")]
    public void GetDisplayApiKey_ReturnsExpectedValue(string? apiKey, bool isPrivacyMode, string expected)
    {
        var result = ProviderApiKeyPresentationCatalog.GetDisplayApiKey(apiKey, isPrivacyMode);

        Assert.Equal(expected, result);
    }
}
