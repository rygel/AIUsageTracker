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
    private ObservableCollection<SubProviderCardViewModel> _details = new();

    private ProviderCardPresentation? _presentation;

    public ProviderCardViewModel(ProviderUsage usage, AppPreferences prefs, bool isPrivacyMode)
    {
        this._usage = usage;
        this._isPrivacyMode = isPrivacyMode;
        this._yellowThreshold = prefs.ColorThresholdYellow;
        this._redThreshold = prefs.ColorThresholdRed;
        this._showUsedPercentages = prefs.ShowUsedPercentages;

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

    public bool HasDetails => this.Details.Count > 0;

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
        OnPropertyChanged(nameof(IsQuotaBased));
        OnPropertyChanged(nameof(HasDualQuotaBuckets));
        OnPropertyChanged(nameof(PrimaryUsedPercent));
        OnPropertyChanged(nameof(SecondaryUsedPercent));
        OnPropertyChanged(nameof(ResetBadgeText));
        OnPropertyChanged(nameof(NextResetTime));
        OnPropertyChanged(nameof(TooltipContent));
        OnPropertyChanged(nameof(HasDetails));
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
