// <copyright file="SettingsWindowDeterministicFixtureTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Reflection;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Tests.UI;

public class SettingsWindowDeterministicFixtureTests
{
    [Fact]
    public void Create_UsesProviderMetadataForConfigsAndUsages()
    {
        var fixtureType = typeof(AIUsageTracker.UI.Slim.SettingsWindow).Assembly
            .GetType("AIUsageTracker.UI.Slim.SettingsWindowDeterministicFixture", throwOnError: true)!;
        var createMethod = fixtureType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!;

        var fixture = createMethod.Invoke(null, null)!;
        var configs = (IEnumerable<ProviderConfig>)fixture.GetType().GetProperty("Configs")!.GetValue(fixture)!;
        var usages = (IEnumerable<ProviderUsage>)fixture.GetType().GetProperty("Usages")!.GetValue(fixture)!;
        var historyRows = (IEnumerable<object>)fixture.GetType().GetProperty("HistoryRows")!.GetValue(fixture)!;

        foreach (var config in configs)
        {
            Assert.True(
                ProviderMetadataCatalog.TryGet(config.ProviderId, out _),
                $"Deterministic fixture config '{config.ProviderId}' must resolve through provider metadata.");
        }

        foreach (var usage in usages)
        {
            var definition = Assert.Single(
                ProviderMetadataCatalog.Definitions,
                d => d.HandlesProviderId(usage.ProviderId));
            Assert.Equal(ProviderMetadataCatalog.ResolveDisplayLabel(usage.ProviderId), usage.ProviderName);
            Assert.Equal(definition.PlanType, usage.PlanType);
            Assert.Equal(definition.IsQuotaBased, usage.IsQuotaBased);
        }

        foreach (var historyRow in historyRows)
        {
            var providerName = (string)historyRow.GetType().GetProperty("ProviderName")!.GetValue(historyRow)!;
            var planType = (string)historyRow.GetType().GetProperty("PlanType")!.GetValue(historyRow)!;

            var definition = Assert.Single(
                ProviderMetadataCatalog.Definitions,
                d => string.Equals(d.DisplayName, providerName, StringComparison.OrdinalIgnoreCase) ||
                     d.DisplayNameOverrides.Values.Any(value => string.Equals(value, providerName, StringComparison.OrdinalIgnoreCase)));
            Assert.Equal(definition.PlanType.ToString(), planType);
        }
    }
}
