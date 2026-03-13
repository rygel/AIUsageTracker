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
                ProviderName = ProviderMetadataCatalog.GetConfiguredDisplayName(provider.ProviderId),
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
                var detail = new ProviderUsageDetail
                {
                    Name = model.ModelName,
                    ModelName = model.ModelId,
                    Description = modelState.Description,
                    NextResetTime = modelState.NextResetTime,
                    DetailType = ProviderUsageDetailType.Model,
                    QuotaBucketKind = WindowKind.None,
                };

                if (provider.IsQuotaBased)
                {
                    detail.SetPercentageValue(modelState.RemainingPercentage, PercentageValueSemantic.Remaining, decimalPlaces: 1);
                }
                else
                {
                    detail.SetPercentageValue(modelState.UsedPercentage, PercentageValueSemantic.Used, decimalPlaces: 1);
                }

                return detail;
            })
            .ToList();
    }

    private static IReadOnlyList<ProviderUsage> BuildVisibleDerivedRows(
        AgentGroupedProviderUsage provider,
        ProviderUsage parentUsage)
    {
        if (provider.Models.Count == 0)
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

        var assignments = ProviderDerivedModelAssignmentResolver.Resolve(provider.ProviderId, orderedModels);
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
                ProviderName = ProviderMetadataCatalog.ResolveDisplayLabel(
                    assignment.ProviderId,
                    ProviderMetadataCatalog.GetDerivedModelDisplayName(provider.ProviderId, model.ModelName)),
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
