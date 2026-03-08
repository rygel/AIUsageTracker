using System.IO;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Paths;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim;

public class UiPreferencesStore
{
    private readonly ILogger<UiPreferencesStore> _logger;
    private readonly IAppPathProvider _pathProvider;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };
    private string? _preferencesPathOverride;

    public UiPreferencesStore(ILogger<UiPreferencesStore> logger, IAppPathProvider pathProvider)
    {
        _logger = logger;
        _pathProvider = pathProvider;
    }

    internal void SetPreferencesPathOverrideForTesting(string? path)
    {
        _preferencesPathOverride = string.IsNullOrWhiteSpace(path) ? null : path;
    }

    private string GetPreferencesPath()
    {
        if (!string.IsNullOrWhiteSpace(_preferencesPathOverride))
        {
            return _preferencesPathOverride;
        }

        return _pathProvider.GetPreferencesFilePath();
    }

    public async Task<AppPreferences> LoadAsync()
    {
        var path = GetPreferencesPath();
        if (!File.Exists(path))
        {
            var legacyPreferences = await TryLoadLegacyPreferencesAsync();
            return legacyPreferences ?? new AppPreferences();
        }

        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<AppPreferences>(json) ?? new AppPreferences();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse Slim preferences");
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to read Slim preferences");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied reading Slim preferences");
        }

        return new AppPreferences();
    }

    public async Task<bool> SaveAsync(AppPreferences preferences)
    {
        var path = GetPreferencesPath();
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            _logger.LogWarning("Failed to resolve Slim preferences directory");
            return false;
        }

        try
        {
            Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(preferences, _jsonOptions);
            await File.WriteAllTextAsync(path, json);
            return true;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to write Slim preferences");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied writing Slim preferences");
        }

        return false;
    }

    private async Task<AppPreferences?> TryLoadLegacyPreferencesAsync()
    {
        foreach (var legacyPath in this.GetLegacyPreferenceCandidates())
        {
            if (!File.Exists(legacyPath))
            {
                continue;
            }

            try
            {
                var legacyJson = await File.ReadAllTextAsync(legacyPath);
                if (legacyPath.EndsWith("preferences.json", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonSerializer.Deserialize<AppPreferences>(legacyJson);
                }

                var root = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(legacyJson);
                if (root != null &&
                    root.TryGetValue("app_settings", out var appSettings) &&
                    appSettings.ValueKind == JsonValueKind.Object)
                {
                    return JsonSerializer.Deserialize<AppPreferences>(appSettings.GetRawText());
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse legacy Slim preferences from {Path}", legacyPath);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Failed to read legacy Slim preferences from {Path}", legacyPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Access denied reading legacy Slim preferences from {Path}", legacyPath);
            }
        }

        return null;
    }

    private IEnumerable<string> GetLegacyPreferenceCandidates()
    {
        var userProfileRoot = _pathProvider.GetUserProfileRoot();

        return DeprecatedPathCatalog.GetAuthFilePaths(userProfileRoot)
            .Concat(DeprecatedPathCatalog.GetPreferencesFilePaths(userProfileRoot));
    }
}
