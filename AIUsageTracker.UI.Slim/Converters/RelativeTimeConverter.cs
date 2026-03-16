// <copyright file="RelativeTimeConverter.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Windows.Data;

namespace AIUsageTracker.UI.Slim.Converters;

/// <summary>
/// Converts a DateTime value to a relative time string (e.g., "5m", "2h", "3d").
/// </summary>
public class RelativeTimeConverter : IValueConverter
{
    /// <summary>
    /// Gets or sets a value indicating whether to include parentheses around the output.
    /// </summary>
    public bool IncludeParentheses { get; set; }

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        DateTime? dateTime = value switch
        {
            DateTime dt => dt,
            DateTimeOffset dto => dto.DateTime,
            _ => null,
        };

        if (!dateTime.HasValue)
        {
            return null;
        }

        var relativeTime = GetRelativeTimeString(dateTime.Value);

        if (string.IsNullOrEmpty(relativeTime))
        {
            return null;
        }

        return this.IncludeParentheses ? $"({relativeTime})" : relativeTime;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static string GetRelativeTimeString(DateTime targetTime)
    {
        var diff = targetTime - DateTime.Now;

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
