// <copyright file="DesignTimeCollapsibleSectionViewModel.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;

namespace AIUsageTracker.UI.Slim.ViewModels.DesignTime;

/// <summary>
/// Design-time ViewModel for collapsible sections, providing sample data for XAML designer.
/// </summary>
public class DesignTimeCollapsibleSectionViewModel
{
    public DesignTimeCollapsibleSectionViewModel()
    {
        this.Items = new ObservableCollection<DesignTimeProviderCardViewModel>();
    }

    public string Title { get; set; } = "SECTION TITLE";

    public bool IsExpanded { get; set; } = true;

    public bool IsGroupHeader { get; set; } = true;

    public ObservableCollection<DesignTimeProviderCardViewModel> Items { get; set; }

    public string ToggleSymbol => this.IsExpanded ? "\u25BC" : "\u25B6";

    public string DisplayTitle => this.IsGroupHeader ? this.Title.ToUpperInvariant() : this.Title;

    public Brush AccentBrush => Brushes.DeepSkyBlue;

    public Brush TitleBrush => this.AccentBrush;

    public FontWeight FontWeight => this.IsGroupHeader ? FontWeights.Bold : FontWeights.Normal;

    public double FontSize => this.IsGroupHeader ? 10.0 : 9.0;

    public double ToggleOpacity => this.IsGroupHeader ? 1.0 : 0.8;

    public double LineOpacity => this.IsGroupHeader ? 0.5 : 0.3;

    public Thickness HeaderMargin => this.IsGroupHeader
        ? new Thickness(0, 8, 0, 4)
        : new Thickness(20, 4, 0, 2);

    public string SectionKey => "DesignTimeSection";
}
