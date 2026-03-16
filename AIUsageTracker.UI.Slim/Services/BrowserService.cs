// <copyright file="BrowserService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Net.Http;
using AIUsageTracker.Core.Updates;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim.Services;

/// <summary>
/// Service for browser-related operations.
/// </summary>
public class BrowserService : IBrowserService
{
    private const string WebUiUrl = "http://localhost:5100";
    private readonly ILogger<BrowserService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrowserService"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public BrowserService(ILogger<BrowserService> logger)
    {
        this._logger = logger;
    }

    /// <inheritdoc/>
    public void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to open URL: {Url}", url);
        }
    }

    /// <inheritdoc/>
    public async Task OpenWebUIAsync()
    {
        try
        {
            // Check if web service is already running
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            try
            {
                var response = await client.GetAsync(WebUiUrl).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    this._logger.LogDebug("Web service responded with status: {Status}", response.StatusCode);
                }
            }
            catch (HttpRequestException)
            {
                // Service not running - that's OK, we'll try to start it
                this._logger.LogDebug("Web service not running, attempting to start");
            }
            catch (TaskCanceledException)
            {
                // Timeout - service may be starting
                this._logger.LogDebug("Web service connection timed out");
            }

            // Open browser to the Web UI
            this.OpenUrl(WebUiUrl);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to open Web UI");
            throw;
        }
    }

    /// <inheritdoc/>
    public void OpenReleasesPage()
    {
        this.OpenUrl(ReleaseUrlCatalog.GetReleasesPageUrl());
    }
}
