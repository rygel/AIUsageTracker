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
                    UsedPercent = 65.7,
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
        Assert.Equal(2.9, minute.UsedPercent, 1); // UsedPercentage = 2.9
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
                    UsedPercent = 72,
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
        Assert.Equal(28, spark.UsedPercent, 1); // RemainingPercentage = 72 → UsedPercent = 28
        Assert.Equal(TimeSpan.FromDays(7), spark.PeriodDuration);
    }

    [Fact]
    public void Expand_ClaudeSnapshot_MapsDeclaredWindowKinds_ForParentModelDetails()
    {
        var now = DateTime.UtcNow;
        var snapshot = new AgentGroupedUsageSnapshot
        {
            Providers = new[]
            {
                new AgentGroupedProviderUsage
                {
                    ProviderId = "claude-code",
                    ProviderName = "Claude Code",
                    IsAvailable = true,
                    IsQuotaBased = true,
                    PlanType = PlanType.Usage,
                    UsedPercent = 73,
                    Models = new[]
                    {
                        new AgentGroupedModelUsage
                        {
                            ModelId = "sonnet",
                            ModelName = "Sonnet",
                            RemainingPercentage = 27,
                            UsedPercentage = 73,
                            NextResetTime = now.AddDays(1),
                        },
                        new AgentGroupedModelUsage
                        {
                            ModelId = "all-models",
                            ModelName = "All Models",
                            RemainingPercentage = 15,
                            UsedPercentage = 85,
                            NextResetTime = now.AddDays(1),
                        },
                    },
                },
            },
        };

        var usages = GroupedUsageDisplayAdapter.Expand(snapshot);

        var parent = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "claude-code", StringComparison.Ordinal));
        Assert.Equal(TimeSpan.FromDays(7), parent.PeriodDuration);
        Assert.NotNull(parent.Details);

        var sonnet = Assert.Single(parent.Details!, detail => string.Equals(detail.Name, "Sonnet", StringComparison.Ordinal));
        var allModels = Assert.Single(parent.Details!, detail => string.Equals(detail.Name, "All Models", StringComparison.Ordinal));

        Assert.Equal(WindowKind.ModelSpecific, sonnet.QuotaBucketKind);
        Assert.Equal(WindowKind.Rolling, allModels.QuotaBucketKind);
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
                    UsedPercent = 64,
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
                    UsedPercent = 90,
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
                    UsedPercent = 80,
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
        Assert.Equal(65, derived.UsedPercent, 1); // most constrained bucket: 65% used
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
        Assert.Equal(60, spark.UsedPercent, 1); // RemainingPercentage=40 → UsedPercent = 60
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
        Assert.Equal(23, derived.UsedPercent, 1); // EffectiveUsedPercentage = 23
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
        Assert.Equal(0, derived.UsedPercent, 1); // UsedPercentage = 0 (100% remaining)
        Assert.Equal("100% Remaining", derived.Description);
    }

    [Fact]
    public void Expand_KimiSnapshot_AttachesProviderQuotaDetailsToParent_WhenNoModelDetails()
    {
        // Kimi has no Model-type details; all its details are QuotaWindow (Weekly + 5h).
        // ProviderQuotaDetails must be surfaced as the parent's Details so that
        // MainWindowRuntimeLogic.TryGetDualQuotaBucketPresentation can render two bars.
        var weeklyDetail = new ProviderUsageDetail
        {
            Name = "Weekly Limit",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Rolling,
        };
        weeklyDetail.SetPercentageValue(25.0, PercentageValueSemantic.Used, decimalPlaces: 1);

        var burstDetail = new ProviderUsageDetail
        {
            Name = "5h Limit",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Burst,
        };
        burstDetail.SetPercentageValue(0.0, PercentageValueSemantic.Used, decimalPlaces: 1);

        var snapshot = new AgentGroupedUsageSnapshot
        {
            Providers = new[]
            {
                new AgentGroupedProviderUsage
                {
                    ProviderId = "kimi-for-coding",
                    IsAvailable = true,
                    IsQuotaBased = true,
                    UsedPercent = 25,
                    Models = Array.Empty<AgentGroupedModelUsage>(),
                    ProviderQuotaDetails = new[] { weeklyDetail, burstDetail },
                },
            },
        };

        var usages = GroupedUsageDisplayAdapter.Expand(snapshot);

        var parent = Assert.Single(usages, u => string.Equals(u.ProviderId, "kimi-for-coding", StringComparison.Ordinal));
        Assert.NotNull(parent.Details);
        Assert.Equal(2, parent.Details!.Count);

        var weekly = Assert.Single(parent.Details, d => d.QuotaBucketKind == WindowKind.Rolling);
        Assert.Equal("Weekly Limit", weekly.Name);

        var burst = Assert.Single(parent.Details, d => d.QuotaBucketKind == WindowKind.Burst);
        Assert.Equal("5h Limit", burst.Name);
    }

    [Fact]
    public void Expand_CodexSparkModelWithBurstAndRollingBuckets_GivesChildCardDualBarDetails()
    {
        // Regression: when the Spark model has QuotaBuckets with Burst and Rolling kinds,
        // the child codex.spark card must have Details with those kinds so
        // MainWindowRuntimeLogic.TryGetDualQuotaBucketPresentation can render dual bars.
        var snapshot = new AgentGroupedUsageSnapshot
        {
            Providers = new[]
            {
                new AgentGroupedProviderUsage
                {
                    ProviderId = "codex",
                    IsAvailable = true,
                    IsQuotaBased = true,
                    UsedPercent = 98,
                    Models = new[]
                    {
                        new AgentGroupedModelUsage
                        {
                            ModelId = "GPT-5.3-Codex-Spark",
                            ModelName = "GPT-5.3-Codex-Spark",
                            RemainingPercentage = 2,
                            UsedPercentage = 98,
                            EffectiveRemainingPercentage = 2,
                            EffectiveUsedPercentage = 98,
                            EffectiveDescription = "2.0% Remaining",
                            QuotaBuckets = new[]
                            {
                                new AgentGroupedQuotaBucketUsage
                                {
                                    BucketId = "spark-5h-quota",
                                    BucketName = "Spark 5h quota",
                                    RemainingPercentage = 100,
                                    UsedPercentage = 0,
                                    QuotaBucketKind = WindowKind.Burst,
                                },
                                new AgentGroupedQuotaBucketUsage
                                {
                                    BucketId = "weekly-quota",
                                    BucketName = "Weekly quota",
                                    RemainingPercentage = 2,
                                    UsedPercentage = 98,
                                    QuotaBucketKind = WindowKind.Rolling,
                                },
                            },
                        },
                    },
                    ProviderQuotaDetails = Array.Empty<ProviderUsageDetail>(),
                },
            },
        };

        var usages = GroupedUsageDisplayAdapter.Expand(snapshot);

        var spark = Assert.Single(usages, u => string.Equals(u.ProviderId, "codex.spark", StringComparison.Ordinal));
        Assert.NotNull(spark.Details);
        Assert.Equal(2, spark.Details!.Count);
        Assert.Single(spark.Details, d => d.QuotaBucketKind == WindowKind.Burst);
        Assert.Single(spark.Details, d => d.QuotaBucketKind == WindowKind.Rolling);

        // Effective used must reflect the binding constraint (98%), not the burst window (0%)
        Assert.Equal(98, spark.UsedPercent, 1);
    }

    [Fact]
    public void Expand_CodexSnapshot_PrefersProviderQuotaDetails_ForParentCard_WhenModelsAlsoPresent()
    {
        // Regression: when a provider has both Models (for child card building) and ProviderQuotaDetails
        // (5h + Weekly windows), the parent card must use the QuotaWindow details — not the Model details.
        // If model details win, TryGetPresentation finds no QuotaWindow entries and the parent never
        // renders dual progress bars.
        var burstDetail = new ProviderUsageDetail
        {
            Name = "5-hour quota",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Burst,
        };
        burstDetail.SetPercentageValue(20.0, PercentageValueSemantic.Used);

        var rollingDetail = new ProviderUsageDetail
        {
            Name = "Weekly quota",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Rolling,
        };
        rollingDetail.SetPercentageValue(10.0, PercentageValueSemantic.Used);

        var snapshot = new AgentGroupedUsageSnapshot
        {
            Providers = new[]
            {
                new AgentGroupedProviderUsage
                {
                    ProviderId = "codex",
                    IsAvailable = true,
                    IsQuotaBased = true,
                    UsedPercent = 20,
                    Models = new[]
                    {
                        new AgentGroupedModelUsage
                        {
                            ModelId = "gpt-5.3-codex-spark",
                            ModelName = "GPT-5.3-Codex-Spark",
                            RemainingPercentage = 80,
                        },
                    },
                    ProviderQuotaDetails = new[] { burstDetail, rollingDetail },
                },
            },
        };

        var usages = GroupedUsageDisplayAdapter.Expand(snapshot);

        var parent = Assert.Single(usages, u => string.Equals(u.ProviderId, "codex", StringComparison.Ordinal));
        Assert.NotNull(parent.Details);

        // Parent must have the QuotaWindow details (for dual bar), not the Model detail.
        Assert.All(parent.Details!, d => Assert.Equal(ProviderUsageDetailType.QuotaWindow, d.DetailType));
        Assert.Single(parent.Details!, d => d.QuotaBucketKind == WindowKind.Burst);
        Assert.Single(parent.Details!, d => d.QuotaBucketKind == WindowKind.Rolling);

        // Child card must still be built from the model.
        Assert.Single(usages, u => string.Equals(u.ProviderId, "codex.spark", StringComparison.Ordinal));
    }
}

