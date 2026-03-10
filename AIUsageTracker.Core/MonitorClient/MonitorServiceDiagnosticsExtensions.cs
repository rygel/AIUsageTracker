// <copyright file="MonitorServiceDiagnosticsExtensions.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageTracker.Core.Interfaces;

namespace AIUsageTracker.Core.MonitorClient;

public static class MonitorServiceDiagnosticsExtensions
{
    public static async Task<AgentDiagnosticsSnapshot?> GetDiagnosticsSnapshotAsync(this IMonitorService monitorService)
    {
        ArgumentNullException.ThrowIfNull(monitorService);

        if (monitorService is MonitorService typedMonitorService)
        {
            return await typedMonitorService.GetDiagnosticsSnapshotAsync().ConfigureAwait(false);
        }

        var diagnosticsDetails = await monitorService.GetDiagnosticsDetailsAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(diagnosticsDetails))
        {
            return null;
        }

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
            };

            return JsonSerializer.Deserialize<AgentDiagnosticsSnapshot>(diagnosticsDetails, options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
