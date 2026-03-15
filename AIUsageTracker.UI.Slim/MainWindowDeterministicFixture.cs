// <copyright file="MainWindowDeterministicFixture.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.UI.Slim;

internal static class MainWindowDeterministicFixture
{
    public static MainWindowDeterministicFixtureData Create()
    {
        var deterministicNow = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Local);

        var usages = new List<ProviderUsage>
        {
            CreateUsage(
                new DeterministicProviderScenario(AntigravityProvider.StaticDefinition.ProviderId, "local-session", AuthSource: "local app"),
                deterministicNow,
                new FixtureUsageScenario(40.0, 40, 100, "60.0% Remaining", 6),
                details: new List<ProviderUsageDetail>
                {
                    CreateDetail(deterministicNow, "Claude Opus 4.6 (Thinking)", 40.0, 10),
                    CreateDetail(deterministicNow, "Claude Sonnet 4.6 (Thinking)", 40.0, 10),
                    CreateDetail(deterministicNow, "Gemini 3 Flash", 0.0, 6),
                    CreateDetail(deterministicNow, "Gemini 3.1 Pro (High)", 0.0, 14),
                    CreateDetail(deterministicNow, "Gemini 3.1 Pro (Low)", 0.0, 14),
                    CreateDetail(deterministicNow, "GPT-OSS 120B (Medium)", 40.0, 8),
                }),
        };

        usages.AddRange(DeterministicProviderScenarioCatalog.Scenarios
            .Where(scenario => scenario.MainWindowUsage != null)
            .Select(scenario => CreateUsage(scenario, deterministicNow, scenario.MainWindowUsage!)));

        return new MainWindowDeterministicFixtureData
        {
            Preferences = new AppPreferences
            {
                AlwaysOnTop = true,
                ShowUsedPercentages = false,
                ColorThresholdYellow = 60,
                ColorThresholdRed = 80,
                FontFamily = "Segoe UI",
                FontSize = 12,
                FontBold = false,
                FontItalic = false,
                IsPrivacyMode = true,
            },
            LastMonitorUpdate = deterministicNow,
            Usages = usages,
        };
    }

    private static ProviderUsage CreateUsage(
        DeterministicProviderScenario scenario,
        DateTime deterministicNow,
        FixtureUsageScenario usageScenario,
        bool isAvailable = true,
        IReadOnlyList<ProviderUsageDetail>? details = null)
    {
        if (!ProviderMetadataCatalog.TryGetUsageSemantics(scenario.ProviderId, out var planType, out var isQuotaBased))
        {
            throw new InvalidOperationException($"Unknown provider id '{scenario.ProviderId}' in deterministic screenshot data.");
        }

        return new ProviderUsage
        {
            ProviderId = scenario.ProviderId,
            ProviderName = ProviderMetadataCatalog.ResolveDisplayLabel(scenario.ProviderId),
            IsAvailable = isAvailable,
            IsQuotaBased = isQuotaBased,
            PlanType = planType,
            DisplayAsFraction = isQuotaBased,
            UsedPercent = usageScenario.UsedPercent,
            RequestsUsed = usageScenario.RequestsUsed,
            RequestsAvailable = usageScenario.RequestsAvailable,
            Description = usageScenario.Description,
            Details = details,
            NextResetTime = usageScenario.ResetHours.HasValue
                ? deterministicNow.AddHours(usageScenario.ResetHours.Value)
                : null,
            AuthSource = scenario.AuthSource,
        };
    }

    private static ProviderUsageDetail CreateDetail(DateTime deterministicNow, string name, double usedPercent, int resetHours)
    {
        var detail = new ProviderUsageDetail
        {
            Name = name,
            ModelName = name,
            GroupName = "Recommended Group 1",
            Description = $"{100.0 - usedPercent:F0}% remaining",
            NextResetTime = deterministicNow.AddHours(resetHours),
        };
        detail.SetPercentageValue(usedPercent, PercentageValueSemantic.Used);
        return detail;
    }
}
