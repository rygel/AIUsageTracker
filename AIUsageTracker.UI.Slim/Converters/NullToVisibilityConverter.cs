// <copyright file="NullToVisibilityConverter.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AIUsageTracker.UI.Slim.Converters;

/// <summary>
/// Converts null or empty values to Visibility.
/// When the value is null or empty string, returns Collapsed (or Hidden).
/// When the value is non-null, returns Visible.
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// Gets or sets a value indicating whether to invert the visibility logic.
    /// When true, null/empty becomes Visible and non-null becomes hidden.
    /// </summary>
    public bool Invert { get; set; }

    /// <summary>
    /// Gets or sets the Visibility value to use when the value is null/empty.
    /// Defaults to Collapsed.
    /// </summary>
    public Visibility NullValue { get; set; } = Visibility.Collapsed;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isNullOrEmpty = value is null || (value is string s && string.IsNullOrWhiteSpace(s));

        if (this.Invert)
        {
            isNullOrEmpty = !isNullOrEmpty;
        }

        return isNullOrEmpty ? this.NullValue : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
