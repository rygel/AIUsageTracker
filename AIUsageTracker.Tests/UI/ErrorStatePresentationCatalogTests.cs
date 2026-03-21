// <copyright file="ErrorStatePresentationCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class ErrorStatePresentationCatalogTests
{
    [Fact]
    public void Create_WithExistingUsages_PreservesCardsAndUsesWarning()
    {
        var presentation = ErrorStatePresentationCatalog.Create(hasUsages: true);

        Assert.False(presentation.ReplaceProviderCards);
        Assert.Equal(StatusType.Warning, presentation.StatusType);
    }

    [Fact]
    public void Create_WithoutUsages_ReplacesCardsAndUsesError()
    {
        var presentation = ErrorStatePresentationCatalog.Create(hasUsages: false);

        Assert.True(presentation.ReplaceProviderCards);
        Assert.Equal(StatusType.Error, presentation.StatusType);
    }
}
