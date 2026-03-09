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

        var configs = DeterministicProviderScenarioCatalog.Scenarios
            .Select(CreateConfig)
            .ToList();

        var usages = new List<ProviderUsage>
        {
            CreateUsage(
                new DeterministicProviderScenario(AntigravityProvider.StaticDefinition.ProviderId, "local-session"),
                deterministicNow,
                new FixtureUsageScenario(60.0, 0, 0, "60.0% Remaining", 6),
                details: new List<ProviderUsageDetail>
                {
                    CreateDetail(deterministicNow, "Claude Opus 4.6 (Thinking)", "60%", 10),
                    CreateDetail(deterministicNow, "Claude Sonnet 4.6 (Thinking)", "60%", 10),
                    CreateDetail(deterministicNow, "Gemini 3 Flash", "100%", 6),
                    CreateDetail(deterministicNow, "Gemini 3.1 Pro (High)", "100%", 14),
                    CreateDetail(deterministicNow, "Gemini 3.1 Pro (Low)", "100%", 14),
                    CreateDetail(deterministicNow, "GPT-OSS 120B (Medium)", "60%", 8),
                }),
        };

        usages.AddRange(DeterministicProviderScenarioCatalog.Scenarios
            .Where(scenario => scenario.SettingsWindowUsage != null)
            .Select(scenario => CreateUsage(scenario, deterministicNow, scenario.SettingsWindowUsage!)));

        var historyRows = DeterministicProviderScenarioCatalog.Scenarios
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
        bool isAvailable = true,
        List<ProviderUsageDetail>? details = null)
    {
        var definition = ProviderMetadataCatalog.Find(scenario.ProviderId)
            ?? throw new InvalidOperationException($"Unknown provider id '{scenario.ProviderId}' in deterministic screenshot data.");

        return new ProviderUsage
        {
            ProviderId = scenario.ProviderId,
            ProviderName = ProviderMetadataCatalog.GetDisplayName(scenario.ProviderId),
            IsAvailable = isAvailable,
            IsQuotaBased = definition.IsQuotaBased,
            PlanType = definition.PlanType,
            RequestsPercentage = usageScenario.RequestsPercentage,
            RequestsUsed = usageScenario.RequestsUsed,
            RequestsAvailable = usageScenario.RequestsAvailable,
            Description = usageScenario.Description,
            Details = details,
            NextResetTime = usageScenario.ResetHours.HasValue
                ? deterministicNow.AddHours(usageScenario.ResetHours.Value)
                : null,
        };
    }

    private static ProviderUsageDetail CreateDetail(DateTime deterministicNow, string name, string used, int resetHours)
    {
        return new ProviderUsageDetail
        {
            Name = name,
            ModelName = name,
            GroupName = "Recommended Group 1",
            Used = used,
            Description = $"{used} remaining",
            NextResetTime = deterministicNow.AddHours(resetHours),
        };
    }

    private static SettingsWindowHistoryRow CreateHistoryRow(string providerId, FixtureHistoryScenario scenario)
    {
        var definition = ProviderMetadataCatalog.Find(providerId)
            ?? throw new InvalidOperationException($"Unknown provider id '{providerId}' in deterministic screenshot history.");

        return new SettingsWindowHistoryRow
        {
            ProviderName = ProviderMetadataCatalog.GetDisplayName(providerId),
            UsagePercentage = scenario.UsagePercentage,
            Used = scenario.Used,
            Limit = scenario.Limit,
            PlanType = definition.PlanType.ToString(),
            Description = scenario.Description,
            FetchedAt = scenario.FetchedAt,
        };
    }
}
