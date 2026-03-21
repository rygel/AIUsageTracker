// <copyright file="MonitorStartupPresentationCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class MonitorStartupPresentationCatalogTests
{
    [Theory]
    [InlineData(false, 0, true)]
    [InlineData(false, 1, true)]
    [InlineData(false, 2, false)]
    [InlineData(true, 0, false)]
    public void ShouldShowConnectionFailureState_UsesCatalogRule(
        bool hasUsages,
        int providersListChildCount,
        bool expected)
    {
        var shouldShow = MonitorStartupPresentationCatalog.ShouldShowConnectionFailureState(
            hasUsages,
            providersListChildCount);

        Assert.Equal(expected, shouldShow);
    }
}
