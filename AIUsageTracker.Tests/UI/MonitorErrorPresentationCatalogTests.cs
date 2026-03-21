// <copyright file="MonitorErrorPresentationCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class MonitorErrorPresentationCatalogTests
{
    [Fact]
    public void BuildErrorMessage_NoErrors_UsesFallbackOnly()
    {
        var message = MonitorErrorPresentationCatalog.BuildErrorMessage(
            heading: "Heading",
            fallbackDetails: "Fallback",
            errors: Array.Empty<string>());

        Assert.Equal("Heading\n\nFallback", message);
    }

    [Fact]
    public void BuildErrorMessage_WithErrors_ListsUpToThree()
    {
        var message = MonitorErrorPresentationCatalog.BuildErrorMessage(
            heading: "Heading",
            fallbackDetails: "Fallback",
            errors: new[] { "Error 1", "Error 2", "Error 3", "Error 4" });

        Assert.Contains("Heading", message, StringComparison.Ordinal);
        Assert.Contains("Monitor reported:", message, StringComparison.Ordinal);
        Assert.Contains("- Error 1", message, StringComparison.Ordinal);
        Assert.Contains("- Error 2", message, StringComparison.Ordinal);
        Assert.Contains("- Error 3", message, StringComparison.Ordinal);
        Assert.DoesNotContain("- Error 4", message, StringComparison.Ordinal);
        Assert.EndsWith("Fallback", message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildLaunchErrorMessage_UsesLaunchHeading()
    {
        var message = MonitorErrorPresentationCatalog.BuildLaunchErrorMessage(Array.Empty<string>());

        Assert.StartsWith("Monitor failed to start.", message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildConnectionErrorMessage_UsesConnectionHeading()
    {
        var message = MonitorErrorPresentationCatalog.BuildConnectionErrorMessage(Array.Empty<string>());

        Assert.StartsWith("Cannot connect to Monitor.", message, StringComparison.Ordinal);
    }
}
