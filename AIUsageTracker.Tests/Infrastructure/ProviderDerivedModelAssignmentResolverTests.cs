// <copyright file="ProviderDerivedModelAssignmentResolverTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Tests.Infrastructure;

public sealed class ProviderDerivedModelAssignmentResolverTests
{
    [Fact]
    public void Resolve_CodexModels_ReturnsEmpty_WhenQuotaWindowCards()
    {
        // Codex cards all have WindowKind set (Burst/Rolling/ModelSpecific) — the resolver has
        // nothing to do; they flow to ProviderDetails, not flat model rows.
        var assignments = ProviderDerivedModelAssignmentResolver.Resolve(
            "codex",
            new[]
            {
                new AgentGroupedModelUsage
                {
                    ModelId = "burst",
                    ModelName = "5-hour quota",
                },
                new AgentGroupedModelUsage
                {
                    ModelId = "spark",
                    ModelName = "Spark",
                },
            });

        Assert.Empty(assignments);
    }

    [Fact]
    public void Resolve_GeminiModels_ReturnsEmpty_WhenWindowKindNoneCards()
    {
        // Gemini cards have WindowKind.None — they flow directly as flat model rows; the resolver has nothing to do.
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

        Assert.Empty(assignments);
    }

    [Fact]
    public void Resolve_AntigravityModels_ReturnsEmpty_WhenWindowKindNoneCards()
    {
        // Antigravity cards have WindowKind.None — no derived model selectors, no dynamic rows.
        // Cards are emitted directly by the provider with CardId; Resolve has nothing to do.
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

        Assert.Empty(assignments);
    }
}
