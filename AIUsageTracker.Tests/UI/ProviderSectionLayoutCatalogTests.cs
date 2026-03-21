// <copyright file="ProviderSectionLayoutCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class ProviderSectionLayoutCatalogTests
{
    [Fact]
    public void BuildLayouts_EmptyInput_ReturnsEmpty()
    {
        var layouts = MainWindowRuntimeLogic.BuildProviderSectionLayouts(Array.Empty<ProviderUsage>());

        Assert.Empty(layouts);
    }

    [Fact]
    public void BuildLayouts_GroupsByContiguousQuotaState()
    {
        var usages = new[]
        {
            new ProviderUsage { ProviderId = "p1", IsQuotaBased = true },
            new ProviderUsage { ProviderId = "p2", IsQuotaBased = true },
            new ProviderUsage { ProviderId = "p3", IsQuotaBased = false },
            new ProviderUsage { ProviderId = "p4", IsQuotaBased = false },
            new ProviderUsage { ProviderId = "p5", IsQuotaBased = true },
        };

        var layouts = MainWindowRuntimeLogic.BuildProviderSectionLayouts(usages);

        Assert.Equal(3, layouts.Count);

        Assert.True(layouts[0].IsQuotaBased);
        Assert.Equal("PlansAndQuotas", layouts[0].SectionKey);
        Assert.Equal(new[] { "p1", "p2" }, layouts[0].Usages.Select(usage => usage.ProviderId));

        Assert.False(layouts[1].IsQuotaBased);
        Assert.Equal("PayAsYouGo", layouts[1].SectionKey);
        Assert.Equal(new[] { "p3", "p4" }, layouts[1].Usages.Select(usage => usage.ProviderId));

        Assert.True(layouts[2].IsQuotaBased);
        Assert.Equal("PlansAndQuotas", layouts[2].SectionKey);
        Assert.Equal(new[] { "p5" }, layouts[2].Usages.Select(usage => usage.ProviderId));
    }
}
