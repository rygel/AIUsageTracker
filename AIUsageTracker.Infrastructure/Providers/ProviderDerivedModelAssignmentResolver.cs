// <copyright file="ProviderDerivedModelAssignmentResolver.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;

namespace AIUsageTracker.Infrastructure.Providers;

public static class ProviderDerivedModelAssignmentResolver
{
    public static IReadOnlyList<ProviderDerivedModelAssignment> Resolve(
        string canonicalProviderId,
        IReadOnlyList<AgentGroupedModelUsage> orderedModels)
    {
        if (string.IsNullOrWhiteSpace(canonicalProviderId) ||
            orderedModels.Count == 0)
        {
            return Array.Empty<ProviderDerivedModelAssignment>();
        }

        var definition = ProviderMetadataCatalog.Find(canonicalProviderId);
        if (definition == null)
        {
            return Array.Empty<ProviderDerivedModelAssignment>();
        }

        if (definition.VisibleDerivedProviderIds.Count > 0)
        {
            return BuildDerivedAssignments(definition, canonicalProviderId, orderedModels);
        }

        if (definition.UseChildProviderRowsForGroupedModels)
        {
            return BuildDynamicModelAssignments(canonicalProviderId, orderedModels);
        }

        return Array.Empty<ProviderDerivedModelAssignment>();
    }

    private static IReadOnlyList<ProviderDerivedModelAssignment> BuildDerivedAssignments(
        ProviderDefinition definition,
        string canonicalProviderId,
        IReadOnlyList<AgentGroupedModelUsage> orderedModels)
    {
        var visibleDerivedProviderIds = definition.VisibleDerivedProviderIds.ToList();
        if (visibleDerivedProviderIds.Count == 0 || orderedModels.Count == 0)
        {
            return Array.Empty<ProviderDerivedModelAssignment>();
        }

        var assignments = new List<ProviderDerivedModelAssignment>(Math.Min(visibleDerivedProviderIds.Count, orderedModels.Count));
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
            assignments.Add(new ProviderDerivedModelAssignment(derivedProviderId, matched));
        }

        if (visibleDerivedProviderIds.Count <= 1)
        {
            return assignments;
        }

        var remainingAssignments = BuildDynamicModelAssignments(
            canonicalProviderId,
            orderedModels,
            assignments.Select(assignment => assignment.ProviderId),
            assignments.Select(assignment => GetModelAssignmentKey(assignment.Model))
                       .Concat(definition.ExcludedDerivedModelIds));

        if (remainingAssignments.Count == 0)
        {
            return assignments;
        }

        assignments.AddRange(remainingAssignments);
        return assignments;
    }

    private static IReadOnlyList<ProviderDerivedModelAssignment> BuildDynamicModelAssignments(
        string canonicalProviderId,
        IReadOnlyList<AgentGroupedModelUsage> orderedModels,
        IEnumerable<string>? reservedProviderIds = null,
        IEnumerable<string>? reservedModelKeys = null)
    {
        var assignments = new List<ProviderDerivedModelAssignment>(orderedModels.Count);
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

            assignments.Add(new ProviderDerivedModelAssignment(derivedProviderId, model));
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
}
