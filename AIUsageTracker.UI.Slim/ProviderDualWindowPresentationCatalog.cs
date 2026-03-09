using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim;

internal sealed record ProviderDualWindowPresentation(
    string PrimaryLabel,
    double PrimaryUsedPercent,
    DateTime? PrimaryResetTime,
    string SecondaryLabel,
    double SecondaryUsedPercent,
    DateTime? SecondaryResetTime);
`n
internal static class ProviderDualWindowPresentationCatalog
{
    public static bool TryGetDualWindowUsedPercentages(ProviderUsage usage, out double hourlyUsed, out double weeklyUsed)
    {
        hourlyUsed = 0;
        weeklyUsed = 0;

        if (!TryGetPresentation(usage, out var presentation))
        {
            return false;
        }

        hourlyUsed = presentation.PrimaryUsedPercent;
        weeklyUsed = presentation.SecondaryUsedPercent;
        return true;
    }
`n
    public static bool TryGetPresentation(ProviderUsage usage, out ProviderDualWindowPresentation presentation)
    {
        presentation = null!;

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

        presentation = new ProviderDualWindowPresentation(
            PrimaryLabel: SimplifyWindowLabel(hourlyDetail.Name, "Primary"),
            PrimaryUsedPercent: parsedHourly.Value,
            PrimaryResetTime: hourlyDetail.NextResetTime,
            SecondaryLabel: SimplifyWindowLabel(weeklyDetail.Name, "Secondary"),
            SecondaryUsedPercent: parsedWeekly.Value,
            SecondaryResetTime: weeklyDetail.NextResetTime);
        return true;
    }
`n
    private static string SimplifyWindowLabel(string? rawLabel, string fallback)
    {
        if (string.IsNullOrWhiteSpace(rawLabel))
        {
            return fallback;
        }

        var label = rawLabel.Trim();
        label = label.Replace(" quota", string.Empty, StringComparison.OrdinalIgnoreCase);
        label = label.Replace(" limit", string.Empty, StringComparison.OrdinalIgnoreCase);
        return string.IsNullOrWhiteSpace(label) ? fallback : label;
    }
}
