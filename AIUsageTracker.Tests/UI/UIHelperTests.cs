// <copyright file="UIHelperTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows.Media;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class UIHelperTests
{
    [Fact]
    public void GetResourceBrush_WhenResourceMissing_ReturnsFallback()
    {
        var fallback = Brushes.Gray;

        var result = UIHelper.GetResourceBrush("missing-brush-key", fallback);

        Assert.Same(fallback, result);
    }

    [Fact]
    public void GetResourceBrush_WhenResourceMissingAndNoFallback_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => UIHelper.GetResourceBrush("missing-brush-key"));
        Assert.Contains("missing-brush-key", ex.Message, StringComparison.Ordinal);
    }
}

