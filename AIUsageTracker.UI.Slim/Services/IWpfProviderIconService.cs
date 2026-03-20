// <copyright file="IWpfProviderIconService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows;

namespace AIUsageTracker.UI.Slim.Services;

/// <summary>
/// Returns a WPF icon element for a provider, loading an SVG asset or falling back to an initial badge.
/// </summary>
internal interface IWpfProviderIconService
{
    /// <summary>
    /// Returns a 16×16 provider icon for <paramref name="providerId"/>.
    /// First tries an SVG asset; falls back to a coloured initial badge.
    /// </summary>
    FrameworkElement CreateIcon(string providerId);
}
