// <copyright file="CloseWindowBehavior.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows;
using System.Windows.Controls;

namespace AIUsageTracker.UI.Slim.Behaviors;

/// <summary>
/// Attached behavior for closing windows on button click.
/// </summary>
/// <remarks>
/// Usage in XAML:
/// <code>
/// &lt;Button behaviors:CloseWindowBehavior.CloseOnClick="True" /&gt;
/// </code>
/// For hiding instead of closing:
/// <code>
/// &lt;Button behaviors:CloseWindowBehavior.HideOnClick="True" /&gt;
/// </code>
/// </remarks>
public static class CloseWindowBehavior
{
    /// <summary>
    /// Identifies the CloseOnClick attached property.
    /// </summary>
    public static readonly DependencyProperty CloseOnClickProperty =
        DependencyProperty.RegisterAttached(
            "CloseOnClick",
            typeof(bool),
            typeof(CloseWindowBehavior),
            new PropertyMetadata(defaultValue: false, propertyChangedCallback: OnCloseOnClickChanged));

    /// <summary>
    /// Identifies the HideOnClick attached property.
    /// </summary>
    public static readonly DependencyProperty HideOnClickProperty =
        DependencyProperty.RegisterAttached(
            "HideOnClick",
            typeof(bool),
            typeof(CloseWindowBehavior),
            new PropertyMetadata(defaultValue: false, propertyChangedCallback: OnHideOnClickChanged));

    /// <summary>
    /// Gets the CloseOnClick property value for the specified button.
    /// </summary>
    /// <param name="button">The button to get the property from.</param>
    /// <returns>True if the window should close on click; otherwise, false.</returns>
    public static bool GetCloseOnClick(Button button)
    {
        ArgumentNullException.ThrowIfNull(button);
        return (bool)button.GetValue(CloseOnClickProperty);
    }

    /// <summary>
    /// Sets the CloseOnClick property value for the specified button.
    /// </summary>
    /// <param name="button">The button to set the property on.</param>
    /// <param name="value">True to close the window on click; otherwise, false.</param>
    public static void SetCloseOnClick(Button button, bool value)
    {
        ArgumentNullException.ThrowIfNull(button);
        button.SetValue(CloseOnClickProperty, value);
    }

    /// <summary>
    /// Gets the HideOnClick property value for the specified button.
    /// </summary>
    /// <param name="button">The button to get the property from.</param>
    /// <returns>True if the window should hide on click; otherwise, false.</returns>
    public static bool GetHideOnClick(Button button)
    {
        ArgumentNullException.ThrowIfNull(button);
        return (bool)button.GetValue(HideOnClickProperty);
    }

    /// <summary>
    /// Sets the HideOnClick property value for the specified button.
    /// </summary>
    /// <param name="button">The button to set the property on.</param>
    /// <param name="value">True to hide the window on click; otherwise, false.</param>
    public static void SetHideOnClick(Button button, bool value)
    {
        ArgumentNullException.ThrowIfNull(button);
        button.SetValue(HideOnClickProperty, value);
    }

    private static void OnCloseOnClickChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Button button)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            button.Click += OnCloseClick;
        }
        else
        {
            button.Click -= OnCloseClick;
        }
    }

    private static void OnHideOnClickChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Button button)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            button.Click += OnHideClick;
        }
        else
        {
            button.Click -= OnHideClick;
        }
    }

    private static void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (sender is not DependencyObject element)
        {
            return;
        }

        var window = Window.GetWindow(element);
        window?.Close();
    }

    private static void OnHideClick(object sender, RoutedEventArgs e)
    {
        if (sender is not DependencyObject element)
        {
            return;
        }

        var window = Window.GetWindow(element);
        window?.Hide();
    }
}
