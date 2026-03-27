// <copyright file="JsonProviderConfigExportBuilder.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;

using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Infrastructure.Configuration;

internal static class JsonProviderConfigExportBuilder
{
    public static void RemoveNonPersistedProviders(Dictionary<string, object> payload)
    {
        foreach (var providerId in payload.Keys.Where(id => !ProviderMetadataCatalog.ShouldPersistProviderId(id)).ToList())
        {
            payload.Remove(providerId);
        }
    }

    public static void MergeProviderConfig(
        Dictionary<string, object> exportAuth,
        Dictionary<string, object> exportProviders,
        ProviderConfig config)
    {
        if (!ProviderMetadataCatalog.ShouldPersistProviderId(config.ProviderId))
        {
            return;
        }

        var authDict = GetMutablePayloadEntry(exportAuth, config.ProviderId);
        authDict["key"] = config.ApiKey;
        exportAuth[config.ProviderId] = authDict;

        var providerDict = GetMutablePayloadEntry(exportProviders, config.ProviderId);
        providerDict["type"] = config.Type;
        providerDict["show_in_tray"] = config.ShowInTray;
        providerDict["enable_notifications"] = config.EnableNotifications;
        providerDict["enabled_sub_trays"] = config.EnabledSubTrays;

        if (!string.IsNullOrEmpty(config.BaseUrl))
        {
            providerDict["base_url"] = config.BaseUrl;
        }

        exportProviders[config.ProviderId] = providerDict;
    }

    private static Dictionary<string, object?> GetMutablePayloadEntry(Dictionary<string, object> payload, string providerId)
    {
        if (!payload.TryGetValue(providerId, out var existingValue))
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        if (existingValue is JsonElement existingElement)
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(existingElement.GetRawText())
                   ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        if (existingValue is Dictionary<string, object?> existingDictionary)
        {
            return existingDictionary;
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal);
    }
}
