using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AIConsumptionTracker.Core.Models;

namespace AIConsumptionTracker.UI.Slim;

internal static class UiPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string GetPreferencesPath()
    {
        var basePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIConsumptionTracker",
            "UI.Slim");
        return Path.Combine(basePath, "preferences.json");
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
            Debug.WriteLine($"Failed to parse Slim preferences: {ex.Message}");
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"Failed to read Slim preferences: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"Access denied reading Slim preferences: {ex.Message}");
        }

        return new AppPreferences();
    }

    public static async Task<bool> SaveAsync(AppPreferences preferences)
    {
        var path = GetPreferencesPath();
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            Debug.WriteLine("Failed to resolve Slim preferences directory.");
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
            Debug.WriteLine($"Failed to write Slim preferences: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"Access denied writing Slim preferences: {ex.Message}");
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
            Debug.WriteLine($"Failed to parse legacy Slim preferences: {ex.Message}");
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"Failed to read legacy Slim preferences: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"Access denied reading legacy Slim preferences: {ex.Message}");
        }

        return null;
    }
}
