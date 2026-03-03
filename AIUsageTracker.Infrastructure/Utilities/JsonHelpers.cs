using System.Globalization;
using System.Text.Json;

namespace AIUsageTracker.Infrastructure.Utilities;

public static class JsonHelpers
{
    public static string? ReadString(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (!current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    public static double? ReadDouble(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (!current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        if (current.ValueKind == JsonValueKind.Number && current.TryGetDouble(out var number))
        {
            return number;
        }

        if (current.ValueKind == JsonValueKind.String &&
            double.TryParse(current.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    public static bool? ReadBool(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (!current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(current.GetString(), out var boolValue) => boolValue,
            JsonValueKind.Number when current.TryGetDouble(out var doubleValue) && Math.Abs(doubleValue - 1) < 0.0001 => doubleValue == 1,
            _ => null
        };
    }

    public static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement? property)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            property = null;
            return false;
        }

        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                property = prop.Value;
                return true;
            }
        }

        property = null;
        return false;
    }

    public static bool TryGetDoubleProperty(JsonElement source, string propertyName, out double value)
    {
        value = 0;
        if (source.ValueKind != JsonValueKind.Object ||
            !TryGetPropertyIgnoreCase(source, propertyName, out var property))
        {
            return false;
        }

        if (property is null)
        {
            return false;
        }

        if (property.Value.ValueKind == JsonValueKind.Number)
        {
            return property.Value.TryGetDouble(out value);
        }

        if (property.Value.ValueKind == JsonValueKind.String)
        {
            var text = property.Value.GetString();
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        return false;
    }

    public static bool TryGetDoubleCandidate(JsonElement source, out double value, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (TryGetDoubleProperty(source, candidate, out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }
}
