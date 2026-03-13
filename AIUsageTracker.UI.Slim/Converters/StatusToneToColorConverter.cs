// <copyright file="StatusToneToColorConverter.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace AIUsageTracker.UI.Slim.Converters;

/// <summary>
/// Converts a ProviderCardStatusTone enum value to a Brush for display.
/// </summary>
public class StatusToneToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ProviderCardStatusTone tone)
        {
            return GetResourceBrush("SecondaryText");
        }

        return tone switch
        {
            ProviderCardStatusTone.Missing => Brushes.IndianRed,
            ProviderCardStatusTone.Warning => Brushes.Orange,
            ProviderCardStatusTone.Error => Brushes.Red,
            _ => GetResourceBrush("SecondaryText"),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
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
