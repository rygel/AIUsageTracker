// <copyright file="MonitorUsageEndpoints.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Monitor.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Monitor.Endpoints;

internal static class MonitorUsageEndpoints
{
    private const string UsageCacheControlHeader = "private, max-age=5, must-revalidate";

    public static void Map(WebApplication app)
    {
        MapGetUsage(app);
        MapGetGroupedUsage(app);
        MapGetUsageByProvider(app);
        MapPostRefresh(app);
        MapPostNotificationTest(app);
    }

    private static void MapGetUsage(WebApplication app)
    {
        app.MapGet(MonitorApiRoutes.Usage, async (HttpResponse response, UsageDatabase db, ILogger<Program> logger) =>
        {
            ApplyUsageCachingHeaders(response);
            var usage = await db.GetLatestHistoryAsync().ConfigureAwait(false);

            logger.LogDebug(
                "GET /api/usage returning {Count} providers: {Providers}",
                usage.Count,
                string.Join(", ", usage.Select(u => u.ProviderId)));

            return Results.Ok(usage);
        });
    }

    private static void MapGetGroupedUsage(WebApplication app)
    {
        app.MapGet(MonitorApiRoutes.UsageGrouped, async (HttpRequest request, HttpResponse response, CachedGroupedUsageProjectionService projectionService, ILogger<Program> logger) =>
        {
            var cacheEntry = await projectionService.GetGroupedUsageWithMetadataAsync().ConfigureAwait(false);
            ApplyUsageCachingHeaders(response, cacheEntry.ETag);

            if (IsNotModified(request, cacheEntry.ETag))
            {
                logger.LogDebug("GET {Route} returning 304 Not Modified", MonitorApiRoutes.UsageGrouped);
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }

            logger.LogDebug(
                "GET {Route} returning {Count} grouped providers",
                MonitorApiRoutes.UsageGrouped,
                cacheEntry.Snapshot.Providers.Count);

            return Results.Ok(cacheEntry.Snapshot);
        });
    }

    private static void MapGetUsageByProvider(WebApplication app)
    {
        app.MapGet(MonitorApiRoutes.UsageByProviderTemplate, async (string providerId, UsageDatabase db, ILogger<Program> logger) =>
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                return Results.BadRequest(new { message = "providerId is required." });
            }

            logger.LogDebug("GET {Route}: {ProviderId}", MonitorApiRoutes.UsageByProviderTemplate, providerId);
            var usage = await db.GetHistoryByProviderAsync(providerId, 1).ConfigureAwait(false);
            var result = usage.FirstOrDefault();
            return result != null ? Results.Ok(result) : Results.NotFound();
        });
    }

    private static void MapPostRefresh(WebApplication app)
    {
        app.MapPost(MonitorApiRoutes.Refresh, ([FromServices] ProviderRefreshService refreshService, ILogger<Program> logger, [FromQuery] bool forceAll = false, [FromQuery] string? providerIds = null) =>
        {
            var includeProviderIds = ParseProviderIds(providerIds);
            logger.LogDebug(
                "POST {Route} forceAll={ForceAll} includeProviderCount={IncludeProviderCount}",
                MonitorApiRoutes.Refresh,
                forceAll,
                includeProviderIds?.Count ?? 0);
            var queued = refreshService.QueueForceRefresh(
                forceAll: forceAll,
                includeProviderIds: includeProviderIds);
            return Results.Ok(new
            {
                message = queued ? "Refresh queued" : "Refresh already queued",
                queued,
                forceAll,
                includeProviderCount = includeProviderIds?.Count ?? 0,
            });
        });
    }

    private static void MapPostNotificationTest(WebApplication app)
    {
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

    private static IReadOnlyCollection<string>? ParseProviderIds(string? providerIds)
    {
        if (string.IsNullOrWhiteSpace(providerIds))
        {
            return null;
        }

        var parsed = providerIds
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return parsed.Length == 0 ? null : parsed;
    }

    private static void ApplyUsageCachingHeaders(HttpResponse response, string? eTag = null)
    {
        response.Headers.CacheControl = UsageCacheControlHeader;

        if (!string.IsNullOrWhiteSpace(eTag))
        {
            response.Headers.ETag = eTag;
            response.Headers.Vary = "If-None-Match";
        }
    }

    private static bool IsNotModified(HttpRequest request, string currentETag)
    {
        if (!request.Headers.TryGetValue("If-None-Match", out var headerValues))
        {
            return false;
        }

        return headerValues
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .SelectMany(h => h.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Any(candidate => string.Equals(candidate, "*", StringComparison.Ordinal) ||
                              string.Equals(candidate, currentETag, StringComparison.Ordinal));
    }
}
