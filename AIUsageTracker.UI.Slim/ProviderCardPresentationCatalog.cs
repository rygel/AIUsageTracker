using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.UI.Slim;

internal enum ProviderCardStatusTone
{
    Secondary,
    Missing,
    Warning,
    Error
}

internal sealed record ProviderCardPresentation(
    bool IsMissing,
    bool IsUnknown,
    bool IsError,
    bool IsAntigravityParent,
    bool ShouldHaveProgress,
    bool SuppressSingleResetTime,
    double UsedPercent,
    double RemainingPercent,
    string StatusText,
    ProviderCardStatusTone StatusTone);

internal static class ProviderCardPresentationCatalog
{
    public static ProviderCardPresentation Create(ProviderUsage usage, bool showUsed)
    {
        var providerId = usage.ProviderId ?? string.Empty;
        var description = usage.Description ?? string.Empty;

        var isMissing = description.Contains("not found", StringComparison.OrdinalIgnoreCase);
        var isConsoleCheck = description.Contains("Check Console", StringComparison.OrdinalIgnoreCase);
        var isError = description.Contains("[Error]", StringComparison.OrdinalIgnoreCase);
        var isUnknown = description.Contains("unknown", StringComparison.OrdinalIgnoreCase);
        var isAntigravityParent = ProviderMetadataCatalog.IsAggregateParentProviderId(providerId);
        var isStatusOnlyProvider = string.Equals(usage.UsageUnit, "Status", StringComparison.OrdinalIgnoreCase);
        var hasDualWindowPresentation = ProviderDualWindowPresentationCatalog.TryGetPresentation(usage, out var dualWindowPresentation);

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

        if (isMissing)
        {
            return CreatePresentation(isMissing, isUnknown, isError, isAntigravityParent, shouldHaveProgress, false, usedPercent, remainingPercent, "Key Missing", ProviderCardStatusTone.Missing);
        }

        if (isError)
        {
            return CreatePresentation(isMissing, isUnknown, isError, isAntigravityParent, shouldHaveProgress, false, usedPercent, remainingPercent, "Error", ProviderCardStatusTone.Error);
        }

        if (isConsoleCheck)
        {
            return CreatePresentation(isMissing, isUnknown, isError, isAntigravityParent, shouldHaveProgress, false, usedPercent, remainingPercent, "Check Console", ProviderCardStatusTone.Warning);
        }

        var statusText = description;
        var suppressSingleResetTime = false;
        if (isAntigravityParent)
        {
            statusText = string.IsNullOrWhiteSpace(description) ? "Per-model quotas" : description;
        }
        else if (hasDualWindowPresentation)
        {
            statusText = BuildDualWindowStatusText(dualWindowPresentation, showUsed);
            suppressSingleResetTime = true;
        }
        else if (!isUnknown && !isStatusOnlyProvider && usage.IsQuotaBased)
        {
            statusText = usage.DisplayAsFraction
                ? GetQuotaFractionStatusText(usage, showUsed)
                : GetQuotaPercentStatusText(usage, showUsed);
        }
        else if (!isUnknown && !isStatusOnlyProvider && usage.PlanType == PlanType.Usage && usage.RequestsAvailable > 0)
        {
            var clampedUsedPercent = UsageMath.ClampPercent(usage.RequestsPercentage);
            statusText = showUsed
                ? $"{clampedUsedPercent:F0}% used"
                : $"{(100.0 - clampedUsedPercent):F0}% remaining";
        }

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

    private static string BuildDualWindowStatusText(ProviderDualWindowPresentation presentation, bool showUsed)
    {
        return $"{FormatDualWindowSegment(presentation.PrimaryLabel, presentation.PrimaryUsedPercent, showUsed)} | {FormatDualWindowSegment(presentation.SecondaryLabel, presentation.SecondaryUsedPercent, showUsed)}";
    }

    private static string FormatDualWindowSegment(string label, double usedPercent, bool showUsed)
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
            ? $"{(100.0 - clampedRemainingPercent):F0}% used"
            : $"{clampedRemainingPercent:F0}% remaining";
    }
}
