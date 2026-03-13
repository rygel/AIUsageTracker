// <copyright file="CollapsibleSectionViewModel.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AIUsageTracker.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIUsageTracker.UI.Slim.ViewModels;

/// <summary>
/// ViewModel for a collapsible section containing provider cards.
/// </summary>
public partial class CollapsibleSectionViewModel : BaseViewModel
{
    private readonly AppPreferences _preferences;
    private readonly bool _isQuotaSection;
    private readonly Func<Task> _savePreferencesAsync;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isGroupHeader = true;

    [ObservableProperty]
    private ObservableCollection<ProviderCardViewModel> _items = new();

    public CollapsibleSectionViewModel(
        string title,
        bool isQuotaSection,
        AppPreferences preferences,
        Func<Task>? savePreferencesAsync = null)
    {
        this._title = title;
        this._isQuotaSection = isQuotaSection;
        this._preferences = preferences;
        this._savePreferencesAsync = savePreferencesAsync ?? (() => Task.CompletedTask);

        // Initialize collapsed state from preferences
        this._isExpanded = !(this._isQuotaSection
            ? preferences.IsPlansAndQuotasCollapsed
            : preferences.IsPayAsYouGoCollapsed);
    }

    public string ToggleSymbol => this.IsExpanded ? "\u25BC" : "\u25B6"; // ▼ or ▶

    public string DisplayTitle => this.IsGroupHeader
        ? this.Title.ToUpper(CultureInfo.InvariantCulture)
        : this.Title;

    public Brush AccentBrush => this._isQuotaSection
        ? Brushes.DeepSkyBlue
        : Brushes.MediumSeaGreen;

    public Brush TitleBrush => this.IsGroupHeader
        ? this.AccentBrush
        : GetResourceBrush("SecondaryText");

    public FontWeight FontWeight => this.IsGroupHeader ? FontWeights.Bold : FontWeights.Normal;

    public double FontSize => this.IsGroupHeader ? 10.0 : 9.0;

    public double ToggleOpacity => this.IsGroupHeader ? 1.0 : 0.8;

    public double LineOpacity => this.IsGroupHeader ? 0.5 : 0.3;

    public Thickness HeaderMargin => this.IsGroupHeader
        ? new Thickness(0, 8, 0, 4)
        : new Thickness(20, 4, 0, 2);

    public string SectionKey => this._isQuotaSection ? "PlansAndQuotas" : "PayAsYouGo";

    [RelayCommand]
    private async Task ToggleExpandedAsync()
    {
        this.IsExpanded = !this.IsExpanded;

        // Update preferences
        if (this._isQuotaSection)
        {
            this._preferences.IsPlansAndQuotasCollapsed = !this.IsExpanded;
        }
        else
        {
            this._preferences.IsPayAsYouGoCollapsed = !this.IsExpanded;
        }

        // Save preferences
        await this._savePreferencesAsync();
    }

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ToggleSymbol));
    }

    partial void OnTitleChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayTitle));
    }

    partial void OnIsGroupHeaderChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(TitleBrush));
        OnPropertyChanged(nameof(FontWeight));
        OnPropertyChanged(nameof(FontSize));
        OnPropertyChanged(nameof(ToggleOpacity));
        OnPropertyChanged(nameof(LineOpacity));
        OnPropertyChanged(nameof(HeaderMargin));
    }

    private static SolidColorBrush GetResourceBrush(string key)
    {
        if (Application.Current?.Resources[key] is SolidColorBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Colors.Gray);
    }
}
