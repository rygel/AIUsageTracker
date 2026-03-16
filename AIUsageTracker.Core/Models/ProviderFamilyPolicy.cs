// <copyright file="ProviderFamilyPolicy.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models;

public static class ProviderFamilyPolicy
{
    public static bool SupportsChildProviderIds(ProviderFamilyMode familyMode)
    {
        return familyMode != ProviderFamilyMode.Standalone;
    }

    public static bool ShouldCollapseDerivedChildrenInMainWindow(ProviderFamilyMode familyMode)
    {
        return familyMode == ProviderFamilyMode.CollapsedDerivedProviders;
    }

    public static bool ShouldRenderSyntheticChildrenInMainWindow(ProviderFamilyMode familyMode)
    {
        return familyMode == ProviderFamilyMode.SyntheticAggregateChildren;
    }

    public static bool UsesChildProviderRowsForGroupedModels(ProviderFamilyMode familyMode)
    {
        return familyMode == ProviderFamilyMode.DynamicChildProviderRows;
    }

    public static bool HasDisplayableDerivedProviders(
        IReadOnlyCollection<string> visibleDerivedProviderIds,
        ProviderFamilyMode familyMode)
    {
        return visibleDerivedProviderIds.Count > 0 ||
               familyMode == ProviderFamilyMode.DynamicChildProviderRows ||
               familyMode == ProviderFamilyMode.SyntheticAggregateChildren;
    }

    public static bool BelongsToProviderFamily(
        IReadOnlyCollection<string> handledProviderIds,
        string candidateProviderId,
        ProviderFamilyMode familyMode)
    {
        if (string.IsNullOrWhiteSpace(candidateProviderId))
        {
            return false;
        }

        return handledProviderIds.Contains(candidateProviderId, StringComparer.OrdinalIgnoreCase) ||
               IsChildProviderId(handledProviderIds, candidateProviderId, familyMode);
    }

    public static bool IsChildProviderId(
        IReadOnlyCollection<string> handledProviderIds,
        string candidateProviderId,
        ProviderFamilyMode familyMode)
    {
        if (string.IsNullOrWhiteSpace(candidateProviderId) || !SupportsChildProviderIds(familyMode))
        {
            return false;
        }

        if (handledProviderIds.Contains(candidateProviderId, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return handledProviderIds.Any(handledProviderId =>
            candidateProviderId.StartsWith($"{handledProviderId}.", StringComparison.OrdinalIgnoreCase));
    }

    public static bool TryGetChildProviderKey(
        IReadOnlyCollection<string> handledProviderIds,
        string candidateProviderId,
        ProviderFamilyMode familyMode,
        out string childProviderKey)
    {
        childProviderKey = string.Empty;
        if (!IsChildProviderId(handledProviderIds, candidateProviderId, familyMode))
        {
            return false;
        }

        var matchedHandledProviderId = handledProviderIds
            .Where(handledProviderId => candidateProviderId.StartsWith($"{handledProviderId}.", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(handledProviderId => handledProviderId.Length)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(matchedHandledProviderId))
        {
            return false;
        }

        childProviderKey = candidateProviderId[(matchedHandledProviderId.Length + 1)..];
        return true;
    }
}
