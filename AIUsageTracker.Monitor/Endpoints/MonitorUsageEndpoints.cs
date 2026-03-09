// <copyright file="MonitorUsageEndpoints.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Monitor.Endpoints
{
    using AIUsageTracker.Core.Interfaces;
    using AIUsageTracker.Infrastructure.Providers;
    using AIUsageTracker.Monitor.Services;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;

    internal static class MonitorUsageEndpoints
    {
        public static void Map(WebApplication app)
        {
            app.MapGet(MonitorApiRoutes.Usage, async (UsageDatabase db, IConfigService configService, ILogger<Program> logger) =>
            {
                var usage = await db.GetLatestHistoryAsync().ConfigureAwait(false);

                var configs = await configService.GetConfigsAsync().ConfigureAwait(false);
                usage = usage
                    .Where(u => !ProviderMetadataCatalog.ShouldSuppressUsageProviderId(configs, u.ProviderId))
                    .ToList();

                logger.LogDebug(
                    "GET /api/usage returning {Count} providers: {Providers}",
                    usage.Count,
                    string.Join(", ", usage.Select(u => u.ProviderId)));

                return Results.Ok(usage);
            });

            app.MapGet(MonitorApiRoutes.UsageByProvider, async (string providerId, UsageDatabase db, ILogger<Program> logger) =>
            {
                if (string.IsNullOrWhiteSpace(providerId))
                {
                    return Results.BadRequest(new { message = "providerId is required." });
                }

                logger.LogDebug("GET {Route}: {ProviderId}", MonitorApiRoutes.UsageByProvider, providerId);
                var usage = await db.GetHistoryByProviderAsync(providerId, 1).ConfigureAwait(false);
                var result = usage.FirstOrDefault();
                return result != null ? Results.Ok(result) : Results.NotFound();
            });

            app.MapPost(MonitorApiRoutes.Refresh, async ([FromServices] ProviderRefreshService refreshService, ILogger<Program> logger) =>
            {
                logger.LogDebug("POST {Route}", MonitorApiRoutes.Refresh);
                await refreshService.TriggerRefreshAsync().ConfigureAwait(false);
                return Results.Ok(new { message = "Refresh triggered" });
            });

            app.MapPost(MonitorApiRoutes.NotificationTest, ([FromServices] INotificationService notificationService, ILogger<Program> logger) =>
            {
                logger.LogDebug("POST {Route}", MonitorApiRoutes.NotificationTest);
                notificationService.ShowNotification(
                    "AI Usage Tracker",
                    "This is a test notification from Slim Settings.",
                    "openSettings",
                    "notifications");
                return Results.Ok(new { message = "Test notification sent" });
            });
        }
    }
}
