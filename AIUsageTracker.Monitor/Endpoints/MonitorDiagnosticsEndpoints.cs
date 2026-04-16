// <copyright file="MonitorDiagnosticsEndpoints.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Monitor.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Monitor.Endpoints;

internal static class MonitorDiagnosticsEndpoints
{
    public static void Map(
        WebApplication app,
        bool isDebugMode,
        int port,
        string agentVersion,
        string contractVersion,
        string minClientContractVersion,
        string[] args)
    {
        MapHealth(
            app,
            isDebugMode,
            port,
            agentVersion,
            contractVersion,
            minClientContractVersion);
        MapDiagnostics(app, isDebugMode, port, args);
    }

    private static void MapHealth(
        WebApplication app,
        bool isDebugMode,
        int port,
        string agentVersion,
        string contractVersion,
        string minClientContractVersion)
    {
        app.MapGet(MonitorApiRoutes.Health, (ProviderRefreshService refreshService, ILogger<Program> logger) =>
        {
            if (isDebugMode)
            {
                logger.LogDebug("GET {Route}", MonitorApiRoutes.Health);
            }

            return Results.Ok(BuildHealthSnapshot(
                refreshService.GetRefreshTelemetrySnapshot(),
                port,
                agentVersion,
                contractVersion,
                minClientContractVersion));
        });
    }

    private static void MapDiagnostics(WebApplication app, bool isDebugMode, int port, string[] args)
    {
        app.MapGet(MonitorApiRoutes.Diagnostics, (EndpointDataSource endpointDataSource, ProviderRefreshService refreshService, IMonitorJobScheduler scheduler, ILogger<Program> logger) =>
        {
            if (isDebugMode)
            {
                logger.LogDebug("GET {Route}", MonitorApiRoutes.Diagnostics);
            }

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
                endpoints = BuildApiEndpoints(endpointDataSource),
                refreshTelemetry = refreshService.GetRefreshTelemetrySnapshot(),
                schedulerTelemetry = scheduler.GetSnapshot(),
            });
        });
    }

    private static MonitorHealthSnapshot BuildHealthSnapshot(
        RefreshTelemetrySnapshot refreshTelemetry,
        int port,
        string agentVersion,
        string contractVersion,
        string minClientContractVersion)
    {
        var failingProviders = refreshTelemetry.ProviderDiagnostics
            .Where(diagnostic => !string.IsNullOrWhiteSpace(diagnostic.LastRefreshError))
            .Select(diagnostic => diagnostic.ProviderId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(providerId => providerId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var providersInBackoff = refreshTelemetry.ProviderDiagnostics.Count(diagnostic => diagnostic.IsCircuitOpen);
        string refreshStatus;
        if (refreshTelemetry.LastRefreshAttemptUtc == null)
        {
            refreshStatus = "idle";
        }
        else if (providersInBackoff > 0 || failingProviders.Length > 0 || !string.IsNullOrWhiteSpace(refreshTelemetry.LastError))
        {
            refreshStatus = "degraded";
        }
        else
        {
            refreshStatus = "healthy";
        }

        return new MonitorHealthSnapshot
        {
            Status = "healthy",
            ServiceHealth = refreshStatus,
            Timestamp = DateTime.UtcNow,
            Port = port,
            ProcessId = Environment.ProcessId,
            AgentVersion = agentVersion,
            ContractVersion = contractVersion,
            MinClientContractVersion = minClientContractVersion,
            RefreshHealth = new MonitorRefreshHealthSnapshot
            {
                Status = refreshStatus,
                LastRefreshAttemptUtc = refreshTelemetry.LastRefreshAttemptUtc,
                LastRefreshCompletedUtc = refreshTelemetry.LastRefreshCompletedUtc,
                LastSuccessfulRefreshUtc = refreshTelemetry.LastSuccessfulRefreshUtc,
                LastError = refreshTelemetry.LastError,
                ProvidersInBackoff = providersInBackoff,
                FailingProviders = failingProviders,
            },
        };
    }

    private static List<object> BuildApiEndpoints(EndpointDataSource endpointDataSource)
    {
        return endpointDataSource.Endpoints
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
                    .OrderBy(method => method, StringComparer.Ordinal)
                    .ToArray(),
            })
            .OrderBy(endpoint => endpoint.route, StringComparer.Ordinal)
            .Cast<object>()
            .ToList();
    }
}
