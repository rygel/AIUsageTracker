// <copyright file="KeyboardShortcutBehavior.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows;
using System.Windows.Input;

namespace AIUsageTracker.UI.Slim.Behaviors;

/// <summary>
/// Attached behavior for handling keyboard shortcuts on windows.
/// </summary>
/// <remarks>
/// Usage in XAML:
/// <code>
/// &lt;Window behaviors:KeyboardShortcutBehavior.CloseOnEscape="True" /&gt;
/// </code>
/// </remarks>
public static class KeyboardShortcutBehavior
{
    /// <summary>
    /// Identifies the CloseOnEscape attached property.
    /// </summary>
    public static readonly DependencyProperty CloseOnEscapeProperty =
        DependencyProperty.RegisterAttached(
            "CloseOnEscape",
            typeof(bool),
            typeof(KeyboardShortcutBehavior),
            new PropertyMetadata(false, OnCloseOnEscapeChanged));

    /// <summary>
    /// Identifies the HideOnEscape attached property.
    /// </summary>
    public static readonly DependencyProperty HideOnEscapeProperty =
        DependencyProperty.RegisterAttached(
            "HideOnEscape",
            typeof(bool),
            typeof(KeyboardShortcutBehavior),
            new PropertyMetadata(false, OnHideOnEscapeChanged));

    /// <summary>
    /// Identifies the RefreshCommand attached property.
    /// </summary>
    public static readonly DependencyProperty RefreshCommandProperty =
        DependencyProperty.RegisterAttached(
            "RefreshCommand",
            typeof(ICommand),
            typeof(KeyboardShortcutBehavior),
            new PropertyMetadata(null, OnRefreshCommandChanged));

    /// <summary>
    /// Gets the CloseOnEscape property value for the specified window.
    /// </summary>
    /// <param name="window">The window to get the property from.</param>
    /// <returns>True if the window should close on Escape; otherwise, false.</returns>
    public static bool GetCloseOnEscape(Window window)
    {
        return (bool)window.GetValue(CloseOnEscapeProperty);
    }

    /// <summary>
    /// Sets the CloseOnEscape property value for the specified window.
    /// </summary>
    /// <param name="window">The window to set the property on.</param>
    /// <param name="value">True to close the window on Escape; otherwise, false.</param>
    public static void SetCloseOnEscape(Window window, bool value)
    {
        window.SetValue(CloseOnEscapeProperty, value);
    }

    /// <summary>
    /// Gets the HideOnEscape property value for the specified window.
    /// </summary>
    /// <param name="window">The window to get the property from.</param>
    /// <returns>True if the window should hide on Escape; otherwise, false.</returns>
    public static bool GetHideOnEscape(Window window)
    {
        return (bool)window.GetValue(HideOnEscapeProperty);
    }

    /// <summary>
    /// Sets the HideOnEscape property value for the specified window.
    /// </summary>
    /// <param name="window">The window to set the property on.</param>
    /// <param name="value">True to hide the window on Escape; otherwise, false.</param>
    public static void SetHideOnEscape(Window window, bool value)
    {
        window.SetValue(HideOnEscapeProperty, value);
    }

    /// <summary>
    /// Gets the RefreshCommand property value for the specified window.
    /// </summary>
    /// <param name="window">The window to get the property from.</param>
    /// <returns>The command to execute on Ctrl+R.</returns>
    public static ICommand? GetRefreshCommand(Window window)
    {
        return (ICommand?)window.GetValue(RefreshCommandProperty);
    }

    /// <summary>
    /// Sets the RefreshCommand property value for the specified window.
    /// </summary>
    /// <param name="window">The window to set the property on.</param>
    /// <param name="value">The command to execute on Ctrl+R.</param>
    public static void SetRefreshCommand(Window window, ICommand? value)
    {
        window.SetValue(RefreshCommandProperty, value);
    }

    private static void OnCloseOnEscapeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            window.KeyDown += OnCloseOnEscapeKeyDown;
        }
        else
        {
            window.KeyDown -= OnCloseOnEscapeKeyDown;
        }
    }

    private static void OnHideOnEscapeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            window.KeyDown += OnHideOnEscapeKeyDown;
        }
        else
        {
            window.KeyDown -= OnHideOnEscapeKeyDown;
        }
    }

    private static void OnRefreshCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window)
        {
            return;
        }

        if (e.NewValue != null)
        {
            window.KeyDown += OnRefreshKeyDown;
        }
        else
        {
            window.KeyDown -= OnRefreshKeyDown;
        }
    }

    private static void OnCloseOnEscapeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && sender is Window window)
        {
            window.Close();
            e.Handled = true;
        }
    }

    private static void OnHideOnEscapeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && sender is Window window)
        {
            window.Hide();
            e.Handled = true;
        }
    }

    private static void OnRefreshKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.R &&
            Keyboard.Modifiers == ModifierKeys.Control &&
            sender is Window window)
        {
            var command = GetRefreshCommand(window);
            if (command?.CanExecute(null) == true)
            {
                command.Execute(null);
                e.Handled = true;
            }
        }
    }
}
