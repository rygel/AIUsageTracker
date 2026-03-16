// <copyright file="IBrowserService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim.Services;

/// <summary>
/// Service interface for browser-related operations.
/// </summary>
public interface IBrowserService
{
    /// <summary>
    /// Opens a URL in the default browser.
    /// </summary>
    /// <param name="url">The URL to open.</param>
    void OpenUrl(string url);

    /// <summary>
    /// Opens the Web UI dashboard.
    /// </summary>
    /// <returns>A task representing the async operation.</returns>
    Task OpenWebUIAsync();

    /// <summary>
    /// Opens the releases page.
    /// </summary>
    void OpenReleasesPage();
}
