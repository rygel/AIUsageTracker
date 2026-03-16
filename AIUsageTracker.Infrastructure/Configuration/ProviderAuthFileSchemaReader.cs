// <copyright file="ProviderAuthFileSchemaReader.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using AIUsageTracker.Core.Helpers;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Infrastructure.Configuration;

internal static class ProviderAuthFileSchemaReader
{
    public static ProviderAuthData? Read(
        JsonElement root,
        IEnumerable<ProviderAuthFileSchema> schemas)
    {
        foreach (var schema in schemas)
        {
            if (!TryResolveSchemaRoot(root, schema.RootProperty, out var sessionRoot) ||
                sessionRoot.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var accessToken = sessionRoot.ReadString(schema.AccessTokenProperty);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                continue;
            }

            var accountId = !string.IsNullOrWhiteSpace(schema.AccountIdProperty)
                ? sessionRoot.ReadString(schema.AccountIdProperty)
                : null;

            var identityToken = !string.IsNullOrWhiteSpace(schema.IdentityTokenProperty)
                ? sessionRoot.ReadString(schema.IdentityTokenProperty)
                : null;

            return new ProviderAuthData(accessToken, accountId, identityToken);
        }

        return null;
    }

    private static bool TryResolveSchemaRoot(
        JsonElement root,
        string rootProperty,
        out JsonElement sessionRoot)
    {
        sessionRoot = root;
        var parts = rootProperty.Split('.');
        var lastPart = parts[^1];

        foreach (var part in parts)
        {
            if (!sessionRoot.TryGetProperty(part, out sessionRoot) ||
                (sessionRoot.ValueKind != JsonValueKind.Object && !string.Equals(part, lastPart, StringComparison.Ordinal)))
            {
                return false;
            }
        }

        return true;
    }
}
