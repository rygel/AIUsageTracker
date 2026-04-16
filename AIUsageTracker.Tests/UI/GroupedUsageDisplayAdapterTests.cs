// <copyright file="GroupedUsageDisplayAdapterTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Linq;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public class GroupedUsageDisplayAdapterTests
{
    [Fact]
    public void Expand_GeminiSnapshot_ProducesThreeFlatCards_NoParent()
    {
        // Gemini uses FlatWindowCards: each model becomes an independent flat card.
        // All flat cards share the parent ProviderId; the model is stored in CardId.
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

        Assert.Equal(3, usages.Count);
        Assert.Equal(3, usages.Count(u => string.Equals(u.ProviderId, "gemini-cli", StringComparison.Ordinal)));

        var flashLite = Assert.Single(usages, u => string.Equals(u.ProviderId, "gemini-cli", StringComparison.Ordinal) && string.Equals(u.CardId, "gemini-2.5-flash-lite", StringComparison.Ordinal));
        var flashPreview = Assert.Single(usages, u => string.Equals(u.ProviderId, "gemini-cli", StringComparison.Ordinal) && string.Equals(u.CardId, "gemini-3-flash-preview", StringComparison.Ordinal));
        var pro = Assert.Single(usages, u => string.Equals(u.ProviderId, "gemini-cli", StringComparison.Ordinal) && string.Equals(u.CardId, "gemini-2.5-pro", StringComparison.Ordinal));

        Assert.Equal("Gemini 2.5 Flash Lite", flashLite.ProviderName);
        Assert.Equal("Gemini 3 Flash Preview", flashPreview.ProviderName);
        Assert.Equal("Gemini 2.5 Pro", pro.ProviderName);
        Assert.Equal(2.9, flashLite.UsedPercent, 1);
    }

    [Fact]
    public void Expand_CodexSnapshot_CreatesSparkFlatCard_WhenModelIdIsSpark()
    {
        // When the snapshot already has Models populated, each model becomes a flat card.
        // ModelId = "spark" produces a card with ProviderId = "codex", CardId = "spark"
        // with PeriodDuration from the matched QuotaWindow.
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
                            ModelId = "spark",
                            ModelName = "Spark",
                            RemainingPercentage = 72,
                        },
                    },
                },
            },
        };

        var usages = GroupedUsageDisplayAdapter.Expand(snapshot);

        Assert.Single(usages);
        var spark = usages[0];
        Assert.Equal("codex", spark.ProviderId);
        Assert.Equal("spark", spark.CardId);
        Assert.Equal("Spark", spark.ProviderName);
        Assert.Equal(28, spark.UsedPercent, 1); // RemainingPercentage = 72 → UsedPercent = 28
        Assert.Equal(TimeSpan.FromDays(7), spark.PeriodDuration);
    }

    [Fact]
    public void Expand_ClaudeSnapshot_PassesThroughProviderDetails_ToParentCard()
    {
        // ProviderDetails is the single source of truth: window cards flow through to WindowCards.
        var snapshot = new AgentGroupedUsageSnapshot
        {
            Providers = new[]
            {
                new AgentGroupedProviderUsage
                {
                    ProviderId = "claude-code",
                    IsAvailable = true,
                    IsQuotaBased = true,
                    UsedPercent = 73,
                    ProviderDetails = new[]
                    {
                        new ProviderUsage { ProviderId = "claude-code", Name = "Current Session", WindowKind = WindowKind.Burst,   UsedPercent = 14.0 },
                        new ProviderUsage { ProviderId = "claude-code", Name = "All Models",      WindowKind = WindowKind.Rolling, UsedPercent = 73.0 },
                    },
                },
            },
        };

        var usages = GroupedUsageDisplayAdapter.Expand(snapshot);

        var parent = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "claude-code", StringComparison.Ordinal));
        Assert.Equal(TimeSpan.FromDays(7), parent.PeriodDuration);
        Assert.NotNull(parent.WindowCards);
        Assert.Equal(2, parent.WindowCards!.Count);

        var session = Assert.Single(parent.WindowCards!, d => string.Equals(d.Name, "Current Session", StringComparison.Ordinal));
        var allModels = Assert.Single(parent.WindowCards!, d => string.Equals(d.Name, "All Models", StringComparison.Ordinal));

        Assert.Equal(WindowKind.Burst, session.WindowKind);
        Assert.Equal(WindowKind.Rolling, allModels.WindowKind);
    }

    [Fact]
    public void Expand_CodexSnapshot_NoSparkFlatCard_WhenModelIdIsNotSpark()
    {
        // Without a model with ModelId = "spark", there is no card with CardId = "spark".
        // Any model creates a flat card with ProviderId = parent, CardId = modelId.
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
        Assert.Equal("gpt-5.3-codex", usages[0].CardId);
        Assert.DoesNotContain(usages, u => string.Equals(u.CardId, "spark", StringComparison.Ordinal));
    }

    [Fact]
    public void Expand_GithubCopilotWithSingleModel_ProducesSingleFlatCard()
    {
        // Any provider with models uses flat cards — one card per model.
        // ProviderId = parent provider, CardId = modelId.
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
        Assert.Equal("weekly", usages[0].CardId);
        Assert.Equal("Weekly Quota", usages[0].ProviderName);
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

        var derived = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "gemini-cli", StringComparison.Ordinal) && string.Equals(usage.CardId, "gemini-3-flash-preview", StringComparison.Ordinal));
        Assert.Equal(65, derived.UsedPercent, 1); // most constrained bucket: 65% used
        Assert.Equal(65, derived.RequestsUsed, 1);
        Assert.Equal("35% remaining", derived.Description);
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

        var minute = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "gemini-cli", StringComparison.Ordinal) && string.Equals(usage.CardId, "minute", StringComparison.Ordinal));
        var hourly = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "gemini-cli", StringComparison.Ordinal) && string.Equals(usage.CardId, "hourly", StringComparison.Ordinal));
        var daily = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "gemini-cli", StringComparison.Ordinal) && string.Equals(usage.CardId, "daily", StringComparison.Ordinal));

        Assert.Equal("Zulu Minute", minute.ProviderName);
        Assert.Equal("Bravo Hourly", hourly.ProviderName);
        Assert.Equal("Alpha Daily", daily.ProviderName);
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

        var minute = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "gemini-cli", StringComparison.Ordinal) && string.Equals(usage.CardId, "minute", StringComparison.Ordinal));
        Assert.Equal("Minute Model", minute.ProviderName);

        var dynamic = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "gemini-cli", StringComparison.Ordinal) && string.Equals(usage.CardId, "gemini-3-pro", StringComparison.Ordinal));
        Assert.Equal("Gemini 3 Pro", dynamic.ProviderName);
    }

    [Fact]
    public void Expand_CodexSnapshot_CreatesFlatCardPerModel_KeyedByModelId()
    {
        // When Models are present, each becomes a flat card: ProviderId = parent, CardId = modelId.
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
                            ModelId = "burst",
                            ModelName = "5-hour quota",
                            RemainingPercentage = 65,
                        },
                        new AgentGroupedModelUsage
                        {
                            ModelId = "spark",
                            ModelName = "Spark",
                            RemainingPercentage = 40,
                        },
                    },
                },
            },
        };

        var usages = GroupedUsageDisplayAdapter.Expand(snapshot);

        Assert.Equal(2, usages.Count);
        var burst = Assert.Single(usages, u => string.Equals(u.ProviderId, "codex", StringComparison.Ordinal) && string.Equals(u.CardId, "burst", StringComparison.Ordinal));
        var spark = Assert.Single(usages, u => string.Equals(u.ProviderId, "codex", StringComparison.Ordinal) && string.Equals(u.CardId, "spark", StringComparison.Ordinal));
        Assert.Equal("5-hour quota", burst.ProviderName);
        Assert.Equal("Spark", spark.ProviderName);
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

        var derived = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "gemini-cli", StringComparison.Ordinal) && string.Equals(usage.CardId, "gemini-2.5-flash-lite", StringComparison.Ordinal));
        Assert.Equal(23, derived.UsedPercent, 1); // EffectiveUsedPercentage = 23
        Assert.Equal(23, derived.RequestsUsed, 1);
        Assert.Equal("77.0% Remaining", derived.Description);
        Assert.Equal(now.AddHours(3), derived.NextResetTime);
    }

    [Fact]
    public void Expand_AntigravitySnapshot_ProducesSingleFlatCard_NoParent()
    {
        // Antigravity uses FlatWindowCards: each model becomes an independent flat card.
        // All flat cards share ProviderId = "antigravity"; the model is stored in CardId.
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

        Assert.Single(usages);
        var flat = usages[0];
        Assert.Equal("antigravity", flat.ProviderId);
        Assert.Equal("gemini-3-flash", flat.CardId);
        Assert.Equal("Gemini 3 Flash", flat.ProviderName);
        Assert.Equal(0, flat.UsedPercent, 1);
        Assert.Equal("100% Remaining", flat.Description);
    }

    [Fact]
    public void Expand_KimiSnapshot_AttachesProviderDetailsToParent()
    {
        // Kimi has no Model-type details; all its details are QuotaWindow (Weekly + 5h).
        // ProviderDetails window cards must flow through to WindowCards on the parent
        // so that TryGetDualQuotaBucketPresentation can render two bars.
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
                    ProviderDetails = new[]
                    {
                        new ProviderUsage { ProviderId = "kimi-for-coding", Name = "Weekly Limit", WindowKind = WindowKind.Rolling, UsedPercent = 25.0 },
                        new ProviderUsage { ProviderId = "kimi-for-coding", Name = "5h Limit",     WindowKind = WindowKind.Burst,   UsedPercent = 0.0 },
                    },
                },
            },
        };

        var usages = GroupedUsageDisplayAdapter.Expand(snapshot);

        var parent = Assert.Single(usages, u => string.Equals(u.ProviderId, "kimi-for-coding", StringComparison.Ordinal));
        Assert.NotNull(parent.WindowCards);
        Assert.Equal(2, parent.WindowCards!.Count);

        var weekly = Assert.Single(parent.WindowCards, d => d.WindowKind == WindowKind.Rolling);
        Assert.Equal("Weekly Limit", weekly.Name);

        var burst = Assert.Single(parent.WindowCards, d => d.WindowKind == WindowKind.Burst);
        Assert.Equal("5h Limit", burst.Name);
    }

    [Fact]
    public void Expand_CodexSparkFlatCard_UsesEffectiveUsedPercentage()
    {
        // Flat cards have no WindowCards. EffectiveUsedPercentage overrides bucket values.
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
                            ModelId = "spark",
                            ModelName = "Spark",
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
                },
            },
        };

        var usages = GroupedUsageDisplayAdapter.Expand(snapshot);

        var spark = Assert.Single(usages, u => string.Equals(u.ProviderId, "codex", StringComparison.Ordinal) && string.Equals(u.CardId, "spark", StringComparison.Ordinal));
        Assert.Null(spark.WindowCards); // Flat cards have no window cards
        Assert.Equal(98, spark.UsedPercent, 1); // EffectiveUsedPercentage = 98 wins
    }

    [Fact]
    public void Expand_CodexSnapshot_ProviderDetailsIgnored_WhenModelsPresent()
    {
        // When Models are present, flat cards are built from Models only.
        // ProviderDetails are not used; there is no parent aggregate card.
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
                            ModelId = "spark",
                            ModelName = "Spark",
                            RemainingPercentage = 80,
                        },
                    },
                    ProviderDetails = new[]
                    {
                        new ProviderUsage { ProviderId = "codex", Name = "5-hour quota", WindowKind = WindowKind.Burst,   UsedPercent = 20.0 },
                        new ProviderUsage { ProviderId = "codex", Name = "Weekly quota", WindowKind = WindowKind.Rolling, UsedPercent = 10.0 },
                    },
                },
            },
        };

        var usages = GroupedUsageDisplayAdapter.Expand(snapshot);

        Assert.Single(usages); // Only the flat card, no parent
        var spark = usages[0];
        Assert.Equal("codex", spark.ProviderId);
        Assert.Equal("spark", spark.CardId);
        Assert.Null(spark.WindowCards); // ProviderDetails are not mapped to WindowCards on flat cards
        Assert.DoesNotContain(usages, u => string.Equals(u.ProviderId, "codex", StringComparison.Ordinal) && u.CardId == null);
    }

    [Fact]
    public void Expand_ClaudeCodeSnapshot_ProducesFlatWindowCards_NoParent()
    {
        // Claude Code uses FlatWindowCards mode: every quota window becomes its own
        // independent top-level card. ProviderId = "claude-code" on all; CardId = modelId.
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
                    UsedPercent = 5,
                    Models = new[]
                    {
                        new AgentGroupedModelUsage { ModelId = "current-session", ModelName = "Current Session", UsedPercentage = 3 },
                        new AgentGroupedModelUsage { ModelId = "sonnet",          ModelName = "Sonnet",          UsedPercentage = 2 },
                        new AgentGroupedModelUsage { ModelId = "all-models",      ModelName = "All Models",      UsedPercentage = 5 },
                    },
                },
            },
        };

        var usages = GroupedUsageDisplayAdapter.Expand(snapshot);

        // Three flat cards — all with ProviderId = "claude-code", each with a distinct CardId
        Assert.Equal(3, usages.Count);
        Assert.Equal(3, usages.Count(u => string.Equals(u.ProviderId, "claude-code", StringComparison.Ordinal)));

        var currentSession = Assert.Single(usages, u => string.Equals(u.ProviderId, "claude-code", StringComparison.Ordinal) && string.Equals(u.CardId, "current-session", StringComparison.Ordinal));
        var sonnet = Assert.Single(usages, u => string.Equals(u.ProviderId, "claude-code", StringComparison.Ordinal) && string.Equals(u.CardId, "sonnet", StringComparison.Ordinal));
        var allModels = Assert.Single(usages, u => string.Equals(u.ProviderId, "claude-code", StringComparison.Ordinal) && string.Equals(u.CardId, "all-models", StringComparison.Ordinal));

        Assert.Equal("Claude Code (Current Session)", currentSession.ProviderName);
        Assert.Equal("Claude Code (Sonnet)", sonnet.ProviderName);
        Assert.Equal("Claude Code (All Models)", allModels.ProviderName);

        Assert.Equal(3, currentSession.UsedPercent, 1);
        Assert.Equal(2, sonnet.UsedPercent, 1);
        Assert.Equal(5, allModels.UsedPercent, 1);

        // PeriodDuration resolved from the provider's Rolling QuotaWindowDefinition (same for all flat cards)
        Assert.Equal(TimeSpan.FromDays(7), currentSession.PeriodDuration);
        Assert.Equal(TimeSpan.FromDays(7), sonnet.PeriodDuration);
        Assert.Equal(TimeSpan.FromDays(7), allModels.PeriodDuration);
    }

    [Fact]
    public void Expand_LegacyPath_PropagatesStateFromSnapshot()
    {
        // Verifies that State=Missing survives the AgentGroupedProviderUsage → ProviderUsage
        // conversion, so PrepareForMainWindow can filter unconfigured StandardApiKey providers.
        var snapshot = new AgentGroupedUsageSnapshot
        {
            Providers = new[]
            {
                new AgentGroupedProviderUsage
                {
                    ProviderId = "openrouter",
                    IsAvailable = false,
                    State = ProviderUsageState.Missing,
                    IsQuotaBased = true,
                    PlanType = PlanType.Usage,
                    Description = "API Key missing",
                    Models = Array.Empty<AgentGroupedModelUsage>(),
                },
            },
        };

        var usages = GroupedUsageDisplayAdapter.Expand(snapshot);

        var card = Assert.Single(usages);
        Assert.Equal("openrouter", card.ProviderId);
        Assert.Equal(ProviderUsageState.Missing, card.State);
    }
}
