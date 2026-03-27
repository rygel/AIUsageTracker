// <copyright file="JsonElementExtensions.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Text.Json;

namespace AIUsageTracker.Core.Helpers;

public static class JsonElementExtensions
{
    public static string? ReadString(this JsonElement root, params string[] path)
    {
        if (!TryNavigate(root, path, out var current))
        {
            return null;
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    public static double? ReadDouble(this JsonElement root, params string[] path)
    {
        if (!TryNavigate(root, path, out var current))
        {
            return null;
        }

        if (current.ValueKind == JsonValueKind.Number && current.TryGetDouble(out var number))
        {
            return number;
        }

        if (current.ValueKind == JsonValueKind.String)
        {
            var raw = current.GetString();
            if (!string.IsNullOrWhiteSpace(raw) &&
                double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    public static bool? ReadBool(this JsonElement root, params string[] path)
    {
        if (!TryNavigate(root, path, out var current))
        {
            return null;
        }

        return current.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static bool TryNavigate(JsonElement root, string[] path, out JsonElement result)
    {
        var navigated = path.Aggregate<string, JsonElement?>(
            root,
            (current, segment) => current.HasValue && current.Value.TryGetProperty(segment, out var next) ? next : null);

        result = navigated ?? default;
        return navigated.HasValue;
    }
}
