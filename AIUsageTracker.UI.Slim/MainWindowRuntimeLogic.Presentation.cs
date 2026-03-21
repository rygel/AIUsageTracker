// <copyright file="MainWindowRuntimeLogic.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using AIUsageTracker.Core.Models;
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
            ? MaskAccountIdentifier(accountName)
            : accountName;
    }

    public static ProviderCardPresentation Create(
        ProviderUsage usage,
        bool showUsed)
    {
        var providerId = usage.ProviderId ?? string.Empty;
        var isStale = usage.Details?.Any(d => d.IsStale) == true;
        var description = usage.Description ?? string.Empty;
        var isMissing = usage.State == ProviderUsageState.Missing;
        var isConsoleCheck = usage.State == ProviderUsageState.ConsoleCheck;
        var isError = usage.State == ProviderUsageState.Error;
        var isUnknown = usage.State == ProviderUsageState.Unknown;
        var canonicalProviderId = ProviderMetadataCatalog.GetCanonicalProviderId(providerId);
        var presDef = ProviderMetadataCatalog.Find(canonicalProviderId);
        var isAggregateParent = presDef != null
            && string.Equals(canonicalProviderId, presDef.ProviderId, StringComparison.OrdinalIgnoreCase)
            && presDef.RenderDetailsAsSyntheticChildrenInMainWindow
            && string.Equals(providerId, canonicalProviderId, StringComparison.OrdinalIgnoreCase);
        var isStatusOnlyProvider = usage.IsStatusOnly;
        var hasDualQuotaBucketPresentation = TryGetDualQuotaBucketPresentation(usage, out var dualQuotaBucketPresentation);
        var remainingPercent = usage.RemainingPercent;
        var usedPercent = usage.UsedPercent;
        var shouldHaveProgress = usage.IsAvailable &&
            !isUnknown &&
            !isAggregateParent &&
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
            isAggregateParent,
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
        bool isAggregateParent,
        bool isStatusOnlyProvider,
        bool hasDualQuotaBucketPresentation,
        (string PrimaryLabel, double PrimaryUsedPercent, DateTime? PrimaryResetTime, string SecondaryLabel, double SecondaryUsedPercent, DateTime? SecondaryResetTime) dualQuotaBucketPresentation)
    {
        if (isAggregateParent)
        {
            return (string.IsNullOrWhiteSpace(description) ? "Quota details unavailable" : description, false);
        }

        if ((ProviderMetadataCatalog.Find(usage.ProviderId ?? string.Empty)?.IsTooltipOnly ?? false))
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
            IsStale: isStale);
    }

    private static string BuildDualQuotaBucketStatusText(
        (string PrimaryLabel, double PrimaryUsedPercent, DateTime? PrimaryResetTime, string SecondaryLabel, double SecondaryUsedPercent, DateTime? SecondaryResetTime) presentation,
        bool showUsed)
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
        return showUsed
            ? $"{UsageMath.ClampPercent(usage.UsedPercent):F0}% used"
            : $"{UsageMath.ClampPercent(usage.RemainingPercent):F0}% remaining";
    }

    public static (string ProviderId, string Title, IReadOnlyList<ProviderUsageDetail> Details, bool IsCollapsed)? Build(
        ProviderUsage usage,
        AppPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(preferences);

        var details = GetDisplayableDetails(usage);
        if (details.Count == 0)
        {
            return null;
        }

        var providerId = usage.ProviderId ?? string.Empty;
        var title = $"{ProviderMetadataCatalog.ResolveDisplayLabel(usage)} Details";
        var isCollapsed = GetIsCollapsed(preferences, providerId);

        return (providerId, title, details, isCollapsed);
    }

    public static IReadOnlyList<ProviderUsageDetail> GetDisplayableDetails(ProviderUsage usage)
    {
        if (usage.Details?.Any() != true)
        {
            return Array.Empty<ProviderUsageDetail>();
        }

        if (ProviderMetadataCatalog.Find(usage.ProviderId ?? string.Empty)?.HasDisplayableDerivedProviders ?? false)
        {
            return Array.Empty<ProviderUsageDetail>();
        }

        if ((ProviderMetadataCatalog.Find(usage.ProviderId ?? string.Empty)?.IsTooltipOnly ?? false))
        {
            return Array.Empty<ProviderUsageDetail>();
        }

        return usage.Details
            .Where(IsDisplayableDetail)
            .OrderBy(GetDetailSortOrder)
            .ThenBy(detail => detail.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static (bool HasProgress, double UsedPercent, double IndicatorWidth, string DisplayText, string? ResetText)
        BuildDetailPresentation(
            ProviderUsageDetail detail,
            bool showUsed,
            Func<DateTime, string> relativeTimeFormatter)
    {
        var parsedUsed = UsageMath.GetEffectiveUsedPercent(detail);
        var hasPercent = parsedUsed.HasValue;
        var usedPercent = parsedUsed ?? 0;
        var remainingPercent = 100.0 - usedPercent;
        var displayPercent = showUsed ? usedPercent : remainingPercent;
        var displayText = hasPercent
            ? GetDisplayText(detail, showUsed, includeSemanticLabel: false)
            : GetStoredDisplayText(detail);
        var indicatorWidth = Math.Clamp(displayPercent, 0, 100);
        var resetText = detail.NextResetTime.HasValue
            ? $"({relativeTimeFormatter(detail.NextResetTime.Value)})"
            : null;

        return (
            HasProgress: hasPercent,
            UsedPercent: usedPercent,
            IndicatorWidth: indicatorWidth,
            DisplayText: displayText,
            ResetText: resetText);
    }

    public static bool IsEligibleDetail(ProviderUsageDetail detail, bool includeRateLimit = true)
    {
        if (string.IsNullOrWhiteSpace(detail.Name))
        {
            return false;
        }

        return detail.DetailType == ProviderUsageDetailType.Model ||
               detail.DetailType == ProviderUsageDetailType.Other ||
               (includeRateLimit && detail.DetailType == ProviderUsageDetailType.RateLimit);
    }

    public static bool IsEligibleTrayDetail(ProviderUsageDetail detail)
    {
        if (!IsEligibleDetail(detail, includeRateLimit: true))
        {
            return false;
        }

        return !detail.Name.Contains("window", StringComparison.OrdinalIgnoreCase) &&
               !detail.Name.Contains("credit", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetStoredDisplayText(ProviderUsageDetail detail, bool includeComplement = false)
    {
        if (detail.TryGetPercentageValue(out var percentage, out var semantic, out var decimalPlaces))
        {
            return FormatPercentage(percentage, semantic, decimalPlaces, includeComplement);
        }

        return string.IsNullOrWhiteSpace(detail.Description) ? "No data" : detail.Description;
    }

    public static string GetDisplayText(
        ProviderUsageDetail detail,
        bool showUsed,
        bool includeSemanticLabel,
        bool includeComplement = false)
    {
        var usedPercent = UsageMath.GetEffectiveUsedPercent(detail);
        if (!usedPercent.HasValue)
        {
            return GetStoredDisplayText(detail, includeComplement: false);
        }

        var decimalPlaces = detail.TryGetPercentageValue(out _, out _, out var precision)
            ? precision
            : 0;
        var displayPercent = showUsed
            ? UsageMath.ClampPercent(usedPercent.Value)
            : UsageMath.ClampPercent(100.0 - usedPercent.Value);

        if (!includeSemanticLabel)
        {
            return $"{displayPercent.ToString($"F{decimalPlaces}", CultureInfo.InvariantCulture)}%";
        }

        var semantic = showUsed ? PercentageValueSemantic.Used : PercentageValueSemantic.Remaining;
        return FormatPercentage(displayPercent, semantic, decimalPlaces, includeComplement);
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
        out (string PrimaryLabel, double PrimaryUsedPercent, DateTime? PrimaryResetTime, string SecondaryLabel, double SecondaryUsedPercent, DateTime? SecondaryResetTime) presentation)
    {
        presentation = default;

        if (usage.Details?.Any() != true)
        {
            return false;
        }

        var quotaBuckets = usage.Details
            .Where(detail => detail.DetailType == ProviderUsageDetailType.QuotaWindow)
            .Where(detail => detail.QuotaBucketKind != WindowKind.None)
            .ToList();

        if (quotaBuckets.Count < 2)
        {
            return false;
        }

        ProviderMetadataCatalog.TryGet(usage.ProviderId ?? string.Empty, out var definition);
        var declaredWindows = definition?.QuotaWindows.Where(w => w.Kind != WindowKind.None).ToList();
        if (declaredWindows == null || declaredWindows.Count == 0)
        {
            return false;
        }

        var orderedBuckets = quotaBuckets
            .Select(detail => new
            {
                Detail = detail,
                DeclaredWindow = FindMatchingPresentationWindow(detail, declaredWindows),
            })
            .Where(x => x.DeclaredWindow != null)
            .OrderBy(x => GetDeclaredWindowOrder(x.DeclaredWindow!, declaredWindows))
            .ToList();

        if (orderedBuckets.Count < 2)
        {
            return false;
        }

        var first = orderedBuckets[0];
        var second = orderedBuckets.Skip(1).FirstOrDefault(x => x.Detail.QuotaBucketKind != first.Detail.QuotaBucketKind);

        if (second == null)
        {
            return false;
        }

        var parsedFirst = UsageMath.GetEffectiveUsedPercent(first.Detail);
        var parsedSecond = UsageMath.GetEffectiveUsedPercent(second.Detail);

        if (!parsedFirst.HasValue || !parsedSecond.HasValue)
        {
            return false;
        }

        presentation = (
            PrimaryLabel: GetWindowLabel(first.Detail, first.DeclaredWindow!),
            PrimaryUsedPercent: parsedFirst.Value,
            PrimaryResetTime: first.Detail.NextResetTime,
            SecondaryLabel: GetWindowLabel(second.Detail, second.DeclaredWindow!),
            SecondaryUsedPercent: parsedSecond.Value,
            SecondaryResetTime: second.Detail.NextResetTime);
        return true;
    }

    private static int GetDeclaredWindowOrder(QuotaWindowDefinition declaredWindow, IReadOnlyList<QuotaWindowDefinition> windows)
    {
        for (var index = 0; index < windows.Count; index++)
        {
            if (ReferenceEquals(windows[index], declaredWindow))
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    private static bool ShouldUseSharedCollapsePreference(string providerId)
    {
        return ProviderMetadataCatalog.Find(
            ProviderMetadataCatalog.GetCanonicalProviderId(providerId ?? string.Empty))?.CollapseDerivedChildrenInMainWindow ?? false;
    }

    private static bool IsDisplayableDetail(ProviderUsageDetail detail) => IsEligibleDetail(detail, includeRateLimit: true);

    private static int GetDetailSortOrder(ProviderUsageDetail detail)
    {
        return detail.DetailType switch
        {
            ProviderUsageDetailType.Model => 0,
            ProviderUsageDetailType.RateLimit => 1,
            ProviderUsageDetailType.Other => 2,
            _ => 3,
        };
    }

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

    private static string GetWindowLabel(ProviderUsageDetail detail, QuotaWindowDefinition declaredWindow)
    {
        var nameLabel = ExtractDurationLabelFromDetailName(detail.Name);
        if (!string.IsNullOrWhiteSpace(nameLabel))
        {
            return nameLabel;
        }

        return declaredWindow.DualBarLabel;
    }

    private static QuotaWindowDefinition? FindMatchingPresentationWindow(
        ProviderUsageDetail detail,
        IReadOnlyList<QuotaWindowDefinition> declaredWindows)
    {
        var detailNameMatch = declaredWindows.FirstOrDefault(window =>
            window.Kind == detail.QuotaBucketKind &&
            window.DetailName != null &&
            string.Equals(window.DetailName, detail.Name, StringComparison.OrdinalIgnoreCase));
        if (detailNameMatch != null)
        {
            return detailNameMatch;
        }

        var sameKindWindows = declaredWindows.Where(window => window.Kind == detail.QuotaBucketKind).ToList();
        return sameKindWindows.Count == 1 ? sameKindWindows[0] : null;
    }

    private static string? ExtractDurationLabelFromDetailName(string? name)
    {
        const string suffix = " Limit";
        if (string.IsNullOrWhiteSpace(name) || !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return name[..^suffix.Length].Trim();
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

    private static string MaskAccountIdentifier(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input;
        }

        var atIndex = input.IndexOf('@');
        if (atIndex > 0 && atIndex < input.Length - 1)
        {
            var localPart = input[..atIndex];
            var domainPart = input[(atIndex + 1)..];
            var maskedDomainChars = domainPart.ToCharArray();
            for (var i = 0; i < maskedDomainChars.Length; i++)
            {
                if (maskedDomainChars[i] != '.')
                {
                    maskedDomainChars[i] = '*';
                }
            }

            var maskedDomain = new string(maskedDomainChars);
            if (localPart.Length <= 2)
            {
                return $"{new string('*', localPart.Length)}@{maskedDomain}";
            }

            return $"{localPart[0]}{new string('*', localPart.Length - 2)}{localPart[^1]}@{maskedDomain}";
        }

        return MaskString(input);
    }

    private static string MaskString(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        if (input.Length <= 2)
        {
            return new string('*', input.Length);
        }

        return input[0] + new string('*', input.Length - 2) + input[^1];
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
    bool IsStale = false)
{
    public bool HasDualBuckets => this.DualBucketPrimaryUsed.HasValue;
}

