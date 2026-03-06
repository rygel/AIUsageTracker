using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.UI.Slim;

internal sealed record ProviderRenderPreparation(
    IReadOnlyList<ProviderUsage> DisplayableUsages,
    bool HasAntigravityParent);

internal static class ProviderUsageDisplayCatalog
{
    public static ProviderRenderPreparation PrepareForMainWindow(IReadOnlyCollection<ProviderUsage> usages)
    {
        var filteredUsages = usages.ToList();
        var hasAntigravityParent = filteredUsages.Any(IsAntigravityParent);

        filteredUsages = filteredUsages
            .Where(ShouldDisplayUsage(hasAntigravityParent))
            .GroupBy(usage => usage.ProviderId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        return new ProviderRenderPreparation(filteredUsages, hasAntigravityParent);
    }

    public static IReadOnlyList<ProviderUsage> CreateAntigravityModelUsages(ProviderUsage parentUsage)
    {
        if (parentUsage.Details?.Any() != true)
        {
            return Array.Empty<ProviderUsage>();
        }

        return parentUsage.Details
            .Where(detail => !string.IsNullOrWhiteSpace(detail.Name) && !detail.Name.StartsWith("[", StringComparison.Ordinal))
            .GroupBy(detail => detail.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(detail => detail.Name, StringComparer.OrdinalIgnoreCase)
            .Select(detail => CreateAntigravityModelUsage(detail, parentUsage))
            .ToList();
    }

    private static Func<ProviderUsage, bool> ShouldDisplayUsage(bool hasAntigravityParent)
    {
        return usage =>
        {
            var providerId = usage.ProviderId ?? string.Empty;
            return !IsUnavailableAntigravityParent(usage) &&
                   (!providerId.StartsWith("antigravity.", StringComparison.OrdinalIgnoreCase) || !hasAntigravityParent);
        };
    }

    private static bool IsUnavailableAntigravityParent(ProviderUsage usage)
    {
        return IsAntigravityParent(usage) && !usage.IsAvailable;
    }

    private static bool IsAntigravityParent(ProviderUsage usage)
    {
        return ProviderMetadataCatalog.IsAggregateParentProviderId(usage.ProviderId ?? string.Empty);
    }

    private static ProviderUsage CreateAntigravityModelUsage(ProviderUsageDetail detail, ProviderUsage parentUsage)
    {
        var remainingPercent = UsageMath.ParsePercent(detail.Used);
        var hasRemainingPercent = remainingPercent.HasValue;
        var effectiveRemaining = remainingPercent ?? 0;

        return new ProviderUsage
        {
            ProviderId = $"antigravity.{detail.Name.ToLowerInvariant().Replace(" ", "-", StringComparison.Ordinal)}",
            ProviderName = $"{detail.Name} [Antigravity]",
            RequestsPercentage = effectiveRemaining,
            RequestsUsed = 100.0 - effectiveRemaining,
            RequestsAvailable = 100,
            UsageUnit = "Quota %",
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            Description = hasRemainingPercent ? $"{effectiveRemaining:F0}% Remaining" : "Usage unknown",
            NextResetTime = detail.NextResetTime,
            IsAvailable = parentUsage.IsAvailable,
            AuthSource = parentUsage.AuthSource
        };
    }
}
