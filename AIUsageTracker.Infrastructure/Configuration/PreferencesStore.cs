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
public class PreferencesStore : IPreferencesStore
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

    public async Task<AppPreferences> LoadAsync()
    {
        var path = this.GetPreferencesPath();
        if (!File.Exists(path))
        {
            return new AppPreferences();
        }

        try
        {
            // Use FileShare.ReadWrite so the read succeeds even when the monitor
            // or a previous app instance holds the file open for writing.
#pragma warning disable MA0004
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
#pragma warning restore MA0004
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync().ConfigureAwait(false);
            return AppPreferences.Deserialize(json);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            this._logger.LogWarning(ex, "Failed to load preferences from {Path}", path);
            var backupPath = GetBackupPath(path);
            if (!File.Exists(backupPath))
            {
                throw;
            }

            this._logger.LogWarning("Attempting to load preferences backup from {BackupPath}", backupPath);
            var backupJson = await File.ReadAllTextAsync(backupPath).ConfigureAwait(false);
            return AppPreferences.Deserialize(backupJson);
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
                this._logger,
                backupPath: GetBackupPath(path)).ConfigureAwait(false);
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

    private static string GetBackupPath(string preferencesPath) => $"{preferencesPath}.bak";
}
