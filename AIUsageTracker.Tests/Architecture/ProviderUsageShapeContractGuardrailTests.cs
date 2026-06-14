// <copyright file="ProviderUsageShapeContractGuardrailTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Reflection;
using System.Text.Json.Serialization;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Tests.Architecture;

public sealed class ProviderUsageShapeContractGuardrailTests
{
    [Fact]
    public void ProviderUsage_IsAbstract()
    {
        Assert.True(typeof(ProviderUsage).IsAbstract);
    }

    [Fact]
    public void ProviderUsage_HasOnlyBaseClassProperties()
    {
        var props = typeof(ProviderUsage)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(p => p.Name)
            .ToHashSet();

        Assert.Contains("ProviderId", props);
        Assert.Contains("IsAvailable", props);
        Assert.Contains("HttpStatus", props);
        Assert.Contains("FailureContext", props);

        Assert.DoesNotContain("RequestsUsed", props);
        Assert.DoesNotContain("RequestsAvailable", props);
        Assert.DoesNotContain("UsedPercent", props);
        Assert.DoesNotContain("PlanType", props);
        Assert.DoesNotContain("IsQuotaBased", props);
        Assert.DoesNotContain("IsStatusOnly", props);
        Assert.DoesNotContain("NextResetTime", props);
        Assert.DoesNotContain("WindowKind", props);
        Assert.DoesNotContain("ModelName", props);
        Assert.DoesNotContain("CardId", props);
        Assert.DoesNotContain("WindowCards", props);
        Assert.DoesNotContain("UsagePerHour", props);
    }

    [Fact]
    public void AllProviderUsageSubtypes_AreDiscoveredByJsonDerivedType()
    {
        var derivedTypes = typeof(ProviderUsage)
            .GetCustomAttributes<JsonDerivedTypeAttribute>()
            .Select(a => a.DerivedType)
            .ToHashSet();

        Assert.Contains(typeof(QuotaProviderUsage), derivedTypes);
        Assert.Contains(typeof(WindowedProviderUsage), derivedTypes);
        Assert.Contains(typeof(ModelScopedProviderUsage), derivedTypes);
        Assert.Contains(typeof(StatusProviderUsage), derivedTypes);
        Assert.Equal(4, derivedTypes.Count);
    }

    [Fact]
    public void QuotaProviderUsage_IsNotSealed()
    {
        Assert.False(typeof(QuotaProviderUsage).IsSealed);
    }

    [Fact]
    public void WindowedProviderUsage_InheritsFromQuotaProviderUsage()
    {
        Assert.Equal(typeof(QuotaProviderUsage), typeof(WindowedProviderUsage).BaseType);
    }

    [Fact]
    public void ModelScopedProviderUsage_InheritsFromQuotaProviderUsage()
    {
        Assert.Equal(typeof(QuotaProviderUsage), typeof(ModelScopedProviderUsage).BaseType);
    }

    [Fact]
    public void StatusProviderUsage_IsSealedAndInheritsFromProviderUsage()
    {
        Assert.True(typeof(StatusProviderUsage).IsSealed);
        Assert.Equal(typeof(ProviderUsage), typeof(StatusProviderUsage).BaseType);
    }

    [Fact]
    public void WindowedProviderUsage_And_ModelScopedProviderUsage_AreSealed()
    {
        Assert.True(typeof(WindowedProviderUsage).IsSealed);
        Assert.True(typeof(ModelScopedProviderUsage).IsSealed);
    }
}
