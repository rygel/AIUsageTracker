// <copyright file="RemainingPercentageConverter.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AIUsageTracker.UI.Slim.Converters;

/// <summary>
/// Converts a percentage value to its remaining counterpart (100 - value).
/// Returns a GridLength for use in grid column widths.
/// </summary>
public class RemainingPercentageConverter : IValueConverter
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

        // Calculate remaining and clamp
        var remaining = Math.Max(0.001, 100 - Math.Clamp(percentage, 0, 100));

        // Return as a star-based GridLength
        return new GridLength(remaining, GridUnitType.Star);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is GridLength gridLength && gridLength.IsStar)
        {
            return 100 - gridLength.Value;
        }

        return 0.0;
    }
}
