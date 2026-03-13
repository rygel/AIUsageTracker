// <copyright file="SubProviderCardViewModel.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AIUsageTracker.UI.Slim.ViewModels;

/// <summary>
/// ViewModel for a sub-provider detail card.
/// </summary>
public partial class SubProviderCardViewModel : BaseViewModel
{
    [ObservableProperty]
    private ProviderUsageDetail _detail;

    [ObservableProperty]
    private bool _isQuotaBased;

    [ObservableProperty]
    private bool _isPrivacyMode;

    [ObservableProperty]
    private bool _showUsedPercentages;

    private ProviderSubDetailPresentation? _presentation;

    public SubProviderCardViewModel(
        ProviderUsageDetail detail,
        bool isQuotaBased,
        bool isPrivacyMode,
        bool showUsedPercentages)
    {
        this._detail = detail;
        this._isQuotaBased = isQuotaBased;
        this._isPrivacyMode = isPrivacyMode;
        this._showUsedPercentages = showUsedPercentages;
        this.UpdatePresentation();
    }

    public string DisplayName => this.Detail.Name;

    public string DisplayValue => this._presentation?.DisplayText ?? string.Empty;

    public double IndicatorWidth => this._presentation?.IndicatorWidth ?? 0;

    public double UsedPercent => this._presentation?.UsedPercent ?? 0;

    public bool HasProgress => this._presentation?.HasProgress ?? false;

    public string? ResetText => this._presentation?.ResetText;

    public DateTime? NextResetTime => this.Detail.NextResetTime;

    partial void OnDetailChanged(ProviderUsageDetail value)
    {
        UpdatePresentation();
        NotifyAllPropertiesChanged();
    }

    partial void OnIsQuotaBasedChanged(bool value)
    {
        UpdatePresentation();
        NotifyAllPropertiesChanged();
    }

    partial void OnShowUsedPercentagesChanged(bool value)
    {
        UpdatePresentation();
        NotifyAllPropertiesChanged();
    }

    private void UpdatePresentation()
    {
        this._presentation = ProviderSubDetailPresentationCatalog.Create(
            this.Detail,
            this.IsQuotaBased,
            this.ShowUsedPercentages,
            GetRelativeTimeString);
    }

    private void NotifyAllPropertiesChanged()
    {
        this.OnPropertyChanged(nameof(this.DisplayName));
        this.OnPropertyChanged(nameof(this.DisplayValue));
        this.OnPropertyChanged(nameof(this.IndicatorWidth));
        this.OnPropertyChanged(nameof(this.UsedPercent));
        this.OnPropertyChanged(nameof(this.HasProgress));
        this.OnPropertyChanged(nameof(this.ResetText));
        this.OnPropertyChanged(nameof(this.NextResetTime));
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
