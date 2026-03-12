// <copyright file="ProviderSubTrayCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;

namespace AIUsageTracker.UI.Slim;

internal static class ProviderSubTrayCatalog
{
    public static IReadOnlyList<ProviderUsageDetail> GetEligibleDetails(ProviderUsage? usage)
    {
        return GetEligibleDetails(usage, capabilities: null);
    }

    public static IReadOnlyList<ProviderUsageDetail> GetEligibleDetails(
        ProviderUsage? usage,
        AgentProviderCapabilitiesSnapshot? capabilities)
    {
        if (usage?.Details == null)
        {
            return Array.Empty<ProviderUsageDetail>();
        }

        if (ProviderCapabilityCatalog.HasVisibleDerivedProviders(usage.ProviderId ?? string.Empty, capabilities))
        {
            return Array.Empty<ProviderUsageDetail>();
        }

        return usage.Details
            .Where(detail =>
                !string.IsNullOrWhiteSpace(detail.Name) &&
                !detail.Name.StartsWith("[", StringComparison.Ordinal) &&
                IsEligibleDetail(detail))
            .GroupBy(detail => detail.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(detail => detail.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsEligibleDetail(ProviderUsageDetail detail)
    {
        if (string.IsNullOrWhiteSpace(detail.Name))
        {
            return false;
        }

        return detail.DetailType == ProviderUsageDetailType.Model ||
               detail.DetailType == ProviderUsageDetailType.Other;
    }
}
