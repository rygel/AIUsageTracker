// <copyright file="JsonConfigFileStore.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Configuration;

internal static class JsonConfigFileStore
{
    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

    public static async Task<Dictionary<string, JsonElement>?> ReadJsonElementMapAsync(
        string path,
        ILogger logger)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogDebug(ex, "Failed to read JSON element map from {Path}", path);
            return null;
        }
    }

    public static async Task<T?> ReadAsync<T>(string path, ILogger logger)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogDebug(ex, "Failed to deserialize {TypeName} from {Path}", typeof(T).Name, path);
            return default;
        }
    }

    public static async Task WriteIndentedAsync<T>(string path, T value)
    {
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, IndentedOptions)).ConfigureAwait(false);
    }
}
