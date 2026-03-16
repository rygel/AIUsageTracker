// <copyright file="PercentageToWidthConverter.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AIUsageTracker.UI.Slim.Converters;

/// <summary>
/// Converts a percentage value (0-100) to a GridLength for use in progress bar column widths.
/// </summary>
public class PercentageToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var percentage = value switch
        {
            double d => d,
            int i => i,
            float f => f,
            _ => 0.0,
        };

        // Clamp to valid range
        var clampedPercentage = Math.Clamp(percentage, 0, 100);

        // Return as a star-based GridLength
        return new GridLength(clampedPercentage, GridUnitType.Star);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is GridLength gridLength && gridLength.IsStar)
        {
            return gridLength.Value;
        }

        return 0.0;
    }
}
