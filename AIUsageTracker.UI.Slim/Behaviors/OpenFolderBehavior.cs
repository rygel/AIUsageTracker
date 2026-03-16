// <copyright file="OpenFolderBehavior.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim.Behaviors;

/// <summary>
/// Attached behavior for opening folders in Windows Explorer.
/// </summary>
/// <remarks>
/// Usage in XAML:
/// <code>
/// &lt;Hyperlink behaviors:OpenFolderBehavior.FolderPath="{Binding ConfigDirectory}" /&gt;
/// </code>
/// </remarks>
public static class OpenFolderBehavior
{
    /// <summary>
    /// Identifies the FolderPath attached property.
    /// </summary>
    public static readonly DependencyProperty FolderPathProperty =
        DependencyProperty.RegisterAttached(
            "FolderPath",
            typeof(string),
            typeof(OpenFolderBehavior),
            new PropertyMetadata(null, OnFolderPathChanged));

    /// <summary>
    /// Gets the FolderPath property value for the specified element.
    /// </summary>
    /// <param name="element">The element to get the property from.</param>
    /// <returns>The folder path.</returns>
    public static string? GetFolderPath(DependencyObject element)
    {
        return (string?)element.GetValue(FolderPathProperty);
    }

    /// <summary>
    /// Sets the FolderPath property value for the specified element.
    /// </summary>
    /// <param name="element">The element to set the property on.</param>
    /// <param name="value">The folder path to open when clicked.</param>
    public static void SetFolderPath(DependencyObject element, string? value)
    {
        element.SetValue(FolderPathProperty, value);
    }

    private static void OnFolderPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Hyperlink hyperlink)
        {
            if (e.NewValue != null)
            {
                hyperlink.Click += OnHyperlinkClick;
            }
            else
            {
                hyperlink.Click -= OnHyperlinkClick;
            }
        }
    }

    private static void OnHyperlinkClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Hyperlink hyperlink)
        {
            return;
        }

        var path = GetFolderPath(hyperlink);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        OpenFolder(path);
    }

    /// <summary>
    /// Opens the specified folder in Windows Explorer.
    /// </summary>
    /// <param name="path">The folder path to open.</param>
    public static void OpenFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true,
                });
            }
            else if (File.Exists(path))
            {
                // If it's a file, open the containing folder and select the file
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true,
                });
            }
            else
            {
                var logger = App.CreateLogger<object>();
                logger.LogWarning("Path does not exist: {Path}", path);
            }
        }
        catch (Exception ex)
        {
            var logger = App.CreateLogger<object>();
            logger.LogError(ex, "Failed to open folder: {Path}", path);
        }
    }
}
