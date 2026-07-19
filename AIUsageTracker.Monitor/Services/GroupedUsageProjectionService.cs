// <copyright file="GroupedUsageProjectionService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Core.Providers;

namespace AIUsageTracker.Monitor.Services;

public static class GroupedUsageProjectionService
{
    public static AgentGroupedUsageSnapshot Build(IReadOnlyCollection<ProviderUsage> usages)
    {
        ArgumentNullException.ThrowIfNull(usages);

        var providerGroups = usages
            .Where(usage => !string.IsNullOrWhiteSpace(usage.ProviderId))
            .GroupBy(
                usage => ProviderMetadataCatalog.GetProviderOwnerId(usage.ProviderId),
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
        var providerId = group.Key;
        var models = BuildModels(group, providerId);
        var accountName = group
            .OrderByDescending(usage => usage.FetchedAt)
            .Select(usage => usage.AccountName)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

        var availableQuota = group
            .OfType<QuotaProviderUsage>()
            .Where(q => q.IsAvailable)
            .ToList();

        double requestsUsed, requestsAvailable, usedPercent;
        string description;
        ProviderUsageState state;
        PlanType planType;
        bool isQuotaBased;
        DateTime? nextResetTime;

        if (availableQuota.Count > 0)
        {
            var newestFetchedAt = availableQuota.Max(q => q.FetchedAt.Ticks);
            var currentBatch = availableQuota.Where(q => q.FetchedAt.Ticks == newestFetchedAt).ToList();
            var newest = currentBatch.OrderByDescending(q => q.FetchedAt).First();
            var totalUsed = currentBatch.Sum(q => q.RequestsUsed);
            var totalAvailable = currentBatch.Sum(q => q.RequestsAvailable);

            requestsUsed = totalUsed;
            requestsAvailable = totalAvailable;
            usedPercent = totalAvailable > 0
                ? UsageMath.CalculateUsedPercent(totalUsed, totalAvailable)
                : newest.UsedPercent;
            description = newest.Description;
            state = newest.State;
            planType = newest.PlanType;
            isQuotaBased = newest.IsQuotaBased;
            nextResetTime = currentBatch
                .Select(q => q.NextResetTime)
                .Where(t => t.HasValue)
                .OrderBy(t => t!.Value)
                .FirstOrDefault();
        }
        else
        {
            var best = group
                .OrderByDescending(u => u.IsAvailable)
                .ThenByDescending(u => u.FetchedAt)
                .First();
            var bestQ = best as QuotaProviderUsage;

            requestsUsed = 0;
            requestsAvailable = 0;
            usedPercent = 0;
            description = best.Description;
            state = best.State;
            planType = bestQ?.PlanType ?? PlanType.Usage;
            isQuotaBased = bestQ?.IsQuotaBased ?? false;
            nextResetTime = bestQ?.NextResetTime;
        }

        nextResetTime ??= models
            .Where(model => model.NextResetTime.HasValue)
            .Select(model => model.NextResetTime!.Value)
            .OrderBy(reset => reset)
            .FirstOrDefault();

        var displayName = ProviderMetadataCatalog.GetConfiguredDisplayName(providerId);
        var providerDetails = (IReadOnlyList<ProviderUsage>)group
            .Where(u => !u.IsStale && ((u as WindowedProviderUsage)?.WindowKind ?? (u as ModelScopedProviderUsage)?.WindowKind ?? WindowKind.None) != WindowKind.None)
            .OrderBy(u => (u as QuotaProviderUsage)?.NextResetTime ?? DateTime.MaxValue)
            .ThenBy(u => (u as WindowedProviderUsage)?.Name ?? (u as ModelScopedProviderUsage)?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AgentGroupedProviderUsage
        {
            ProviderId = providerId,
            ProviderName = displayName,
            AccountName = accountName,
            IsAvailable = group.Any(usage => usage.IsAvailable),
            State = state,
            PlanType = planType,
            IsQuotaBased = isQuotaBased,
            RequestsUsed = requestsUsed,
            RequestsAvailable = requestsAvailable,
            UsedPercent = usedPercent,
            Description = description,
            FetchedAt = group.Max(usage => usage.FetchedAt),
            NextResetTime = nextResetTime,
            Models = models,
            ProviderDetails = providerDetails,
        };
    }

    private static List<AgentGroupedModelUsage> BuildModels(
        IEnumerable<ProviderUsage> group,
        string ownerProviderId)
    {
        var usages = group.ToList();
        var definition = ProviderMetadataCatalog.Find(ownerProviderId);
        var cardCandidates = definition?.FamilyMode == ProviderFamilyMode.FlatWindowCards
            ? usages.Where(u => !string.IsNullOrWhiteSpace(GetCardId(u)) && u is QuotaProviderUsage q && !q.IsCurrencyUsage)
            : usages.Where(u =>
                !string.IsNullOrWhiteSpace(GetCardId(u)) &&
                u is QuotaProviderUsage q &&
                !q.IsCurrencyUsage &&
                GetWindowKind(u) == WindowKind.None);

        return BuildModelsFromFlatCards(cardCandidates);
    }

    private static string? GetCardId(ProviderUsage u) =>
        (u as WindowedProviderUsage)?.CardId ?? (u as ModelScopedProviderUsage)?.CardId;

    private static string? GetName(ProviderUsage u) =>
        (u as WindowedProviderUsage)?.Name ?? (u as ModelScopedProviderUsage)?.Name;

    private static WindowKind GetWindowKind(ProviderUsage u) =>
        (u as WindowedProviderUsage)?.WindowKind ?? (u as ModelScopedProviderUsage)?.WindowKind ?? WindowKind.None;

    private static List<AgentGroupedModelUsage> BuildModelsFromFlatCards(
        IEnumerable<ProviderUsage> group)
    {
        return group
            .Where(u => !string.IsNullOrWhiteSpace(GetCardId(u)) && !string.IsNullOrWhiteSpace(GetName(u)))
            .GroupBy(u => GetCardId(u)!, StringComparer.OrdinalIgnoreCase)
            .Select(cardGroup => cardGroup.OrderByDescending(u => u.FetchedAt).First())
            .OrderBy(u => GetCardId(u), StringComparer.OrdinalIgnoreCase)
            .Select(u =>
            {
                var q = u as QuotaProviderUsage;
                var usedPercentage = q != null ? UsageMath.ClampPercent(q.UsedPercent) : (double?)null;
                var remainingPercentage = q != null ? UsageMath.ClampPercent(q.RemainingPercent) : (double?)null;
                var model = new AgentGroupedModelUsage
                {
                    ModelId = GetCardId(u) ?? string.Empty,
                    ModelName = GetName(u) ?? string.Empty,
                    UsedPercentage = usedPercentage,
                    RemainingPercentage = remainingPercentage,
                    NextResetTime = q?.NextResetTime,
                    ResetCreditsAvailable = q?.ResetCreditsAvailable,
                    ResetCreditExpirationsUtc = q?.ResetCreditExpirationsUtc,
                    Description = u.Description ?? string.Empty,
                    QuotaBuckets = BuildSummaryQuotaBuckets(usedPercentage, remainingPercentage, q?.NextResetTime, u.Description),
                };
                ApplyEffectiveModelState(model, q?.IsQuotaBased ?? false);
                return model;
            })
            .ToList();
    }

    private static AgentGroupedQuotaBucketUsage[] BuildSummaryQuotaBuckets(
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
