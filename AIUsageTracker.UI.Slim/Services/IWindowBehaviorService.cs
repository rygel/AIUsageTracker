// <copyright file="IWindowBehaviorService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows;

namespace AIUsageTracker.UI.Slim.Services;

/// <summary>
/// Service interface for managing window behavior, especially topmost/always-on-top functionality.
/// </summary>
public interface IWindowBehaviorService
{
    /// <summary>
    /// Ensures the window is displayed as topmost, using both WPF and Win32 methods.
    /// </summary>
    /// <param name="window">The window to make topmost.</param>
    void EnsureTopmost(Window window);

    /// <summary>
    /// Schedules a delayed topmost recovery operation.
    /// </summary>
    /// <param name="window">The window to recover topmost state for.</param>
    /// <param name="delay">The delay before attempting recovery.</param>
    void ScheduleTopmostRecovery(Window window, TimeSpan delay);

    /// <summary>
    /// Applies Win32 topmost positioning to a window handle.
    /// </summary>
    /// <param name="handle">The window handle.</param>
    /// <param name="noActivate">Whether to avoid activating the window.</param>
    /// <param name="alwaysOnTop">Whether to set always-on-top (true) or remove it (false).</param>
    void ApplyWin32Topmost(IntPtr handle, bool noActivate, bool alwaysOnTop = true);

    /// <summary>
    /// Gets a diagnostic summary of the currently focused foreground window.
    /// </summary>
    /// <returns>A summary string describing the foreground window.</returns>
    string GetForegroundWindowSummary();
}
