// <copyright file="ProviderFamilyPolicyTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Tests.Core.Models;

public sealed class ProviderFamilyPolicyTests
{
    [Theory]
    [InlineData(ProviderFamilyMode.Standalone, false)]
    [InlineData(ProviderFamilyMode.VisibleDerivedProviders, true)]
    [InlineData(ProviderFamilyMode.DynamicChildProviderRows, true)]
    public void SupportsChildProviderIds_UsesFamilyMode(ProviderFamilyMode familyMode, bool expected)
    {
        Assert.Equal(expected, ProviderFamilyPolicy.SupportsChildProviderIds(familyMode));
    }

    [Theory]
    [InlineData(ProviderFamilyMode.Standalone, false)]
    [InlineData(ProviderFamilyMode.DynamicChildProviderRows, true)]
    [InlineData(ProviderFamilyMode.VisibleDerivedProviders, false)]
    public void UsesChildProviderRowsForGroupedModels_UsesFamilyMode(ProviderFamilyMode familyMode, bool expected)
    {
        Assert.Equal(expected, ProviderFamilyPolicy.UsesChildProviderRowsForGroupedModels(familyMode));
    }

    [Theory]
    [InlineData("codex.spark", ProviderFamilyMode.VisibleDerivedProviders, true)]
    [InlineData("antigravity.model", ProviderFamilyMode.DynamicChildProviderRows, true)]
    [InlineData("openai.spark", ProviderFamilyMode.Standalone, false)]
    public void IsChildProviderId_UsesFamilyMode(
        string candidateProviderId,
        ProviderFamilyMode familyMode,
        bool expected)
    {
        var handledProviderIds = new[] { candidateProviderId.Split('.')[0] };

        Assert.Equal(expected, ProviderFamilyPolicy.IsChildProviderId(handledProviderIds, candidateProviderId, familyMode));
    }

    [Fact]
    public void ProviderDefinition_HandlesProviderId_UsesSharedFamilyPolicy()
    {
        var definition = new ProviderDefinition(
            "codex",
            "OpenAI",
            PlanType.Coding,
            isQuotaBased: true,
            defaultConfigType: "quota-based")
        {
            FamilyMode = ProviderFamilyMode.VisibleDerivedProviders,
            VisibleDerivedProviderIds = new[] { "codex.spark" },
        };

        Assert.True(definition.HandlesProviderId("codex"));
        Assert.True(definition.HandlesProviderId("codex.spark"));
        Assert.False(definition.HandlesProviderId("openai.spark"));
    }
}
