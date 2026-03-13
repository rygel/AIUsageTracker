// <copyright file="PercentageToColorConverter.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace AIUsageTracker.UI.Slim.Converters;

/// <summary>
/// Converts a percentage value along with threshold settings to a progress bar color.
/// For quota-based providers, the color logic is inverted (low remaining = red).
/// </summary>
public class PercentageToColorConverter : IMultiValueConverter
{
    /// <summary>
    /// Converts percentage and threshold values to a color brush.
    /// </summary>
    /// <param name="values">
    /// Expected values:
    /// [0] - percentage (double): The used percentage value (0-100)
    /// [1] - yellowThreshold (int): Threshold for yellow color
    /// [2] - redThreshold (int): Threshold for red color
    /// [3] - isQuota (bool): Whether this is a quota-based provider (inverts color logic)
    /// </param>
    /// <param name="targetType">The target type.</param>
    /// <param name="parameter">Optional parameter.</param>
    /// <param name="culture">The culture info.</param>
    /// <returns>A SolidColorBrush representing the appropriate color.</returns>
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 4 ||
            values[0] is not double percentage ||
            values[1] is not int yellowThreshold ||
            values[2] is not int redThreshold ||
            values[3] is not bool isQuota)
        {
            return GetResourceBrush("ProgressBarGreen");
        }

        // For quota-based providers, we compare remaining percentage.
        // For pay-as-you-go, we compare used percentage.
        // The percentage passed in is always the "used" percentage.
        var valueToCompare = isQuota ? (100 - percentage) : percentage;

        if (valueToCompare >= redThreshold)
        {
            return GetResourceBrush("ProgressBarRed");
        }

        if (valueToCompare >= yellowThreshold)
        {
            return GetResourceBrush("ProgressBarYellow");
        }

        return GetResourceBrush("ProgressBarGreen");
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static SolidColorBrush GetResourceBrush(string key)
    {
        if (Application.Current?.Resources[key] is SolidColorBrush brush)
        {
            return brush;
        }

        return key switch
        {
            "ProgressBarRed" => new SolidColorBrush(Color.FromRgb(244, 67, 54)),
            "ProgressBarYellow" => new SolidColorBrush(Color.FromRgb(255, 193, 7)),
            _ => new SolidColorBrush(Color.FromRgb(76, 175, 80)),
        };
    }
}
