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
        var primary = SelectPrimaryUsage(group, providerId);
        var models = BuildModels(group, providerId);
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

        var displayName = ProviderMetadataCatalog.GetConfiguredDisplayName(providerId);
        var providerDetails = (IReadOnlyList<ProviderUsage>)group
            .Where(u => u.WindowKind != WindowKind.None && !u.IsStale)
            .OrderBy(u => u.NextResetTime ?? DateTime.MaxValue)
            .ThenBy(u => u.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AgentGroupedProviderUsage
        {
            ProviderId = providerId,
            ProviderName = displayName,
            AccountName = accountName,
            IsAvailable = group.Any(usage => usage.IsAvailable),
            State = primary.State,
            PlanType = primary.PlanType,
            IsQuotaBased = primary.IsQuotaBased,
            RequestsUsed = primary.RequestsUsed,
            RequestsAvailable = primary.RequestsAvailable,
            UsedPercent = primary.UsedPercent,
            Description = primary.Description,
            FetchedAt = group.Max(usage => usage.FetchedAt),
            NextResetTime = nextResetTime,
            Models = models,
            ProviderDetails = providerDetails,
        };
    }

    private static ProviderUsage SelectPrimaryUsage(IEnumerable<ProviderUsage> group, string ownerProviderId)
    {
        var ownerUsage = group
            .Where(usage => string.Equals(usage.ProviderId, ownerProviderId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(usage => usage.FetchedAt)
            .FirstOrDefault();

        if (ownerUsage != null)
        {
            return ownerUsage;
        }

        throw new InvalidOperationException(
            $"Grouped usage for owner '{ownerProviderId}' did not contain a matching owner row.");
    }

    private static List<AgentGroupedModelUsage> BuildModels(
        IEnumerable<ProviderUsage> group,
        string ownerProviderId)
    {
        var usages = group.ToList();
        var definition = ProviderMetadataCatalog.Find(ownerProviderId);
        var cardCandidates = definition?.FamilyMode == ProviderFamilyMode.FlatWindowCards
            ? usages.Where(u => !string.IsNullOrWhiteSpace(u.CardId) && !u.IsCurrencyUsage)
            : usages.Where(u =>
                !string.IsNullOrWhiteSpace(u.CardId) &&
                !u.IsCurrencyUsage &&
                u.WindowKind == WindowKind.None);

        return BuildModelsFromFlatCards(cardCandidates);
    }

    private static List<AgentGroupedModelUsage> BuildModelsFromFlatCards(
        IEnumerable<ProviderUsage> group)
    {
        return group
            .Where(u => !string.IsNullOrWhiteSpace(u.CardId) && !string.IsNullOrWhiteSpace(u.Name))
            .GroupBy(u => u.CardId!, StringComparer.OrdinalIgnoreCase)
            .Select(cardGroup => cardGroup.OrderByDescending(u => u.FetchedAt).First())
            .OrderBy(u => u.CardId, StringComparer.OrdinalIgnoreCase)
            .Select(u =>
            {
                var usedPercentage = UsageMath.ClampPercent(u.UsedPercent);
                var remainingPercentage = UsageMath.ClampPercent(u.RemainingPercent);
                var model = new AgentGroupedModelUsage
                {
                    ModelId = u.CardId!,
                    ModelName = u.Name!,
                    UsedPercentage = usedPercentage,
                    RemainingPercentage = remainingPercentage,
                    NextResetTime = u.NextResetTime,
                    Description = u.Description ?? string.Empty,
                    QuotaBuckets = BuildSummaryQuotaBuckets(usedPercentage, remainingPercentage, u.NextResetTime, u.Description),
                };
                ApplyEffectiveModelState(model, u.IsQuotaBased);
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
