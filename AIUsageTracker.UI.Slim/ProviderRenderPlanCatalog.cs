// <copyright file="ProviderRenderPlanCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim;

internal static class ProviderRenderPlanCatalog
{
    public static ProviderRenderPlan Build(
        IReadOnlyCollection<ProviderUsage> usages,
        IEnumerable<string>? hiddenProviderItemIds)
    {
        ArgumentNullException.ThrowIfNull(usages);

        if (usages.Count == 0)
        {
            return new ProviderRenderPlan(
                RawCount: 0,
                RenderedCount: 0,
                Message: "No provider data available.",
                Sections: Array.Empty<ProviderSectionLayout>());
        }

        var expandedUsages = ProviderUsageDisplayCatalog.BuildMainWindowUsageList(usages, hiddenProviderItemIds);
        if (expandedUsages.Count == 0)
        {
            return new ProviderRenderPlan(
                RawCount: usages.Count,
                RenderedCount: 0,
                Message: "Data received, but no displayable providers were found.",
                Sections: Array.Empty<ProviderSectionLayout>());
        }

        var sections = ProviderSectionLayoutCatalog.BuildLayouts(expandedUsages);
        return new ProviderRenderPlan(
            RawCount: usages.Count,
            RenderedCount: expandedUsages.Count,
            Message: null,
            Sections: sections);
    }
}
