// <copyright file="AgentGroupedUsageValueResolver.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Core.MonitorClient;

public static class AgentGroupedUsageValueResolver
{
    public static (double UsedPercentage, double RemainingPercentage, string Description, DateTime? NextResetTime) ResolveModelEffectiveState(
        AgentGroupedModelUsage model,
        bool parentIsQuotaBased)
    {
        ArgumentNullException.ThrowIfNull(model);

        var displayBucket = SelectDisplayQuotaBucket(model.QuotaBuckets);
        var usedPercentage = ResolveModelUsedPercentage(model, displayBucket, parentIsQuotaBased);
        var remainingPercentage = ResolveModelRemainingPercentage(model, displayBucket, parentIsQuotaBased);

        if (model.EffectiveUsedPercentage.HasValue)
        {
            usedPercentage = UsageMath.ClampPercent(model.EffectiveUsedPercentage.Value);
            if (!model.EffectiveRemainingPercentage.HasValue)
            {
                remainingPercentage = UsageMath.ClampPercent(100.0 - usedPercentage);
            }
        }

        if (model.EffectiveRemainingPercentage.HasValue)
        {
            remainingPercentage = UsageMath.ClampPercent(model.EffectiveRemainingPercentage.Value);
            if (!model.EffectiveUsedPercentage.HasValue)
            {
                usedPercentage = UsageMath.ClampPercent(100.0 - remainingPercentage);
            }
        }

        var description = !string.IsNullOrWhiteSpace(model.EffectiveDescription)
            ? model.EffectiveDescription
            : ResolveModelDescription(model, displayBucket, remainingPercentage);
        var nextResetTime = model.EffectiveNextResetTime ?? ResolveModelNextResetTime(model);

        return (
            usedPercentage,
            remainingPercentage,
            description ?? string.Empty,
            nextResetTime);
    }

    public static double ResolveBucketUsedPercentage(
        AgentGroupedQuotaBucketUsage bucket,
        bool parentIsQuotaBased)
    {
        ArgumentNullException.ThrowIfNull(bucket);

        if (bucket.UsedPercentage.HasValue)
        {
            return UsageMath.ClampPercent(bucket.UsedPercentage.Value);
        }

        if (bucket.RemainingPercentage.HasValue)
        {
            return UsageMath.ClampPercent(100.0 - bucket.RemainingPercentage.Value);
        }

        return parentIsQuotaBased ? 0.0 : 100.0;
    }

    public static double ResolveBucketRemainingPercentage(
        AgentGroupedQuotaBucketUsage bucket,
        bool parentIsQuotaBased)
    {
        ArgumentNullException.ThrowIfNull(bucket);

        if (bucket.RemainingPercentage.HasValue)
        {
            return UsageMath.ClampPercent(bucket.RemainingPercentage.Value);
        }

        if (bucket.UsedPercentage.HasValue)
        {
            return UsageMath.ClampPercent(100.0 - bucket.UsedPercentage.Value);
        }

        return parentIsQuotaBased ? 100.0 : 0.0;
    }

    private static double ResolveModelRemainingPercentage(
        AgentGroupedModelUsage model,
        AgentGroupedQuotaBucketUsage? displayBucket,
        bool parentIsQuotaBased)
    {
        if (displayBucket != null)
        {
            return ResolveBucketRemainingPercentage(displayBucket, parentIsQuotaBased);
        }

        if (model.RemainingPercentage.HasValue)
        {
            return UsageMath.ClampPercent(model.RemainingPercentage.Value);
        }

        if (model.UsedPercentage.HasValue)
        {
            return UsageMath.ClampPercent(100.0 - model.UsedPercentage.Value);
        }

        return parentIsQuotaBased ? 100.0 : 0.0;
    }

    private static double ResolveModelUsedPercentage(
        AgentGroupedModelUsage model,
        AgentGroupedQuotaBucketUsage? displayBucket,
        bool parentIsQuotaBased)
    {
        if (displayBucket != null)
        {
            return ResolveBucketUsedPercentage(displayBucket, parentIsQuotaBased);
        }

        if (model.UsedPercentage.HasValue)
        {
            return UsageMath.ClampPercent(model.UsedPercentage.Value);
        }

        if (model.RemainingPercentage.HasValue)
        {
            return UsageMath.ClampPercent(100.0 - model.RemainingPercentage.Value);
        }

        return parentIsQuotaBased ? 0.0 : 100.0;
    }

    private static string ResolveModelDescription(
        AgentGroupedModelUsage model,
        AgentGroupedQuotaBucketUsage? displayBucket,
        double remainingPercentage)
    {
        if (!string.IsNullOrWhiteSpace(model.Description))
        {
            return model.Description;
        }

        if (!string.IsNullOrWhiteSpace(displayBucket?.Description))
        {
            return displayBucket.Description;
        }

        return $"{remainingPercentage.ToString("F1", CultureInfo.InvariantCulture)}% Remaining";
    }

    private static DateTime? ResolveModelNextResetTime(AgentGroupedModelUsage model)
    {
        if (model.NextResetTime.HasValue)
        {
            return model.NextResetTime;
        }

        return model.QuotaBuckets
            .Select(bucket => bucket.NextResetTime)
            .Where(reset => reset.HasValue)
            .OrderBy(reset => reset)
            .FirstOrDefault();
    }

    private static AgentGroupedQuotaBucketUsage? SelectDisplayQuotaBucket(
        IReadOnlyList<AgentGroupedQuotaBucketUsage> quotaBuckets)
    {
        if (quotaBuckets.Count == 0)
        {
            return null;
        }

        return quotaBuckets
            .OrderBy(bucket => bucket.RemainingPercentage.HasValue ? 0 : 1)
            .ThenBy(bucket => bucket.RemainingPercentage ?? double.MaxValue)
            .ThenBy(bucket => bucket.NextResetTime ?? DateTime.MaxValue)
            .ThenBy(bucket => bucket.BucketName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
}
