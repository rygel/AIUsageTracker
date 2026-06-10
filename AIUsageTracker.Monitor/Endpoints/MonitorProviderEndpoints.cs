// <copyright file="MonitorProviderEndpoints.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;
using Microsoft.AspNetCore.Mvc;

namespace AIUsageTracker.Monitor.Endpoints;

internal static class MonitorProviderEndpoints
{
    public static void Map(WebApplication app)
    {
        MapTestConnection(app);
    }

    private static void MapTestConnection(WebApplication app)
    {
        app.MapPost(MonitorApiRoutes.ProviderTestTemplate, async (
            string providerId,
            [FromBody] ProviderTestRequest body,
            [FromServices] ProviderManagerLifecycleService providerLifecycle,
            [FromServices] ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                return Results.BadRequest(new AgentProviderCheckResponse
                {
                    Success = false,
                    Message = "providerId is required.",
                });
            }

            if (string.IsNullOrWhiteSpace(body.ApiKey))
            {
                return Results.BadRequest(new AgentProviderCheckResponse
                {
                    Success = false,
                    Message = "apiKey is required.",
                });
            }

            var provider = providerLifecycle.CurrentManager?
                .GetProviderService(providerId);

            if (provider is null)
            {
                logger.LogWarning("Test connection requested for unknown provider: {ProviderId}", providerId);
                return Results.NotFound(new AgentProviderCheckResponse
                {
                    Success = false,
                    Message = $"Unknown provider: {providerId}",
                });
            }

            logger.LogInformation("Testing connection for provider: {ProviderId}", providerId);

            var testConfig = new ProviderConfig
            {
                ProviderId = providerId,
                ApiKey = body.ApiKey,
            };

            try
            {
                var results = await provider.GetUsageAsync(testConfig, cancellationToken: cancellationToken).ConfigureAwait(false);
                var usage = results.FirstOrDefault();

                if (usage is null)
                {
                    return Results.Ok(new AgentProviderCheckResponse
                    {
                        Success = false,
                        Message = "Provider returned no data.",
                    });
                }

                return Results.Ok(new AgentProviderCheckResponse
                {
                    Success = usage.IsAvailable,
                    Message = usage.IsAvailable
                        ? $"Connected successfully. {usage.Description}"
                        : usage.Description ?? "Connection failed.",
                });
            }
            catch (HttpRequestException ex)
            {
                logger.LogError(ex, "Test connection HTTP error for provider: {ProviderId}", providerId);
                return Results.Ok(new AgentProviderCheckResponse
                {
                    Success = false,
                    Message = $"HTTP error: {ex.Message}",
                });
            }
            catch (TaskCanceledException ex)
            {
                logger.LogError(ex, "Test connection timeout for provider: {ProviderId}", providerId);
                return Results.Ok(new AgentProviderCheckResponse
                {
                    Success = false,
                    Message = "Connection timed out.",
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Results.Ok(new AgentProviderCheckResponse
                {
                    Success = false,
                    Message = "Request was cancelled.",
                });
            }
        });
    }
}
