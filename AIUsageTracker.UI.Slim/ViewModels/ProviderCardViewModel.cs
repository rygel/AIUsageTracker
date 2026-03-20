// <copyright file="ProviderCardViewModel.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Collections.ObjectModel;
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
        : ProviderAccountDisplayCatalog.ResolveDisplayAccountName(this.ProviderId, this.Usage.AccountName, false);

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
    /// True when the provider API returned HTTP 429 (Too Many Requests).
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
            var resetTimes = ProviderResetBadgePresentationCatalog.ResolveResetTimes(this.Usage, suppressSingle);
            if (resetTimes.Count == 0)
            {
                return null;
            }

            var resetParts = resetTimes.Select(GetRelativeTimeString).ToList();
            return $"({string.Join(" | ", resetParts)})";
        }
    }

    public DateTime? NextResetTime => this.Usage.NextResetTime;

    public string? TooltipContent => ProviderTooltipPresentationCatalog.BuildContent(this.Usage, this.DisplayName);

    /// <summary>
    /// Returns a formatted req/hr badge string when ShowUsagePerHour is enabled and data is available,
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
    /// Pace-adjusted used percentage used solely for progress-bar colour decisions.
    /// For rolling-window providers with a known period duration this is reduced when the
    /// user is under pace, so the bar stays green/yellow rather than turning red due to the
    /// raw percentage crossing a threshold while consumption is still within budget.
    /// Equals <see cref="UsedPercent"/> for providers without rolling-window data.
    /// </summary>
    public double ColorIndicatorPercent
    {
        get
        {
            if (!this.EnablePaceAdjustment)
            {
                return this.UsedPercent;
            }

            var (nextReset, period) = ResolveRollingWindowInfo();
            if (nextReset == null || period == null)
            {
                return this.UsedPercent;
            }

            return UsageMath.CalculatePaceAdjustedColorPercent(
                this.UsedPercent,
                nextReset.Value.ToUniversalTime(),
                period.Value);
        }
    }

    /// <summary>
    /// Short text badge indicating rolling-window pace, or null when pace info is unavailable.
    /// Returns "On pace" when the user is consuming at or below the expected rate for the
    /// elapsed fraction of the quota window — a positive signal that suppresses alarm.
    /// Returns null when at/over pace (raw percentage already conveys urgency) or when
    /// no period duration is known.
    /// </summary>
    public string? PaceBadgeText
    {
        get
        {
            if (!this.EnablePaceAdjustment)
            {
                return null;
            }

            var (nextReset, period) = ResolveRollingWindowInfo();
            if (nextReset == null || period == null || period.Value.TotalSeconds <= 0)
            {
                return null;
            }

            var periodStart = nextReset.Value.ToUniversalTime() - period.Value;
            var elapsed = DateTime.UtcNow - periodStart;
            var elapsedFraction = Math.Clamp(elapsed.TotalSeconds / period.Value.TotalSeconds, 0.01, 1.0);
            var expectedPercent = elapsedFraction * 100.0;

            // Only show the badge when the user is meaningfully under pace.
            // A 5% margin avoids flickering the badge when nearly at pace.
            if (this.UsedPercent < expectedPercent * 0.95)
            {
                return "On pace";
            }

            return null;
        }
    }

    private (DateTime? NextReset, TimeSpan? PeriodDuration) ResolveRollingWindowInfo()
    {
        // Synthetic-child rows carry PeriodDuration directly on the ProviderUsage.
        if (this.Usage.PeriodDuration.HasValue && this.Usage.NextResetTime.HasValue)
        {
            return (this.Usage.NextResetTime, this.Usage.PeriodDuration);
        }

        // For regular providers, find the first rolling-window detail with timing data.
        var rollingDetail = this.Usage.Details?
            .FirstOrDefault(d => d.QuotaBucketKind == WindowKind.Rolling
                                 && d.PeriodDuration.HasValue
                                 && d.NextResetTime.HasValue);
        if (rollingDetail != null)
        {
            return (rollingDetail.NextResetTime, rollingDetail.PeriodDuration);
        }

        return (null, null);
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
        this._presentation = ProviderCardPresentationCatalog.Create(this.Usage, this.ShowUsedPercentages);
    }

    private void PopulateDetails()
    {
        this.Details.Clear();

        var displayableDetails = ProviderSubDetailPresentationCatalog.GetDisplayableDetails(this.Usage);
        foreach (var detail in displayableDetails)
        {
            this.Details.Add(new SubProviderCardViewModel(detail, this.Usage.IsQuotaBased, this.IsPrivacyMode, this.ShowUsedPercentages));
        }
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
