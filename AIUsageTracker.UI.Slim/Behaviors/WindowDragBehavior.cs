// <copyright file="WindowDragBehavior.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows;
using System.Windows.Input;

namespace AIUsageTracker.UI.Slim.Behaviors;

/// <summary>
/// Attached behavior that enables window dragging when the mouse is pressed on an element.
/// </summary>
/// <remarks>
/// Usage in XAML:
/// <code>
/// &lt;Border behaviors:WindowDragBehavior.EnableDrag="True" /&gt;
/// </code>
/// </remarks>
public static class WindowDragBehavior
{
    /// <summary>
    /// Identifies the EnableDrag attached property.
    /// </summary>
    public static readonly DependencyProperty EnableDragProperty =
        DependencyProperty.RegisterAttached(
            "EnableDrag",
            typeof(bool),
            typeof(WindowDragBehavior),
            new PropertyMetadata(defaultValue: false, propertyChangedCallback: OnEnableDragChanged));

    /// <summary>
    /// Gets the EnableDrag property value for the specified element.
    /// </summary>
    /// <param name="element">The element to get the property from.</param>
    /// <returns>True if drag is enabled; otherwise, false.</returns>
    public static bool GetEnableDrag(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(EnableDragProperty);
    }

    /// <summary>
    /// Sets the EnableDrag property value for the specified element.
    /// </summary>
    /// <param name="element">The element to set the property on.</param>
    /// <param name="value">True to enable drag; otherwise, false.</param>
    public static void SetEnableDrag(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(EnableDragProperty, value);
    }

    private static void OnEnableDragChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            element.MouseLeftButtonDown += OnMouseLeftButtonDown;
        }
        else
        {
            element.MouseLeftButtonDown -= OnMouseLeftButtonDown;
        }
    }

    private static void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DependencyObject element)
        {
            return;
        }

        var window = Window.GetWindow(element);
        if (window != null && e.LeftButton == MouseButtonState.Pressed)
        {
            window.DragMove();
        }
    }
}
