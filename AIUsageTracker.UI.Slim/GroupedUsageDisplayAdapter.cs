// <copyright file="GroupedUsageDisplayAdapter.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.UI.Slim;

internal static class GroupedUsageDisplayAdapter
{
    public static IReadOnlyList<ProviderUsage> Expand(AgentGroupedUsageSnapshot? snapshot)
    {
        if (snapshot?.Providers == null || snapshot.Providers.Count == 0)
        {
            return Array.Empty<ProviderUsage>();
        }

        var usages = new List<ProviderUsage>(snapshot.Providers.Count * 2);
        foreach (var provider in snapshot.Providers
                     .Where(provider => !string.IsNullOrWhiteSpace(provider.ProviderId))
                     .OrderBy(provider => provider.ProviderId, StringComparer.OrdinalIgnoreCase))
        {
            var details = BuildModelDetails(provider);
            var parentUsage = new ProviderUsage
            {
                ProviderId = provider.ProviderId,
                ProviderName = provider.ProviderName,
                AccountName = provider.AccountName,
                IsAvailable = provider.IsAvailable,
                PlanType = provider.PlanType,
                IsQuotaBased = provider.IsQuotaBased,
                RequestsUsed = provider.RequestsUsed,
                RequestsAvailable = provider.RequestsAvailable,
                RequestsPercentage = provider.RequestsPercentage,
                Description = provider.Description,
                FetchedAt = provider.FetchedAt,
                NextResetTime = provider.NextResetTime,
                UsageUnit = provider.IsQuotaBased ? "Quota %" : "Requests",
                Details = details.Count > 0 ? details : null,
            };

            usages.Add(parentUsage);
            usages.AddRange(BuildVisibleDerivedRows(provider, parentUsage));
        }

        return usages;
    }

    private static IReadOnlyList<ProviderUsageDetail> BuildModelDetails(AgentGroupedProviderUsage provider)
    {
        if (provider.Models.Count == 0)
        {
            return Array.Empty<ProviderUsageDetail>();
        }

        return provider.Models
            .Where(model => !string.IsNullOrWhiteSpace(model.ModelName))
            .OrderBy(model => model.ModelName, StringComparer.OrdinalIgnoreCase)
            .Select(model =>
            {
                var modelState = ResolveModelState(model, provider.IsQuotaBased);
                return new ProviderUsageDetail
                {
                    Name = model.ModelName,
                    ModelName = model.ModelId,
                    Used = $"{modelState.RemainingPercentage:F1}%",
                    Description = modelState.Description,
                    NextResetTime = modelState.NextResetTime,
                    DetailType = ProviderUsageDetailType.Model,
                    QuotaBucketKind = WindowKind.None,
                };
            })
            .ToList();
    }

    private static IReadOnlyList<ProviderUsage> BuildVisibleDerivedRows(
        AgentGroupedProviderUsage provider,
        ProviderUsage parentUsage)
    {
        if (!ProviderMetadataCatalog.TryGet(provider.ProviderId, out var definition) ||
            definition.VisibleDerivedProviderIds.Count == 0 ||
            provider.Models.Count == 0)
        {
            return Array.Empty<ProviderUsage>();
        }

        var orderedModels = provider.Models
            .Where(model => !string.IsNullOrWhiteSpace(model.ModelName))
            .OrderBy(model => model.ModelName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (orderedModels.Count == 0)
        {
            return Array.Empty<ProviderUsage>();
        }

        var assignments = BuildDerivedAssignments(definition, provider.ProviderId, orderedModels);
        if (assignments.Count == 0)
        {
            return Array.Empty<ProviderUsage>();
        }

        var childRows = new List<ProviderUsage>(assignments.Count);
        foreach (var assignment in assignments)
        {
            var model = assignment.Model;
            var modelState = ResolveModelState(model, provider.IsQuotaBased);
            var quotaBucketDetails = BuildQuotaBucketDetails(model, provider.IsQuotaBased);

            childRows.Add(new ProviderUsage
            {
                ProviderId = assignment.ProviderId,
                ProviderName = ProviderMetadataCatalog.GetDerivedModelDisplayName(provider.ProviderId, model.ModelName),
                AccountName = parentUsage.AccountName,
                IsAvailable = parentUsage.IsAvailable,
                PlanType = parentUsage.PlanType,
                IsQuotaBased = parentUsage.IsQuotaBased,
                RequestsUsed = modelState.UsedPercentage,
                RequestsAvailable = 100,
                RequestsPercentage = parentUsage.IsQuotaBased
                    ? modelState.RemainingPercentage
                    : modelState.UsedPercentage,
                Description = modelState.Description,
                FetchedAt = parentUsage.FetchedAt,
                NextResetTime = modelState.NextResetTime,
                UsageUnit = parentUsage.UsageUnit,
                Details = quotaBucketDetails.Count > 0 ? quotaBucketDetails : null,
            });
        }

        return childRows;
    }

    private static IReadOnlyList<(string ProviderId, AgentGroupedModelUsage Model)> BuildDerivedAssignments(
        ProviderDefinition definition,
        string canonicalProviderId,
        IReadOnlyList<AgentGroupedModelUsage> orderedModels)
    {
        var visibleDerivedProviderIds = definition.VisibleDerivedProviderIds.ToList();
        if (visibleDerivedProviderIds.Count == 0 || orderedModels.Count == 0)
        {
            return Array.Empty<(string ProviderId, AgentGroupedModelUsage Model)>();
        }

        var assignments = new List<(string ProviderId, AgentGroupedModelUsage Model)>(Math.Min(visibleDerivedProviderIds.Count, orderedModels.Count));
        var usedModelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selectorsByProviderId = definition.DerivedModelSelectors
            .GroupBy(selector => selector.DerivedProviderId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var derivedProviderId in visibleDerivedProviderIds)
        {
            if (!selectorsByProviderId.TryGetValue(derivedProviderId, out var selector))
            {
                continue;
            }

            if (!TrySelectModelForDerivedProvider(selector, orderedModels, usedModelIds, out var matched) || matched == null)
            {
                continue;
            }

            usedModelIds.Add(GetModelAssignmentKey(matched));
            assignments.Add((derivedProviderId, matched));
        }

        if (visibleDerivedProviderIds.Count <= 1)
        {
            return assignments;
        }

        var remainingAssignments = BuildDynamicModelAssignments(
            canonicalProviderId,
            orderedModels,
            assignments.Select(assignment => assignment.ProviderId),
            assignments.Select(assignment => GetModelAssignmentKey(assignment.Model)));

        if (remainingAssignments.Count == 0)
        {
            return assignments;
        }

        assignments.AddRange(remainingAssignments);
        return assignments;
    }

    private static IReadOnlyList<(string ProviderId, AgentGroupedModelUsage Model)> BuildDynamicModelAssignments(
        string canonicalProviderId,
        IReadOnlyList<AgentGroupedModelUsage> orderedModels,
        IEnumerable<string>? reservedProviderIds = null,
        IEnumerable<string>? reservedModelKeys = null)
    {
        var assignments = new List<(string ProviderId, AgentGroupedModelUsage Model)>(orderedModels.Count);
        var usedProviderIds = new HashSet<string>(
            reservedProviderIds ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        var usedModelKeys = new HashSet<string>(
            reservedModelKeys ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);

        foreach (var model in orderedModels)
        {
            var modelKey = GetModelAssignmentKey(model);
            if (string.IsNullOrWhiteSpace(modelKey) || usedModelKeys.Contains(modelKey))
            {
                continue;
            }

            var derivedProviderId = $"{canonicalProviderId}.{modelKey}";
            if (!usedProviderIds.Add(derivedProviderId))
            {
                continue;
            }

            assignments.Add((derivedProviderId, model));
            usedModelKeys.Add(modelKey);
        }

        return assignments;
    }

    private static string GetModelAssignmentKey(AgentGroupedModelUsage model)
    {
        if (!string.IsNullOrWhiteSpace(model.ModelId))
        {
            return model.ModelId;
        }

        return model.ModelName;
    }

    private static bool TrySelectModelForDerivedProvider(
        ProviderDerivedModelSelector selector,
        IReadOnlyList<AgentGroupedModelUsage> models,
        ISet<string> usedModelIds,
        out AgentGroupedModelUsage? matched)
    {
        matched = null;

        var candidates = models
            .Where(model => !usedModelIds.Contains(GetModelAssignmentKey(model)))
            .ToList();
        if (candidates.Count == 0)
        {
            return false;
        }

        if (selector.ModelIdEquals.Count > 0)
        {
            matched = candidates.FirstOrDefault(model =>
                selector.ModelIdEquals.Contains(model.ModelId, StringComparer.OrdinalIgnoreCase));
            if (matched != null)
            {
                return true;
            }
        }

        if (selector.ModelIdContains.Count > 0)
        {
            matched = candidates.FirstOrDefault(model =>
                ContainsAnyToken(model.ModelId, selector.ModelIdContains));
            if (matched != null)
            {
                return true;
            }
        }

        if (selector.ModelNameContains.Count > 0)
        {
            matched = candidates.FirstOrDefault(model =>
                ContainsAnyToken(model.ModelName, selector.ModelNameContains));
            if (matched != null)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAnyToken(string? source, IReadOnlyCollection<string> tokens)
    {
        if (string.IsNullOrWhiteSpace(source) || tokens.Count == 0)
        {
            return false;
        }

        foreach (var token in tokens)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            if (source.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<ProviderUsageDetail> BuildQuotaBucketDetails(
        AgentGroupedModelUsage model,
        bool parentIsQuotaBased)
    {
        if (model.QuotaBuckets.Count == 0)
        {
            return Array.Empty<ProviderUsageDetail>();
        }

        return model.QuotaBuckets
            .Where(bucket => !string.IsNullOrWhiteSpace(bucket.BucketName))
            .OrderBy(bucket => bucket.NextResetTime ?? DateTime.MaxValue)
            .ThenBy(bucket => bucket.BucketName, StringComparer.OrdinalIgnoreCase)
            .Select(bucket =>
            {
                var usedPercent = AgentGroupedUsageValueResolver.ResolveBucketUsedPercentage(bucket, parentIsQuotaBased);
                var remainingPercent = AgentGroupedUsageValueResolver.ResolveBucketRemainingPercentage(bucket, parentIsQuotaBased);
                return new ProviderUsageDetail
                {
                    Name = bucket.BucketName,
                    Used = parentIsQuotaBased
                        ? $"{remainingPercent:F1}% remaining ({usedPercent:F1}% used)"
                        : $"{usedPercent:F1}% used",
                    Description = bucket.Description ?? string.Empty,
                    NextResetTime = bucket.NextResetTime,
                    DetailType = ProviderUsageDetailType.QuotaWindow,
                    QuotaBucketKind = WindowKind.None,
                };
            })
            .ToList();
    }

    private static (double UsedPercentage, double RemainingPercentage, string Description, DateTime? NextResetTime) ResolveModelState(
        AgentGroupedModelUsage model,
        bool parentIsQuotaBased)
    {
        return AgentGroupedUsageValueResolver.ResolveModelEffectiveState(model, parentIsQuotaBased);
    }
}
