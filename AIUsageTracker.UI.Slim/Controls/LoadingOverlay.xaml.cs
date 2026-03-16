// <copyright file="LoadingOverlay.xaml.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows;
using System.Windows.Controls;

namespace AIUsageTracker.UI.Slim.Controls;

/// <summary>
/// A loading overlay control that displays a progress indicator and optional message.
/// </summary>
public partial class LoadingOverlay : UserControl
{
    public static new readonly DependencyProperty IsVisibleProperty =
        DependencyProperty.Register(
            nameof(IsVisible),
            typeof(bool),
            typeof(LoadingOverlay),
            new PropertyMetadata(false));

    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(
            nameof(Message),
            typeof(string),
            typeof(LoadingOverlay),
            new PropertyMetadata(string.Empty, OnMessageChanged));

    public LoadingOverlay()
    {
        this.InitializeComponent();
    }

    /// <summary>
    /// Gets or sets a value indicating whether the overlay is visible.
    /// </summary>
    public new bool IsVisible
    {
        get => (bool)this.GetValue(IsVisibleProperty);
        set => this.SetValue(IsVisibleProperty, value);
    }

    /// <summary>
    /// Gets or sets the loading message to display.
    /// </summary>
    public string Message
    {
        get => (string)this.GetValue(MessageProperty);
        set => this.SetValue(MessageProperty, value);
    }

    /// <summary>
    /// Gets a value indicating whether there is a message to display.
    /// </summary>
    public bool HasMessage => !string.IsNullOrWhiteSpace(this.Message);

    private static void OnMessageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LoadingOverlay overlay)
        {
            // Notify that HasMessage may have changed
            overlay.OnPropertyChanged(new DependencyPropertyChangedEventArgs(
                MessageProperty,
                e.OldValue,
                e.NewValue));
        }
    }

    private new void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        // This triggers a re-evaluation of HasMessage binding
    }
}
