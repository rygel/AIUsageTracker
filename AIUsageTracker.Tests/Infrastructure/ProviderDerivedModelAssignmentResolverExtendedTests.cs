// <copyright file="ProviderDerivedModelAssignmentResolverExtendedTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Reflection;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Tests.Infrastructure;

public sealed class ProviderDerivedModelAssignmentResolverExtendedTests
{
    [Fact]
    public void Resolve_ThrowsOnNullModels()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ProviderDerivedModelAssignmentResolver.Resolve("test", null!));
    }

    [Fact]
    public void Resolve_ReturnsEmpty_ForNullOrWhitespaceProviderId()
    {
        var models = new[] { new AgentGroupedModelUsage { ModelId = "m1" } };
        Assert.Empty(ProviderDerivedModelAssignmentResolver.Resolve(null!, models));
        Assert.Empty(ProviderDerivedModelAssignmentResolver.Resolve("", models));
        Assert.Empty(ProviderDerivedModelAssignmentResolver.Resolve("   ", models));
    }

    [Fact]
    public void Resolve_ReturnsEmpty_ForEmptyModelsList()
    {
        Assert.Empty(ProviderDerivedModelAssignmentResolver.Resolve("openai", Array.Empty<AgentGroupedModelUsage>()));
    }

    [Fact]
    public void Resolve_ReturnsEmpty_ForUnknownProviderId()
    {
        var models = new[] { new AgentGroupedModelUsage { ModelId = "m1" } };
        Assert.Empty(ProviderDerivedModelAssignmentResolver.Resolve("nonexistent-provider-xyz", models));
    }

    [Fact]
    public void BuildDynamicModelAssignments_CreatesDerivedProviderIds()
    {
        var result = InvokeBuildDynamicModelAssignments(
            "test-provider",
            new[]
            {
                new AgentGroupedModelUsage { ModelId = "model-a", ModelName = "Model A" },
                new AgentGroupedModelUsage { ModelId = "model-b", ModelName = "Model B" },
            });

        Assert.Equal(2, result.Count);
        Assert.Equal("test-provider.model-a", result[0].ProviderId);
        Assert.Equal("test-provider.model-b", result[1].ProviderId);
        Assert.Equal("model-a", result[0].Model.ModelId);
        Assert.Equal("model-b", result[1].Model.ModelId);
    }

    [Fact]
    public void BuildDynamicModelAssignments_UsesModelNameAsFallback_WhenModelIdEmpty()
    {
        var result = InvokeBuildDynamicModelAssignments(
            "test-provider",
            new[]
            {
                new AgentGroupedModelUsage { ModelId = "", ModelName = "ValidModel" },
                new AgentGroupedModelUsage { ModelId = "model-b", ModelName = "Model B" },
            });

        Assert.Equal(2, result.Count);
        Assert.Equal("test-provider.ValidModel", result[0].ProviderId);
        Assert.Equal("test-provider.model-b", result[1].ProviderId);
    }

    [Fact]
    public void BuildDynamicModelAssignments_SkipsDuplicateModelKeys()
    {
        var result = InvokeBuildDynamicModelAssignments(
            "test-provider",
            new[]
            {
                new AgentGroupedModelUsage { ModelId = "same", ModelName = "First" },
                new AgentGroupedModelUsage { ModelId = "same", ModelName = "Second" },
            });

        Assert.Single(result);
        Assert.Equal("First", result[0].Model.ModelName);
    }

    [Fact]
    public void BuildDynamicModelAssignments_RespectsReservedProviderIds()
    {
        var result = InvokeBuildDynamicModelAssignments(
            "test-provider",
            new[]
            {
                new AgentGroupedModelUsage { ModelId = "reserved", ModelName = "Reserved" },
                new AgentGroupedModelUsage { ModelId = "available", ModelName = "Available" },
            },
            reservedProviderIds: new[] { "test-provider.reserved" });

        Assert.Single(result);
        Assert.Equal("test-provider.available", result[0].ProviderId);
    }

    [Fact]
    public void BuildDynamicModelAssignments_RespectsReservedModelKeys()
    {
        var result = InvokeBuildDynamicModelAssignments(
            "test-provider",
            new[]
            {
                new AgentGroupedModelUsage { ModelId = "excluded", ModelName = "Excluded" },
                new AgentGroupedModelUsage { ModelId = "available", ModelName = "Available" },
            },
            reservedModelKeys: new[] { "excluded" });

        Assert.Single(result);
        Assert.Equal("test-provider.available", result[0].ProviderId);
    }

    [Fact]
    public void BuildDynamicModelAssignments_EmptyModels_ReturnsEmpty()
    {
        var result = InvokeBuildDynamicModelAssignments("test-provider", Array.Empty<AgentGroupedModelUsage>());
        Assert.Empty(result);
    }

    [Fact]
    public void GetModelAssignmentKey_PrefersModelId_WhenPresent()
    {
        var model = new AgentGroupedModelUsage { ModelId = "id-value", ModelName = "name-value" };
        var key = InvokeGetModelAssignmentKey(model);
        Assert.Equal("id-value", key);
    }

    [Fact]
    public void GetModelAssignmentKey_FallsBackToModelName_WhenModelIdEmpty()
    {
        var model = new AgentGroupedModelUsage { ModelId = "", ModelName = "name-value" };
        var key = InvokeGetModelAssignmentKey(model);
        Assert.Equal("name-value", key);
    }

    [Fact]
    public void GetModelAssignmentKey_FallsBackToModelName_WhenModelIdNull()
    {
        var model = new AgentGroupedModelUsage { ModelId = null!, ModelName = "name-value" };
        var key = InvokeGetModelAssignmentKey(model);
        Assert.Equal("name-value", key);
    }

    [Fact]
    public void ContainsAnyToken_ReturnsFalse_ForNullSource()
    {
        Assert.False(InvokeContainsAnyToken(null, new[] { "test" }));
    }

    [Fact]
    public void ContainsAnyToken_ReturnsFalse_ForEmptySource()
    {
        Assert.False(InvokeContainsAnyToken("", new[] { "test" }));
    }

    [Fact]
    public void ContainsAnyToken_ReturnsFalse_ForEmptyTokens()
    {
        Assert.False(InvokeContainsAnyToken("source", Array.Empty<string>()));
    }

    [Fact]
    public void ContainsAnyToken_ReturnsTrue_WhenTokenMatches()
    {
        Assert.True(InvokeContainsAnyToken("hello world", new[] { "world" }));
    }

    [Fact]
    public void ContainsAnyToken_IsCaseInsensitive()
    {
        Assert.True(InvokeContainsAnyToken("Hello World", new[] { "WORLD" }));
    }

    [Fact]
    public void ContainsAnyToken_SkipsEmptyTokens()
    {
        Assert.False(InvokeContainsAnyToken("source", new[] { "", "  ", null! }));
    }

    [Fact]
    public void ContainsAnyToken_ReturnsFalse_WhenNoTokenMatches()
    {
        Assert.False(InvokeContainsAnyToken("hello world", new[] { "xyz" }));
    }

    private static IReadOnlyList<ProviderDerivedModelAssignment> InvokeBuildDynamicModelAssignments(
        string canonicalProviderId,
        IReadOnlyList<AgentGroupedModelUsage> orderedModels,
        IEnumerable<string>? reservedProviderIds = null,
        IEnumerable<string>? reservedModelKeys = null)
    {
        var method = typeof(ProviderDerivedModelAssignmentResolver)
            .GetMethod("BuildDynamicModelAssignments", BindingFlags.NonPublic | BindingFlags.Static)!;

        return (IReadOnlyList<ProviderDerivedModelAssignment>)method.Invoke(
            null,
            new object?[] { canonicalProviderId, orderedModels, reservedProviderIds, reservedModelKeys })!;
    }

    private static string InvokeGetModelAssignmentKey(AgentGroupedModelUsage model)
    {
        var method = typeof(ProviderDerivedModelAssignmentResolver)
            .GetMethod("GetModelAssignmentKey", BindingFlags.NonPublic | BindingFlags.Static)!;

        return (string)method.Invoke(null, new object[] { model })!;
    }

    private static bool InvokeContainsAnyToken(string? source, IReadOnlyCollection<string> tokens)
    {
        var method = typeof(ProviderDerivedModelAssignmentResolver)
            .GetMethod("ContainsAnyToken", BindingFlags.NonPublic | BindingFlags.Static)!;

        return (bool)method.Invoke(null, new object?[] { source, tokens })!;
    }
}
