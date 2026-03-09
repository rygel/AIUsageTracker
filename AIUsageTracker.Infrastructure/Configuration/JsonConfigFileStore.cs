// <copyright file="JsonConfigFileStore.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Infrastructure.Configuration
{
    using System.Text.Json;
    using Microsoft.Extensions.Logging;

    internal static class JsonConfigFileStore
    {
        private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

        public static async Task<Dictionary<string, JsonElement>?> ReadJsonElementMapAsync(
            string path,
            ILogger logger,
            string failureMessage)
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
            catch (Exception ex)
            {
                logger.LogDebug(ex, failureMessage, path);
                return null;
            }
        }

        public static async Task<T?> ReadAsync<T>(string path, ILogger logger, string failureMessage)
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
            catch (Exception ex)
            {
                logger.LogDebug(ex, failureMessage, path);
                return default;
            }
        }

        public static async Task WriteIndentedAsync<T>(string path, T value)
        {
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, IndentedOptions)).ConfigureAwait(false);
        }
    }
}
