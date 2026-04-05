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
        fallback ??= Brushes.Gray;
        try
        {
            if (Application.Current?.Resources[key] is SolidColorBrush brush)
            {
                return brush;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Windows.Markup.XamlParseException)
        {
            // Design-time or resource not found
        }

        return fallback;
    }
}
