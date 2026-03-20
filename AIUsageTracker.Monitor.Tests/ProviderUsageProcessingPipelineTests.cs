// <copyright file="ProviderUsageProcessingPipelineTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AIUsageTracker.Monitor.Tests;

public class ProviderUsageProcessingPipelineTests
{
    private readonly ProviderUsageProcessingPipeline _pipeline;

    public ProviderUsageProcessingPipelineTests()
    {
        var logger = new Mock<ILogger<ProviderUsageProcessingPipeline>>();
        this._pipeline = new ProviderUsageProcessingPipeline(logger.Object);
    }

    [Fact]
    public void Process_WhenDetailContractInvalid_ConvertsUsageToUnavailableError()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "openai",
            ProviderName = "OpenAI",
            RequestsUsed = 10,
            RequestsAvailable = 100,
            UsedPercent = 10,
            IsAvailable = true,
            Details = new[]
            {
                new ProviderUsageDetail
                {
                    Name = string.Empty,
                    DetailType = ProviderUsageDetailType.Unknown,
                    QuotaBucketKind = WindowKind.None,
                },
            },
        };

        var result = this._pipeline.Process(
            new[] { usage },
            new[] { "openai" },
            isPrivacyMode: false);

        var processed = Assert.Single(result.Usages);
        Assert.False(processed.IsAvailable);
        Assert.True(processed.Description.Contains("Invalid detail contract:", StringComparison.Ordinal));
        Assert.Equal(UpstreamResponseValidity.Invalid, processed.UpstreamResponseValidity);
        Assert.Equal(1, result.DetailContractAdjustedCount);
    }

    [Fact]
    public void Process_WhenHttpStatusIsSuccess_MarksUpstreamResponseValid()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "openai",
            ProviderName = "OpenAI",
            IsAvailable = true,
            Description = "Connected",
            HttpStatus = 200,
        };

        var result = this._pipeline.Process(
            new[] { usage },
            new[] { "openai" },
            isPrivacyMode: false);

        var processed = Assert.Single(result.Usages);
        Assert.Equal(UpstreamResponseValidity.Valid, processed.UpstreamResponseValidity);
        Assert.Equal("HTTP 200", processed.UpstreamResponseNote);
    }

    [Fact]
    public void Process_WhenNoUpstreamMetadata_MarksResponseAsNotAttempted()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "antigravity",
            ProviderName = "Antigravity",
            IsAvailable = false,
            Description = "Application not running",
            HttpStatus = 0,
            RawJson = null,
        };

        var result = this._pipeline.Process(
            new[] { usage },
            new[] { "antigravity" },
            isPrivacyMode: false);

        var processed = Assert.Single(result.Usages);
        Assert.Equal(UpstreamResponseValidity.NotAttempted, processed.UpstreamResponseValidity);
    }

    [Fact]
    public void Process_WhenUnavailableWithDescription_KeepsUsage()
    {
        // Missing/Unavailable entries with a description are actionable
        // (e.g. "API Key missing", "auth token not found") and must reach
        // the DB so the UI can show them instead of stale cached data.
        var usage = new ProviderUsage
        {
            ProviderId = "openrouter",
            ProviderName = "OpenRouter",
            RequestsUsed = 0,
            RequestsAvailable = 0,
            UsedPercent = 0,
            IsAvailable = false,
            State = ProviderUsageState.Missing,
            Description = "API Key missing",
        };

        var result = this._pipeline.Process(
            new[] { usage },
            new[] { "openrouter" },
            isPrivacyMode: false);

        var kept = Assert.Single(result.Usages);
        Assert.Equal("API Key missing", kept.Description);
        Assert.Equal(0, result.PlaceholderFilteredCount);
    }

    [Fact]
    public void Process_WhenUnavailableWithNoDescription_DropsUsage()
    {
        // Truly empty entries (no description, no quota data) are placeholders
        // and should not pollute the DB or UI.
        var usage = new ProviderUsage
        {
            ProviderId = "openrouter",
            ProviderName = "OpenRouter",
            RequestsUsed = 0,
            RequestsAvailable = 0,
            UsedPercent = 0,
            IsAvailable = false,
            State = ProviderUsageState.Missing,
            Description = string.Empty,
        };

        var result = this._pipeline.Process(
            new[] { usage },
            new[] { "openrouter" },
            isPrivacyMode: false);

        Assert.Empty(result.Usages);
        Assert.Equal(1, result.PlaceholderFilteredCount);
    }

    [Fact]
    public void Process_WhenPrivacyModeEnabled_RedactsSensitiveFields()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "openrouter",
            ProviderName = "OpenRouter",
            RequestsUsed = 1,
            RequestsAvailable = 10,
            UsedPercent = 10,
            IsAvailable = true,
            AccountName = "user@example.com",
            ConfigKey = "config-key",
            RawJson = "{ \"secret\": \"value\" }",
        };

        var result = this._pipeline.Process(
            new[] { usage },
            new[] { "openrouter" },
            isPrivacyMode: true);

        var processed = Assert.Single(result.Usages);
        Assert.Equal(string.Empty, processed.AccountName);
        Assert.Equal(string.Empty, processed.ConfigKey);
        Assert.Null(processed.RawJson);
        Assert.Equal(1, result.PrivacyRedactedCount);
    }

    [Fact]
    public void Process_WhenPercentageInvalid_NormalizesFromUsageValues()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "zai",
            ProviderName = "Z.AI",
            RequestsUsed = 30,
            RequestsAvailable = 100,
            UsedPercent = double.NaN,
            IsQuotaBased = false,
            IsAvailable = true,
        };

        var result = this._pipeline.Process(
            new[] { usage },
            new[] { "zai" },
            isPrivacyMode: false);

        var processed = Assert.Single(result.Usages);
        Assert.Equal(30d, processed.UsedPercent);
        Assert.True(result.NormalizedCount >= 1);
    }

    [Fact]
    public void Process_WhenUsageIsDynamicChildOfActiveProvider_KeepsEntry()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "antigravity.claude-sonnet",
            ProviderName = "Claude Sonnet",
            RequestsUsed = 50,
            RequestsAvailable = 100,
            UsedPercent = 50,
            IsAvailable = true,
        };

        var result = this._pipeline.Process(
            new[] { usage },
            new[] { "antigravity" },
            isPrivacyMode: false);

        Assert.Single(result.Usages);
        Assert.Equal(0, result.InactiveProviderFilteredCount);
    }

    [Fact]
    public void Process_WhenUsageMatchesCanonicalProviderAlias_KeepsEntry()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "gemini-cli.hourly",
            ProviderName = "Gemini CLI (Hourly)",
            RequestsUsed = 20,
            RequestsAvailable = 100,
            UsedPercent = 80,
            IsAvailable = true,
        };

        var result = this._pipeline.Process(
            new[] { usage },
            new[] { "gemini" },
            isPrivacyMode: false);

        var accepted = Assert.Single(result.Usages);
        Assert.Equal("gemini-cli.hourly", accepted.ProviderId);
        Assert.Equal(0, result.InactiveProviderFilteredCount);
    }

    [Fact]
    public void Process_WhenFamilyMemberMissingAccountName_PropagatesCanonicalAccountIdentity()
    {
        var result = this._pipeline.Process(
            new[]
            {
                new ProviderUsage
                {
                    ProviderId = "gemini-cli.daily",
                    ProviderName = "Gemini CLI (Daily)",
                    RequestsUsed = 10,
                    RequestsAvailable = 100,
                    UsedPercent = 90,
                    IsAvailable = true,
                    AccountName = "alex@example.com",
                },
                new ProviderUsage
                {
                    ProviderId = "gemini-cli.hourly",
                    ProviderName = "Gemini CLI (Hourly)",
                    RequestsUsed = 20,
                    RequestsAvailable = 100,
                    UsedPercent = 80,
                    IsAvailable = true,
                    AccountName = string.Empty,
                },
            },
            new[] { "gemini" },
            isPrivacyMode: false);

        Assert.Equal(2, result.Usages.Count);
        Assert.All(result.Usages, usage => Assert.Equal("alex@example.com", usage.AccountName));
    }

    [Fact]
    public void Process_WhenUsageUsesUnsupportedDottedProviderId_FiltersInactiveEntry()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "openai.spark",
            ProviderName = "Unexpected Child",
            RequestsUsed = 20,
            RequestsAvailable = 100,
            UsedPercent = 20,
            IsAvailable = true,
        };

        var result = this._pipeline.Process(
            new[] { usage },
            new[] { "openai" },
            isPrivacyMode: false);

        Assert.Empty(result.Usages);
        Assert.Equal(1, result.InactiveProviderFilteredCount);
    }

    [Fact]
    public void Process_WhenProviderIdMissing_FiltersInvalidIdentity()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "   ",
            ProviderName = "Invalid",
            RequestsUsed = 10,
            RequestsAvailable = 100,
            UsedPercent = 10,
            IsAvailable = true,
        };

        var result = this._pipeline.Process(
            new[] { usage },
            new[] { "openai" },
            isPrivacyMode: false);

        Assert.Empty(result.Usages);
        Assert.Equal(1, result.InvalidIdentityCount);
    }

    [Fact]
    public void Process_WhenUsageNotInActiveProviderSet_FiltersInactiveEntry()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "not-active-provider",
            ProviderName = "Not Active",
            RequestsUsed = 1,
            RequestsAvailable = 10,
            UsedPercent = 10,
            IsAvailable = true,
        };

        var result = this._pipeline.Process(
            new[] { usage },
            new[] { "openai", "anthropic" },
            isPrivacyMode: false);

        Assert.Empty(result.Usages);
        Assert.Equal(1, result.InactiveProviderFilteredCount);
    }

    [Fact]
    public void Process_WhenUnavailableWithoutDescription_NormalizesDescriptionAndUtcTimestamp()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "openai",
            ProviderName = " OpenAI ",
            RequestsUsed = -5,
            RequestsAvailable = -10,
            UsedPercent = -20,
            IsAvailable = false,
            Description = " ",
            FetchedAt = default,
            ResponseLatencyMs = -2,
            HttpStatus = 999,
        };

        var result = this._pipeline.Process(
            new[] { usage },
            new[] { "openai" },
            isPrivacyMode: false);

        var processed = Assert.Single(result.Usages);
        Assert.Equal("OpenAI", processed.ProviderName);
        Assert.Equal(0, processed.RequestsUsed);
        Assert.Equal(0, processed.RequestsAvailable);
        Assert.Equal(0, processed.UsedPercent);
        Assert.Equal("Unavailable", processed.Description);
        Assert.Equal(0, processed.ResponseLatencyMs);
        Assert.Equal(0, processed.HttpStatus);
        Assert.NotEqual(default, processed.FetchedAt);
        Assert.Equal(DateTimeKind.Utc, processed.FetchedAt.Kind);
        Assert.True(result.NormalizedCount >= 1);
    }

    [Fact]
    public void Process_WhenNextResetMissing_InferNextResetFromDetails()
    {
        var futureReset = DateTime.UtcNow.AddHours(2);
        var usage = new ProviderUsage
        {
            ProviderId = "github-copilot",
            ProviderName = "GitHub Copilot",
            IsAvailable = false,
            NextResetTime = null,
            Details =
            [
                new ProviderUsageDetail
                {
                    Name = "Weekly Quota",
                    Description = "14% used",
                    DetailType = ProviderUsageDetailType.QuotaWindow,
                    QuotaBucketKind = WindowKind.Rolling,
                    NextResetTime = futureReset,
                },
            ],
        };

        var result = this._pipeline.Process(
            new[] { usage },
            new[] { "github-copilot" },
            isPrivacyMode: false);

        var processed = Assert.Single(result.Usages);
        Assert.NotNull(processed.NextResetTime);
        Assert.Equal(futureReset, processed.NextResetTime!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void GetSnapshot_BeforeProcess_ReturnsZeroedTelemetry()
    {
        var snapshot = this._pipeline.GetSnapshot();

        Assert.Equal(0, snapshot.TotalProcessedEntries);
        Assert.Equal(0, snapshot.TotalAcceptedEntries);
        Assert.Equal(0, snapshot.TotalRejectedEntries);
        Assert.Equal(0, snapshot.InvalidIdentityCount);
        Assert.Equal(0, snapshot.InactiveProviderFilteredCount);
        Assert.Equal(0, snapshot.PlaceholderFilteredCount);
        Assert.Equal(0, snapshot.DetailContractAdjustedCount);
        Assert.Equal(0, snapshot.NormalizedCount);
        Assert.Equal(0, snapshot.PrivacyRedactedCount);
        Assert.Null(snapshot.LastProcessedAtUtc);
        Assert.Equal(0, snapshot.LastRunTotalEntries);
        Assert.Equal(0, snapshot.LastRunAcceptedEntries);
    }

    [Fact]
    public void Process_MultipleRuns_AccumulatesTelemetrySnapshot()
    {
        _ = this._pipeline.Process(CreateFirstRunUsages(), ["openai"], isPrivacyMode: true);
        _ = this._pipeline.Process(CreateSecondRunUsages(), ["openai"], isPrivacyMode: false);

        var snapshot = this._pipeline.GetSnapshot();

        Assert.Equal(6, snapshot.TotalProcessedEntries);
        Assert.Equal(3, snapshot.TotalAcceptedEntries);
        Assert.Equal(3, snapshot.TotalRejectedEntries);
        Assert.Equal(1, snapshot.InvalidIdentityCount);
        Assert.Equal(1, snapshot.InactiveProviderFilteredCount);
        Assert.Equal(1, snapshot.PlaceholderFilteredCount);
        Assert.Equal(1, snapshot.DetailContractAdjustedCount);
        Assert.True(snapshot.NormalizedCount >= 1);
        Assert.Equal(1, snapshot.PrivacyRedactedCount);
        Assert.NotNull(snapshot.LastProcessedAtUtc);
        Assert.Equal(DateTimeKind.Utc, snapshot.LastProcessedAtUtc!.Value.Kind);
        Assert.Equal(1, snapshot.LastRunTotalEntries);
        Assert.Equal(1, snapshot.LastRunAcceptedEntries);
    }

    private static IReadOnlyList<ProviderUsage> CreateFirstRunUsages()
    {
        return
        [
            new ProviderUsage
            {
                ProviderId = " openai ",
                ProviderName = " OpenAI ",
                IsAvailable = true,
                RequestsUsed = double.NaN,
                RequestsAvailable = 100,
                UsedPercent = double.PositiveInfinity,
                RawJson = "{ \"key\": \"value\" }",
                AccountName = "user@example.com",
                ConfigKey = "cfg-openai",
                FetchedAt = default,
            },
            new ProviderUsage
            {
                ProviderId = string.Empty,
                ProviderName = "Invalid",
                IsAvailable = true,
            },
            new ProviderUsage
            {
                ProviderId = "anthropic",
                ProviderName = "Anthropic",
                IsAvailable = true,
            },
            new ProviderUsage
            {
                ProviderId = "openai",
                ProviderName = "OpenAI",
                IsAvailable = false,
                State = ProviderUsageState.Missing,
                Description = string.Empty,
                RequestsUsed = 0,
                RequestsAvailable = 0,
                UsedPercent = 0,
            },
            new ProviderUsage
            {
                ProviderId = "openai",
                ProviderName = "OpenAI",
                IsAvailable = true,
                Details =
                [
                    new ProviderUsageDetail
                    {
                        Name = string.Empty,
                        DetailType = ProviderUsageDetailType.Unknown,
                        QuotaBucketKind = WindowKind.None,
                    },
                ],
            },
        ];
    }

    private static IReadOnlyList<ProviderUsage> CreateSecondRunUsages()
    {
        return
        [
            new ProviderUsage
            {
                ProviderId = "openai",
                ProviderName = "OpenAI",
                IsAvailable = true,
                RequestsUsed = 5,
                RequestsAvailable = 100,
                UsedPercent = 5,
                FetchedAt = DateTime.UtcNow,
            },
        ];
    }

    // ── NormalizeDetails field preservation ────────────────────────────────────
    // These guard against regressions like the beta.39 bug where NormalizeDetails
    // dropped PercentageValue/PercentageSemantic, causing TryGetPresentation to
    // return false for providers that used typed percentage fields (e.g. Codex).

    [Fact]
    public void Process_NormalizeDetails_PreservesTypedPercentageValue()
    {
        // Arrange: detail with typed percentage only (no legacy Used string),
        // which is exactly how CodexProvider and ClaudeCodeProvider emit details.
        var detail = new ProviderUsageDetail
        {
            Name = "Weekly quota",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Rolling,
        };
        detail.SetPercentageValue(35.0, PercentageValueSemantic.Remaining);

        var usage = new ProviderUsage
        {
            ProviderId = "codex",
            ProviderName = "Codex",
            IsAvailable = true,
            UsedPercent = 65,
            IsQuotaBased = true,
            Details = new[] { detail },
        };

        var result = this._pipeline.Process(
            new[] { usage },
            new[] { "codex" },
            isPrivacyMode: false);

        var processed = Assert.Single(result.Usages);
        var processedDetail = Assert.Single(processed.Details!);
        Assert.True(processedDetail.TryGetPercentageValue(out var pct, out var semantic, out _));
        Assert.Equal(35.0, pct, precision: 5);
        Assert.Equal(PercentageValueSemantic.Remaining, semantic);
    }

    [Fact]
    public void Process_NormalizeDetails_PreservesNextResetTimeOnDetails()
    {
        var localTime = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Local);
        var detail = new ProviderUsageDetail
        {
            Name = "5-hour quota",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Burst,
            NextResetTime = localTime,
        };
        detail.SetPercentageValue(80.0, PercentageValueSemantic.Remaining);

        var usage = new ProviderUsage
        {
            ProviderId = "codex",
            IsAvailable = true,
            IsQuotaBased = true,
            UsedPercent = 80,
            Details = new[] { detail },
        };

        var result = this._pipeline.Process(
            new[] { usage },
            new[] { "codex" },
            isPrivacyMode: false);

        var processedDetail = Assert.Single(Assert.Single(result.Usages).Details!);
        Assert.NotNull(processedDetail.NextResetTime);
        Assert.Equal(
            localTime.ToUniversalTime(),
            processedDetail.NextResetTime!.Value,
            precision: TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Process_NormalizeDetails_PreservesQuotaBucketKind()
    {
        var detail = new ProviderUsageDetail
        {
            Name = "Weekly quota",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Rolling,
        };
        detail.SetPercentageValue(60.0, PercentageValueSemantic.Remaining);

        var usage = new ProviderUsage
        {
            ProviderId = "codex",
            IsAvailable = true,
            IsQuotaBased = true,
            UsedPercent = 60,
            Details = new[] { detail },
        };

        var result = this._pipeline.Process(
            new[] { usage },
            new[] { "codex" },
            isPrivacyMode: false);

        var processedDetail = Assert.Single(Assert.Single(result.Usages).Details!);
        Assert.Equal(WindowKind.Rolling, processedDetail.QuotaBucketKind);
    }

    [Fact]
    public void Process_NormalizeDetails_PreservesIsStale()
    {
        var detail = new ProviderUsageDetail
        {
            Name = "5-hour quota",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            QuotaBucketKind = WindowKind.Burst,
            IsStale = true,
        };
        detail.SetPercentageValue(50.0, PercentageValueSemantic.Remaining);

        var usage = new ProviderUsage
        {
            ProviderId = "codex",
            IsAvailable = true,
            IsQuotaBased = true,
            UsedPercent = 50,
            Details = new[] { detail },
        };

        var result = this._pipeline.Process(
            new[] { usage },
            new[] { "codex" },
            isPrivacyMode: false);

        var processedDetail = Assert.Single(Assert.Single(result.Usages).Details!);
        Assert.True(processedDetail.IsStale);
    }
}
