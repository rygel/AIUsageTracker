// <copyright file="ProviderDualQuotaBucketPresentationCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim;

internal static class ProviderDualQuotaBucketPresentationCatalog
{
    public static bool TryGetDualQuotaBucketUsedPercentages(ProviderUsage usage, out double primaryUsed, out double secondaryUsed)
    {
        primaryUsed = 0;
        secondaryUsed = 0;

        if (!TryGetPresentation(usage, out var presentation))
        {
            return false;
        }

        primaryUsed = presentation.PrimaryUsedPercent;
        secondaryUsed = presentation.SecondaryUsedPercent;
        return true;
    }

    public static bool TryGetPresentation(ProviderUsage usage, out ProviderDualQuotaBucketPresentation presentation)
    {
        presentation = null!;

        if (usage.Details?.Any() != true)
        {
            return false;
        }

        var quotaBuckets = usage.Details
            .Where(detail => detail.DetailType == ProviderUsageDetailType.QuotaWindow)
            .ToList();

        if (quotaBuckets.Count < 2)
        {
            return false;
        }

        var primaryDetail = quotaBuckets.FirstOrDefault(detail => detail.QuotaBucketKind == WindowKind.Primary) ??
                            quotaBuckets.FirstOrDefault(detail => detail.QuotaBucketKind == WindowKind.Spark);
        var secondaryDetail = quotaBuckets.FirstOrDefault(detail => detail.QuotaBucketKind == WindowKind.Secondary);

        if (primaryDetail == null || secondaryDetail == null || primaryDetail == secondaryDetail)
        {
            return false;
        }

        var parsedPrimary = UsageMath.GetEffectiveUsedPercent(primaryDetail, usage.IsQuotaBased);
        var parsedSecondary = UsageMath.GetEffectiveUsedPercent(secondaryDetail, usage.IsQuotaBased);

        if (!parsedPrimary.HasValue || !parsedSecondary.HasValue)
        {
            return false;
        }

        presentation = new ProviderDualQuotaBucketPresentation(
            PrimaryLabel: SimplifyQuotaBucketLabel(primaryDetail.Name, "Primary"),
            PrimaryUsedPercent: parsedPrimary.Value,
            PrimaryResetTime: primaryDetail.NextResetTime,
            SecondaryLabel: SimplifyQuotaBucketLabel(secondaryDetail.Name, "Secondary"),
            SecondaryUsedPercent: parsedSecondary.Value,
            SecondaryResetTime: secondaryDetail.NextResetTime);
        return true;
    }

    private static string SimplifyQuotaBucketLabel(string? rawLabel, string fallback)
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
