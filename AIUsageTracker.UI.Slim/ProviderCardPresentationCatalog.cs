// <copyright file="ProviderCardPresentationCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.UI.Slim;

internal static class ProviderCardPresentationCatalog
{
    public static ProviderCardPresentation Create(
        ProviderUsage usage,
        bool showUsed)
    {
        var providerId = usage.ProviderId ?? string.Empty;
        var description = usage.Description ?? string.Empty;
        var isMissing = description.Contains("not found", StringComparison.OrdinalIgnoreCase);
        var isConsoleCheck = description.Contains("Check Console", StringComparison.OrdinalIgnoreCase);
        var isError = description.Contains("[Error]", StringComparison.OrdinalIgnoreCase);
        var isUnknown = description.Contains("unknown", StringComparison.OrdinalIgnoreCase);
        var isAntigravityParent = ProviderCapabilityCatalog.ShouldRenderAggregateDetailsInMainWindow(providerId);
        var isStatusOnlyProvider = string.Equals(usage.UsageUnit, "Status", StringComparison.OrdinalIgnoreCase);
        var hasDualQuotaBucketPresentation = ProviderDualQuotaBucketPresentationCatalog.TryGetPresentation(usage, out var dualQuotaBucketPresentation);
        var remainingPercent = usage.IsQuotaBased
            ? usage.RequestsPercentage
            : Math.Max(0, 100 - usage.RequestsPercentage);
        var usedPercent = usage.IsQuotaBased
            ? Math.Max(0, 100 - usage.RequestsPercentage)
            : usage.RequestsPercentage;
        var shouldHaveProgress = usage.IsAvailable &&
            !isUnknown &&
            !isAntigravityParent &&
            (usage.RequestsPercentage > 0 || usage.IsQuotaBased) &&
            !isMissing &&
            !isError;

        if (TryCreateSpecialPresentation(
            isMissing,
            isUnknown,
            isError,
            isAntigravityParent,
            isConsoleCheck,
            shouldHaveProgress,
            usedPercent,
            remainingPercent,
            out var specialPresentation))
        {
            return specialPresentation;
        }

        var (statusText, suppressSingleResetTime) = ResolveStatusText(
            usage,
            showUsed,
            description,
            isUnknown,
            isAntigravityParent,
            isStatusOnlyProvider,
            hasDualQuotaBucketPresentation,
            dualQuotaBucketPresentation);

        return CreatePresentation(
            isMissing,
            isUnknown,
            isError,
            isAntigravityParent,
            shouldHaveProgress,
            suppressSingleResetTime,
            usedPercent,
            remainingPercent,
            statusText,
            ProviderCardStatusTone.Secondary);
    }

    private static bool TryCreateSpecialPresentation(
        bool isMissing,
        bool isUnknown,
        bool isError,
        bool isAntigravityParent,
        bool isConsoleCheck,
        bool shouldHaveProgress,
        double usedPercent,
        double remainingPercent,
        out ProviderCardPresentation presentation)
    {
        if (isMissing)
        {
            presentation = CreatePresentation(
                isMissing,
                isUnknown,
                isError,
                isAntigravityParent,
                shouldHaveProgress,
                false,
                usedPercent,
                remainingPercent,
                "Key Missing",
                ProviderCardStatusTone.Missing);
            return true;
        }

        if (isError)
        {
            presentation = CreatePresentation(
                isMissing,
                isUnknown,
                isError,
                isAntigravityParent,
                shouldHaveProgress,
                false,
                usedPercent,
                remainingPercent,
                "Error",
                ProviderCardStatusTone.Error);
            return true;
        }

        if (isConsoleCheck)
        {
            presentation = CreatePresentation(
                isMissing,
                isUnknown,
                isError,
                isAntigravityParent,
                shouldHaveProgress,
                false,
                usedPercent,
                remainingPercent,
                "Check Console",
                ProviderCardStatusTone.Warning);
            return true;
        }

        presentation = null!;
        return false;
    }

    private static (string StatusText, bool SuppressSingleResetTime) ResolveStatusText(
        ProviderUsage usage,
        bool showUsed,
        string description,
        bool isUnknown,
        bool isAntigravityParent,
        bool isStatusOnlyProvider,
        bool hasDualQuotaBucketPresentation,
        ProviderDualQuotaBucketPresentation dualQuotaBucketPresentation)
    {
        if (isAntigravityParent)
        {
            return (string.IsNullOrWhiteSpace(description) ? "Per-model quotas" : description, false);
        }

        if (ShouldUseTooltipOnlyInfoStatus(usage))
        {
            return (GetTooltipOnlyCompactStatus(usage, description), false);
        }

        if (hasDualQuotaBucketPresentation)
        {
            return (BuildDualQuotaBucketStatusText(dualQuotaBucketPresentation, showUsed), true);
        }

        if (!isUnknown && !isStatusOnlyProvider && usage.IsQuotaBased)
        {
            return (
                usage.DisplayAsFraction
                    ? GetQuotaFractionStatusText(usage, showUsed)
                    : GetQuotaPercentStatusText(usage, showUsed),
                false);
        }

        if (!isUnknown && !isStatusOnlyProvider && usage.PlanType == PlanType.Usage && usage.RequestsAvailable > 0)
        {
            var clampedUsedPercent = UsageMath.ClampPercent(usage.RequestsPercentage);
            return (
                showUsed
                    ? $"{clampedUsedPercent:F0}% used"
                    : $"{100.0 - clampedUsedPercent:F0}% remaining",
                false);
        }

        return (description, false);
    }

    private static bool ShouldUseTooltipOnlyInfoStatus(ProviderUsage usage)
    {
        var providerId = usage.ProviderId ?? string.Empty;
        return OpenCodeZenProvider.StaticDefinition.HandledProviderIds.Contains(providerId, StringComparer.OrdinalIgnoreCase);
    }

    private static string GetTooltipOnlyCompactStatus(ProviderUsage usage, string description)
    {
        if (!usage.IsAvailable)
        {
            return description;
        }

        if (usage.PlanType == PlanType.Usage && usage.RequestsUsed >= 0)
        {
            if (string.Equals(usage.UsageUnit, "USD", StringComparison.OrdinalIgnoreCase))
            {
                return $"${usage.RequestsUsed.ToString("F2", CultureInfo.InvariantCulture)}";
            }

            var unit = string.IsNullOrWhiteSpace(usage.UsageUnit) ? string.Empty : $" {usage.UsageUnit}";
            return $"{usage.RequestsUsed.ToString("F2", CultureInfo.InvariantCulture)}{unit}";
        }

        return description;
    }

    private static ProviderCardPresentation CreatePresentation(
        bool isMissing,
        bool isUnknown,
        bool isError,
        bool isAntigravityParent,
        bool shouldHaveProgress,
        bool suppressSingleResetTime,
        double usedPercent,
        double remainingPercent,
        string statusText,
        ProviderCardStatusTone statusTone)
    {
        return new ProviderCardPresentation(
            IsMissing: isMissing,
            IsUnknown: isUnknown,
            IsError: isError,
            IsAntigravityParent: isAntigravityParent,
            ShouldHaveProgress: shouldHaveProgress,
            SuppressSingleResetTime: suppressSingleResetTime,
            UsedPercent: usedPercent,
            RemainingPercent: remainingPercent,
            StatusText: statusText,
            StatusTone: statusTone);
    }

    private static string BuildDualQuotaBucketStatusText(ProviderDualQuotaBucketPresentation presentation, bool showUsed)
    {
        return $"{FormatDualQuotaBucketSegment(presentation.PrimaryLabel, presentation.PrimaryUsedPercent, showUsed)} | " +
               $"{FormatDualQuotaBucketSegment(presentation.SecondaryLabel, presentation.SecondaryUsedPercent, showUsed)}";
    }

    private static string FormatDualQuotaBucketSegment(string label, double usedPercent, bool showUsed)
    {
        var clampedUsed = UsageMath.ClampPercent(usedPercent);
        var clampedRemaining = UsageMath.ClampPercent(100.0 - clampedUsed);
        return showUsed
            ? $"{label} {clampedUsed:F0}% used"
            : $"{label} {clampedRemaining:F0}% remaining";
    }

    private static string GetQuotaFractionStatusText(ProviderUsage usage, bool showUsed)
    {
        if (showUsed)
        {
            return $"{usage.RequestsUsed:N0} / {usage.RequestsAvailable:N0} used";
        }

        var remaining = usage.RequestsAvailable - usage.RequestsUsed;
        return $"{remaining:N0} / {usage.RequestsAvailable:N0} remaining";
    }

    private static string GetQuotaPercentStatusText(ProviderUsage usage, bool showUsed)
    {
        var clampedRemainingPercent = UsageMath.ClampPercent(usage.RequestsPercentage);
        return showUsed
            ? $"{100.0 - clampedRemainingPercent:F0}% used"
            : $"{clampedRemainingPercent:F0}% remaining";
    }
}
