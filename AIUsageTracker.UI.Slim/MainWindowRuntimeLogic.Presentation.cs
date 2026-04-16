// <copyright file="MainWindowRuntimeLogic.Presentation.cs" company="AIUsageTracker">
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
        bool isPrivacyMode,
        ProviderDefinition? definition = null)
    {
        var resolvedDefinition = definition ?? ProviderMetadataCatalog.Find(providerId);
        if (!(resolvedDefinition?.SupportsAccountIdentity ?? false))
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
        bool enablePaceAdjustment = false)
    {
        var isStale = usage.IsStale;
        var description = usage.Description ?? string.Empty;
        var isMissing = usage.State == ProviderUsageState.Missing;
        var isConsoleCheck = usage.State == ProviderUsageState.ConsoleCheck;
        var isError = usage.State == ProviderUsageState.Error;
        var isUnknown = usage.State == ProviderUsageState.Unknown;
        var isExpired = usage.State == ProviderUsageState.Expired;
        var isStatusOnlyProvider = usage.IsStatusOnly;
        var remainingPercent = usage.RemainingPercent;
        var usedPercent = usage.UsedPercent;
        var shouldHaveProgress = usage.IsAvailable &&
            !isUnknown &&
            !isStatusOnlyProvider &&
            (usage.UsedPercent > 0 || usage.IsQuotaBased) &&
            !isMissing &&
            !isError;

        // Always compute pace — used for bar color and badge regardless of card layout.
        var paceColor = UsageMath.ComputePaceColor(
            usedPercent,
            usage.NextResetTime,
            usage.PeriodDuration,
            enablePaceAdjustment);

        // HTTP 429 (rate limited) is a known temporary state — show as Warning (orange), not Error (red),
        // so users understand this is transient and not a configuration problem.
        if (usage.HttpStatus == 429)
        {
            var rateLimitText = string.IsNullOrWhiteSpace(description)
                ? "Rate limited — request limit reached"
                : description;

            return new ProviderCardPresentation(
                IsMissing: false,
                IsUnknown: false,
                IsError: false,
                ShouldHaveProgress: false,
                SuppressSingleResetTime: false,
                UsedPercent: usedPercent,
                RemainingPercent: remainingPercent,
                StatusText: rateLimitText,
                StatusTone: ProviderCardStatusTone.Warning,
                PaceColor: paceColor,
                IsStale: isStale);
        }

        if (TryCreateSpecialPresentation(
            isMissing,
            isUnknown,
            isError,
            isConsoleCheck,
            isExpired,
            shouldHaveProgress,
            usedPercent,
            remainingPercent,
            description,
            paceColor,
            isStale,
            out var specialPresentation))
        {
            return specialPresentation;
        }

        var dualBar = TryBuildDualBarData(usage, enablePaceAdjustment);

        var (statusText, suppressSingleResetTime) = ResolveStatusText(
            usage,
            showUsed,
            description,
            isUnknown,
            isStatusOnlyProvider,
            dualBar);

        return new ProviderCardPresentation(
            IsMissing: isMissing,
            IsUnknown: isUnknown,
            IsError: isError,
            ShouldHaveProgress: shouldHaveProgress,
            SuppressSingleResetTime: suppressSingleResetTime,
            UsedPercent: usedPercent,
            RemainingPercent: remainingPercent,
            StatusText: statusText,
            StatusTone: ProviderCardStatusTone.Secondary,
            PaceColor: paceColor,
            DualBar: dualBar,
            IsStale: isStale);
    }

    private static bool TryCreateSpecialPresentation(
        bool isMissing,
        bool isUnknown,
        bool isError,
        bool isConsoleCheck,
        bool isExpired,
        bool shouldHaveProgress,
        double usedPercent,
        double remainingPercent,
        string description,
        PaceColorResult paceColor,
        bool isStale,
        out ProviderCardPresentation presentation)
    {
        if (isMissing)
        {
            presentation = new ProviderCardPresentation(
                IsMissing: isMissing,
                IsUnknown: isUnknown,
                IsError: isError,
                ShouldHaveProgress: shouldHaveProgress,
                SuppressSingleResetTime: false,
                UsedPercent: usedPercent,
                RemainingPercent: remainingPercent,
                StatusText: description,
                StatusTone: ProviderCardStatusTone.Missing,
                PaceColor: paceColor,
                IsStale: isStale);
            return true;
        }

        if (isExpired)
        {
            var expiredText = string.IsNullOrWhiteSpace(description)
                ? "Subscription expired"
                : description;

            presentation = new ProviderCardPresentation(
                IsMissing: false,
                IsUnknown: false,
                IsError: false,
                ShouldHaveProgress: false,
                SuppressSingleResetTime: false,
                UsedPercent: usedPercent,
                RemainingPercent: remainingPercent,
                StatusText: expiredText,
                StatusTone: ProviderCardStatusTone.Warning,
                PaceColor: paceColor,
                IsExpired: true,
                IsStale: isStale);
            return true;
        }

        if (isError)
        {
            presentation = new ProviderCardPresentation(
                IsMissing: isMissing,
                IsUnknown: isUnknown,
                IsError: isError,
                ShouldHaveProgress: shouldHaveProgress,
                SuppressSingleResetTime: false,
                UsedPercent: usedPercent,
                RemainingPercent: remainingPercent,
                StatusText: description,
                StatusTone: ProviderCardStatusTone.Error,
                PaceColor: paceColor,
                IsStale: isStale);
            return true;
        }

        if (isConsoleCheck)
        {
            presentation = new ProviderCardPresentation(
                IsMissing: isMissing,
                IsUnknown: isUnknown,
                IsError: isError,
                ShouldHaveProgress: shouldHaveProgress,
                SuppressSingleResetTime: false,
                UsedPercent: usedPercent,
                RemainingPercent: remainingPercent,
                StatusText: description,
                StatusTone: ProviderCardStatusTone.Warning,
                PaceColor: paceColor,
                IsStale: isStale);
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
        DualBarData? dualBar)
    {
        if (usage.IsTooltipOnly)
        {
            return (GetTooltipOnlyCompactStatus(usage, description), false);
        }

        if (dualBar != null)
        {
            return (BuildDualQuotaBucketStatusText(dualBar, showUsed), true);
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

    private static string BuildDualQuotaBucketStatusText(DualBarData dualBar, bool showUsed)
    {
        return $"{FormatDualQuotaBucketSegment(dualBar.Primary.Label, dualBar.Primary.UsedPercent, showUsed)} | " +
               $"{FormatDualQuotaBucketSegment(dualBar.Secondary.Label, dualBar.Secondary.UsedPercent, showUsed)}";
    }

    internal static string BuildSingleDualQuotaStatusText(
        ProviderCardPresentation presentation,
        bool showUsed,
        DualQuotaSingleBarMode mode)
    {
        if (presentation.DualBar == null)
        {
            return presentation.StatusText;
        }

        var bar = mode == DualQuotaSingleBarMode.Burst
            ? presentation.DualBar.Primary
            : presentation.DualBar.Secondary;

        if (string.IsNullOrWhiteSpace(bar.Label))
        {
            return presentation.StatusText;
        }

        return FormatDualQuotaBucketSegment(bar.Label, bar.UsedPercent, showUsed);
    }

    internal static DualBarData? TryBuildDualBarData(ProviderUsage usage, bool enablePaceAdjustment)
    {
        var windowCards = usage.WindowCards;
        if (windowCards == null || windowCards.Count == 0)
        {
            return null;
        }

        var burstCard = windowCards.FirstOrDefault(c => c.WindowKind == WindowKind.Burst);
        var rollingCard = windowCards.FirstOrDefault(c => c.WindowKind == WindowKind.Rolling);

        if (burstCard == null || rollingCard == null)
        {
            return null;
        }

        if (!ProviderMetadataCatalog.TryGet(usage.ProviderId ?? string.Empty, out var definition))
        {
            return null;
        }

        var burstWindow = definition.QuotaWindows.FirstOrDefault(w => w.Kind == WindowKind.Burst);
        var rollingWindow = definition.QuotaWindows.FirstOrDefault(w => w.Kind == WindowKind.Rolling);

        var burstLabel = burstCard.Name ?? burstWindow?.DetailName ?? "Burst";
        var rollingLabel = rollingCard.Name ?? rollingWindow?.DetailName ?? "Rolling";

        // Burst window is Primary (top bar), Rolling window is Secondary (bottom bar).
        var primaryPace = UsageMath.ComputePaceColor(
            burstCard.UsedPercent, burstCard.NextResetTime, burstWindow?.PeriodDuration,
            enablePaceAdjustment);
        var secondaryPace = UsageMath.ComputePaceColor(
            rollingCard.UsedPercent, rollingCard.NextResetTime, rollingWindow?.PeriodDuration,
            enablePaceAdjustment);

        return new DualBarData(
            Primary: new BarData(
                Label: burstLabel,
                UsedPercent: burstCard.UsedPercent,
                ResetTime: burstCard.NextResetTime,
                PeriodDuration: burstWindow?.PeriodDuration,
                PaceColor: primaryPace),
            Secondary: new BarData(
                Label: rollingLabel,
                UsedPercent: rollingCard.UsedPercent,
                ResetTime: rollingCard.NextResetTime,
                PeriodDuration: rollingWindow?.PeriodDuration,
                PaceColor: secondaryPace));
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

        return false;
    }

    public static void SetIsCollapsed(AppPreferences preferences, string providerId, bool isCollapsed)
    {
        ArgumentNullException.ThrowIfNull(preferences);
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

internal sealed record BarData(
    string Label,
    double UsedPercent,
    DateTime? ResetTime,
    TimeSpan? PeriodDuration,
    PaceColorResult PaceColor);

internal sealed record DualBarData(
    BarData Primary,
    BarData Secondary);

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
    PaceColorResult PaceColor,
    DualBarData? DualBar = null,
    bool IsExpired = false,
    bool IsStale = false)
{
    public bool HasDualBuckets => this.DualBar != null;
}
