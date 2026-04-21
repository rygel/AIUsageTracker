// <copyright file="UIHelper.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows;
using System.Windows.Media;

namespace AIUsageTracker.UI.Slim;

internal static class UIHelper
{
    public static SolidColorBrush GetResourceBrush(string key, SolidColorBrush? fallback = null)
    {
        if (Application.Current?.Resources[key] is SolidColorBrush brush)
        {
            return brush;
        }

        if (fallback != null)
        {
            return fallback;
        }

        throw new InvalidOperationException($"Missing SolidColorBrush resource '{key}'.");
    }
}
