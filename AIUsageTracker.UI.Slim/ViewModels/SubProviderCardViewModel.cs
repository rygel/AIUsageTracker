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

    private bool _hasProgress;
    private double _usedPercent;
    private double _indicatorWidth;
    private string _displayValue = string.Empty;
    private string? _resetText;

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

    public string DisplayValue => this._displayValue;

    public double IndicatorWidth => this._indicatorWidth;

    public double UsedPercent => this._usedPercent;

    public bool HasProgress => this._hasProgress;

    public string? ResetText => this._resetText;

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
        var presentation = MainWindowRuntimeLogic.BuildDetailPresentation(
            this.Detail,
            this.ShowUsedPercentages,
            GetRelativeTimeString);
        this._hasProgress = presentation.HasProgress;
        this._usedPercent = presentation.UsedPercent;
        this._indicatorWidth = presentation.IndicatorWidth;
        this._displayValue = presentation.DisplayText;
        this._resetText = presentation.ResetText;
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


