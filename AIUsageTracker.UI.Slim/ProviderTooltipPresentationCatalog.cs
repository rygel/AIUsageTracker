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
        if (usage.Details?.Any() == true)
        {
            var tooltipBuilder = new StringBuilder();
            tooltipBuilder.AppendLine(friendlyName);
            tooltipBuilder.AppendLine($"Status: {(usage.IsAvailable ? "Active" : "Inactive")}");
            if (!string.IsNullOrEmpty(usage.Description))
            {
                tooltipBuilder.AppendLine($"Description: {usage.Description}");
            }

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

            return tooltipBuilder.ToString().Trim();
        }

        if (!string.IsNullOrEmpty(usage.AuthSource))
        {
            return $"{friendlyName}\nSource: {usage.AuthSource}";
        }

        return null;
    }

    private static string GetDetailDisplayName(ProviderUsageDetail detail)
    {
        return detail.Name;
    }

    private static string ResolveDetailDisplayValue(ProviderUsageDetail detail)
    {
        if (!string.IsNullOrWhiteSpace(detail.Used))
        {
            return detail.Used;
        }

        if (!string.IsNullOrWhiteSpace(detail.Description))
        {
            return detail.Description;
        }

        return string.Empty;
    }

    private static int GetDetailSortOrder(ProviderUsageDetail detail)
    {
        return (detail.DetailType, detail.QuotaBucketKind) switch
        {
            (ProviderUsageDetailType.QuotaWindow, WindowKind.Primary) => 0,
            (ProviderUsageDetailType.QuotaWindow, WindowKind.Secondary) => 1,
            (ProviderUsageDetailType.QuotaWindow, WindowKind.Spark) => 2,
            (ProviderUsageDetailType.QuotaWindow, _) => 3,
            (ProviderUsageDetailType.Model, _) => 3,
            (ProviderUsageDetailType.Credit, _) => 4,
            _ => 5,
        };
    }
}
