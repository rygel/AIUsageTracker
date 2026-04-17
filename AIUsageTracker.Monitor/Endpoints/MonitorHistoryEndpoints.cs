// <copyright file="MonitorHistoryEndpoints.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Monitor.Services;

namespace AIUsageTracker.Monitor.Endpoints;

internal static class MonitorHistoryEndpoints
{
    private const int DefaultHistoryLimit = 100;
    private const int MaxHistoryLimit = 5000;
    private const int DefaultResetsLimit = 50;
    private const int MaxResetsLimit = 500;

    public static void Map(WebApplication app)
    {
        app.MapGet(MonitorApiRoutes.History, async (UsageDatabase db, int? limit, ILogger<Program> logger) =>
        {
            var effectiveLimit = Math.Clamp(limit ?? DefaultHistoryLimit, 1, MaxHistoryLimit);
            logger.LogDebug("GET {Route} (limit={Limit})", MonitorApiRoutes.History, effectiveLimit);
            var history = await db.GetHistoryAsync(effectiveLimit).ConfigureAwait(false);
            return Results.Ok(history);
        });

        app.MapGet(MonitorApiRoutes.HistoryByProviderTemplate, async (string providerId, UsageDatabase db, int? limit, ILogger<Program> logger) =>
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                return Results.BadRequest(new { message = "providerId is required." });
            }

            var effectiveLimit = Math.Clamp(limit ?? DefaultHistoryLimit, 1, MaxHistoryLimit);
            logger.LogDebug("GET {Route}: {ProviderId}", MonitorApiRoutes.HistoryByProviderTemplate, providerId);
            var history = await db.GetHistoryByProviderAsync(providerId, effectiveLimit).ConfigureAwait(false);
            return Results.Ok(history);
        });

        app.MapGet(MonitorApiRoutes.ResetsByProviderTemplate, async (string providerId, UsageDatabase db, int? limit, ILogger<Program> logger) =>
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                return Results.BadRequest(new { message = "providerId is required." });
            }

            var effectiveLimit = Math.Clamp(limit ?? DefaultResetsLimit, 1, MaxResetsLimit);
            logger.LogDebug("GET {Route}: {ProviderId}", MonitorApiRoutes.ResetsByProviderTemplate, providerId);
            var resets = await db.GetResetEventsAsync(providerId, effectiveLimit).ConfigureAwait(false);
            return Results.Ok(resets);
        });
    }
}
