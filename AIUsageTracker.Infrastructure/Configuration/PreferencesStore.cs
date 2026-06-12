// <copyright file="PreferencesStore.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Configuration;

/// <summary>
/// Reads and writes AppPreferences to the shared preferences.json file.
/// Shared across Desktop, Monitor, and Web projects.
/// </summary>
public class PreferencesStore
{
    private readonly ILogger<PreferencesStore> _logger;
    private readonly IAppPathProvider _pathProvider;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
    };

    private string? _preferencesPathOverride;

    public PreferencesStore(ILogger<PreferencesStore> logger, IAppPathProvider pathProvider)
    {
        this._logger = logger;
        this._pathProvider = pathProvider;
    }

    private static readonly TimeSpan[] LoadRetryBackoff =
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(300),
        TimeSpan.FromMilliseconds(500),
    ];

    public async Task<AppPreferences> LoadAsync()
    {
        var path = this.GetPreferencesPath();
        if (!File.Exists(path))
        {
            return new AppPreferences();
        }

        for (var attempt = 0; ; attempt++)
        {
            try
            {
#pragma warning disable MA0004
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
#pragma warning restore MA0004
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync().ConfigureAwait(false);
                return AppPreferences.Deserialize(json);
            }
            catch (JsonException ex)
            {
                // Corrupt JSON — retrying won't help.
                this._logger.LogWarning(ex, "Preferences file is corrupt at {Path}", path);
                return new AppPreferences();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < LoadRetryBackoff.Length)
            {
                // Transient file lock (e.g. during update restart) — retry.
                this._logger.LogDebug(ex, "Preferences file locked at {Path}, retry {Attempt}", path, attempt + 1);
                await Task.Delay(LoadRetryBackoff[attempt]).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Retries exhausted — return defaults rather than throwing.
                this._logger.LogWarning(ex, "Failed to load preferences from {Path} after {Attempts} retries", path, LoadRetryBackoff.Length);
                return new AppPreferences();
            }
        }
    }

    public async Task<bool> SaveAsync(AppPreferences preferences)
    {
        var path = this.GetPreferencesPath();
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            this._logger.LogWarning("Failed to resolve preferences directory");
            return false;
        }

        try
        {
            Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(preferences, this._jsonOptions);
            await AtomicFileWriter.WriteAllTextAtomicAsync(
                path,
                json,
                this._logger).ConfigureAwait(false);
            return true;
        }
        catch (IOException ex)
        {
            this._logger.LogWarning(ex, "Failed to write preferences");
        }
        catch (UnauthorizedAccessException ex)
        {
            this._logger.LogWarning(ex, "Access denied writing preferences");
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
