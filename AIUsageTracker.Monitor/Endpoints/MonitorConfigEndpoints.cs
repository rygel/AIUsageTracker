// <copyright file="MonitorConfigEndpoints.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Monitor.Services;
using Microsoft.AspNetCore.Mvc;

namespace AIUsageTracker.Monitor.Endpoints;

internal static class MonitorConfigEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet(MonitorApiRoutes.Config, async (IConfigService configService, ILogger<Program> logger) =>
        {
            logger.LogDebug("GET {Route}", MonitorApiRoutes.Config);
            var configs = await configService.GetConfigsAsync().ConfigureAwait(false);
            return Results.Ok(configs);
        });

        app.MapPost(
            MonitorApiRoutes.Config,
            async (
                ProviderConfig config,
                IConfigService configService,
                ProviderRefreshService refreshService,
                ProviderRefreshCircuitBreakerService circuitBreakerService,
                CachedGroupedUsageProjectionService projectionService,
                ILogger<Program> logger) =>
        {
            if (string.IsNullOrWhiteSpace(config.ProviderId))
            {
                return Results.BadRequest(new { message = "providerId is required." });
            }

            logger.LogDebug("POST {Route} ({ProviderId})", MonitorApiRoutes.Config, config.ProviderId);
            await configService.SaveConfigAsync(config).ConfigureAwait(false);

            // Invalidate the snapshot cache so the exclusion filter re-evaluates against the
            // updated config (e.g. a cleared API key should immediately hide the provider).
            projectionService.Invalidate();

            // Config/auth updates should retry immediately and not wait for a stale backoff window.
            circuitBreakerService.ResetProvider(config.ProviderId, "config update");
            var refreshQueued = refreshService.QueueForceRefresh(
                forceAll: false,
                includeProviderIds: new[] { config.ProviderId });

            return Results.Ok(new { message = "Config saved", refreshQueued });
        });

        app.MapDelete(MonitorApiRoutes.ConfigByProviderTemplate, async (string providerId, IConfigService configService, CachedGroupedUsageProjectionService projectionService, ILogger<Program> logger) =>
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                return Results.BadRequest(new { message = "providerId is required." });
            }

            logger.LogDebug("DELETE {Route}: {ProviderId}", MonitorApiRoutes.ConfigByProviderTemplate, providerId);
            await configService.RemoveConfigAsync(providerId).ConfigureAwait(false);
            projectionService.Invalidate();
            return Results.Ok(new { message = "Config removed" });
        });

        app.MapPost(MonitorApiRoutes.ScanKeys, async ([FromServices] IConfigService configService, [FromServices] ProviderRefreshService refreshService, ILogger<Program> logger) =>
        {
            logger.LogDebug("POST {Route}", MonitorApiRoutes.ScanKeys);
            var discovered = await configService.ScanForKeysAsync().ConfigureAwait(false);
            logger.LogDebug("Discovered {Count} keys", discovered.Count);

            // Queue a high-priority force refresh so newly discovered keys bypass temporary backoff and appear quickly.
            var refreshQueued = refreshService.QueueForceRefresh(forceAll: true);

            return Results.Ok(new AgentScanKeysResponse
            {
                Discovered = discovered.Count,
                RefreshQueued = refreshQueued,
                Configs = discovered,
            });
        });
    }
}
