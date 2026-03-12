// <copyright file="GroupedUsageProjectionService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Monitor.Services;

public static class GroupedUsageProjectionService
{
    public static AgentGroupedUsageSnapshot Build(IReadOnlyCollection<ProviderUsage> usages)
    {
        ArgumentNullException.ThrowIfNull(usages);

        var providerGroups = usages
            .Where(usage => !string.IsNullOrWhiteSpace(usage.ProviderId))
            .GroupBy(
                usage => ProviderMetadataCatalog.GetCanonicalProviderId(usage.ProviderId),
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(BuildProviderGroup)
            .ToList();

        return new AgentGroupedUsageSnapshot
        {
            ContractVersion = MonitorApiContract.CurrentVersion,
            GeneratedAtUtc = DateTime.UtcNow,
            Providers = providerGroups,
        };
    }

    private static AgentGroupedProviderUsage BuildProviderGroup(IGrouping<string, ProviderUsage> group)
    {
        var canonicalProviderId = group.Key;
        var primary = SelectPrimaryUsage(group, canonicalProviderId);
        var models = BuildModels(group);
        var accountName = group
            .OrderByDescending(usage => usage.FetchedAt)
            .Select(usage => usage.AccountName)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

        var nextResetTime = primary.NextResetTime
            ?? models
                .Where(model => model.NextResetTime.HasValue)
                .Select(model => model.NextResetTime!.Value)
                .OrderBy(reset => reset)
                .FirstOrDefault();

        var displayName = ResolveProviderDisplayName(primary, canonicalProviderId);
        return new AgentGroupedProviderUsage
        {
            ProviderId = canonicalProviderId,
            ProviderName = displayName,
            AccountName = accountName,
            IsAvailable = group.Any(usage => usage.IsAvailable),
            PlanType = primary.PlanType,
            IsQuotaBased = primary.IsQuotaBased,
            RequestsUsed = primary.RequestsUsed,
            RequestsAvailable = primary.RequestsAvailable,
            RequestsPercentage = primary.RequestsPercentage,
            Description = primary.Description,
            FetchedAt = group.Max(usage => usage.FetchedAt),
            NextResetTime = nextResetTime,
            ModelCount = models.Count,
            Models = models,
        };
    }

    private static string ResolveProviderDisplayName(ProviderUsage primary, string canonicalProviderId)
    {
        if (string.Equals(primary.ProviderId, canonicalProviderId, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(primary.ProviderName))
        {
            return primary.ProviderName;
        }

        return ProviderMetadataCatalog.GetDisplayName(canonicalProviderId, primary.ProviderName);
    }

    private static ProviderUsage SelectPrimaryUsage(IEnumerable<ProviderUsage> group, string canonicalProviderId)
    {
        return group.FirstOrDefault(usage =>
                string.Equals(usage.ProviderId, canonicalProviderId, StringComparison.OrdinalIgnoreCase))
            ?? group
                .OrderByDescending(usage => usage.FetchedAt)
                .First();
    }

    private static IReadOnlyList<AgentGroupedModelUsage> BuildModels(
        IEnumerable<ProviderUsage> group)
    {
        return BuildModelsFromDetails(group);
    }

    private static IReadOnlyList<AgentGroupedModelUsage> BuildModelsFromDetails(IEnumerable<ProviderUsage> group)
    {
        var models = new Dictionary<string, AgentGroupedModelUsage>(StringComparer.OrdinalIgnoreCase);

        foreach (var usage in group.OrderByDescending(item => item.FetchedAt))
        {
            if (usage.Details == null || usage.Details.Count == 0)
            {
                continue;
            }

            var quotaDetails = usage.Details
                .Where(detail => detail.DetailType == ProviderUsageDetailType.QuotaWindow)
                .ToList();

            foreach (var detail in usage.Details.Where(detail =>
                         detail.DetailType == ProviderUsageDetailType.Model &&
                         !string.IsNullOrWhiteSpace(detail.Name)))
            {
                var modelId = !string.IsNullOrWhiteSpace(detail.ModelName)
                    ? detail.ModelName
                    : CreateModelIdFromName(detail.Name);
                if (models.ContainsKey(modelId))
                {
                    continue;
                }

                var usedPercentage = UsageMath.GetEffectiveUsedPercent(detail, usage.IsQuotaBased);
                var remainingPercentage = usedPercentage.HasValue
                    ? (double?)UsageMath.ClampPercent(100.0 - usedPercentage.Value)
                    : null;
                var modelScopedQuotaDetails = quotaDetails
                    .Where(quotaDetail => string.Equals(quotaDetail.ModelName, modelId, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var quotaBuckets = BuildQuotaBucketsFromDetails(modelScopedQuotaDetails, usage.IsQuotaBased);
                var model = new AgentGroupedModelUsage
                {
                    ModelId = modelId,
                    ModelName = detail.Name,
                    UsedPercentage = usedPercentage,
                    RemainingPercentage = remainingPercentage,
                    NextResetTime = detail.NextResetTime,
                    Description = detail.Description ?? string.Empty,
                    QuotaBuckets = quotaBuckets.Count > 0
                        ? quotaBuckets
                        : BuildSummaryQuotaBuckets(
                            usedPercentage,
                            remainingPercentage,
                            detail.NextResetTime,
                            detail.Description),
                };
                ApplyEffectiveModelState(model, usage.IsQuotaBased);
                models[modelId] = model;
            }
        }

        return models.Values
            .OrderBy(model => model.ModelName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<AgentGroupedQuotaBucketUsage> BuildQuotaBucketsFromDetails(
        IEnumerable<ProviderUsageDetail> quotaDetails,
        bool parentIsQuotaBased)
    {
        var buckets = new List<AgentGroupedQuotaBucketUsage>();
        var usedBucketIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var detail in quotaDetails
                     .Where(detail => !string.IsNullOrWhiteSpace(detail.Name))
                     .OrderBy(detail => detail.NextResetTime ?? DateTime.MaxValue)
                     .ThenBy(detail => detail.Name, StringComparer.OrdinalIgnoreCase))
        {
            var usedPercent = UsageMath.GetEffectiveUsedPercent(detail, parentIsQuotaBased);
            var remainingPercent = usedPercent.HasValue
                ? (double?)UsageMath.ClampPercent(100.0 - usedPercent.Value)
                : null;

            var baseBucketId = CreateModelIdFromName(detail.Name);
            var bucketId = baseBucketId;
            var duplicateCounter = 2;
            while (!usedBucketIds.Add(bucketId))
            {
                bucketId = $"{baseBucketId}-{duplicateCounter}";
                duplicateCounter++;
            }

            buckets.Add(new AgentGroupedQuotaBucketUsage
            {
                BucketId = bucketId,
                BucketName = detail.Name,
                UsedPercentage = usedPercent.HasValue ? UsageMath.ClampPercent(usedPercent.Value) : null,
                RemainingPercentage = remainingPercent,
                NextResetTime = detail.NextResetTime,
                Description = string.IsNullOrWhiteSpace(detail.Description)
                    ? detail.Used ?? string.Empty
                    : detail.Description,
            });
        }

        return buckets;
    }

    private static string CreateModelIdFromName(string name)
    {
        var chars = name
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var value = new string(chars).Trim('-');
        while (value.Contains("--", StringComparison.Ordinal))
        {
            value = value.Replace("--", "-", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(value) ? "model" : value;
    }

    private static IReadOnlyList<AgentGroupedQuotaBucketUsage> BuildSummaryQuotaBuckets(
        double? usedPercentage,
        double? remainingPercentage,
        DateTime? nextResetTime,
        string? description)
    {
        var hasSignal = usedPercentage.HasValue ||
                        remainingPercentage.HasValue ||
                        nextResetTime.HasValue ||
                        !string.IsNullOrWhiteSpace(description);
        if (!hasSignal)
        {
            return Array.Empty<AgentGroupedQuotaBucketUsage>();
        }

        return new[]
        {
            new AgentGroupedQuotaBucketUsage
            {
                BucketId = "effective",
                BucketName = "Effective Quota",
                UsedPercentage = usedPercentage.HasValue ? UsageMath.ClampPercent(usedPercentage.Value) : null,
                RemainingPercentage = remainingPercentage.HasValue ? UsageMath.ClampPercent(remainingPercentage.Value) : null,
                NextResetTime = nextResetTime,
                Description = description ?? string.Empty,
            },
        };
    }

    private static void ApplyEffectiveModelState(AgentGroupedModelUsage model, bool parentIsQuotaBased)
    {
        var effective = AgentGroupedUsageValueResolver.ResolveModelEffectiveState(model, parentIsQuotaBased);
        model.EffectiveUsedPercentage = effective.UsedPercentage;
        model.EffectiveRemainingPercentage = effective.RemainingPercentage;
        model.EffectiveDescription = effective.Description;
        model.EffectiveNextResetTime = effective.NextResetTime;
    }
}
