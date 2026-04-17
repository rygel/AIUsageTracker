// <copyright file="MonitorDiagnosticsSnapshotFactory.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using AIUsageTracker.Monitor.Services;

namespace AIUsageTracker.Monitor.Endpoints;

internal static class MonitorDiagnosticsSnapshotFactory
{
    public static MonitorDiagnosticsSnapshot Create(
        EndpointDataSource endpointDataSource,
        int port,
        IReadOnlyList<string> args,
        RefreshTelemetrySnapshot refreshTelemetry,
        MonitorJobSchedulerSnapshot schedulerTelemetry,
        ProviderUsageProcessingTelemetrySnapshot pipelineTelemetry)
    {
        ArgumentNullException.ThrowIfNull(endpointDataSource);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(refreshTelemetry);
        ArgumentNullException.ThrowIfNull(schedulerTelemetry);
        ArgumentNullException.ThrowIfNull(pipelineTelemetry);

        return new MonitorDiagnosticsSnapshot
        {
            Port = port,
            ProcessId = Environment.ProcessId,
            WorkingDir = Directory.GetCurrentDirectory(),
            BaseDir = AppDomain.CurrentDomain.BaseDirectory,
            StartedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            Os = Environment.OSVersion.ToString(),
            Runtime = Environment.Version.ToString(),
            Args = args,
            Endpoints = BuildApiEndpoints(endpointDataSource),
            RefreshTelemetry = refreshTelemetry,
            SchedulerTelemetry = schedulerTelemetry,
            PipelineTelemetry = pipelineTelemetry,
            Observability = new MonitorObservabilitySnapshot
            {
                ActivitySourceNames =
                [
                    MonitorActivitySources.RefreshSourceName,
                    MonitorActivitySources.SchedulerSourceName,
                ],
            },
        };
    }

    private static MonitorApiEndpointDescriptor[] BuildApiEndpoints(EndpointDataSource endpointDataSource)
    {
        return endpointDataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) == true)
            .GroupBy(endpoint => endpoint.RoutePattern.RawText!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new MonitorApiEndpointDescriptor
            {
                Route = group.Key,
                Methods = group
                    .SelectMany(endpoint => endpoint.Metadata
                        .OfType<HttpMethodMetadata>()
                        .SelectMany(metadata => metadata.HttpMethods))
                    .Where(method => !string.IsNullOrWhiteSpace(method))
                    .Select(method => method.ToUpperInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(method => method, StringComparer.Ordinal)
                    .ToArray(),
            })
            .OrderBy(endpoint => endpoint.Route, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
