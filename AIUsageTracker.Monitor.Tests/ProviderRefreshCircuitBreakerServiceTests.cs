// <copyright file="ProviderRefreshCircuitBreakerServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace AIUsageTracker.Monitor.Tests;

public class ProviderRefreshCircuitBreakerServiceTests
{
    private readonly ProviderRefreshCircuitBreakerService _service;

    public ProviderRefreshCircuitBreakerServiceTests()
    {
        var logger = new Mock<ILogger<ProviderRefreshCircuitBreakerService>>();
        this._service = new ProviderRefreshCircuitBreakerService(logger.Object);
    }

    [Fact]
    public void GetRefreshableConfigs_ReturnsAllProviders_WhenNoFailuresRecorded()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "openai" },
            new() { ProviderId = "anthropic" },
        };

        var result = this._service.GetRefreshableConfigs(configs, forceAll: false);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, config => string.Equals(config.ProviderId, "openai", StringComparison.Ordinal));
        Assert.Contains(result, config => string.Equals(config.ProviderId, "anthropic", StringComparison.Ordinal));
    }

    [Fact]
    public void UpdateProviderFailureStates_OpensCircuitAfterThreeFailures()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "openai" },
        };

        for (var attempt = 0; attempt < 3; attempt++)
        {
            this._service.UpdateProviderFailureStates(configs, Array.Empty<ProviderUsage>());
        }

        var result = this._service.GetRefreshableConfigs(configs, forceAll: false);

        Assert.Empty(result);
    }

    [Fact]
    public void UpdateProviderFailureStates_ResetsCircuitAfterSuccessfulUsage()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "openai" },
        };

        for (var attempt = 0; attempt < 3; attempt++)
        {
            this._service.UpdateProviderFailureStates(configs, Array.Empty<ProviderUsage>());
        }

        this._service.UpdateProviderFailureStates(
            configs,
            new[]
            {
                new ProviderUsage
                {
                    ProviderId = "openai",
                    IsAvailable = true,
                    HttpStatus = 200,
                },
            });

        var result = this._service.GetRefreshableConfigs(configs, forceAll: false);

        Assert.Single(result);
        Assert.Equal("openai", result[0].ProviderId);
    }

    [Fact]
    public void UpdateProviderFailureStates_DoesNotResetCircuitFromUnsupportedDottedProviderId()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "openai" },
        };

        for (var attempt = 0; attempt < 3; attempt++)
        {
            this._service.UpdateProviderFailureStates(configs, Array.Empty<ProviderUsage>());
        }

        this._service.UpdateProviderFailureStates(
            configs,
            new[]
            {
                new ProviderUsage
                {
                    ProviderId = "openai.spark",
                    IsAvailable = true,
                    HttpStatus = 200,
                },
            });

        var result = this._service.GetRefreshableConfigs(configs, forceAll: false);

        Assert.Empty(result);
        var diagnostic = Assert.Single(this._service.GetProviderDiagnostics());
        Assert.Equal("openai", diagnostic.ProviderId);
        Assert.True(diagnostic.IsCircuitOpen);
    }

    [Fact]
    public void GetRefreshableConfigs_ForceAllBypassesOpenCircuit()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "openai" },
        };

        for (var attempt = 0; attempt < 3; attempt++)
        {
            this._service.UpdateProviderFailureStates(configs, Array.Empty<ProviderUsage>());
        }

        var result = this._service.GetRefreshableConfigs(configs, forceAll: true);

        Assert.Single(result);
        Assert.Equal("openai", result[0].ProviderId);
    }

    [Fact]
    public void GetProviderDiagnostics_ReturnsFailureStateDetails()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "openai" },
        };

        for (var attempt = 0; attempt < 3; attempt++)
        {
            this._service.UpdateProviderFailureStates(configs, Array.Empty<ProviderUsage>());
        }

        var diagnostics = this._service.GetProviderDiagnostics();

        var openAi = Assert.Single(diagnostics);
        Assert.Equal("openai", openAi.ProviderId);
        Assert.Equal(3, openAi.ConsecutiveFailures);
        Assert.Equal("No usage data returned", openAi.LastRefreshError);
        Assert.NotNull(openAi.LastRefreshAttemptUtc);
        Assert.Null(openAi.LastSuccessfulRefreshUtc);
        Assert.True(openAi.IsCircuitOpen);
        Assert.NotNull(openAi.CircuitOpenUntilUtc);
    }

    [Fact]
    public void GetProviderDiagnostics_TracksSuccessfulRefreshAfterFailure()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "openai" },
        };

        this._service.UpdateProviderFailureStates(configs, Array.Empty<ProviderUsage>());
        this._service.UpdateProviderFailureStates(
            configs,
            new[]
            {
                new ProviderUsage
                {
                    ProviderId = "openai",
                    IsAvailable = true,
                    HttpStatus = 200,
                },
            });

        var diagnostics = this._service.GetProviderDiagnostics();

        var openAi = Assert.Single(diagnostics);
        Assert.Equal("openai", openAi.ProviderId);
        Assert.Equal(0, openAi.ConsecutiveFailures);
        Assert.Null(openAi.LastRefreshError);
        Assert.False(openAi.IsCircuitOpen);
        Assert.Null(openAi.CircuitOpenUntilUtc);
        Assert.NotNull(openAi.LastRefreshAttemptUtc);
        Assert.NotNull(openAi.LastSuccessfulRefreshUtc);
    }

    [Fact]
    public void ResetProvider_ClearsCircuitStateImmediately()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "gemini-cli" },
        };

        for (var attempt = 0; attempt < 3; attempt++)
        {
            this._service.UpdateProviderFailureStates(configs, Array.Empty<ProviderUsage>());
        }

        Assert.Empty(this._service.GetRefreshableConfigs(configs, forceAll: false));

        this._service.ResetProvider("gemini-cli", "config update");

        var refreshable = this._service.GetRefreshableConfigs(configs, forceAll: false);
        Assert.Single(refreshable);
        Assert.Equal("gemini-cli", refreshable[0].ProviderId);

        var diagnostic = Assert.Single(this._service.GetProviderDiagnostics());
        Assert.Equal(0, diagnostic.ConsecutiveFailures);
        Assert.False(diagnostic.IsCircuitOpen);
        Assert.Null(diagnostic.LastRefreshError);
    }

    // ── CreateCircuitOpenUsages ───────────────────────────────────────────────
    [Fact]
    public void CreateCircuitOpenUsages_WhenCircuitOpen_ReturnsSyntheticEntry()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "openrouter" },
        };

        // Trip the circuit.
        for (var i = 0; i < 3; i++)
        {
            this._service.UpdateProviderFailureStates(configs, Array.Empty<ProviderUsage>());
        }

        var usages = this._service.CreateCircuitOpenUsages(configs);

        var entry = Assert.Single(usages);
        Assert.Equal("openrouter", entry.ProviderId);
        Assert.False(entry.IsAvailable);
        Assert.Equal(ProviderUsageState.Unavailable, entry.State);
        Assert.Contains("Temporarily paused", entry.Description, StringComparison.Ordinal);
        Assert.Contains("next check at", entry.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateCircuitOpenUsages_WhenCircuitClosed_ReturnsEmpty()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "openrouter" },
        };

        // Only 2 failures — circuit stays closed.
        for (var i = 0; i < 2; i++)
        {
            this._service.UpdateProviderFailureStates(configs, Array.Empty<ProviderUsage>());
        }

        var usages = this._service.CreateCircuitOpenUsages(configs);

        Assert.Empty(usages);
    }

    [Fact]
    public void CreateCircuitOpenUsages_WhenLastErrorPresent_IncludesItInDescription()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "mistral" },
        };

        var failedUsage = new ProviderUsage
        {
            ProviderId = "mistral",
            IsAvailable = false,
            Description = "HTTP 503 Service Unavailable",
        };

        for (var i = 0; i < 3; i++)
        {
            this._service.UpdateProviderFailureStates(configs, new[] { failedUsage });
        }

        var usages = this._service.CreateCircuitOpenUsages(configs);

        var entry = Assert.Single(usages);
        Assert.Contains("HTTP 503 Service Unavailable", entry.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateCircuitOpenUsages_AfterCircuitReset_ReturnsEmpty()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "openrouter" },
        };

        for (var i = 0; i < 3; i++)
        {
            this._service.UpdateProviderFailureStates(configs, Array.Empty<ProviderUsage>());
        }

        this._service.ResetProvider("openrouter", "manual reset");

        var usages = this._service.CreateCircuitOpenUsages(configs);
        Assert.Empty(usages);
    }

    // ── Phase 5: classification-aware backoff ─────────────────────────────────

    [Fact]
    public void UpdateProviderFailureStates_RateLimitWithRetryAfter_UsesRetryAfterAsBackoff()
    {
        var configs = new List<ProviderConfig> { new() { ProviderId = "openai" } };
        var retryAfter = TimeSpan.FromMinutes(5);

        var rateLimitUsage = new ProviderUsage
        {
            ProviderId = "openai",
            IsAvailable = false,
            HttpStatus = 429,
            State = ProviderUsageState.Error,
            Description = "Rate limited",
            FailureContext = new HttpFailureContext
            {
                Classification = HttpFailureClassification.RateLimit,
                HttpStatus = 429,
                IsLikelyTransient = true,
                RetryAfter = retryAfter,
            },
        };

        for (var i = 0; i < 3; i++)
        {
            this._service.UpdateProviderFailureStates(configs, new[] { rateLimitUsage });
        }

        var diagnostic = Assert.Single(this._service.GetProviderDiagnostics());
        Assert.True(diagnostic.IsCircuitOpen);

        // Backoff should be ~5 min (the RetryAfter), not the default exponential 1 min
        var remaining = diagnostic.CircuitOpenUntilUtc!.Value - DateTime.UtcNow;
        Assert.True(remaining > TimeSpan.FromMinutes(4), $"Expected >4 min backoff, got {remaining.TotalMinutes:F1} min");
        Assert.True(remaining <= TimeSpan.FromMinutes(5.1), $"Expected ≤5 min backoff, got {remaining.TotalMinutes:F1} min");
    }

    [Fact]
    public void UpdateProviderFailureStates_RateLimitWithoutRetryAfter_UsesBaseBackoffNotExponential()
    {
        var configs = new List<ProviderConfig> { new() { ProviderId = "openai" } };

        var rateLimitUsage = new ProviderUsage
        {
            ProviderId = "openai",
            IsAvailable = false,
            HttpStatus = 429,
            State = ProviderUsageState.Error,
            FailureContext = new HttpFailureContext
            {
                Classification = HttpFailureClassification.RateLimit,
                HttpStatus = 429,
                IsLikelyTransient = true,
                RetryAfter = null,
            },
        };

        // Trip with 5 failures — without rate-limit awareness this would be 4-min exponential backoff
        for (var i = 0; i < 5; i++)
        {
            this._service.UpdateProviderFailureStates(configs, new[] { rateLimitUsage });
        }

        var diagnostic = Assert.Single(this._service.GetProviderDiagnostics());
        Assert.True(diagnostic.IsCircuitOpen);

        // Backoff should be base (1 min), NOT the exponential 4 min that 5 failures would normally produce
        var remaining = diagnostic.CircuitOpenUntilUtc!.Value - DateTime.UtcNow;
        Assert.True(remaining <= TimeSpan.FromMinutes(1.1), $"Expected ≤1 min base backoff, got {remaining.TotalMinutes:F1} min");
    }

    [Fact]
    public void UpdateProviderFailureStates_NoFailureContext_UsesExistingExponentialBackoff()
    {
        var configs = new List<ProviderConfig> { new() { ProviderId = "openai" } };

        var failedUsage = new ProviderUsage
        {
            ProviderId = "openai",
            IsAvailable = false,
            HttpStatus = 500,
            State = ProviderUsageState.Error,
            FailureContext = null, // no context — backward compat path
        };

        // At 5 failures, exponential backoff = base * 2^(5-3) = 1 min * 4 = 4 min
        for (var i = 0; i < 5; i++)
        {
            this._service.UpdateProviderFailureStates(configs, new[] { failedUsage });
        }

        var diagnostic = Assert.Single(this._service.GetProviderDiagnostics());
        Assert.True(diagnostic.IsCircuitOpen);

        var remaining = diagnostic.CircuitOpenUntilUtc!.Value - DateTime.UtcNow;
        Assert.True(remaining > TimeSpan.FromMinutes(3.5), $"Expected >3.5 min exponential backoff, got {remaining.TotalMinutes:F1} min");
    }

    [Fact]
    public void GetProviderDiagnostics_ExposesLastFailureClassification_WhenContextAttached()
    {
        var configs = new List<ProviderConfig> { new() { ProviderId = "openai" } };

        var rateLimitUsage = new ProviderUsage
        {
            ProviderId = "openai",
            IsAvailable = false,
            HttpStatus = 429,
            State = ProviderUsageState.Error,
            FailureContext = new HttpFailureContext
            {
                Classification = HttpFailureClassification.RateLimit,
                HttpStatus = 429,
            },
        };

        this._service.UpdateProviderFailureStates(configs, new[] { rateLimitUsage });

        var diagnostic = Assert.Single(this._service.GetProviderDiagnostics());
        Assert.Equal(HttpFailureClassification.RateLimit, diagnostic.LastFailureClassification);
    }

    [Fact]
    public void GetProviderDiagnostics_LastFailureClassificationIsNull_WhenNoContextAttached()
    {
        var configs = new List<ProviderConfig> { new() { ProviderId = "openai" } };

        this._service.UpdateProviderFailureStates(configs, Array.Empty<ProviderUsage>());

        var diagnostic = Assert.Single(this._service.GetProviderDiagnostics());
        Assert.Null(diagnostic.LastFailureClassification);
    }

    [Fact]
    public void GetProviderDiagnostics_LastFailureClassificationClearsOnSuccess()
    {
        var configs = new List<ProviderConfig> { new() { ProviderId = "openai" } };

        var failedUsage = new ProviderUsage
        {
            ProviderId = "openai",
            IsAvailable = false,
            HttpStatus = 500,
            State = ProviderUsageState.Error,
            FailureContext = new HttpFailureContext { Classification = HttpFailureClassification.Server },
        };

        this._service.UpdateProviderFailureStates(configs, new[] { failedUsage });

        var successUsage = new ProviderUsage
        {
            ProviderId = "openai",
            IsAvailable = true,
            HttpStatus = 200,
        };

        this._service.UpdateProviderFailureStates(configs, new[] { successUsage });

        var diagnostic = Assert.Single(this._service.GetProviderDiagnostics());
        Assert.False(diagnostic.IsCircuitOpen);
        // Classification is from previous failure state; null after reset
        Assert.Null(diagnostic.LastFailureClassification);
    }
}
