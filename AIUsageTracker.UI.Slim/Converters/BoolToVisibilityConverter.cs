// <copyright file="BoolToVisibilityConverter.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AIUsageTracker.UI.Slim.Converters;

/// <summary>
/// Converts a boolean value to a Visibility value.
/// Supports inversion and customizable false value (Collapsed or Hidden).
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// Gets or sets a value indicating whether to invert the boolean logic.
    /// When true, false becomes Visible and true becomes FalseValue.
    /// </summary>
    public bool Invert { get; set; }

    /// <summary>
    /// Gets or sets the Visibility value to use when the boolean is false (or true when inverted).
    /// Defaults to Collapsed.
    /// </summary>
    public Visibility FalseValue { get; set; } = Visibility.Collapsed;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var boolValue = value is bool b && b;

        if (this.Invert)
        {
            boolValue = !boolValue;
        }

        return boolValue ? Visibility.Visible : this.FalseValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visibility = value is Visibility v ? v : Visibility.Collapsed;
        var boolValue = visibility == Visibility.Visible;

        return this.Invert ? !boolValue : boolValue;
    }
}
