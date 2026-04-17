// <copyright file="WebProviderDisplayNameMapper.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Web.Services;

internal static class WebProviderDisplayNameMapper
{
    public static void Apply<T>(IReadOnlyList<T> results)
    {
        switch (results)
        {
            case IReadOnlyList<ProviderInfo> providers:
                ApplyProviderInfoDisplayNames(providers);
                break;
            case IReadOnlyList<ChartDataPoint> points:
                ApplyChartDataDisplayNames(points);
                break;
            case IReadOnlyList<ResetEvent> resetEvents:
                ApplyResetEventDisplayNames(resetEvents);
                break;
        }
    }

    private static void ApplyProviderInfoDisplayNames(IEnumerable<ProviderInfo> providers)
    {
        foreach (var provider in providers)
        {
            provider.ProviderName = provider.ProviderName ?? ProviderMetadataCatalog.GetConfiguredDisplayName(provider.ProviderId ?? string.Empty);
        }
    }

    private static void ApplyChartDataDisplayNames(IEnumerable<ChartDataPoint> points)
    {
        foreach (var point in points)
        {
            point.ProviderName = point.ProviderName ?? ProviderMetadataCatalog.GetConfiguredDisplayName(point.ProviderId ?? string.Empty);
        }
    }

    private static void ApplyResetEventDisplayNames(IEnumerable<ResetEvent> resetEvents)
    {
        foreach (var resetEvent in resetEvents)
        {
            resetEvent.ProviderName = resetEvent.ProviderName ?? ProviderMetadataCatalog.GetConfiguredDisplayName(resetEvent.ProviderId ?? string.Empty);
        }
    }
}
