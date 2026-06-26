// <copyright file="MainWindowRuntimeLogic.Presentation.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

// ARCHITECTURE RULE: Card labels, durations, and rendering decisions come from
// ProviderDefinition → QuotaWindowDefinition (DualBarLabel, PeriodDuration, Kind).
// The Monitor sends raw usage values only — it does NOT control how cards are
// rendered. Never use card.Name ?? "Burst" or any hardcoded fallback. Always
// read from the definition. No filtering by type (.OfType<WindowedProviderUsage>).
// All QuotaProviderUsage subtypes are valid window cards.

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
        var isUnavailable = usage.State == ProviderUsageState.Unavailable;
        var isExpired = usage.State == ProviderUsageState.Expired;

        // Enrich generic descriptions with actionable messages from FailureContext.
        if (!isMissing && (isError || isUnavailable) && usage.FailureContext is not null)
        {
            description = ResolveActionableErrorText(description, usage.FailureContext);
        }

        var qUsage = usage as QuotaProviderUsage;
        var isStatusOnlyProvider = qUsage?.IsStatusOnly ?? false;
        var remainingPercent = qUsage?.RemainingPercent ?? 0;
        var usedPercent = qUsage?.UsedPercent ?? 0;

        var shouldHaveProgress = usage.IsAvailable &&
            !isUnknown &&
            !isStatusOnlyProvider &&
            (usedPercent > 0 || (qUsage?.IsQuotaBased ?? false)) &&
            !isMissing &&
            !isError &&
            !isUnavailable;

        // Always compute pace — used for bar color and badge regardless of card layout.
        var periodDuration = usage switch
        {
            WindowedProviderUsage w => w.PeriodDuration,
            ModelScopedProviderUsage m => m.PeriodDuration,
            _ => null,
        };
        var paceColor = UsageMath.ComputePaceColor(
            usedPercent,
            qUsage?.NextResetTime,
            periodDuration,
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
                IsStale: isStale,
                FetchedAt: usage.FetchedAt);
        }

        if (TryCreateSpecialPresentation(
            isMissing,
            isUnknown,
            isError,
            isUnavailable,
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

        var (statusText, suppressSingleResetTime) = ResolveStatusText(usage, showUsed, dualBar);

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
            IsStale: isStale,
            FetchedAt: usage.FetchedAt);
    }

#pragma warning disable S107
    private static bool TryCreateSpecialPresentation(
        bool isMissing,
        bool isUnknown,
        bool isError,
        bool isUnavailable,
        bool isConsoleCheck,
        bool isExpired,
        bool shouldHaveProgress,
        double usedPercent,
        double remainingPercent,
        string description,
        PaceColorResult paceColor,
        bool isStale,
        out ProviderCardPresentation presentation)
#pragma warning restore S107
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

        if (isUnknown)
        {
            presentation = new ProviderCardPresentation(
                IsMissing: false,
                IsUnknown: true,
                IsError: false,
                ShouldHaveProgress: false,
                SuppressSingleResetTime: false,
                UsedPercent: usedPercent,
                RemainingPercent: remainingPercent,
                StatusText: string.IsNullOrWhiteSpace(description) ? "Status unknown" : description,
                StatusTone: ProviderCardStatusTone.Warning,
                PaceColor: paceColor,
                IsStale: isStale);
            return true;
        }

        if (isUnavailable)
        {
            presentation = new ProviderCardPresentation(
                IsMissing: false,
                IsUnknown: false,
                IsError: false,
                ShouldHaveProgress: false,
                SuppressSingleResetTime: false,
                UsedPercent: usedPercent,
                RemainingPercent: remainingPercent,
                StatusText: string.IsNullOrWhiteSpace(description) ? "Provider unavailable" : description,
                StatusTone: ProviderCardStatusTone.Warning,
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
        DualBarData? dualBar)
    {
        var description = usage.Description ?? string.Empty;
        var qu = usage as QuotaProviderUsage;

        // Safety check: unavailable providers should never show percentage-based text.
        if (!usage.IsAvailable || usage.State != ProviderUsageState.Available)
        {
            return (description, false);
        }

        if (qu?.IsStatusOnly == true)
        {
            return (description, false);
        }

        if (usage.IsTooltipOnly)
        {
            return (GetTooltipOnlyCompactStatus(usage, description), false);
        }

        if (dualBar != null)
        {
            return (BuildDualQuotaBucketStatusText(dualBar, showUsed), true);
        }

        if (qu?.IsQuotaBased == true)
        {
            return (
                qu.DisplayAsFraction
                    ? GetQuotaFractionStatusText(qu, showUsed)
                    : GetQuotaPercentStatusText(qu, showUsed),
                false);
        }

        if (qu?.PlanType == PlanType.Usage)
        {
            return (description, false);
        }

        return (description, false);
    }

    private static string GetTooltipOnlyCompactStatus(ProviderUsage usage, string description)
    {
        if (!usage.IsAvailable)
        {
            return description;
        }

        if (usage is QuotaProviderUsage qu && qu.PlanType == PlanType.Usage && qu.RequestsUsed >= 0)
        {
            if (qu.IsCurrencyUsage)
            {
                return $"${qu.RequestsUsed.ToString("F2", CultureInfo.InvariantCulture)}";
            }

            return qu.RequestsUsed.ToString("F2", CultureInfo.InvariantCulture);
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
        if (usage is not WindowedProviderUsage wu)
        {
            return null;
        }

        var windowCards = wu.WindowCards;
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

        if (burstWindow == null || rollingWindow == null)
        {
            return null;
        }

        // Burst window is Primary (top bar), Rolling window is Secondary (bottom bar).
        var primaryPace = UsageMath.ComputePaceColor(
            burstCard.UsedPercent, burstCard.NextResetTime, burstWindow.PeriodDuration,
            enablePaceAdjustment);
        var secondaryPace = UsageMath.ComputePaceColor(
            rollingCard.UsedPercent, rollingCard.NextResetTime, rollingWindow.PeriodDuration,
            enablePaceAdjustment);

        return new DualBarData(
            Primary: new BarData(
                Label: burstWindow.DualBarLabel,
                UsedPercent: burstCard.UsedPercent,
                ResetTime: burstCard.NextResetTime,
                PeriodDuration: burstWindow.PeriodDuration,
                PaceColor: primaryPace),
            Secondary: new BarData(
                Label: rollingWindow.DualBarLabel,
                UsedPercent: rollingCard.UsedPercent,
                ResetTime: rollingCard.NextResetTime,
                PeriodDuration: rollingWindow.PeriodDuration,
                PaceColor: secondaryPace));
    }

    private static string FormatDualQuotaBucketSegment(string label, double usedPercent, bool showUsed)
    {
        return showUsed
            ? $"{label} {UsageMath.FormatUsedPercent(usedPercent)}"
            : $"{label} {UsageMath.FormatRemainingPercent(100.0 - usedPercent)}";
    }

    private static string GetQuotaFractionStatusText(QuotaProviderUsage usage, bool showUsed)
    {
        if (showUsed)
        {
            return $"{usage.RequestsUsed:N0} / {usage.RequestsAvailable:N0} used";
        }

        var remaining = usage.RequestsAvailable - usage.RequestsUsed;
        return $"{remaining:N0} / {usage.RequestsAvailable:N0} remaining";
    }

    private static string GetQuotaPercentStatusText(QuotaProviderUsage usage, bool showUsed)
    {
        return showUsed
            ? UsageMath.FormatUsedPercent(usage.UsedPercent)
            : UsageMath.FormatRemainingPercent(usage.RemainingPercent);
    }

    private static string ResolveActionableErrorText(string description, HttpFailureContext failureContext)
    {
        if (!string.IsNullOrWhiteSpace(failureContext.UserMessage))
        {
            return failureContext.UserMessage;
        }

        return failureContext.Classification switch
        {
            HttpFailureClassification.Authentication => "Invalid API key — check your credentials",
            HttpFailureClassification.Authorization => "Access denied — check your account permissions",
            HttpFailureClassification.RateLimit => "Rate limited — wait a moment and try again",
            HttpFailureClassification.Network => "Network error — check your internet connection",
            HttpFailureClassification.Timeout => "Connection timed out — the provider may be slow",
            HttpFailureClassification.Server => "Provider service error — try again later",
            HttpFailureClassification.Client => "Bad request — provider may need reconfiguration",
            HttpFailureClassification.Deserialization => "Unexpected response from provider",
            _ => string.IsNullOrWhiteSpace(description) ? "Provider unavailable" : description,
        };
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
    bool IsStale = false,
    DateTime FetchedAt = default)
{
    public bool HasDualBuckets => this.DualBar != null;
}
