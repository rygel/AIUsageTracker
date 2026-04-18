// <copyright file="MonitorJsonSerializer.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIUsageTracker.Core.MonitorClient;

/// <summary>
/// Single source of truth for JSON serialization options used across the Monitor API:
/// the Monitor HTTP endpoints, the MonitorService client, and the usage database.
/// All three layers must use identical options so that data survives every round-trip intact.
/// </summary>
public static class MonitorJsonSerializer
{
    /// <summary>
    /// Gets default options: snake_case property names, case-insensitive reads, string enums in snake_case.
    /// </summary>
    public static JsonSerializerOptions DefaultOptions { get; } = CreateDefaultOptions();

    /// <summary>
    /// Applies the default options to an existing <see cref="JsonSerializerOptions"/> instance.
    /// Used by ASP.NET Core's <c>ConfigureHttpJsonOptions</c>, which owns the options object.
    /// </summary>
    public static void Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.PropertyNameCaseInsensitive = true;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
    }

    private static JsonSerializerOptions CreateDefaultOptions()
    {
        var options = new JsonSerializerOptions();
        Configure(options);
        return options;
    }
}
