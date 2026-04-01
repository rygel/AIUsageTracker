// <copyright file="ProviderContractTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;

namespace AIUsageTracker.Tests.Core;

/// <summary>
/// Verifies the IProviderService contract obligations on ProviderBase helpers,
/// specifically the Phase 3 addition of optional HttpFailureContext attachment.
/// </summary>
public class ProviderContractTests
{
    private class TestProvider : ProviderBase
    {
        public override string ProviderId => "contract-test";

        public override ProviderDefinition Definition => new(
            providerId: "contract-test",
            displayName: "Contract Test Provider",
            planType: PlanType.Coding,
            isQuotaBased: false);

        public override Task<IEnumerable<ProviderUsage>> GetUsageAsync(
            ProviderConfig config,
            Action<ProviderUsage>? progressCallback = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ProviderUsage CallCreateUnavailableUsage(
            string description,
            int httpStatus = 0,
            HttpFailureContext? failureContext = null)
            => this.CreateUnavailableUsage(description, httpStatus, failureContext: failureContext);

        public ProviderUsage CallCreateUnavailableUsageWithIdentity(
            string description,
            string? accountName,
            int httpStatus = 0,
            HttpFailureContext? failureContext = null)
            => this.CreateUnavailableUsageWithIdentity(description, accountName, httpStatus, failureContext: failureContext);
    }

    private readonly TestProvider _provider = new();

    // --- FailureContext is null by default ---

    [Fact]
    public void CreateUnavailableUsage_FailureContextNullByDefault()
    {
        var usage = this._provider.CallCreateUnavailableUsage("Something failed", 500);
        Assert.Null(usage.FailureContext);
    }

    [Fact]
    public void CreateUnavailableUsageWithIdentity_FailureContextNullByDefault()
    {
        var usage = this._provider.CallCreateUnavailableUsageWithIdentity("Something failed", "user@example.com", 500);
        Assert.Null(usage.FailureContext);
    }

    // --- FailureContext is attached when supplied ---

    [Fact]
    public void CreateUnavailableUsage_AttachesFailureContext()
    {
        var context = new HttpFailureContext
        {
            Classification = HttpFailureClassification.RateLimit,
            HttpStatus = 429,
            IsLikelyTransient = true,
        };

        var usage = this._provider.CallCreateUnavailableUsage("Rate limited", 429, failureContext: context);

        Assert.NotNull(usage.FailureContext);
        Assert.Equal(HttpFailureClassification.RateLimit, usage.FailureContext!.Classification);
        Assert.Equal(429, usage.FailureContext.HttpStatus);
        Assert.True(usage.FailureContext.IsLikelyTransient);
    }

    [Fact]
    public void CreateUnavailableUsageWithIdentity_PropagatesFailureContext()
    {
        var context = new HttpFailureContext
        {
            Classification = HttpFailureClassification.Server,
            HttpStatus = 503,
            IsLikelyTransient = true,
        };

        var usage = this._provider.CallCreateUnavailableUsageWithIdentity(
            "Server error", "user@example.com", 503, failureContext: context);

        Assert.NotNull(usage.FailureContext);
        Assert.Equal(HttpFailureClassification.Server, usage.FailureContext!.Classification);
        Assert.Equal("user@example.com", usage.AccountName);
    }

    // --- FailureContext does not affect existing ProviderUsage fields ---

    [Fact]
    public void CreateUnavailableUsage_WithContext_ExistingFieldsUnchanged()
    {
        var context = new HttpFailureContext { Classification = HttpFailureClassification.Authentication, HttpStatus = 401 };

        var usage = this._provider.CallCreateUnavailableUsage("Auth failed", 401, failureContext: context);

        Assert.Equal("contract-test", usage.ProviderId);
        Assert.Equal("Contract Test Provider", usage.ProviderName);
        Assert.False(usage.IsAvailable);
        Assert.Equal("Auth failed", usage.Description);
        Assert.Equal(401, usage.HttpStatus);
        Assert.Equal(ProviderUsageState.Error, usage.State);
        Assert.Equal(0, usage.UsedPercent);
    }

    // --- FailureContext is not serialised ---

    [Fact]
    public void ProviderUsage_FailureContextIsJsonIgnored()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "test",
            FailureContext = new HttpFailureContext { Classification = HttpFailureClassification.Network },
        };

        var json = System.Text.Json.JsonSerializer.Serialize(usage);

        Assert.DoesNotContain("FailureContext", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failureContext", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProviderUsage_FailureContextNullAfterRoundTrip()
    {
        var original = new ProviderUsage
        {
            ProviderId = "test",
            FailureContext = new HttpFailureContext { Classification = HttpFailureClassification.Timeout },
        };

        var json = System.Text.Json.JsonSerializer.Serialize(original);
        var roundTripped = System.Text.Json.JsonSerializer.Deserialize<ProviderUsage>(json);

        Assert.Null(roundTripped?.FailureContext);
    }
}
