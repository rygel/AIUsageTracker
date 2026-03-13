// <copyright file="DeterministicProviderScenarioCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Reflection;

namespace AIUsageTracker.Tests.UI;

public class DeterministicProviderScenarioCatalogTests
{
    [Fact]
    public void Scenarios_DoNotDuplicateProviderIds()
    {
        var catalogType = typeof(AIUsageTracker.UI.Slim.MainWindow).Assembly
            .GetType("AIUsageTracker.UI.Slim.DeterministicProviderScenarioCatalog", throwOnError: true)!;
        var scenarios = ((System.Collections.IEnumerable)catalogType.GetProperty("Scenarios", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!)
            .Cast<object>()
            .ToList();

        var providerIds = scenarios
            .Select(scenario => (string)scenario.GetType().GetProperty("ProviderId")!.GetValue(scenario)!)
            .ToList();

        Assert.Equal(providerIds.Count, providerIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
