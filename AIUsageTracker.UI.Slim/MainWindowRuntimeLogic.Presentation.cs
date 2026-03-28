// <copyright file="MainWindowRuntimeLogic.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Helpers;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.UI.Slim;

internal static partial class MainWindowRuntimeLogic
{
    public static string ResolveDisplayAccountName(
        string providerId,
        string? usageAccountName,
        bool isPrivacyMode)
    {
        if (!(ProviderMetadataCatalog.Find(providerId)?.SupportsAccountIdentity ?? false))
        {
            return string.Empty;
        }

        var accountName = NormalizeIdentity(usageAccountName);

        if (string.IsNullOrWhiteSpace(accountName))
        {
            return string.Empty;
        }

        return isPrivacyMode
            ? PrivacyHelper.MaskAccountIdentifier(accountName)
            : accountName;
    }

    public static ProviderCardPresentation Create(
        ProviderUsage usage,
        bool showUsed,
        double redThreshold = 80)
    {
        var providerId = usage.ProviderId ?? string.Empty;
        // Provider-level IsStale covers providers with old FetchedAt.
        // Detail-level IsStale covers parents that fetch successfully but whose
        // child data is old (e.g. Antigravity "Application not running" — parent
        // refreshes every cycle, but model details are days old).
        var isStale = usage.IsStale;
        var description = usage.Description ?? string.Empty;
        var isMissing = usage.State == ProviderUsageState.Missing;
        var isConsoleCheck = usage.State == ProviderUsageState.ConsoleCheck;
        var isError = usage.State == ProviderUsageState.Error;
        var isUnknown = usage.State == ProviderUsageState.Unknown;
        var isStatusOnlyProvider = usage.IsStatusOnly;
        var hasDualQuotaBucketPresentation = TryGetDualQuotaBucketPresentation(usage, out var dualQuotaBucketPresentation);
        var remainingPercent = usage.RemainingPercent;
        var usedPercent = usage.UsedPercent;
        var shouldHaveProgress = usage.IsAvailable &&
            !isUnknown &&
            !isStatusOnlyProvider &&
            (usage.UsedPercent > 0 || usage.IsQuotaBased) &&
            !isMissing &&
            !isError;

        // HTTP 429 (rate limited) is a known temporary state — show as Warning (orange), not Error (red),
        // so users understand this is transient and not a configuration problem.
        if (usage.HttpStatus == 429)
        {
            var rateLimitText = string.IsNullOrWhiteSpace(description)
                ? "Rate limited — request limit reached"
                : description;

            return CreatePresentation(
                isMissing: false,
                isUnknown: false,
                isError: false,
                shouldHaveProgress: false,
                suppressSingleResetTime: false,
                usedPercent: usedPercent,
                remainingPercent: remainingPercent,
                statusText: rateLimitText,
                statusTone: ProviderCardStatusTone.Warning,
                isStale: isStale);
        }

        if (TryCreateSpecialPresentation(
            isMissing,
            isUnknown,
            isError,
            isConsoleCheck,
            shouldHaveProgress,
            usedPercent,
            remainingPercent,
            description,
            out var specialPresentation))
        {
            return specialPresentation;
        }

        var (statusText, suppressSingleResetTime) = ResolveStatusText(
            usage,
            showUsed,
            description,
            isUnknown,
            isStatusOnlyProvider,
            hasDualQuotaBucketPresentation,
            dualQuotaBucketPresentation);

        return CreatePresentation(
            isMissing,
            isUnknown,
            isError,
            shouldHaveProgress,
            suppressSingleResetTime,
            usedPercent,
            remainingPercent,
            statusText,
            ProviderCardStatusTone.Secondary,
            hasDualQuotaBucketPresentation ? dualQuotaBucketPresentation.PrimaryUsedPercent : (double?)null,
            hasDualQuotaBucketPresentation ? dualQuotaBucketPresentation.SecondaryUsedPercent : (double?)null,
            hasDualQuotaBucketPresentation ? UsageMath.ComputePaceColor(dualQuotaBucketPresentation.PrimaryUsedPercent, dualQuotaBucketPresentation.PrimaryResetTime, dualQuotaBucketPresentation.PrimaryPeriodDuration, redThreshold).ColorPercent : (double?)null,
            hasDualQuotaBucketPresentation ? UsageMath.ComputePaceColor(dualQuotaBucketPresentation.SecondaryUsedPercent, dualQuotaBucketPresentation.SecondaryResetTime, dualQuotaBucketPresentation.SecondaryPeriodDuration, redThreshold).ColorPercent : (double?)null,
            hasDualQuotaBucketPresentation ? dualQuotaBucketPresentation.PrimaryLabel : null,
            hasDualQuotaBucketPresentation ? dualQuotaBucketPresentation.SecondaryLabel : null,
            hasDualQuotaBucketPresentation ? dualQuotaBucketPresentation.PrimaryKind : (WindowKind?)null,
            hasDualQuotaBucketPresentation ? dualQuotaBucketPresentation.SecondaryKind : (WindowKind?)null,
            isStale);
    }

    private static bool TryCreateSpecialPresentation(
        bool isMissing,
        bool isUnknown,
        bool isError,
        bool isConsoleCheck,
        bool shouldHaveProgress,
        double usedPercent,
        double remainingPercent,
        string description,
        out ProviderCardPresentation presentation)
    {
        if (isMissing)
        {
            presentation = CreatePresentation(
                isMissing,
                isUnknown,
                isError,
                shouldHaveProgress,
                false,
                usedPercent,
                remainingPercent,
                description,
                ProviderCardStatusTone.Missing);
            return true;
        }

        if (isError)
        {
            var errorText = description;

            presentation = CreatePresentation(
                isMissing,
                isUnknown,
                isError,
                shouldHaveProgress,
                false,
                usedPercent,
                remainingPercent,
                errorText,
                ProviderCardStatusTone.Error);
            return true;
        }

        if (isConsoleCheck)
        {
            presentation = CreatePresentation(
                isMissing,
                isUnknown,
                isError,
                shouldHaveProgress,
                false,
                usedPercent,
                remainingPercent,
                description,
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
        bool isStatusOnlyProvider,
        bool hasDualQuotaBucketPresentation,
        (string PrimaryLabel, double PrimaryUsedPercent, DateTime? PrimaryResetTime, TimeSpan? PrimaryPeriodDuration, WindowKind PrimaryKind, string SecondaryLabel, double SecondaryUsedPercent, DateTime? SecondaryResetTime, TimeSpan? SecondaryPeriodDuration, WindowKind SecondaryKind) dualQuotaBucketPresentation)
    {
        // Reuse the already-resolved definition from the caller instead of a second catalog lookup.
        var def = ProviderMetadataCatalog.Find(usage.ProviderId ?? string.Empty);
        if (def?.IsTooltipOnly ?? false)
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
            var clampedUsedPercent = UsageMath.ClampPercent(usage.UsedPercent);
            return (
                showUsed
                    ? $"{clampedUsedPercent:F0}% used"
                    : $"{100.0 - clampedUsedPercent:F0}% remaining",
                false);
        }

        return (description, false);
    }

    private static string GetTooltipOnlyCompactStatus(ProviderUsage usage, string description)
    {
        if (!usage.IsAvailable)
        {
            return description;
        }

        if (usage.PlanType == PlanType.Usage && usage.RequestsUsed >= 0)
        {
            if (usage.IsCurrencyUsage)
            {
                return $"${usage.RequestsUsed.ToString("F2", CultureInfo.InvariantCulture)}";
            }

            return usage.RequestsUsed.ToString("F2", CultureInfo.InvariantCulture);
        }

        return description;
    }

    private static ProviderCardPresentation CreatePresentation(
        bool isMissing,
        bool isUnknown,
        bool isError,
        bool shouldHaveProgress,
        bool suppressSingleResetTime,
        double usedPercent,
        double remainingPercent,
        string statusText,
        ProviderCardStatusTone statusTone,
        double? dualBucketPrimaryUsed = null,
        double? dualBucketSecondaryUsed = null,
        double? dualBucketPrimaryColorPercent = null,
        double? dualBucketSecondaryColorPercent = null,
        string? dualBucketPrimaryLabel = null,
        string? dualBucketSecondaryLabel = null,
        WindowKind? dualBucketPrimaryKind = null,
        WindowKind? dualBucketSecondaryKind = null,
        bool isStale = false)
    {
        return new ProviderCardPresentation(
            IsMissing: isMissing,
            IsUnknown: isUnknown,
            IsError: isError,
            ShouldHaveProgress: shouldHaveProgress,
            SuppressSingleResetTime: suppressSingleResetTime,
            UsedPercent: usedPercent,
            RemainingPercent: remainingPercent,
            StatusText: statusText,
            StatusTone: statusTone,
            DualBucketPrimaryUsed: dualBucketPrimaryUsed,
            DualBucketSecondaryUsed: dualBucketSecondaryUsed,
            DualBucketPrimaryColorPercent: dualBucketPrimaryColorPercent,
            DualBucketSecondaryColorPercent: dualBucketSecondaryColorPercent,
            DualBucketPrimaryLabel: dualBucketPrimaryLabel,
            DualBucketSecondaryLabel: dualBucketSecondaryLabel,
            DualBucketPrimaryKind: dualBucketPrimaryKind,
            DualBucketSecondaryKind: dualBucketSecondaryKind,
            IsStale: isStale);
    }

    private static string BuildDualQuotaBucketStatusText(
        (string PrimaryLabel, double PrimaryUsedPercent, DateTime? PrimaryResetTime, TimeSpan? PrimaryPeriodDuration, WindowKind PrimaryKind, string SecondaryLabel, double SecondaryUsedPercent, DateTime? SecondaryResetTime, TimeSpan? SecondaryPeriodDuration, WindowKind SecondaryKind) presentation,
        bool showUsed)
    {
        return $"{FormatDualQuotaBucketSegment(presentation.PrimaryLabel, presentation.PrimaryUsedPercent, showUsed)} | " +
               $"{FormatDualQuotaBucketSegment(presentation.SecondaryLabel, presentation.SecondaryUsedPercent, showUsed)}";
    }

    internal static string BuildSingleDualQuotaStatusText(
        ProviderCardPresentation presentation,
        bool showUsed,
        DualQuotaSingleBarMode mode)
    {
        if (!presentation.HasDualBuckets)
        {
            return presentation.StatusText;
        }

        var usePrimary = ShouldUsePrimaryDualBucket(presentation, mode);
        var label = usePrimary ? presentation.DualBucketPrimaryLabel : presentation.DualBucketSecondaryLabel;
        var usedPercent = usePrimary ? presentation.DualBucketPrimaryUsed : presentation.DualBucketSecondaryUsed;

        if (string.IsNullOrWhiteSpace(label) || !usedPercent.HasValue)
        {
            return presentation.StatusText;
        }

        return FormatDualQuotaBucketSegment(label, usedPercent.Value, showUsed);
    }

    internal static bool ShouldUsePrimaryDualBucket(
        ProviderCardPresentation presentation,
        DualQuotaSingleBarMode mode)
    {
        if (mode == DualQuotaSingleBarMode.Rolling)
        {
            if (presentation.DualBucketPrimaryKind == WindowKind.Rolling)
            {
                return true;
            }

            if (presentation.DualBucketSecondaryKind == WindowKind.Rolling)
            {
                return false;
            }
        }
        else
        {
            if (presentation.DualBucketPrimaryKind == WindowKind.Burst)
            {
                return true;
            }

            if (presentation.DualBucketSecondaryKind == WindowKind.Burst)
            {
                return false;
            }
        }

        return true;
    }

    internal static WindowKind? GetPreferredDualBucketKind(
        ProviderCardPresentation presentation,
        DualQuotaSingleBarMode mode)
    {
        if (!presentation.HasDualBuckets)
        {
            return null;
        }

        return ShouldUsePrimaryDualBucket(presentation, mode)
            ? presentation.DualBucketPrimaryKind
            : presentation.DualBucketSecondaryKind;
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
        return showUsed
            ? $"{UsageMath.ClampPercent(usage.UsedPercent):F0}% used"
            : $"{UsageMath.ClampPercent(usage.RemainingPercent):F0}% remaining";
    }

    public static bool GetIsCollapsed(AppPreferences preferences, string providerId)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        return ShouldUseSharedCollapsePreference(providerId) && preferences.IsAntigravityCollapsed;
    }

    public static void SetIsCollapsed(AppPreferences preferences, string providerId, bool isCollapsed)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        if (!ShouldUseSharedCollapsePreference(providerId))
        {
            return;
        }

        preferences.IsAntigravityCollapsed = isCollapsed;
    }

    internal static bool TryGetDualQuotaBucketPresentation(
        ProviderUsage usage,
        out (string PrimaryLabel, double PrimaryUsedPercent, DateTime? PrimaryResetTime, TimeSpan? PrimaryPeriodDuration, WindowKind PrimaryKind, string SecondaryLabel, double SecondaryUsedPercent, DateTime? SecondaryResetTime, TimeSpan? SecondaryPeriodDuration, WindowKind SecondaryKind) presentation)
    {
        presentation = default;

        // Dual-bar data comes from companion WindowCards: flat ProviderUsage cards
        // with WindowKind = Burst or Rolling, populated by GroupedUsageDisplayAdapter
        // from ProviderDetails. We need exactly one Burst card and one Rolling card.
        var windowCards = usage.WindowCards;
        if (windowCards == null || windowCards.Count == 0)
        {
            return false;
        }

        var burstCard = windowCards.FirstOrDefault(c => c.WindowKind == WindowKind.Burst);
        var rollingCard = windowCards.FirstOrDefault(c => c.WindowKind == WindowKind.Rolling);

        if (burstCard == null || rollingCard == null)
        {
            return false;
        }

        // Resolve labels: prefer the card's Name; fall back to the declared window label.
        if (!ProviderMetadataCatalog.TryGet(usage.ProviderId ?? string.Empty, out var definition))
        {
            return false;
        }

        var burstWindow = definition.QuotaWindows.FirstOrDefault(w => w.Kind == WindowKind.Burst);
        var rollingWindow = definition.QuotaWindows.FirstOrDefault(w => w.Kind == WindowKind.Rolling);

        var burstLabel = burstCard.Name ?? burstWindow?.DetailName ?? "Burst";
        var rollingLabel = rollingCard.Name ?? rollingWindow?.DetailName ?? "Rolling";

        // The shorter window (Burst) is primary (top bar), Rolling is secondary (bottom bar).
        presentation = (
            PrimaryLabel: burstLabel,
            PrimaryUsedPercent: burstCard.UsedPercent,
            PrimaryResetTime: burstCard.NextResetTime,
            PrimaryPeriodDuration: burstWindow?.PeriodDuration,
            PrimaryKind: WindowKind.Burst,
            SecondaryLabel: rollingLabel,
            SecondaryUsedPercent: rollingCard.UsedPercent,
            SecondaryResetTime: rollingCard.NextResetTime,
            SecondaryPeriodDuration: rollingWindow?.PeriodDuration,
            SecondaryKind: WindowKind.Rolling);

        return true;
    }

    private static int GetDeclaredWindowOrder(QuotaWindowDefinition declaredWindow, IReadOnlyList<QuotaWindowDefinition> windows)
    {
        for (var index = 0; index < windows.Count; index++)
        {
            if (windows[index].Kind == declaredWindow.Kind)
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    private static bool ShouldUseSharedCollapsePreference(string providerId) => false;

    private static string FormatPercentage(
        double percentage,
        PercentageValueSemantic semantic,
        int decimalPlaces,
        bool includeComplement)
    {
        var format = $"F{Math.Max(0, decimalPlaces)}";
        var value = UsageMath.ClampPercent(percentage).ToString(format, CultureInfo.InvariantCulture);
        var semanticLabel = semantic switch
        {
            PercentageValueSemantic.Used => "used",
            PercentageValueSemantic.Remaining => "remaining",
            _ => string.Empty,
        };

        if (string.IsNullOrWhiteSpace(semanticLabel))
        {
            return $"{value}%";
        }

        if (!includeComplement)
        {
            return $"{value}% {semanticLabel}";
        }

        var complementValue = UsageMath.ClampPercent(100.0 - percentage).ToString(format, CultureInfo.InvariantCulture);
        var complementLabel = semantic == PercentageValueSemantic.Used ? "remaining" : "used";
        return $"{value}% {semanticLabel} ({complementValue}% {complementLabel})";
    }

    private static string NormalizeIdentity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        return normalized is "Unknown" or "User"
            ? string.Empty
            : normalized;
    }

}

internal sealed record ProviderCardPresentation(
    bool IsMissing,
    bool IsUnknown,
    bool IsError,
    bool ShouldHaveProgress,
    bool SuppressSingleResetTime,
    double UsedPercent,
    double RemainingPercent,
    string StatusText,
    ProviderCardStatusTone StatusTone,
    double? DualBucketPrimaryUsed = null,
    double? DualBucketSecondaryUsed = null,
    double? DualBucketPrimaryColorPercent = null,
    double? DualBucketSecondaryColorPercent = null,
    string? DualBucketPrimaryLabel = null,
    string? DualBucketSecondaryLabel = null,
    WindowKind? DualBucketPrimaryKind = null,
    WindowKind? DualBucketSecondaryKind = null,
    bool IsStale = false)
{
    public bool HasDualBuckets => this.DualBucketPrimaryUsed.HasValue;
}
