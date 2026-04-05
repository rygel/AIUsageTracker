// <copyright file="BrowserService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.IO;
using System.Net.Http;
using AIUsageTracker.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim.Services;

/// <summary>
/// Service for browser-related operations.
/// </summary>
public class BrowserService : IBrowserService
{
    private const string WebUiUrl = "http://localhost:5100";
    private const string WebProjectName = "AIUsageTracker.Web";
    private readonly ILogger<BrowserService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrowserService"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    public BrowserService(ILogger<BrowserService> logger, IHttpClientFactory httpClientFactory)
    {
        this._logger = logger;
        this._httpClientFactory = httpClientFactory;
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
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            this._logger.LogError(ex, "Failed to open URL: {Url}", url);
        }
    }

    /// <inheritdoc/>
    public async Task OpenWebUIAsync()
    {
        try
        {
            var isServiceRunning = false;

            // Check if web service is already running.
            using var client = this._httpClientFactory.CreateClient("LocalhostProbe");
            try
            {
                var response = await client.GetAsync(WebUiUrl).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    isServiceRunning = true;
                }
                else
                {
                    this._logger.LogDebug("Web service responded with status: {Status}", response.StatusCode);
                }
            }
            catch (HttpRequestException ex)
            {
                this._logger.LogDebug(ex, "Web service not reachable, attempting to start it");
            }
            catch (TaskCanceledException ex)
            {
                this._logger.LogDebug(ex, "Web service probe timed out, attempting to start it");
            }

            if (!isServiceRunning)
            {
                this.StartWebService();
            }

            // Open browser to the Web UI.
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
        this.OpenUrl(GitHubUpdateChecker.GetReleasesPageUrl());
    }

    private void StartWebService()
    {
        var possiblePaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", WebProjectName, "bin", "Debug", "net8.0", $"{WebProjectName}.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", WebProjectName, "bin", "Release", "net8.0", $"{WebProjectName}.exe"),
            Path.Combine(AppContext.BaseDirectory, $"{WebProjectName}.exe"),
        };

        var webExecutablePath = possiblePaths.FirstOrDefault(File.Exists);

        if (string.IsNullOrWhiteSpace(webExecutablePath))
        {
            var webProjectDirectory = FindProjectDirectory(WebProjectName);
            if (!string.IsNullOrWhiteSpace(webProjectDirectory))
            {
                var startFromProject = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"run --project \"{webProjectDirectory}\" --urls \"{WebUiUrl}\"",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WorkingDirectory = webProjectDirectory,
                };

                Process.Start(startFromProject);
                this._logger.LogInformation("Started Web service via dotnet run from {ProjectDirectory}", webProjectDirectory);
                return;
            }

            this._logger.LogWarning("Web executable and project directory not found; cannot auto-start Web service.");
            return;
        }

        var startExecutable = new ProcessStartInfo
        {
            FileName = webExecutablePath,
            Arguments = $"--urls \"{WebUiUrl}\"",
            UseShellExecute = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(webExecutablePath),
        };

        Process.Start(startExecutable);
        this._logger.LogInformation("Started Web service executable from {ExecutablePath}", webExecutablePath);
    }

    private static string? FindProjectDirectory(string projectName)
    {
        var currentDirectory = AppContext.BaseDirectory;
        var searchDirectory = new DirectoryInfo(currentDirectory);

        while (searchDirectory != null)
        {
            var projectPath = Path.Combine(searchDirectory.FullName, projectName, $"{projectName}.csproj");
            if (File.Exists(projectPath))
            {
                return Path.GetDirectoryName(projectPath);
            }

            searchDirectory = searchDirectory.Parent;
        }

        return null;
    }
}
