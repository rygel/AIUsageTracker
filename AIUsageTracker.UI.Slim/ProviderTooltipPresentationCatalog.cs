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
            foreach (var detail in usage.Details.OrderBy(GetDetailDisplayName, StringComparer.OrdinalIgnoreCase))
            {
                tooltipBuilder.AppendLine($"  {GetDetailDisplayName(detail)}: {detail.Used}");
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
}
