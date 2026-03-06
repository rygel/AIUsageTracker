using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim;

internal static class ProviderDualWindowPresentationCatalog
{
    public static bool TryGetDualWindowUsedPercentages(ProviderUsage usage, out double hourlyUsed, out double weeklyUsed)
    {
        hourlyUsed = 0;
        weeklyUsed = 0;

        if (usage.Details?.Any() != true)
        {
            return false;
        }

        var windows = usage.Details
            .Where(detail => detail.DetailType == ProviderUsageDetailType.QuotaWindow)
            .ToList();

        if (windows.Count < 2)
        {
            return false;
        }

        var hourlyDetail = windows.FirstOrDefault(detail => detail.WindowKind == WindowKind.Primary)
            ?? windows.FirstOrDefault(detail => detail.WindowKind == WindowKind.Spark);
        var weeklyDetail = windows.FirstOrDefault(detail => detail.WindowKind == WindowKind.Secondary);

        if (hourlyDetail == null || weeklyDetail == null || hourlyDetail == weeklyDetail)
        {
            return false;
        }

        var parsedHourly = UsageMath.GetEffectiveUsedPercent(hourlyDetail, usage.IsQuotaBased);
        var parsedWeekly = UsageMath.GetEffectiveUsedPercent(weeklyDetail, usage.IsQuotaBased);

        if (!parsedHourly.HasValue || !parsedWeekly.HasValue)
        {
            return false;
        }

        hourlyUsed = parsedHourly.Value;
        weeklyUsed = parsedWeekly.Value;
        return true;
    }
}
