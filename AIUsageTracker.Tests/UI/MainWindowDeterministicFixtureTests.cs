// <copyright file="MainWindowDeterministicFixtureTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Reflection;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Tests.UI;

public class MainWindowDeterministicFixtureTests
{
    [Fact]
    public void Create_UsesProviderMetadataForUsages()
    {
        var fixtureType = typeof(AIUsageTracker.UI.Slim.MainWindow).Assembly
            .GetType("AIUsageTracker.UI.Slim.MainWindowDeterministicFixture", throwOnError: true)!;
        var createMethod = fixtureType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!;

        var fixture = createMethod.Invoke(null, null)!;
        var usages = (IEnumerable<ProviderUsage>)fixture.GetType().GetProperty("Usages")!.GetValue(fixture)!;

        foreach (var usage in usages)
        {
            var definition = Assert.Single(
                ProviderMetadataCatalog.Definitions,
                d => d.HandlesProviderId(usage.ProviderId));
            Assert.Equal(ProviderMetadataCatalog.GetConfiguredDisplayName(usage.ProviderId), usage.ProviderName);
            Assert.Equal(definition.PlanType, usage.PlanType);
            Assert.Equal(definition.IsQuotaBased, usage.IsQuotaBased);
        }
    }
}
