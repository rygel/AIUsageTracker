// <copyright file="PrivacyMaskConverter.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Windows.Data;

namespace AIUsageTracker.UI.Slim.Converters;

/// <summary>
/// Converts a value to a masked string when privacy mode is enabled.
/// </summary>
public class PrivacyMaskConverter : IMultiValueConverter
{
    /// <summary>
    /// Gets or sets the mask string to display when privacy mode is enabled.
    /// Defaults to "****".
    /// </summary>
    public string MaskString { get; set; } = "****";

    /// <summary>
    /// Converts a value based on privacy mode state.
    /// </summary>
    /// <param name="values">
    /// Expected values:
    /// [0] - value (string): The original value to potentially mask
    /// [1] - isPrivacyMode (bool): Whether privacy mode is enabled.
    /// </param>
    /// <param name="targetType">The target type.</param>
    /// <param name="parameter">Optional parameter.</param>
    /// <param name="culture">The culture info.</param>
    /// <returns>The original value or a masked string.</returns>
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
        {
            return values.Length > 0 ? values[0]?.ToString() ?? string.Empty : string.Empty;
        }

        var originalValue = values[0]?.ToString() ?? string.Empty;
        var isPrivacyMode = values[1] is bool b && b;

        if (string.IsNullOrEmpty(originalValue))
        {
            return string.Empty;
        }

        return isPrivacyMode ? this.MaskString : originalValue;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
