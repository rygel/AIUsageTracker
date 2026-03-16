// <copyright file="WebProviderUsageMapper.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Web.Services;

internal static class WebProviderUsageMapper
{
    public static ProviderUsage Map(dynamic row)
    {
        var usage = new ProviderUsage
        {
            ProviderId = row.provider_id ?? row.ProviderId,
            ProviderName = row.ProviderName,
            IsAvailable = row.is_available == 1 || (row.IsAvailable != null && row.IsAvailable == 1),
            Description = row.status_message ?? string.Empty,
            RequestsUsed = (double)(row.requests_used ?? row.RequestsUsed ?? 0.0),
            RequestsAvailable = (double)(row.requests_available ?? row.RequestsAvailable ?? 0.0),
            UsedPercent = (double)(row.requests_percentage ?? row.UsedPercent ?? 0.0),
            ResponseLatencyMs = (double)(row.response_latency_ms ?? row.ResponseLatencyMs ?? 0.0),
            FetchedAt = DateTime.Parse(row.fetched_at ?? row.FetchedAt),
        };

        if (row.next_reset_time != null)
        {
            usage.NextResetTime = DateTime.Parse(row.next_reset_time);
        }

        return usage;
    }
}
