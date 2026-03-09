// <copyright file="MonitorConfigEndpoints.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Monitor.Endpoints
{
    using AIUsageTracker.Core.Models;
    using AIUsageTracker.Monitor.Services;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;

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

            app.MapPost(MonitorApiRoutes.Config, async (ProviderConfig config, IConfigService configService, ILogger<Program> logger) =>
            {
                if (string.IsNullOrWhiteSpace(config.ProviderId))
                {
                    return Results.BadRequest(new { message = "providerId is required." });
                }

                logger.LogDebug("POST {Route} ({ProviderId})", MonitorApiRoutes.Config, config.ProviderId);
                await configService.SaveConfigAsync(config).ConfigureAwait(false);
                return Results.Ok(new { message = "Config saved" });
            });

            app.MapDelete(MonitorApiRoutes.ConfigByProvider, async (string providerId, IConfigService configService, ILogger<Program> logger) =>
            {
                if (string.IsNullOrWhiteSpace(providerId))
                {
                    return Results.BadRequest(new { message = "providerId is required." });
                }

                logger.LogDebug("DELETE {Route}: {ProviderId}", MonitorApiRoutes.ConfigByProvider, providerId);
                await configService.RemoveConfigAsync(providerId).ConfigureAwait(false);
                return Results.Ok(new { message = "Config removed" });
            });

            app.MapPost(MonitorApiRoutes.ScanKeys, async ([FromServices] IConfigService configService, [FromServices] ProviderRefreshService refreshService, ILogger<Program> logger) =>
            {
                logger.LogDebug("POST {Route}", MonitorApiRoutes.ScanKeys);
                var discovered = await configService.ScanForKeysAsync().ConfigureAwait(false);
                logger.LogDebug("Discovered {Count} keys", discovered.Count);

                // Immediately refresh so newly discovered keys appear in /api/usage within seconds.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await refreshService.TriggerRefreshAsync(forceAll: true).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Background refresh after key scan failed");
                    }
                });

                return Results.Ok(new { discovered = discovered.Count, configs = discovered });
            });
        }
    }
}
