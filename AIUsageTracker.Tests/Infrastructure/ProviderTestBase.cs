// <copyright file="ProviderTestBase.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Reflection;
using AIUsageTracker.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure;

public abstract class ProviderTestBase<TProvider>
    where TProvider : class
{
    protected ProviderTestBase()
    {
        this.Logger = new Mock<ILogger<TProvider>>();
        var definition = GetProviderDefinition();
        this.Config = new ProviderConfig
        {
            ProviderId = definition?.ProviderId ?? GetProviderId(),
            PlanType = definition?.PlanType ?? PlanType.Usage,
            Type = definition?.DefaultConfigType ?? "pay-as-you-go",
        };
    }

    protected Mock<ILogger<TProvider>> Logger { get; }

    protected ProviderConfig Config { get; }

    protected static string GetProviderId()
    {
        var definition = GetProviderDefinition();
        if (definition != null)
        {
            return definition.ProviderId;
        }

        var providerTypeName = typeof(TProvider).Name;
        if (providerTypeName.EndsWith("Provider", StringComparison.Ordinal))
        {
            providerTypeName = providerTypeName[..^8];
        }

        return providerTypeName.ToLowerInvariant().Replace(" ", "-");
    }

    protected static string LoadFixture(string fileName)
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "TestData", "Providers", fileName);
        Assert.True(File.Exists(fixturePath), $"Fixture file not found: {fixturePath}");
        return File.ReadAllText(fixturePath);
    }

    private static ProviderDefinition? GetProviderDefinition()
    {
        var property = typeof(TProvider).GetProperty(
            "StaticDefinition",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

        return property?.PropertyType == typeof(ProviderDefinition)
            ? property.GetValue(null) as ProviderDefinition
            : null;
    }
}
