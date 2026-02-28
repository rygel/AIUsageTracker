using System.IO;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim;

internal static class UiPreferencesStore
{
    private static readonly ILogger _logger = App.CreateLogger<App>();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string GetPreferencesPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var primaryPath = Path.Combine(appData, "AIUsageTracker", "preferences.json");
        var legacyPath = Path.Combine(appData, "AIConsumptionTracker", "preferences.json");

        if (File.Exists(primaryPath))
        {
            return primaryPath;
        }

        if (File.Exists(legacyPath))
        {
            return legacyPath;
        }

        return primaryPath;
    }

    public static async Task<AppPreferences> LoadAsync()
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

    public static async Task<bool> SaveAsync(AppPreferences preferences)
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
            var json = JsonSerializer.Serialize(preferences, JsonOptions);
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

    private static async Task<AppPreferences?> TryLoadLegacyPreferencesAsync()
    {
        var legacyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".ai-consumption-tracker",
            "auth.json");
        if (!File.Exists(legacyPath))
        {
            return null;
        }

        try
        {
            var legacyJson = await File.ReadAllTextAsync(legacyPath);
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
            _logger.LogWarning(ex, "Failed to parse legacy Slim preferences");
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to read legacy Slim preferences");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied reading legacy Slim preferences");
        }

        return null;
    }
}

