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
        var models = BuildModels(group, canonicalProviderId);
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
        // Flat cards with WindowKind != None are the quota-window cards for this group.
        var providerDetails = (IReadOnlyList<ProviderUsage>)group
            .Where(u => u.WindowKind != WindowKind.None && !u.IsStale)
            .OrderBy(u => u.NextResetTime ?? DateTime.MaxValue)
            .ThenBy(u => u.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
            UsedPercent = primary.UsedPercent,
            Description = primary.Description,
            FetchedAt = group.Max(usage => usage.FetchedAt),
            NextResetTime = nextResetTime,
            Models = models,
            ProviderDetails = providerDetails,
        };
    }

    private static string ResolveProviderDisplayName(ProviderUsage primary, string canonicalProviderId)
    {
        return ProviderMetadataCatalog.GetConfiguredDisplayName(canonicalProviderId);
    }

    private static ProviderUsage SelectPrimaryUsage(IEnumerable<ProviderUsage> group, string canonicalProviderId)
    {
        return group
                .Where(usage => string.Equals(usage.ProviderId, canonicalProviderId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(usage => usage.FetchedAt)
                .FirstOrDefault()
            ?? group
                .OrderByDescending(usage => usage.FetchedAt)
                .First();
    }

    private static IReadOnlyList<AgentGroupedModelUsage> BuildModels(
        IEnumerable<ProviderUsage> group,
        string canonicalProviderId)
    {
        var usages = group.ToList();

        // Flat-card providers: if any non-currency usages have a CardId, project them as models.
        // Currency/balance cards (e.g. DeepSeek balance-usd) have CardId but must not be projected
        // as quota model rows — they are balance display cards, not model-level quota windows.
        var flatModelCards = usages.Where(u => !string.IsNullOrWhiteSpace(u.CardId) && !u.IsCurrencyUsage).ToList();
        if (flatModelCards.Count > 0)
        {
            return BuildModelsFromFlatCards(flatModelCards, canonicalProviderId);
        }

        if ((ProviderMetadataCatalog.Find(canonicalProviderId)?.UseChildProviderRowsForGroupedModels ?? false) &&
            ShouldBuildModelsFromExplicitChildRows(usages, canonicalProviderId))
        {
            return BuildModelsFromExplicitChildRows(usages, canonicalProviderId);
        }

        return BuildModelsFromDetails(usages);
    }

    private static IReadOnlyList<AgentGroupedModelUsage> BuildModelsFromFlatCards(
        IEnumerable<ProviderUsage> group,
        string canonicalProviderId)
    {
        return group
            .Where(u => !string.IsNullOrWhiteSpace(u.CardId))
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
                    ModelName = u.Name ?? u.CardId!,
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

    private static IReadOnlyList<AgentGroupedModelUsage> BuildModelsFromDetails(IEnumerable<ProviderUsage> group)
    {
        // Legacy path: providers that neither emit flat cards nor use explicit child rows.
        // With all providers now emitting flat cards, this path returns empty.
        // Left in place to avoid removing the BuildModels dispatch branch.
        return Array.Empty<AgentGroupedModelUsage>();
    }

    private static bool ShouldBuildModelsFromExplicitChildRows(
        IReadOnlyCollection<ProviderUsage> usages,
        string canonicalProviderId)
    {
        if (!(ProviderMetadataCatalog.Find(canonicalProviderId)?.UseChildProviderRowsForGroupedModels ?? false))
        {
            return false;
        }

        return usages.Any(usage => IsExplicitChildUsage(usage, canonicalProviderId));
    }

    private static IReadOnlyList<AgentGroupedModelUsage> BuildModelsFromExplicitChildRows(
        IEnumerable<ProviderUsage> group,
        string canonicalProviderId)
    {
        return group
            .Where(usage => IsExplicitChildUsage(usage, canonicalProviderId))
            .GroupBy(usage => usage.ProviderId, StringComparer.OrdinalIgnoreCase)
            .Select(childGroup => childGroup
                .OrderByDescending(usage => usage.FetchedAt)
                .First())
            .OrderBy(usage => usage.ProviderName, StringComparer.OrdinalIgnoreCase)
            .Select(usage =>
            {
                var remainingPercentage = UsageMath.ClampPercent(usage.RemainingPercent);
                var usedPercentage = UsageMath.ClampPercent(usage.UsedPercent);
                var quotaBuckets = BuildSummaryQuotaBuckets(usedPercentage, remainingPercentage, usage.NextResetTime, usage.Description);
                var model = new AgentGroupedModelUsage
                {
                    ModelId = ResolveChildModelId(usage, canonicalProviderId),
                    ModelName = ResolveChildModelName(usage, canonicalProviderId),
                    UsedPercentage = usedPercentage,
                    RemainingPercentage = remainingPercentage,
                    NextResetTime = usage.NextResetTime,
                    Description = usage.Description ?? string.Empty,
                    QuotaBuckets = quotaBuckets.Count > 0
                        ? quotaBuckets
                        : BuildSummaryQuotaBuckets(
                            usedPercentage,
                            remainingPercentage,
                            usage.NextResetTime,
                            usage.Description),
                };
                ApplyEffectiveModelState(model, usage.IsQuotaBased);
                return model;
            })
            .ToList();
    }

    private static IReadOnlyList<AgentGroupedQuotaBucketUsage> BuildQuotaBucketsFromCards(
        IEnumerable<ProviderUsage> windowCards)
    {
        var buckets = new List<AgentGroupedQuotaBucketUsage>();
        var usedBucketIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var card in windowCards
                     .Where(card => !string.IsNullOrWhiteSpace(card.Name))
                     .OrderBy(card => card.NextResetTime ?? DateTime.MaxValue)
                     .ThenBy(card => card.Name, StringComparer.OrdinalIgnoreCase))
        {
            var usedPercent = UsageMath.ClampPercent(card.UsedPercent);
            var remainingPercent = UsageMath.ClampPercent(card.RemainingPercent);

            var baseBucketId = card.CardId ?? CreateModelIdFromName(card.Name!);
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
                BucketName = card.Name!,
                UsedPercentage = usedPercent,
                RemainingPercentage = remainingPercent,
                NextResetTime = card.NextResetTime,
                Description = card.Description ?? string.Empty,
                QuotaBucketKind = card.WindowKind,
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

    private static bool IsExplicitChildUsage(ProviderUsage usage, string canonicalProviderId)
    {
        return !string.IsNullOrWhiteSpace(usage.ProviderId) &&
               !string.Equals(usage.ProviderId, canonicalProviderId, StringComparison.OrdinalIgnoreCase) &&
               (ProviderMetadataCatalog.Find(canonicalProviderId)?.IsChildProviderId(usage.ProviderId) ?? false);
    }

    private static string ResolveChildModelId(ProviderUsage usage, string canonicalProviderId)
    {
        if (ProviderMetadataCatalog.Find(canonicalProviderId)?.TryGetChildProviderKey(usage.ProviderId ?? string.Empty, out var childProviderKey) ?? false)
        {
            return childProviderKey;
        }

        return usage.ProviderId ?? string.Empty;
    }

    private static string ResolveChildModelName(ProviderUsage usage, string canonicalProviderId)
    {
        if (!string.IsNullOrWhiteSpace(usage.ProviderName))
        {
            return usage.ProviderName;
        }

        return ResolveChildModelId(usage, canonicalProviderId);
    }
}
