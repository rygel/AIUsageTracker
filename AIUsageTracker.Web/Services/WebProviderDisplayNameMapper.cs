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
            provider.ProviderName = ProviderMetadataCatalog.GetDisplayName(provider.ProviderId, provider.ProviderName);
        }
    }

    private static void ApplyChartDataDisplayNames(IEnumerable<ChartDataPoint> points)
    {
        foreach (var point in points)
        {
            point.ProviderName = ProviderMetadataCatalog.GetDisplayName(point.ProviderId, point.ProviderName);
        }
    }

    private static void ApplyResetEventDisplayNames(IEnumerable<ResetEvent> resetEvents)
    {
        foreach (var resetEvent in resetEvents)
        {
            resetEvent.ProviderName = ProviderMetadataCatalog.GetDisplayName(resetEvent.ProviderId, resetEvent.ProviderName);
        }
    }
}
