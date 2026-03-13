// <copyright file="GroupedUsageDisplayAdapterTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public class GroupedUsageDisplayAdapterTests
{
    [Fact]
    public void Expand_GeminiSnapshot_MapsModelsToDynamicDerivedRows_WhenSelectorTokensDoNotMatch()
    {
        var now = DateTime.UtcNow;
        var snapshot = new AgentGroupedUsageSnapshot
        {
            Providers = new[]
            {
                new AgentGroupedProviderUsage
                {
                    ProviderId = "gemini-cli",
                    ProviderName = "Google Gemini",
                    AccountName = "user@example.com",
                    IsAvailable = true,
                    IsQuotaBased = true,
                    PlanType = PlanType.Coding,
                    RequestsUsed = 34.3,
                    RequestsAvailable = 100,
                    RequestsPercentage = 65.7,
                    Description = "65.7% Remaining",
                    FetchedAt = now,
                    Models = new[]
                    {
                        new AgentGroupedModelUsage
                        {
                            ModelId = "gemini-2.5-flash-lite",
                            ModelName = "Gemini 2.5 Flash Lite",
                            RemainingPercentage = 97.1,
                            UsedPercentage = 2.9,
                            NextResetTime = now.AddHours(5),
                        },
                        new AgentGroupedModelUsage
                        {
                            ModelId = "gemini-3-flash-preview",
                            ModelName = "Gemini 3 Flash Preview",
                            RemainingPercentage = 65.7,
                            UsedPercentage = 34.3,
                            NextResetTime = now.AddHours(6),
                        },
                        new AgentGroupedModelUsage
                        {
                            ModelId = "gemini-2.5-pro",
                            ModelName = "Gemini 2.5 Pro",
                            RemainingPercentage = 0,
                            UsedPercentage = 100,
                            NextResetTime = now.AddHours(8),
                        },
                    },
                },
            },
        };

        var usages = GroupedUsageDisplayAdapter.Expand(snapshot);

        Assert.Equal(4, usages.Count);
        var parent = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "gemini-cli", StringComparison.Ordinal));
        Assert.Equal("Google Gemini", parent.ProviderName);
        Assert.Equal(3, parent.Details?.Count);

        var minute = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "gemini-cli.gemini-2.5-flash-lite", StringComparison.Ordinal));
        var hourly = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "gemini-cli.gemini-2.5-pro", StringComparison.Ordinal));
        var daily = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "gemini-cli.gemini-3-flash-preview", StringComparison.Ordinal));

        Assert.Equal("Gemini 2.5 Flash Lite [Gemini CLI]", minute.ProviderName);
        Assert.Equal("Gemini 2.5 Pro [Gemini CLI]", hourly.ProviderName);
        Assert.Equal("Gemini 3 Flash Preview [Gemini CLI]", daily.ProviderName);
        Assert.Equal(97.1, minute.RequestsPercentage, 1);
    }

    [Fact]
    public void Expand_CodexSnapshot_MapsModelToCodexSparkDerivedRow()
    {
        var snapshot = new AgentGroupedUsageSnapshot
        {
            Providers = new[]
            {
                new AgentGroupedProviderUsage
                {
                    ProviderId = "codex",
                    ProviderName = "OpenAI",
                    IsAvailable = true,
                    IsQuotaBased = true,
                    PlanType = PlanType.Coding,
                    RequestsPercentage = 72,
                    Models = new[]
                    {
                        new AgentGroupedModelUsage
                        {
                            ModelId = "gpt-5.3-codex-spark",
                            ModelName = "GPT-5.3-Codex-Spark",
                            RemainingPercentage = 72,
                        },
                    },
                },
            },
        };

        var usages = GroupedUsageDisplayAdapter.Expand(snapshot);

        Assert.Equal(2, usages.Count);
        var parent = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "codex", StringComparison.Ordinal));
        var spark = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "codex.spark", StringComparison.Ordinal));
        Assert.Equal("OpenAI (Codex)", parent.ProviderName);
        Assert.Equal("OpenAI (GPT-5.3 Codex Spark)", spark.ProviderName);
        Assert.Equal(72, spark.RequestsPercentage, 1);
    }

    [Fact]
    public void Expand_CodexSnapshot_DoesNotCreateSparkDerivedRowWithoutMatchingModel()
    {
        var snapshot = new AgentGroupedUsageSnapshot
        {
            Providers = new[]
            {
                new AgentGroupedProviderUsage
                {
                    ProviderId = "codex",
                    ProviderName = "OpenAI",
                    IsAvailable = true,
                    IsQuotaBased = true,
                    PlanType = PlanType.Coding,
                    RequestsPercentage = 64,
                    Models = new[]
                    {
                        new AgentGroupedModelUsage
                        {
                            ModelId = "gpt-5.3-codex",
                            ModelName = "GPT-5.3-Codex",
                            RemainingPercentage = 64,
                        },
                    },
                },
            },
        };

        var usages = GroupedUsageDisplayAdapter.Expand(snapshot);

        Assert.Single(usages);
        Assert.Equal("codex", usages[0].ProviderId);
    }

    [Fact]
    public void Expand_ProviderWithoutVisibleDerivedRows_KeepsOnlyParentUsage()
    {
        var snapshot = new AgentGroupedUsageSnapshot
        {
            Providers = new[]
            {
                new AgentGroupedProviderUsage
                {
                    ProviderId = "github-copilot",
                    ProviderName = "GitHub Copilot",
                    IsAvailable = true,
                    IsQuotaBased = true,
                    PlanType = PlanType.Coding,
                    RequestsPercentage = 90,
                    Models = new[]
                    {
                        new AgentGroupedModelUsage
                        {
                            ModelId = "weekly",
                            ModelName = "Weekly Quota",
                            RemainingPercentage = 90,
                        },
                    },
                },
            },
        };

        var usages = GroupedUsageDisplayAdapter.Expand(snapshot);

        Assert.Single(usages);
        Assert.Equal("github-copilot", usages[0].ProviderId);
        Assert.Equal("Weekly Quota", Assert.Single(usages[0].Details!).Name);
    }

    [Fact]
    public void Expand_ModelWithQuotaBuckets_UsesMostConstrainedBucketForDerivedRow()
    {
        var snapshot = new AgentGroupedUsageSnapshot
        {
            Providers = new[]
            {
                new AgentGroupedProviderUsage
                {
                    ProviderId = "gemini-cli",
                    ProviderName = "Google Gemini",
                    IsAvailable = true,
                    IsQuotaBased = true,
                    PlanType = PlanType.Coding,
                    RequestsPercentage = 80,
                    Models = new[]
                    {
                        new AgentGroupedModelUsage
                        {
                            ModelId = "gemini-3-flash-preview",
                            ModelName = "Gemini 3 Flash Preview",
                            QuotaBuckets = new[]
                            {
                                new AgentGroupedQuotaBucketUsage
                                {
                                    BucketId = "requests-per-hour",
                                    BucketName = "Requests / Hour",
                                    RemainingPercentage = 80,
                                    UsedPercentage = 20,
                                    Description = "80% remaining",
                                },
                                new AgentGroupedQuotaBucketUsage
                                {
                                    BucketId = "requests-per-day",
                                    BucketName = "Requests / Day",
                                    RemainingPercentage = 35,
                                    UsedPercentage = 65,
                                    Description = "35% remaining",
                                },
                            },
                        },
                    },
                },
            },
        };

        var usages = GroupedUsageDisplayAdapter.Expand(snapshot);

        var derived = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "gemini-cli.gemini-3-flash-preview", StringComparison.Ordinal));
        Assert.Equal(35, derived.RequestsPercentage, 1);
        Assert.Equal(65, derived.RequestsUsed, 1);
        Assert.Equal("35% remaining", derived.Description);
        Assert.NotNull(derived.Details);
        Assert.Equal(2, derived.Details!.Count);
        Assert.All(derived.Details!, detail => Assert.Equal(ProviderUsageDetailType.QuotaWindow, detail.DetailType));
    }

    [Fact]
    public void Expand_GeminiSnapshot_MapsDerivedProviderIdsByModelId()
    {
        var snapshot = new AgentGroupedUsageSnapshot
        {
            Providers = new[]
            {
                new AgentGroupedProviderUsage
                {
                    ProviderId = "gemini-cli",
                    ProviderName = "Google Gemini",
                    IsAvailable = true,
                    IsQuotaBased = true,
                    PlanType = PlanType.Coding,
                    Models = new[]
                    {
                        new AgentGroupedModelUsage
                        {
                            ModelId = "daily",
                            ModelName = "Alpha Daily",
                            RemainingPercentage = 50,
                        },
                        new AgentGroupedModelUsage
                        {
                            ModelId = "minute",
                            ModelName = "Zulu Minute",
                            RemainingPercentage = 80,
                        },
                        new AgentGroupedModelUsage
                        {
                            ModelId = "hourly",
                            ModelName = "Bravo Hourly",
                            RemainingPercentage = 70,
                        },
                    },
                },
            },
        };

        var usages = GroupedUsageDisplayAdapter.Expand(snapshot);

        var minute = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "gemini-cli.minute", StringComparison.Ordinal));
        var hourly = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "gemini-cli.hourly", StringComparison.Ordinal));
        var daily = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "gemini-cli.daily", StringComparison.Ordinal));

        Assert.Equal("Zulu Minute [Gemini CLI]", minute.ProviderName);
        Assert.Equal("Bravo Hourly [Gemini CLI]", hourly.ProviderName);
        Assert.Equal("Alpha Daily [Gemini CLI]", daily.ProviderName);
    }

    [Fact]
    public void Expand_GeminiSnapshot_AddsDynamicRowsForUnmatchedModels_WhenOnlyPartialSelectorMatchesExist()
    {
        var snapshot = new AgentGroupedUsageSnapshot
        {
            Providers = new[]
            {
                new AgentGroupedProviderUsage
                {
                    ProviderId = "gemini-cli",
                    ProviderName = "Google Gemini",
                    IsAvailable = true,
                    IsQuotaBased = true,
                    PlanType = PlanType.Coding,
                    Models = new[]
                    {
                        new AgentGroupedModelUsage
                        {
                            ModelId = "minute",
                            ModelName = "Minute Model",
                            RemainingPercentage = 80,
                        },
                        new AgentGroupedModelUsage
                        {
                            ModelId = "gemini-3-pro",
                            ModelName = "Gemini 3 Pro",
                            RemainingPercentage = 40,
                        },
                    },
                },
            },
        };

        var usages = GroupedUsageDisplayAdapter.Expand(snapshot);

        var minute = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "gemini-cli.minute", StringComparison.Ordinal));
        Assert.Equal("Minute Model [Gemini CLI]", minute.ProviderName);

        var dynamic = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "gemini-cli.gemini-3-pro", StringComparison.Ordinal));
        Assert.Equal("Gemini 3 Pro [Gemini CLI]", dynamic.ProviderName);
    }

    [Fact]
    public void Expand_CodexSnapshot_PrefersSparkTokenMatch_ForCodexSparkDerivedProvider()
    {
        var snapshot = new AgentGroupedUsageSnapshot
        {
            Providers = new[]
            {
                new AgentGroupedProviderUsage
                {
                    ProviderId = "codex",
                    ProviderName = "OpenAI",
                    IsAvailable = true,
                    IsQuotaBased = true,
                    PlanType = PlanType.Coding,
                    Models = new[]
                    {
                        new AgentGroupedModelUsage
                        {
                            ModelId = "gpt-5.3-codex",
                            ModelName = "GPT-5.3 Codex",
                            RemainingPercentage = 65,
                        },
                        new AgentGroupedModelUsage
                        {
                            ModelId = "gpt-5.3-codex-spark",
                            ModelName = "GPT-5.3 Codex Spark",
                            RemainingPercentage = 40,
                        },
                    },
                },
            },
        };

        var usages = GroupedUsageDisplayAdapter.Expand(snapshot);

        var spark = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "codex.spark", StringComparison.Ordinal));
        Assert.Equal("OpenAI (GPT-5.3 Codex Spark)", spark.ProviderName);
        Assert.Equal(40, spark.RequestsPercentage, 1);
    }

    [Fact]
    public void Expand_UsesEffectiveModelState_WhenProvidedByMonitorProjection()
    {
        var now = DateTime.UtcNow;
        var snapshot = new AgentGroupedUsageSnapshot
        {
            Providers = new[]
            {
                new AgentGroupedProviderUsage
                {
                    ProviderId = "gemini-cli",
                    ProviderName = "Google Gemini",
                    IsAvailable = true,
                    IsQuotaBased = true,
                    PlanType = PlanType.Coding,
                    Models = new[]
                    {
                        new AgentGroupedModelUsage
                        {
                            ModelId = "gemini-2.5-flash-lite",
                            ModelName = "Gemini 2.5 Flash Lite",
                            RemainingPercentage = 20,
                            UsedPercentage = 80,
                            EffectiveRemainingPercentage = 77,
                            EffectiveUsedPercentage = 23,
                            EffectiveDescription = "77.0% Remaining",
                            EffectiveNextResetTime = now.AddHours(3),
                            QuotaBuckets = new[]
                            {
                                new AgentGroupedQuotaBucketUsage
                                {
                                    BucketId = "requests-per-hour",
                                    BucketName = "Requests / Hour",
                                    RemainingPercentage = 20,
                                    UsedPercentage = 80,
                                },
                            },
                        },
                    },
                },
            },
        };

        var usages = GroupedUsageDisplayAdapter.Expand(snapshot);

        var derived = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "gemini-cli.gemini-2.5-flash-lite", StringComparison.Ordinal));
        Assert.Equal(77, derived.RequestsPercentage, 1);
        Assert.Equal(23, derived.RequestsUsed, 1);
        Assert.Equal("77.0% Remaining", derived.Description);
        Assert.Equal(now.AddHours(3), derived.NextResetTime);
    }

    [Fact]
    public void Expand_AntigravitySnapshot_AddsDynamicDerivedRows()
    {
        var snapshot = new AgentGroupedUsageSnapshot
        {
            Providers = new[]
            {
                new AgentGroupedProviderUsage
                {
                    ProviderId = "antigravity",
                    ProviderName = "Antigravity",
                    IsAvailable = true,
                    IsQuotaBased = true,
                    PlanType = PlanType.Coding,
                    Models = new[]
                    {
                        new AgentGroupedModelUsage
                        {
                            ModelId = "gemini-3-flash",
                            ModelName = "Gemini 3 Flash",
                            RemainingPercentage = 100,
                            UsedPercentage = 0,
                            Description = "100% Remaining",
                        },
                    },
                },
            },
        };

        var usages = GroupedUsageDisplayAdapter.Expand(snapshot);

        Assert.Equal(2, usages.Count);
        var derived = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "antigravity.gemini-3-flash", StringComparison.Ordinal));
        Assert.Equal("Gemini 3 Flash [Antigravity]", derived.ProviderName);
        Assert.Equal(100, derived.RequestsPercentage, 1);
        Assert.Equal("100% Remaining", derived.Description);
    }
}
