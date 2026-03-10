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

            app.MapGet(MonitorApiRoutes.Diagnostics, (EndpointDataSource endpointDataSource, ProviderRefreshService refreshService, IMonitorJobScheduler scheduler, IProviderUsageProcessingPipeline usageProcessingPipeline, ILogger<Program> logger) =>
            {
                if (isDebugMode)
                {
                    logger.LogDebug("GET {Route}", MonitorApiRoutes.Diagnostics);
                }

                var snapshot = MonitorDiagnosticsSnapshotFactory.Create(
                    endpointDataSource,
                    port,
                    args,
                    refreshService.GetRefreshTelemetrySnapshot(),
                    scheduler.GetSnapshot(),
                    usageProcessingPipeline.GetSnapshot());

                return Results.Ok(snapshot);
            });
        }
    }
}
