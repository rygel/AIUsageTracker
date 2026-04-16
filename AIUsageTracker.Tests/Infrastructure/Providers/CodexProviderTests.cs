// <copyright file="CodexProviderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using System.Text;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Tests.Infrastructure;
using Moq;
using Moq.Protected;
using Xunit;
#pragma warning disable CS0618

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class CodexProviderTests : HttpProviderTestBase<CodexProvider>
{
    [Fact]
    public async Task GetUsageAsync_AuthFileMissing_ReturnsUnavailableAsync()
    {
        // Arrange
        var missingAuthPath = TestTempPaths.CreateFilePath("codex-test-missing-auth", "auth.json");
        var provider = new CodexProvider(this.HttpClient, this.Logger.Object, missingAuthPath);

        // Act
        var usage = (await provider.GetUsageAsync(new ProviderConfig { ProviderId = "codex" })).Single();

        // Assert
        Assert.False(usage.IsAvailable);
        Assert.Contains("auth token not found", usage.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetUsageAsync_MissingAccessToken_PreservesIdentityFromIdTokenAsync()
    {
        var tempDir = TestTempPaths.CreateDirectory("codex-test-id-token-only");
        var authPath = Path.Combine(tempDir, "auth.json");
        var idToken = CreateJwt("codex-user@example.com", "plus");

        await File.WriteAllTextAsync(authPath, JsonSerializer.Serialize(new
        {
            tokens = new
            {
                id_token = idToken,
            },
        }));

        var provider = new CodexProvider(this.HttpClient, this.Logger.Object, authPath);

        try
        {
            var usage = (await provider.GetUsageAsync(new ProviderConfig { ProviderId = "codex" })).Single();
            Assert.False(usage.IsAvailable);
            Assert.Equal("codex-user@example.com", usage.AccountName);
            Assert.Contains("auth token not found", usage.Description, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TestTempPaths.CleanupPath(tempDir);
        }
    }

    [Fact]
    public async Task GetUsageAsync_NativeAuthAndUsageResponse_ReturnsParsedUsageAsync()
    {
        // Arrange
        var tempDir = TestTempPaths.CreateDirectory("codex-test");
        var authPath = Path.Combine(tempDir, "auth.json");
        var token = CreateJwt("user@example.com", "plus");
        var accountId = "acct_123";

        await File.WriteAllTextAsync(authPath, JsonSerializer.Serialize(new
        {
            tokens = new
            {
                access_token = token,
                account_id = accountId,
            },
        }));

        this.SetupHttpResponse("https://chatgpt.com/backend-api/wham/usage", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                model_name = "OpenAI-Codex-Live",
                plan_type = "plus",
                rate_limit = new
                {
                    primary_window = new { used_percent = 25, reset_after_seconds = 1200 },
                    secondary_window = new { used_percent = 10, reset_after_seconds = 600 },
                },
                credits = new
                {
                    balance = 7.5,
                    unlimited = false,
                },
            })),
        });

        var provider = new CodexProvider(this.HttpClient, this.Logger.Object, authPath);

        try
        {
            // Act
            var allUsages = (await provider.GetUsageAsync(new ProviderConfig { ProviderId = "codex" })).ToList();

            // Assert: flat cards — burst card and weekly card emitted separately
            var burstUsage = Assert.Single(allUsages, u => string.Equals(u.ProviderId, "codex", StringComparison.Ordinal) && string.Equals(u.CardId, "burst", StringComparison.Ordinal));
            var weeklyUsage = Assert.Single(allUsages, u => string.Equals(u.ProviderId, "codex", StringComparison.Ordinal) && string.Equals(u.CardId, "weekly", StringComparison.Ordinal));

            Assert.True(burstUsage.IsAvailable);
            Assert.Equal("OpenAI (Codex)", burstUsage.ProviderName);
            Assert.Equal("user@example.com", burstUsage.AccountName);
            Assert.Equal(25.0, burstUsage.UsedPercent); // 25% used (75% remaining)
            Assert.Equal(WindowKind.Burst, burstUsage.WindowKind);
            Assert.Equal(WindowKind.Rolling, weeklyUsage.WindowKind);
        }
        finally
        {
            TestTempPaths.CleanupPath(tempDir);
        }
    }

    [Fact]
    public async Task GetUsageAsync_UsesConfiguredProfileRootForAccountIdentityAsync()
    {
        var tempDir = TestTempPaths.CreateDirectory("codex-test-profile-name");
        var authPath = Path.Combine(tempDir, "auth.json");
        var token = CreateJwtWithProfileName("Codex Profile User");

        await File.WriteAllTextAsync(authPath, JsonSerializer.Serialize(new
        {
            tokens = new
            {
                access_token = token,
            },
        }));

        this.SetupHttpResponse("https://chatgpt.com/backend-api/wham/usage", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                model_name = "OpenAI-Codex-Live",
                plan_type = "plus",
                rate_limit = new
                {
                    primary_window = new { used_percent = 25, reset_after_seconds = 1200 },
                },
            })),
        });

        var provider = new CodexProvider(this.HttpClient, this.Logger.Object, authPath);

        try
        {
            var usage = (await provider.GetUsageAsync(new ProviderConfig { ProviderId = "codex" })).Single();
            Assert.True(usage.IsAvailable);
            Assert.Equal("Codex Profile User", usage.AccountName);
        }
        finally
        {
            TestTempPaths.CleanupPath(tempDir);
        }
    }

    [Fact]
    public async Task GetUsageAsync_PrefersSparkSpecificAdditionalRateLimit_InParentDetails_WhenMultipleCandidatesExistAsync()
    {
        var tempDir = TestTempPaths.CreateDirectory("codex-test-spark-window");
        var authPath = Path.Combine(tempDir, "auth.json");
        var token = CreateJwt("user@example.com", "plus");

        await File.WriteAllTextAsync(authPath, JsonSerializer.Serialize(new
        {
            tokens = new
            {
                access_token = token,
            },
        }));

        this.SetupHttpResponse("https://chatgpt.com/backend-api/wham/usage", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                plan_type = "plus",
                rate_limit = new
                {
                    primary_window = new { used_percent = 30, reset_after_seconds = 1200 },
                },
                additional_rate_limits = new object[]
                {
                    new
                    {
                        limit_name = "gpt-5",
                        model_name = "gpt-5",
                        rate_limit = new
                        {
                            primary_window = new { used_percent = 0, reset_after_seconds = 2000 },
                        },
                    },
                    new
                    {
                        limit_name = "spark-window",
                        model_name = "gpt-5.3-codex-spark",
                        rate_limit = new
                        {
                            primary_window = new { used_percent = 40, reset_after_seconds = 1800 },
                        },
                    },
                },
            })),
        });

        var provider = new CodexProvider(this.HttpClient, this.Logger.Object, authPath);

        try
        {
            var usages = (await provider.GetUsageAsync(new ProviderConfig { ProviderId = "codex" })).ToList();

            // In the flat-card model, a codex.spark card is emitted when spark window data exists.
            var sparkCard = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "codex.spark", StringComparison.Ordinal) && usage.WindowKind == WindowKind.Burst);

            // Spark card: bound by primary (40% used) since no secondary window
            Assert.Equal(40, sparkCard.UsedPercent, precision: 0);
        }
        finally
        {
            TestTempPaths.CleanupPath(tempDir);
        }
    }

    [Fact]
    public async Task GetUsageAsync_SparkCard_ShowsZeroUsed_WhenSparkHasNoOwnSecondaryWindowAsync()
    {
        // When Spark has only a primary_window (no secondary_window), the spark.weekly card
        // must show 0% — no cross-window fallback to the main secondary_window.
        // The main secondary_window (98%) belongs to the "weekly" card, not to spark.weekly.
        var tempDir = TestTempPaths.CreateDirectory("codex-test-spark-no-secondary");
        var authPath = Path.Combine(tempDir, "auth.json");
        var token = CreateJwt("user@example.com", "plus");

        await File.WriteAllTextAsync(authPath, JsonSerializer.Serialize(new
        {
            tokens = new
            {
                access_token = token,
            },
        }));

        this.SetupHttpResponse("https://chatgpt.com/backend-api/wham/usage", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                plan_type = "plus",
                rate_limit = new
                {
                    primary_window = new { used_percent = 0, reset_after_seconds = 18000 },
                    secondary_window = new { used_percent = 98, reset_after_seconds = 604800 },
                },
                additional_rate_limits = new object[]
                {
                    new
                    {
                        limit_name = "GPT-5.3-Codex-Spark",
                        rate_limit = new
                        {
                            // Spark 5h window just reset — own quota is 0% used; no own secondary window
                            primary_window = new { used_percent = 0, reset_after_seconds = 18000 },
                        },
                    },
                },
            })),
        });

        var provider = new CodexProvider(this.HttpClient, this.Logger.Object, authPath);

        try
        {
            var usages = (await provider.GetUsageAsync(new ProviderConfig { ProviderId = "codex" })).ToList();

            var sparkBurst = Assert.Single(usages, usage => usage.ProviderId == "codex.spark" && usage.WindowKind == WindowKind.Burst);
            Assert.Equal(0, sparkBurst.UsedPercent, precision: 0);

            // No cross-window fallback: spark.weekly shows 0% because spark has no own secondary window.
            var sparkWeekly = Assert.Single(usages, usage => usage.ProviderId == "codex.spark" && usage.WindowKind == WindowKind.Rolling);
            Assert.Equal(0, sparkWeekly.UsedPercent, precision: 0);
        }
        finally
        {
            TestTempPaths.CleanupPath(tempDir);
        }
    }

    [Fact]
    public async Task GetUsageAsync_ParsesSparkPrimaryAndSecondaryWindows_InParentDetailsAsync()
    {
        var tempDir = TestTempPaths.CreateDirectory("codex-test-spark-secondary-window");
        var authPath = Path.Combine(tempDir, "auth.json");
        var token = CreateJwt("user@example.com", "plus");

        await File.WriteAllTextAsync(authPath, JsonSerializer.Serialize(new
        {
            tokens = new
            {
                access_token = token,
            },
        }));

        this.SetupHttpResponse("https://chatgpt.com/backend-api/wham/usage", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                plan_type = "plus",
                rate_limit = new
                {
                    primary_window = new { used_percent = 20, reset_after_seconds = 1200 },
                    secondary_window = new { used_percent = 10, reset_after_seconds = 6000 },
                },
                additional_rate_limits = new object[]
                {
                    new
                    {
                        limit_name = "GPT-5.3-Codex-Spark",
                        rate_limit = new
                        {
                            primary_window = new { used_percent = 40, reset_after_seconds = 1800 },
                            secondary_window = new { used_percent = 75, reset_after_seconds = 36000 },
                        },
                    },
                },
            })),
        });

        var provider = new CodexProvider(this.HttpClient, this.Logger.Object, authPath);

        try
        {
            var usages = (await provider.GetUsageAsync(new ProviderConfig { ProviderId = "codex" })).ToList();

            // Codex: burst (20%), weekly (10%). Spark: burst (40%), weekly (75%).
            Assert.Contains(usages, u => u.ProviderId == "codex" && u.CardId == "burst" && u.WindowKind == WindowKind.Burst);
            Assert.Contains(usages, u => u.ProviderId == "codex" && u.CardId == "weekly" && u.WindowKind == WindowKind.Rolling);
            var sparkBurst = Assert.Single(usages, u => u.ProviderId == "codex.spark" && u.WindowKind == WindowKind.Burst);
            Assert.Equal(40, sparkBurst.UsedPercent, precision: 0);
            var sparkWeekly = Assert.Single(usages, u => u.ProviderId == "codex.spark" && u.WindowKind == WindowKind.Rolling);
            Assert.Equal(75, sparkWeekly.UsedPercent, precision: 0);
        }
        finally
        {
            TestTempPaths.CleanupPath(tempDir);
        }
    }

    [Fact]
    public async Task GetUsageAsync_SparkSecondaryWindowIsBindingConstraint_ModelDetailReflectsSparkSecondaryAsync()
    {
        // Regression: when additional_rate_limits[spark].rate_limit.secondary_window is more
        // heavily used than the Spark 5h primary window, the Model detail (and thus the child
        // card) must reflect the secondary constraint, not just the Spark primary window.
        // Previously, sparkPrimary ?? sparkSecondary dropped sparkSecondary when sparkPrimary
        // was present, causing the child card to show e.g. 40% when the real constraint is 75%.
        var tempDir = TestTempPaths.CreateDirectory("codex-test-spark-secondary-binding");
        var authPath = Path.Combine(tempDir, "auth.json");
        var token = CreateJwt("user@example.com", "plus");

        await File.WriteAllTextAsync(authPath, JsonSerializer.Serialize(new
        {
            tokens = new
            {
                access_token = token,
            },
        }));

        this.SetupHttpResponse("https://chatgpt.com/backend-api/wham/usage", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                plan_type = "plus",
                rate_limit = new
                {
                    primary_window = new { used_percent = 20, reset_after_seconds = 18000 },

                    // No secondary_window in the main rate_limit — weekly only in spark block.
                },
                additional_rate_limits = new object[]
                {
                    new
                    {
                        limit_name = "GPT-5.3-Codex-Spark",
                        rate_limit = new
                        {
                            primary_window = new { used_percent = 40, reset_after_seconds = 18000 },
                            secondary_window = new { used_percent = 75, reset_after_seconds = 604800 },
                        },
                    },
                },
            })),
        });

        var provider = new CodexProvider(this.HttpClient, this.Logger.Object, authPath);

        try
        {
            var usages = (await provider.GetUsageAsync(new ProviderConfig { ProviderId = "codex" })).ToList();

            // Spark burst = 40%, Spark weekly = 75% (from spark's own secondary_window).
            var sparkBurst = Assert.Single(usages, u => u.ProviderId == "codex.spark" && u.WindowKind == WindowKind.Burst);
            Assert.Equal(40, sparkBurst.UsedPercent, precision: 0);
            var sparkWeekly = Assert.Single(usages, u => u.ProviderId == "codex.spark" && u.WindowKind == WindowKind.Rolling);
            Assert.Equal(75, sparkWeekly.UsedPercent, precision: 0);

            // The weekly card is absent because there is no main secondary_window in this test response.
            // Only the burst card exists for the parent codex.
            Assert.Contains(usages, u => string.Equals(u.ProviderId, "codex", StringComparison.Ordinal) && string.Equals(u.CardId, "burst", StringComparison.Ordinal));
            Assert.DoesNotContain(usages, u => string.Equals(u.ProviderId, "codex", StringComparison.Ordinal) && string.Equals(u.CardId, "weekly", StringComparison.Ordinal));
        }
        finally
        {
            TestTempPaths.CleanupPath(tempDir);
        }
    }

    [Fact]
    public async Task GetUsageAsync_SparkBurstWindowJustReset_EmitsModelScopedDualBarDetailsAsync()
    {
        // Regression: when the Spark 5h burst window just reset, the API omits used_percent
        // and only returns reset_after_seconds. Previously HasWindowData checked only usage
        // values → evaluated to false → entire spark block skipped → no model-scoped QW details
        // → BuildSummaryQuotaBuckets fallback → single "effective" bucket (Kind=None) → no dual
        // bars on the child card, and 0% used displayed.
        // With the fix, HasWindowData also considers reset timers → spark block runs →
        // Burst detail gets 0% used (100% remaining) + Rolling detail from weekly data.
        var tempDir = TestTempPaths.CreateDirectory("codex-test-spark-burst-reset");
        var authPath = Path.Combine(tempDir, "auth.json");
        var token = CreateJwt("user@example.com", "plus");

        await File.WriteAllTextAsync(authPath, JsonSerializer.Serialize(new
        {
            tokens = new
            {
                access_token = token,
            },
        }));

        this.SetupHttpResponse("https://chatgpt.com/backend-api/wham/usage", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                plan_type = "plus",
                rate_limit = new
                {
                    // Primary (5h) window: has usage data
                    primary_window = new { used_percent = 20, reset_after_seconds = 18000 },

                    // Weekly window present in main block
                    secondary_window = new { used_percent = 19, reset_after_seconds = 604800 },
                },
                additional_rate_limits = new object[]
                {
                    new
                    {
                        limit_name = "GPT-5.3-Codex-Spark",
                        rate_limit = new
                        {
                            // Burst window just reset — API omits used_percent, only reset timer present
                            primary_window = new { reset_after_seconds = 18000 },
                            secondary_window = new { used_percent = 19, reset_after_seconds = 604800 },
                        },
                    },
                },
            })),
        });

        var provider = new CodexProvider(this.HttpClient, this.Logger.Object, authPath);

        try
        {
            var usages = (await provider.GetUsageAsync(new ProviderConfig { ProviderId = "codex" })).ToList();

            // Spark burst: 0% (just reset, API omitted used_percent). Spark weekly: 19%.
            var sparkBurst = Assert.Single(usages, u => u.ProviderId == "codex.spark" && u.WindowKind == WindowKind.Burst);
            Assert.Equal(0, sparkBurst.UsedPercent, precision: 0);
            var sparkWeekly = Assert.Single(usages, u => u.ProviderId == "codex.spark" && u.WindowKind == WindowKind.Rolling);
            Assert.Equal(19, sparkWeekly.UsedPercent, precision: 0);

            // Parent burst card: main primary was 20% used
            var burstCard = Assert.Single(usages, u => string.Equals(u.ProviderId, "codex", StringComparison.Ordinal) && u.WindowKind == WindowKind.Burst);
            Assert.Equal(20, burstCard.UsedPercent, precision: 0);

            // Parent weekly card: main secondary was 19% used
            var weeklyCard = Assert.Single(usages, u => string.Equals(u.ProviderId, "codex", StringComparison.Ordinal) && u.WindowKind == WindowKind.Rolling);
            Assert.Equal(19, weeklyCard.UsedPercent, precision: 0);
        }
        finally
        {
            TestTempPaths.CleanupPath(tempDir);
        }
    }

    [Fact]
    public async Task GetUsageAsync_SparkModelDetailName_ContainsSparkToken_ForChildCardAssignmentAsync()
    {
        // Regression: primaryModelName defaults to "OpenAI" when the API response has no
        // root-level model_name. The DerivedModelSelector for codex.spark requires "spark" in
        // ModelId/ModelName. If the Model detail uses primaryModelName ("OpenAI"), the selector
        // never matches and no codex.spark child card is created in the UI.
        // Fix: when sparkWindow.HasWindowData, derive the Model detail's Name/ModelName from
        // sparkWindow.ModelName ?? sparkWindow.Label so it contains "spark".
        var tempDir = TestTempPaths.CreateDirectory("codex-test-spark-model-name");
        var authPath = Path.Combine(tempDir, "auth.json");
        var token = CreateJwt("user@example.com", "plus");

        await File.WriteAllTextAsync(authPath, JsonSerializer.Serialize(new
        {
            tokens = new { access_token = token },
        }));

        this.SetupHttpResponse("https://chatgpt.com/backend-api/wham/usage", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                plan_type = "plus",

                // No root model_name → primaryModelName defaults to "OpenAI"
                rate_limit = new
                {
                    primary_window = new { used_percent = 20, reset_after_seconds = 18000 },
                    secondary_window = new { used_percent = 15, reset_after_seconds = 604800 },
                },
                additional_rate_limits = new object[]
                {
                    new
                    {
                        limit_name = "GPT-5.3-Codex-Spark",
                        rate_limit = new
                        {
                            primary_window = new { used_percent = 30, reset_after_seconds = 18000 },
                            secondary_window = new { used_percent = 15, reset_after_seconds = 604800 },
                        },
                    },
                },
            })),
        });

        var provider = new CodexProvider(this.HttpClient, this.Logger.Object, authPath);

        try
        {
            var usages = (await provider.GetUsageAsync(new ProviderConfig { ProviderId = "codex" })).ToList();

            // The codex.spark flat card must exist so the DerivedModelSelector for codex.spark can match it.
            Assert.Contains(usages, u => string.Equals(u.ProviderId, "codex.spark", StringComparison.Ordinal));
        }
        finally
        {
            TestTempPaths.CleanupPath(tempDir);
        }
    }

    [Fact]
    public async Task GetUsageAsync_SparkWeeklyFullyConsumed_NoMainSecondaryWindow_EmitsDualBarDetailsAsync()
    {
        // Production regression: real API response has rate_limit.secondary_window = null (absent),
        // and additional_rate_limits[spark].primary_window.used_percent = 0 (burst just reset),
        // secondary_window.used_percent = 100 (weekly fully consumed).
        // Verify: child card shows TWO bars — Burst at 0% used (100% remaining) and
        // Rolling at 100% used (0% remaining). Without hasAnyWeeklyData including spark secondary,
        // the Rolling bar would be silently dropped.
        var tempDir = TestTempPaths.CreateDirectory("codex-test-spark-weekly-full");
        var authPath = Path.Combine(tempDir, "auth.json");
        var token = CreateJwt("user@example.com", "plus");

        await File.WriteAllTextAsync(authPath, JsonSerializer.Serialize(new
        {
            tokens = new { access_token = token },
        }));

        this.SetupHttpResponse("https://chatgpt.com/backend-api/wham/usage", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                plan_type = "plus",
                rate_limit = new
                {
                    // Main burst window: 0% used (just reset)
                    primary_window = new { used_percent = 0, reset_after_seconds = 18000 },

                    // NOTE: no secondary_window — it is null/absent in the real API response
                },
                additional_rate_limits = new object[]
                {
                    new
                    {
                        limit_name = "GPT-5.3-Codex-Spark",
                        rate_limit = new
                        {
                            allowed = false,
                            limit_reached = true,
                            primary_window = new { used_percent = 0, reset_after_seconds = 18000 },
                            secondary_window = new { used_percent = 100, reset_after_seconds = 194425 },
                        },
                    },
                },
            })),
        });

        var provider = new CodexProvider(this.HttpClient, this.Logger.Object, authPath);

        try
        {
            var usages = (await provider.GetUsageAsync(new ProviderConfig { ProviderId = "codex" })).ToList();

            // Burst card: 0% used (just reset)
            var burstCard = Assert.Single(usages, u => u.ProviderId == "codex" && u.CardId == "burst");
            Assert.Equal(0, burstCard.UsedPercent, precision: 0);

            // No main secondary_window → no weekly card
            Assert.DoesNotContain(usages, u => u.ProviderId == "codex" && u.CardId == "weekly");

            // Spark burst: 0% (just reset). Spark weekly: 100% (fully consumed).
            var sparkBurst = Assert.Single(usages, u => u.ProviderId == "codex.spark" && u.WindowKind == WindowKind.Burst);
            Assert.Equal(0, sparkBurst.UsedPercent, precision: 0);
            var sparkWeekly = Assert.Single(usages, u => u.ProviderId == "codex.spark" && u.WindowKind == WindowKind.Rolling);
            Assert.Equal(100, sparkWeekly.UsedPercent, precision: 0);
        }
        finally
        {
            TestTempPaths.CleanupPath(tempDir);
        }
    }

    [Fact]
    public async Task GetUsageAsync_SparkHasOwnLowerWeeklyThanMainWeekly_UsesSparkOwnWeeklyAsync()
    {
        // Production regression: when the main rate_limit.secondary_window has high usage (98%)
        // but Spark's own additional_rate_limits[spark].secondary_window has lower usage (19%),
        // the Spark child card must show Spark's own independent weekly (81% remaining),
        // NOT the main codex weekly (2% remaining). Math.Max was picking the wrong value.
        var tempDir = TestTempPaths.CreateDirectory("codex-test-spark-own-weekly");
        var authPath = Path.Combine(tempDir, "auth.json");
        var token = CreateJwt("user@example.com", "plus");

        await File.WriteAllTextAsync(authPath, JsonSerializer.Serialize(new
        {
            tokens = new { access_token = token },
        }));

        this.SetupHttpResponse("https://chatgpt.com/backend-api/wham/usage", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                plan_type = "plus",
                rate_limit = new
                {
                    primary_window = new { used_percent = 0, reset_after_seconds = 18000 },
                    secondary_window = new { used_percent = 98, reset_after_seconds = 604800 }, // main codex weekly heavily used
                },
                additional_rate_limits = new object[]
                {
                    new
                    {
                        limit_name = "GPT-5.3-Codex-Spark",
                        rate_limit = new
                        {
                            primary_window = new { used_percent = 0, reset_after_seconds = 18000 },
                            secondary_window = new { used_percent = 19, reset_after_seconds = 604800 }, // Spark's own weekly — independent
                        },
                    },
                },
            })),
        });

        var provider = new CodexProvider(this.HttpClient, this.Logger.Object, authPath);

        try
        {
            var usages = (await provider.GetUsageAsync(new ProviderConfig { ProviderId = "codex" })).ToList();

            // Weekly card: driven by main secondary → 98% used
            var weeklyCard = Assert.Single(usages, u => u.ProviderId == "codex" && u.CardId == "weekly");
            Assert.Equal(98, weeklyCard.UsedPercent, precision: 0);

            // Spark burst: 0% (just reset). Spark weekly: uses its OWN secondary (19%), NOT main (98%).
            var sparkBurst = Assert.Single(usages, u => u.ProviderId == "codex.spark" && u.WindowKind == WindowKind.Burst);
            Assert.Equal(0, sparkBurst.UsedPercent, precision: 0);
            var sparkWeekly = Assert.Single(usages, u => u.ProviderId == "codex.spark" && u.WindowKind == WindowKind.Rolling);
            Assert.Equal(19, sparkWeekly.UsedPercent, precision: 0);
        }
        finally
        {
            TestTempPaths.CleanupPath(tempDir);
        }
    }

    private static string CreateJwt(string email, string planType)
    {
        var headerJson = JsonSerializer.Serialize(new { alg = "HS256", typ = "JWT" });
        var payloadJson = JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["https://api.openai.com/profile"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["email"] = email,
            },
            ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
        });

        return $"{Base64UrlEncode(headerJson)}.{Base64UrlEncode(payloadJson)}.sig";
    }

    private static string CreateJwtWithProfileName(string name)
    {
        var headerJson = JsonSerializer.Serialize(new { alg = "HS256", typ = "JWT" });
        var payloadJson = JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["https://api.openai.com/profile"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = name,
            },
            ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
        });

        return $"{Base64UrlEncode(headerJson)}.{Base64UrlEncode(payloadJson)}.sig";
    }

    private static string Base64UrlEncode(string value)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        return encoded.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
