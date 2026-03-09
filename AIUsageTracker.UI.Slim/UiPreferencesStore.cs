// <copyright file="UiPreferencesStore.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.IO;
using System.Text.Json;

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;

using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim;

public class UiPreferencesStore
{
    private readonly ILogger<UiPreferencesStore> _logger;
    private readonly IAppPathProvider _pathProvider;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
    };

    private string? _preferencesPathOverride;

    public UiPreferencesStore(ILogger<UiPreferencesStore> logger, IAppPathProvider pathProvider)
    {
        this._logger = logger;
        this._pathProvider = pathProvider;
    }

    public async Task<AppPreferences> LoadAsync()
    {
        var path = this.GetPreferencesPath();
        if (!File.Exists(path))
        {
            return new AppPreferences();
        }

        try
        {
            var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            return JsonSerializer.Deserialize<AppPreferences>(json) ?? new AppPreferences();
        }
        catch (JsonException ex)
        {
            this._logger.LogWarning(ex, "Failed to parse Slim preferences");
        }
        catch (IOException ex)
        {
            this._logger.LogWarning(ex, "Failed to read Slim preferences");
        }
        catch (UnauthorizedAccessException ex)
        {
            this._logger.LogWarning(ex, "Access denied reading Slim preferences");
        }

        return new AppPreferences();
    }

    public async Task<bool> SaveAsync(AppPreferences preferences)
    {
        var path = this.GetPreferencesPath();
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            this._logger.LogWarning("Failed to resolve Slim preferences directory");
            return false;
        }

        try
        {
            Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(preferences, this._jsonOptions);
            await File.WriteAllTextAsync(path, json).ConfigureAwait(false);
            return true;
        }
        catch (IOException ex)
        {
            this._logger.LogWarning(ex, "Failed to write Slim preferences");
        }
        catch (UnauthorizedAccessException ex)
        {
            this._logger.LogWarning(ex, "Access denied writing Slim preferences");
        }

        return false;
    }

    internal void SetPreferencesPathOverrideForTesting(string? path)
    {
        this._preferencesPathOverride = string.IsNullOrWhiteSpace(path) ? null : path;
    }

    private string GetPreferencesPath()
    {
        if (!string.IsNullOrWhiteSpace(this._preferencesPathOverride))
        {
            return this._preferencesPathOverride;
        }

        return this._pathProvider.GetPreferencesFilePath();
    }
}
