// <copyright file="ProviderTooltipPresentationCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text;

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim;

internal static class ProviderTooltipPresentationCatalog
{
    public static string? BuildContent(ProviderUsage usage, string friendlyName)
    {
        var tooltipBuilder = new StringBuilder();
        tooltipBuilder.AppendLine(friendlyName);
        tooltipBuilder.AppendLine($"Status: {(usage.IsAvailable ? "Active" : "Inactive")}");
        if (!string.IsNullOrEmpty(usage.Description))
        {
            tooltipBuilder.AppendLine($"Description: {usage.Description}");
        }

        if (usage.Details?.Any() == true)
        {
            tooltipBuilder.AppendLine();
            tooltipBuilder.AppendLine("Rate Limits:");
            foreach (var detail in usage.Details
                         .OrderBy(GetDetailSortOrder)
                         .ThenBy(GetDetailDisplayName, StringComparer.OrdinalIgnoreCase))
            {
                var detailValue = ResolveDetailDisplayValue(detail);
                if (string.IsNullOrWhiteSpace(detailValue))
                {
                    continue;
                }

                tooltipBuilder.AppendLine($"  {GetDetailDisplayName(detail)}: {detailValue}");
            }
        }

        if (!string.IsNullOrEmpty(usage.AuthSource))
        {
            tooltipBuilder.AppendLine();
            tooltipBuilder.AppendLine($"Source: {usage.AuthSource}");
        }

        var result = tooltipBuilder.ToString().Trim();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static string GetDetailDisplayName(ProviderUsageDetail detail)
    {
        return detail.Name;
    }

    private static string ResolveDetailDisplayValue(ProviderUsageDetail detail)
    {
        return ProviderUsageDetailValuePresentationCatalog.GetStoredDisplayText(detail);
    }

    private static int GetDetailSortOrder(ProviderUsageDetail detail)
    {
        return (detail.DetailType, detail.QuotaBucketKind) switch
        {
            (ProviderUsageDetailType.QuotaWindow, WindowKind.Burst) => 0,
            (ProviderUsageDetailType.QuotaWindow, WindowKind.Rolling) => 1,
            (ProviderUsageDetailType.QuotaWindow, WindowKind.ModelSpecific) => 2,
            (ProviderUsageDetailType.QuotaWindow, _) => 3,
            (ProviderUsageDetailType.Model, _) => 3,
            (ProviderUsageDetailType.Credit, _) => 4,
            _ => 5,
        };
    }
}
