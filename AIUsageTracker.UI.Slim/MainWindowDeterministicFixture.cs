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
                new FixtureUsageScenario(40.0, 40, 100, "60.0% Remaining", 6)),
        };

        foreach (var scenario in DeterministicProviderScenario.Scenarios.Where(s => s.MainWindowUsage != null))
        {
            if (string.Equals(scenario.ProviderId, CodexProvider.StaticDefinition.ProviderId, StringComparison.OrdinalIgnoreCase))
            {
                usages.AddRange(CreateCodexCards(scenario, deterministicNow, scenario.MainWindowUsage!));
            }
            else
            {
                usages.Add(CreateUsage(scenario, deterministicNow, scenario.MainWindowUsage!));
            }
        }

        return new MainWindowDeterministicFixtureData
        {
            Preferences = new AppPreferences
            {
                AlwaysOnTop = true,
                ShowUsedPercentages = false,
                ColorThresholdYellow = 60,
                ColorThresholdRed = 80,
                ShowDualQuotaBars = true,
                DualQuotaSingleBarMode = DualQuotaSingleBarMode.Rolling,
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

    private static IEnumerable<ProviderUsage> CreateCodexCards(
        DeterministicProviderScenario scenario,
        DateTime deterministicNow,
        FixtureUsageScenario usageScenario)
    {
        var codexProviderId = CodexProvider.StaticDefinition.ProviderId;
        var codexDisplayName = CodexProvider.StaticDefinition.DisplayName;
        var sparkProviderId = CodexProvider.SparkDefinition.ProviderId;
        var sparkDisplayName = CodexProvider.SparkDefinition.DisplayName;

        // provider-id-guardrail-allow: deterministic fixture uses provider metadata constants
        yield return new ProviderUsage
        {
            ProviderId = codexProviderId,
            ProviderName = codexDisplayName,
            CardId = "burst",
            GroupId = codexProviderId,
            Name = "5-hour quota",
            WindowKind = WindowKind.Burst,
            IsAvailable = true,
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            UsedPercent = usageScenario.UsedPercent,
            RequestsUsed = usageScenario.UsedPercent,
            RequestsAvailable = 100.0,
            Description = $"{100.0 - usageScenario.UsedPercent:F0}% remaining | Plan: plus",
            AccountName = string.Empty,
            AuthSource = scenario.AuthSource,
            NextResetTime = deterministicNow.AddHours(3),
            PeriodDuration = TimeSpan.FromHours(5),
        };

        yield return new ProviderUsage
        {
            ProviderId = codexProviderId,
            ProviderName = codexDisplayName,
            CardId = "weekly",
            GroupId = codexProviderId,
            Name = "Weekly quota",
            WindowKind = WindowKind.Rolling,
            IsAvailable = true,
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            UsedPercent = 72.0,
            RequestsUsed = 72.0,
            RequestsAvailable = 100.0,
            Description = "28% remaining | Plan: plus",
            AccountName = string.Empty,
            AuthSource = scenario.AuthSource,
            NextResetTime = deterministicNow.AddDays(4),
            PeriodDuration = TimeSpan.FromDays(7),
        };

        // Spark also emits burst + rolling so its card is dual-bar capable.
        yield return new ProviderUsage
        {
            ProviderId = sparkProviderId,
            ProviderName = sparkDisplayName,
            CardId = "spark.burst",
            GroupId = sparkProviderId,
            Name = "Spark 5-hour quota",
            WindowKind = WindowKind.Burst,
            IsAvailable = true,
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            UsedPercent = 12.0,
            RequestsUsed = 12.0,
            RequestsAvailable = 100.0,
            Description = "88% remaining | Plan: plus",
            AccountName = string.Empty,
            AuthSource = scenario.AuthSource,
            NextResetTime = deterministicNow.AddHours(4),
            PeriodDuration = TimeSpan.FromHours(5),
        };

        yield return new ProviderUsage
        {
            ProviderId = sparkProviderId,
            ProviderName = sparkDisplayName,
            CardId = "spark.weekly",
            GroupId = sparkProviderId,
            Name = "Spark weekly quota",
            WindowKind = WindowKind.Rolling,
            IsAvailable = true,
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            UsedPercent = 8.0,
            RequestsUsed = 8.0,
            RequestsAvailable = 100.0,
            Description = "92% remaining | Plan: plus",
            AccountName = string.Empty,
            AuthSource = scenario.AuthSource,
            NextResetTime = deterministicNow.AddDays(5),
            PeriodDuration = TimeSpan.FromDays(7),
        };
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
            ProviderName = ProviderMetadataCatalog.GetConfiguredDisplayName(scenario.ProviderId),
            IsAvailable = isAvailable,
            IsQuotaBased = isQuotaBased,
            PlanType = planType,
            DisplayAsFraction = isQuotaBased,
            UsedPercent = usageScenario.UsedPercent,
            RequestsUsed = usageScenario.RequestsUsed,
            RequestsAvailable = usageScenario.RequestsAvailable,
            Description = usageScenario.Description,
            NextResetTime = usageScenario.ResetHours.HasValue
                ? deterministicNow.AddHours(usageScenario.ResetHours.Value)
                : null,
            AuthSource = scenario.AuthSource,
        };
    }
}
