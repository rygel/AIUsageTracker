// <copyright file="MonitorDiagnosticsEndpoints.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Monitor.Endpoints
{
    using AIUsageTracker.Monitor.Services;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Routing;
    using Microsoft.Extensions.Logging;

    internal static class MonitorDiagnosticsEndpoints
    {
        public static void Map(
            WebApplication app,
            bool isDebugMode,
            int port,
            string agentVersion,
            string apiContractVersion,
            string[] args)
        {
            app.MapGet(MonitorApiRoutes.Health, (ILogger<Program> logger) =>
            {
                if (isDebugMode)
                {
                    logger.LogDebug("GET {Route}", MonitorApiRoutes.Health);
                }

                return Results.Ok(new
                {
                    status = "healthy",
                    timestamp = DateTime.UtcNow,
                    port,
                    processId = Environment.ProcessId,
                    agentVersion,
                    apiContractVersion,
                });
            });

            app.MapGet(MonitorApiRoutes.Diagnostics, (EndpointDataSource endpointDataSource, ProviderRefreshService refreshService, ILogger<Program> logger) =>
            {
                if (isDebugMode)
                {
                    logger.LogDebug("GET {Route}", MonitorApiRoutes.Diagnostics);
                }

                var apiEndpoints = endpointDataSource.Endpoints
                    .OfType<RouteEndpoint>()
                    .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) == true)
                    .GroupBy(endpoint => endpoint.RoutePattern.RawText!, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new
                    {
                        route = group.Key,
                        methods = group
                            .SelectMany(endpoint => endpoint.Metadata
                                .OfType<HttpMethodMetadata>()
                                .SelectMany(metadata => metadata.HttpMethods))
                            .Where(method => !string.IsNullOrWhiteSpace(method))
                            .Select(method => method.ToUpperInvariant())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(method => method)
                            .ToArray(),
                    })
                    .OrderBy(endpoint => endpoint.route)
                    .ToList();

                return Results.Ok(new
                {
                    port,
                    processId = Environment.ProcessId,
                    workingDir = Directory.GetCurrentDirectory(),
                    baseDir = AppDomain.CurrentDomain.BaseDirectory,
                    startedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                    os = Environment.OSVersion.ToString(),
                    runtime = Environment.Version.ToString(),
                    args,
                    endpoints = apiEndpoints,
                    refreshTelemetry = refreshService.GetRefreshTelemetrySnapshot(),
                });
            });
        }
    }
}
