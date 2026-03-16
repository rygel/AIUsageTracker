// <copyright file="ProviderDerivedModelAssignmentResolverTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Tests.Infrastructure;

public sealed class ProviderDerivedModelAssignmentResolverTests
{
    [Fact]
    public void Resolve_CodexSparkModel_AssignsConfiguredDerivedProvider()
    {
        var assignments = ProviderDerivedModelAssignmentResolver.Resolve(
            "codex",
            new[]
            {
                new AgentGroupedModelUsage
                {
                    ModelId = "gpt-5.3-codex",
                    ModelName = "GPT-5.3 Codex",
                },
                new AgentGroupedModelUsage
                {
                    ModelId = "gpt-5.3-codex-spark",
                    ModelName = "GPT-5.3 Codex Spark",
                },
            });

        var spark = Assert.Single(assignments, assignment => string.Equals(assignment.ProviderId, "codex.spark", StringComparison.Ordinal));
        Assert.Equal("gpt-5.3-codex-spark", spark.Model.ModelId);
    }

    [Fact]
    public void Resolve_GeminiPartialSelectorMatch_AddsDynamicAssignmentForUnmatchedModels()
    {
        var assignments = ProviderDerivedModelAssignmentResolver.Resolve(
            "gemini-cli",
            new[]
            {
                new AgentGroupedModelUsage
                {
                    ModelId = "minute",
                    ModelName = "Minute Model",
                },
                new AgentGroupedModelUsage
                {
                    ModelId = "gemini-3-pro",
                    ModelName = "Gemini 3 Pro",
                },
            });

        Assert.Contains(assignments, assignment => string.Equals(assignment.ProviderId, "gemini-cli.minute", StringComparison.Ordinal));
        Assert.Contains(assignments, assignment => string.Equals(assignment.ProviderId, "gemini-cli.gemini-3-pro", StringComparison.Ordinal));
    }

    [Fact]
    public void Resolve_AntigravityModels_UsesDynamicChildProviderRows()
    {
        var assignments = ProviderDerivedModelAssignmentResolver.Resolve(
            "antigravity",
            new[]
            {
                new AgentGroupedModelUsage
                {
                    ModelId = "gemini-3-flash",
                    ModelName = "Gemini 3 Flash",
                },
            });

        var assignment = Assert.Single(assignments);
        Assert.Equal("antigravity.gemini-3-flash", assignment.ProviderId);
        Assert.Equal("gemini-3-flash", assignment.Model.ModelId);
    }
}
