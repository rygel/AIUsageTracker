// <copyright file="SettingsWindowDeterministicFixture.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.UI.Slim;

internal static class SettingsWindowDeterministicFixture
{
    public static SettingsWindowDeterministicFixtureData Create()
    {
        var deterministicNow = new DateTime(2026, 02, 01, 12, 00, 00, DateTimeKind.Local);

        var configs = DeterministicProviderScenario.Scenarios
            .Select(CreateConfig)
            .ToList();

        var usages = new List<ProviderUsage>
        {
            CreateUsage(
                new DeterministicProviderScenario(AntigravityProvider.StaticDefinition.ProviderId, "local-session"),
                deterministicNow,
                new FixtureUsageScenario(40.0, 0, 0, "60.0% Remaining", 6)),
        };

        usages.AddRange(DeterministicProviderScenario.Scenarios
            .Where(scenario => scenario.SettingsWindowUsage != null)
            .Select(scenario => CreateUsage(scenario, deterministicNow, scenario.SettingsWindowUsage!)));

        var historyRows = DeterministicProviderScenario.Scenarios
            .Where(scenario => scenario.SettingsWindowHistory != null)
            .Select(scenario => CreateHistoryRow(scenario.ProviderId, scenario.SettingsWindowHistory!))
            .ToArray();

        return new SettingsWindowDeterministicFixtureData
        {
            Configs = configs,
            Usages = usages,
            HistoryRows = historyRows,
        };
    }

    private static ProviderConfig CreateConfig(DeterministicProviderScenario scenario)
    {
        if (!ProviderMetadataCatalog.TryCreateDefaultConfig(scenario.ProviderId, out var config, apiKey: scenario.ApiKey))
        {
            throw new InvalidOperationException($"Unknown provider id '{scenario.ProviderId}' in deterministic screenshot data.");
        }

        config.ShowInTray = scenario.ShowInTray;
        config.EnableNotifications = scenario.EnableNotifications;
        return config;
    }

    private static ProviderUsage CreateUsage(
        DeterministicProviderScenario scenario,
        DateTime deterministicNow,
        FixtureUsageScenario usageScenario,
        bool isAvailable = true)
    {
        var def = ProviderMetadataCatalog.Find(scenario.ProviderId)
            ?? throw new InvalidOperationException($"Unknown provider id '{scenario.ProviderId}' in deterministic screenshot data.");
        var planType = def.PlanType;
        var isQuotaBased = def.IsQuotaBased;

        return new ProviderUsage
        {
            ProviderId = scenario.ProviderId,
            ProviderName = ProviderMetadataCatalog.ResolveDisplayLabel(scenario.ProviderId),
            IsAvailable = isAvailable,
            IsQuotaBased = isQuotaBased,
            PlanType = planType,
            UsedPercent = usageScenario.UsedPercent,
            RequestsUsed = usageScenario.RequestsUsed,
            RequestsAvailable = usageScenario.RequestsAvailable,
            Description = usageScenario.Description,
            NextResetTime = usageScenario.ResetHours.HasValue
                ? deterministicNow.AddHours(usageScenario.ResetHours.Value)
                : null,
        };
    }

    private static SettingsWindowHistoryRow CreateHistoryRow(string providerId, FixtureHistoryScenario scenario)
    {
        var histDef = ProviderMetadataCatalog.Find(providerId)
            ?? throw new InvalidOperationException($"Unknown provider id '{providerId}' in deterministic screenshot history.");
        var planType = histDef.PlanType;

        return new SettingsWindowHistoryRow
        {
            ProviderName = ProviderMetadataCatalog.ResolveDisplayLabel(providerId),
            UsagePercentage = scenario.UsagePercentage,
            Used = scenario.Used,
            Limit = scenario.Limit,
            PlanType = planType.ToString(),
            Description = scenario.Description,
            FetchedAt = scenario.FetchedAt,
        };
    }
}
