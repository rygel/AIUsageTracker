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
            var usage = allUsages.Single(u => string.Equals(u.ProviderId, "codex", StringComparison.Ordinal));

            // Assert
            Assert.True(usage.IsAvailable);
            Assert.Equal("OpenAI (Codex)", usage.ProviderName);
            Assert.Equal("user@example.com", usage.AccountName);
            Assert.Equal(25.0, usage.UsedPercent); // 25% used (75% remaining)
            Assert.NotNull(usage.Details);
            Assert.Contains(usage.Details, d => d.QuotaBucketKind == WindowKind.Burst);
            Assert.Contains(usage.Details, d => d.QuotaBucketKind == WindowKind.Rolling);
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
            var parent = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "codex", StringComparison.Ordinal));
            Assert.DoesNotContain(usages, usage => string.Equals(usage.ProviderId, "codex.spark", StringComparison.Ordinal));
            Assert.NotNull(parent.Details);
            Assert.Contains(
                parent.Details!,
                detail => detail.DetailType == ProviderUsageDetailType.QuotaWindow &&
                          detail.Name.Contains("Spark", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TestTempPaths.CleanupPath(tempDir);
        }
    }

    [Fact]
    public async Task GetUsageAsync_SparkDetail_UsesSecondaryWindowUsage_WhenSparkPrimaryWindowHasResetAsync()
    {
        // When Spark 5h window resets (primaryUsed=0) but the shared weekly window is heavily
        // used (secondaryUsed=98), the Spark detail must reflect the binding constraint (98%).
        // Without this fix the Spark card shows "0% used" while the parent shows "98% used".
        var tempDir = TestTempPaths.CreateDirectory("codex-test-spark-secondary-constraint");
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
                            // Spark 5h window just reset — own quota is 0% used
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
            var parent = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "codex", StringComparison.Ordinal));
            Assert.NotNull(parent.Details);

            // Provider-level Spark detail (ModelSpecific kind, no ModelName) drives the parent card.
            // There may also be model-scoped Spark details (with ModelName set) for the child card.
            var sparkQwDetails = parent.Details!
                .Where(d => d.DetailType == ProviderUsageDetailType.QuotaWindow &&
                            d.Name.Contains("Spark", StringComparison.OrdinalIgnoreCase) &&
                            string.IsNullOrWhiteSpace(d.ModelName))
                .ToList();
            var sparkDetail = Assert.Single(sparkQwDetails);

            // Spark QuotaWindow detail must show 98% used (the secondary/weekly constraint),
            // not 0% (the Spark 5h window that just reset).
            var sparkQwUsed = UsageMath.GetEffectiveUsedPercent(sparkDetail);
            Assert.NotNull(sparkQwUsed);
            Assert.Equal(98, sparkQwUsed!.Value, precision: 0);

            // The Model detail drives the codex.spark child card.
            // It must also reflect the effective Spark constraint (98% weekly) so the child
            // card does not show the misleading "0% used" from the just-reset 5h window.
            var modelDetail = Assert.Single(
                parent.Details!,
                d => d.DetailType == ProviderUsageDetailType.Model);
            var modelUsed = UsageMath.GetEffectiveUsedPercent(modelDetail);
            Assert.NotNull(modelUsed);
            Assert.Equal(98, modelUsed!.Value, precision: 0);
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
            var parent = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "codex", StringComparison.Ordinal));
            Assert.DoesNotContain(usages, usage => string.Equals(usage.ProviderId, "codex.spark", StringComparison.Ordinal));
            Assert.NotNull(parent.Details);
            Assert.Contains(
                parent.Details!,
                detail => detail.DetailType == ProviderUsageDetailType.QuotaWindow &&
                          detail.Name.Contains("Spark", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                parent.Details!,
                detail => detail.DetailType == ProviderUsageDetailType.QuotaWindow &&
string.Equals(detail.Name, "5-hour quota", StringComparison.Ordinal));
            Assert.Contains(
                parent.Details!,
                detail => detail.DetailType == ProviderUsageDetailType.QuotaWindow &&
string.Equals(detail.Name, "Weekly quota", StringComparison.Ordinal));
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
            var parent = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "codex", StringComparison.Ordinal));
            Assert.NotNull(parent.Details);

            // Model detail must use the binding Spark constraint (75%) not just the 5h primary (40%).
            var modelDetail = Assert.Single(
                parent.Details!,
                d => d.DetailType == ProviderUsageDetailType.Model);
            var modelUsed = UsageMath.GetEffectiveUsedPercent(modelDetail);
            Assert.NotNull(modelUsed);
            Assert.Equal(75, modelUsed!.Value, precision: 0);

            // Provider-level Spark ModelSpecific detail must also reflect 75%.
            var sparkDetail = Assert.Single(
                parent.Details!,
                d => d.DetailType == ProviderUsageDetailType.QuotaWindow &&
                     d.QuotaBucketKind == WindowKind.ModelSpecific &&
                     string.IsNullOrWhiteSpace(d.ModelName));
            var sparkUsed = UsageMath.GetEffectiveUsedPercent(sparkDetail);
            Assert.NotNull(sparkUsed);
            Assert.Equal(75, sparkUsed!.Value, precision: 0);

            // Model-scoped Rolling detail must be present even though rate_limit.secondary_window
            // is absent — the weekly data comes from the Spark block's secondary_window.
            Assert.Contains(
                parent.Details!,
                d => d.DetailType == ProviderUsageDetailType.QuotaWindow &&
                     d.QuotaBucketKind == WindowKind.Rolling &&
                     string.Equals(d.ModelName, modelDetail.ModelName, StringComparison.Ordinal));
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
            var parent = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "codex", StringComparison.Ordinal));
            Assert.NotNull(parent.Details);

            // Model detail must be present (HasWindowData must be true even with no primary used_percent)
            var modelDetail = Assert.Single(
                parent.Details!,
                d => d.DetailType == ProviderUsageDetailType.Model);

            // The binding constraint is the weekly (19%) since the burst just reset (0%).
            var modelUsed = UsageMath.GetEffectiveUsedPercent(modelDetail);
            Assert.NotNull(modelUsed);
            Assert.Equal(19, modelUsed!.Value, precision: 0);

            // Model-scoped Burst detail must be present (burst window 100% remaining after reset)
            var sparkBurstDetail = Assert.Single(
                parent.Details!,
                d => d.DetailType == ProviderUsageDetailType.QuotaWindow &&
                     d.QuotaBucketKind == WindowKind.Burst &&
                     string.Equals(d.ModelName, modelDetail.ModelName, StringComparison.Ordinal));
            var burstRemaining = sparkBurstDetail.PercentageValue;
            Assert.NotNull(burstRemaining);
            Assert.Equal(100, burstRemaining!.Value, precision: 0);

            // Model-scoped Rolling detail must also be present for dual bar rendering
            Assert.Contains(
                parent.Details!,
                d => d.DetailType == ProviderUsageDetailType.QuotaWindow &&
                     d.QuotaBucketKind == WindowKind.Rolling &&
                     string.Equals(d.ModelName, modelDetail.ModelName, StringComparison.Ordinal));
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
            var parent = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "codex", StringComparison.Ordinal));
            Assert.NotNull(parent.Details);

            // The Model detail's ModelName must contain "spark" so the DerivedModelSelector
            // for codex.spark can match it.
            var modelDetail = Assert.Single(parent.Details!, d => d.DetailType == ProviderUsageDetailType.Model);
            Assert.Contains("spark", modelDetail.ModelName, StringComparison.OrdinalIgnoreCase);

            // All model-scoped QW details must share the same ModelName as the Model detail
            // so BuildModelsFromDetails scopes them to the same model.
            var modelScopedQwDetails = parent.Details!
                .Where(d => d.DetailType == ProviderUsageDetailType.QuotaWindow &&
                            !string.IsNullOrWhiteSpace(d.ModelName))
                .ToList();
            Assert.NotEmpty(modelScopedQwDetails);
            Assert.All(modelScopedQwDetails, d =>
                Assert.Equal(modelDetail.ModelName, d.ModelName, StringComparer.Ordinal));
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
            var parent = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "codex", StringComparison.Ordinal));
            Assert.NotNull(parent.Details);

            // Model detail must contain "spark" for DerivedModelSelector to match
            var modelDetail = Assert.Single(parent.Details!, d => d.DetailType == ProviderUsageDetailType.Model);
            Assert.Contains("spark", modelDetail.ModelName, StringComparison.OrdinalIgnoreCase);

            // effectiveUsedPercent = max(0, 0, 0, 100) = 100 (driven by spark weekly)
            Assert.Equal(100, parent.UsedPercent, precision: 0);

            // Model-scoped Burst detail: burst just reset → 0% used = 100% remaining
            var burstDetail = Assert.Single(
                parent.Details!,
                d => d.DetailType == ProviderUsageDetailType.QuotaWindow &&
                     d.QuotaBucketKind == WindowKind.Burst &&
                     string.Equals(d.ModelName, modelDetail.ModelName, StringComparison.Ordinal));
            Assert.NotNull(burstDetail.PercentageValue);
            Assert.Equal(100, burstDetail.PercentageValue!.Value, precision: 0); // 100% remaining
            Assert.Equal(PercentageValueSemantic.Remaining, burstDetail.PercentageSemantic);

            // Model-scoped Rolling detail: weekly fully consumed → 100% used = 0% remaining
            var rollingDetail = Assert.Single(
                parent.Details!,
                d => d.DetailType == ProviderUsageDetailType.QuotaWindow &&
                     d.QuotaBucketKind == WindowKind.Rolling &&
                     string.Equals(d.ModelName, modelDetail.ModelName, StringComparison.Ordinal));
            Assert.NotNull(rollingDetail.PercentageValue);
            Assert.Equal(0, rollingDetail.PercentageValue!.Value, precision: 0); // 0% remaining
            Assert.Equal(PercentageValueSemantic.Remaining, rollingDetail.PercentageSemantic);
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
            var parent = Assert.Single(usages, usage => string.Equals(usage.ProviderId, "codex", StringComparison.Ordinal));
            Assert.NotNull(parent.Details);

            // Parent card: driven by max across all windows → main secondary wins at 98%
            Assert.Equal(98, parent.UsedPercent, precision: 0);

            // Model detail must use Spark's own weekly (19% used = 81% remaining)
            var modelDetail = Assert.Single(parent.Details!, d => d.DetailType == ProviderUsageDetailType.Model);
            var modelUsed = UsageMath.GetEffectiveUsedPercent(modelDetail);
            Assert.NotNull(modelUsed);
            Assert.Equal(19, modelUsed!.Value, precision: 0); // Spark's own 19%, NOT main 98%

            // Provider-level Spark ModelSpecific detail must also reflect Spark's own weekly (19%)
            var sparkDetail = Assert.Single(
                parent.Details!,
                d => d.DetailType == ProviderUsageDetailType.QuotaWindow &&
                     d.QuotaBucketKind == WindowKind.ModelSpecific &&
                     string.IsNullOrWhiteSpace(d.ModelName));
            var sparkUsed = UsageMath.GetEffectiveUsedPercent(sparkDetail);
            Assert.NotNull(sparkUsed);
            Assert.Equal(19, sparkUsed!.Value, precision: 0); // Spark: 19% used

            // Model-scoped Burst: 0% used = 100% remaining (burst just reset)
            var burstDetail = Assert.Single(
                parent.Details!,
                d => d.DetailType == ProviderUsageDetailType.QuotaWindow &&
                     d.QuotaBucketKind == WindowKind.Burst &&
                     string.Equals(d.ModelName, modelDetail.ModelName, StringComparison.Ordinal));
            Assert.Equal(100, burstDetail.PercentageValue!.Value, precision: 0);

            // Model-scoped Rolling: Spark's own weekly → 81% remaining (19% used)
            var rollingDetail = Assert.Single(
                parent.Details!,
                d => d.DetailType == ProviderUsageDetailType.QuotaWindow &&
                     d.QuotaBucketKind == WindowKind.Rolling &&
                     string.Equals(d.ModelName, modelDetail.ModelName, StringComparison.Ordinal));
            Assert.Equal(81, rollingDetail.PercentageValue!.Value, precision: 0); // NOT 2% from main
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
