// <copyright file="ProviderCardViewModel.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Collections.ObjectModel;
using System.Text;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AIUsageTracker.UI.Slim.ViewModels;

/// <summary>
/// ViewModel for a provider card, wrapping ProviderUsage with presentation logic.
/// </summary>
public partial class ProviderCardViewModel : BaseViewModel
{
    [ObservableProperty]
    private ProviderUsage _usage;

    [ObservableProperty]
    private bool _isPrivacyMode;

    [ObservableProperty]
    private int _yellowThreshold = 60;

    [ObservableProperty]
    private int _redThreshold = 80;

    [ObservableProperty]
    private bool _showUsedPercentages;

    [ObservableProperty]
    private bool _showUsagePerHour;

    [ObservableProperty]
    private bool _enablePaceAdjustment = true;

    [ObservableProperty]
    private ObservableCollection<SubProviderCardViewModel> _details = new();

    private ProviderCardPresentation? _presentation;

    public ProviderCardViewModel(ProviderUsage usage, AppPreferences prefs, bool isPrivacyMode)
    {
        this._usage = usage;
        this._isPrivacyMode = isPrivacyMode;
        this._yellowThreshold = prefs.ColorThresholdYellow;
        this._redThreshold = prefs.ColorThresholdRed;
        this._showUsedPercentages = prefs.ShowUsedPercentages;
        this._showUsagePerHour = prefs.ShowUsagePerHour;
        this._enablePaceAdjustment = prefs.EnablePaceAdjustment;

        this.UpdatePresentation();
        this.PopulateDetails();
    }

    public string ProviderId => this.Usage.ProviderId ?? string.Empty;

    public string DisplayName => ProviderMetadataCatalog.ResolveDisplayLabel(this.Usage);

    public string AccountDisplay => this.IsPrivacyMode
        ? "****"
        : MainWindowRuntimeLogic.ResolveDisplayAccountName(this.ProviderId, this.Usage.AccountName, false);

    public bool HasAccountName => !string.IsNullOrWhiteSpace(this.Usage.AccountName);

    public double ProgressPercentage => this.ShowUsedPercentages ? this.UsedPercent : this.RemainingPercent;

    public double UsedPercent => this._presentation?.UsedPercent ?? 0;

    public double RemainingPercent => this._presentation?.RemainingPercent ?? 100;

    public bool ShouldShowProgress => this._presentation?.ShouldHaveProgress ?? false;

    public string StatusText => this._presentation?.StatusText ?? string.Empty;

    public ProviderCardStatusTone StatusTone => this._presentation?.StatusTone ?? ProviderCardStatusTone.Secondary;

    public bool IsMissing => this._presentation?.IsMissing ?? false;

    public bool IsStale => this._presentation?.IsStale ?? false;

    /// <summary>
    /// Gets a value indicating whether true when the provider API returned HTTP 429 (Too Many Requests).
    /// The card will show a Warning-tone status rather than an Error-tone status.
    /// </summary>
    public bool IsRateLimited => this.Usage.HttpStatus == 429;

    public bool IsQuotaBased => this.Usage.IsQuotaBased;

    public bool HasDualQuotaBuckets => this._presentation?.HasDualBuckets ?? false;

    public double PrimaryUsedPercent => this._presentation?.DualBucketPrimaryUsed ?? 0;

    public double SecondaryUsedPercent => this._presentation?.DualBucketSecondaryUsed ?? 0;

    public string? ResetBadgeText
    {
        get
        {
            var suppressSingle = this._presentation?.SuppressSingleResetTime ?? false;
            var resetTimes = ResolveResetTimes(this.Usage, suppressSingle);
            if (resetTimes.Count == 0)
            {
                return null;
            }

            var resetParts = resetTimes.Select(GetRelativeTimeString).ToList();
            return $"({string.Join(" | ", resetParts)})";
        }
    }

    public DateTime? NextResetTime => this.Usage.NextResetTime;

    public string? TooltipContent => BuildTooltipContent(this.Usage, this.DisplayName);

    /// <summary>
    /// Gets a formatted req/hr badge string when ShowUsagePerHour is enabled and data is available,
    /// or null (causing the badge to collapse via NullToVisibilityConverter).
    /// </summary>
    public string? UsageRateBadgeText
    {
        get
        {
            if (!this.ShowUsagePerHour || this.Usage.UsagePerHour is null)
            {
                return null;
            }

            return $"{this.Usage.UsagePerHour.Value:F1}/hr";
        }
    }

    public bool HasDetails => this.Details.Count > 0;

    /// <summary>
    /// Gets pace-adjusted used percentage for progress-bar colour decisions.
    /// PeriodDuration and NextResetTime are set on every usage by ProviderUsageDisplayCatalog
    /// before the ViewModel is constructed, so no catalog lookup or fallback is needed here.
    /// </summary>
    public double ColorIndicatorPercent
    {
        get
        {
            return GetColorIndicatorPercent(
                this.Usage,
                this.UsedPercent,
                this.EnablePaceAdjustment);
        }
    }

    /// <summary>
    /// Gets "On pace" badge when usage is meaningfully under the expected rate for the elapsed
    /// fraction of the quota window. Null when pace info is unavailable or user is at/over pace.
    /// </summary>
    public string? PaceBadgeText
    {
        get
        {
            return GetPaceBadgeText(
                this.Usage,
                this.UsedPercent,
                this.EnablePaceAdjustment);
        }
    }

    partial void OnUsageChanged(ProviderUsage value)
    {
        UpdatePresentation();
        PopulateDetails();
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(AccountDisplay));
        OnPropertyChanged(nameof(HasAccountName));
        OnPropertyChanged(nameof(ProgressPercentage));
        OnPropertyChanged(nameof(UsedPercent));
        OnPropertyChanged(nameof(RemainingPercent));
        OnPropertyChanged(nameof(ShouldShowProgress));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusTone));
        OnPropertyChanged(nameof(IsMissing));
        OnPropertyChanged(nameof(IsStale));
        OnPropertyChanged(nameof(IsRateLimited));
        OnPropertyChanged(nameof(IsQuotaBased));
        OnPropertyChanged(nameof(HasDualQuotaBuckets));
        OnPropertyChanged(nameof(PrimaryUsedPercent));
        OnPropertyChanged(nameof(SecondaryUsedPercent));
        OnPropertyChanged(nameof(ResetBadgeText));
        OnPropertyChanged(nameof(NextResetTime));
        OnPropertyChanged(nameof(TooltipContent));
        OnPropertyChanged(nameof(HasDetails));
        OnPropertyChanged(nameof(UsageRateBadgeText));
        OnPropertyChanged(nameof(ColorIndicatorPercent));
        OnPropertyChanged(nameof(PaceBadgeText));
    }

    partial void OnIsPrivacyModeChanged(bool value)
    {
        OnPropertyChanged(nameof(AccountDisplay));
    }

    partial void OnShowUsedPercentagesChanged(bool value)
    {
        UpdatePresentation();
        OnPropertyChanged(nameof(ProgressPercentage));
        OnPropertyChanged(nameof(StatusText));
        PopulateDetails();
    }

    partial void OnShowUsagePerHourChanged(bool value)
    {
        OnPropertyChanged(nameof(UsageRateBadgeText));
    }

    partial void OnEnablePaceAdjustmentChanged(bool value)
    {
        OnPropertyChanged(nameof(ColorIndicatorPercent));
        OnPropertyChanged(nameof(PaceBadgeText));
    }

    private void UpdatePresentation()
    {
        this._presentation = MainWindowRuntimeLogic.Create(this.Usage, this.ShowUsedPercentages);
    }

    private void PopulateDetails()
    {
        this.Details.Clear();

        var displayableDetails = MainWindowRuntimeLogic.GetDisplayableDetails(this.Usage);
        foreach (var detail in displayableDetails)
        {
            this.Details.Add(new SubProviderCardViewModel(detail, this.Usage.IsQuotaBased, this.IsPrivacyMode, this.ShowUsedPercentages));
        }
    }

    private static double GetColorIndicatorPercent(
        ProviderUsage usage,
        double usedPercent,
        bool enablePaceAdjustment,
        DateTime? nowUtc = null)
    {
        if (!enablePaceAdjustment || !usage.PeriodDuration.HasValue || !usage.NextResetTime.HasValue)
        {
            return usedPercent;
        }

        return UsageMath.CalculatePaceAdjustedColorPercent(
            usedPercent,
            usage.NextResetTime.Value.ToUniversalTime(),
            usage.PeriodDuration.Value,
            nowUtc);
    }

    private static string? GetPaceBadgeText(
        ProviderUsage usage,
        double usedPercent,
        bool enablePaceAdjustment,
        DateTime? nowUtc = null)
    {
        if (!enablePaceAdjustment || !usage.PeriodDuration.HasValue || !usage.NextResetTime.HasValue)
        {
            return null;
        }

        var projected = UsageMath.CalculateProjectedFinalPercent(
            usedPercent,
            usage.NextResetTime.Value.ToUniversalTime(),
            usage.PeriodDuration.Value,
            nowUtc);

        if (projected >= 100.0)
        {
            return "Over pace";
        }

        return projected < 90.0 ? "On pace" : null;
    }

    private static IReadOnlyList<DateTime> ResolveResetTimes(ProviderUsage usage, bool suppressSingleResetFallback)
    {
        ArgumentNullException.ThrowIfNull(usage);

        var detailResetTimes = usage.Details?
            .Where(detail => detail.DetailType == ProviderUsageDetailType.QuotaWindow)
            .Where(detail => detail.QuotaBucketKind != WindowKind.None)
            .Where(detail => detail.NextResetTime.HasValue)
            .Where(detail => UsageMath.GetEffectiveUsedPercent(detail).HasValue)
            .Select(detail => detail.NextResetTime!.Value)
            .Distinct()
            .ToList()
            ?? new List<DateTime>();

        if (detailResetTimes.Count >= 2)
        {
            return detailResetTimes;
        }

        if (suppressSingleResetFallback)
        {
            return Array.Empty<DateTime>();
        }

        return usage.NextResetTime.HasValue
            ? new[] { usage.NextResetTime.Value }
            : Array.Empty<DateTime>();
    }

    private static string? BuildTooltipContent(ProviderUsage usage, string friendlyName)
    {
        var tooltipBuilder = new StringBuilder();
        tooltipBuilder.AppendLine(friendlyName);
        tooltipBuilder.AppendLine($"Status: {(usage.IsAvailable ? "Active" : "Inactive")}");
        if (!string.IsNullOrEmpty(usage.Description))
        {
            tooltipBuilder.AppendLine($"Description: {usage.Description}");
        }

        if (usage.Details?.Any() == true)
        {
            tooltipBuilder.AppendLine();
            tooltipBuilder.AppendLine("Rate Limits:");
            foreach (var detail in usage.Details
                         .OrderBy(GetDetailSortOrder)
                         .ThenBy(GetDetailDisplayName, StringComparer.OrdinalIgnoreCase))
            {
                var detailValue = GetDetailDisplayValue(detail);
                if (string.IsNullOrWhiteSpace(detailValue))
                {
                    continue;
                }

                tooltipBuilder.AppendLine($"  {GetDetailDisplayName(detail)}: {detailValue}");
            }
        }

        if (!string.IsNullOrEmpty(usage.AuthSource))
        {
            tooltipBuilder.AppendLine();
            tooltipBuilder.AppendLine($"Source: {usage.AuthSource}");
        }

        var result = tooltipBuilder.ToString().Trim();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static string GetDetailDisplayName(ProviderUsageDetail detail)
    {
        return detail.Name;
    }

    private static string GetDetailDisplayValue(ProviderUsageDetail detail)
    {
        return MainWindowRuntimeLogic.GetStoredDisplayText(detail);
    }

    private static int GetDetailSortOrder(ProviderUsageDetail detail)
    {
        return (detail.DetailType, detail.QuotaBucketKind) switch
        {
            (ProviderUsageDetailType.QuotaWindow, WindowKind.Burst) => 0,
            (ProviderUsageDetailType.QuotaWindow, WindowKind.Rolling) => 1,
            (ProviderUsageDetailType.QuotaWindow, WindowKind.ModelSpecific) => 2,
            (ProviderUsageDetailType.QuotaWindow, _) => 3,
            (ProviderUsageDetailType.Model, _) => 3,
            (ProviderUsageDetailType.Credit, _) => 4,
            _ => 5,
        };
    }

    private static string GetRelativeTimeString(DateTime nextReset)
    {
        var diff = nextReset - DateTime.Now;

        if (diff.TotalSeconds <= 0)
        {
            return "0m";
        }

        if (diff.TotalDays >= 1)
        {
            return $"{diff.Days}d {diff.Hours}h";
        }

        if (diff.TotalHours >= 1)
        {
            return $"{diff.Hours}h {diff.Minutes}m";
        }

        return $"{diff.Minutes}m";
    }
}


