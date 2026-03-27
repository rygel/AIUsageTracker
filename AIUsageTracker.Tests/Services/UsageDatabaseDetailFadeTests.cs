// <copyright file="UsageDatabaseDetailFadeTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;
using AIUsageTracker.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageTracker.Tests.Services;

public sealed class UsageDatabaseDetailFadeTests : IDisposable
{
    private readonly string _dbPath;

    public UsageDatabaseDetailFadeTests()
    {
        this._dbPath = TestTempPaths.CreateFilePath("usage-db-fade-tests", "usage.db");
    }

    [Fact]
    public async Task GetLatestHistoryAsync_IncludesMissingDetail_WhenSeenWithinSevenDaysAsync()
    {
        var database = await this.CreateDatabaseAsync();
        var providerId = "antigravity";

        await database.StoreHistoryAsync(new[]
        {
            CreateUsage(
                providerId,
                fetchedAtUtc: DateTime.UtcNow.AddDays(-2),
                new ProviderUsageDetail
                {
                    Name = "GPT OSS",
                    DetailType = ProviderUsageDetailType.Model,
                    Description = "exhausted",
                }),
        });

        await database.StoreHistoryAsync(new[]
        {
            CreateUsage(
                providerId,
                fetchedAtUtc: DateTime.UtcNow,
                new ProviderUsageDetail
                {
                    Name = "Gemini 3 Flash",
                    DetailType = ProviderUsageDetailType.Model,
                    Description = "20% used",
                }),
        });

        var latest = await database.GetLatestHistoryAsync();
        var antigravity = Assert.Single(latest.Where(x => string.Equals(x.ProviderId, providerId, StringComparison.Ordinal)));
        Assert.NotNull(antigravity.Details);

        var names = antigravity.Details!.Select(d => d.Name).ToList();
        Assert.Contains("Gemini 3 Flash", names);
        Assert.Contains("GPT OSS", names);

        var restored = antigravity.Details.First(d => string.Equals(d.Name, "GPT OSS", StringComparison.Ordinal));
        Assert.Contains("stale; last seen", restored.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetLatestHistoryAsync_DoesNotIncludeMissingDetail_WhenOlderThanSevenDaysAsync()
    {
        var database = await this.CreateDatabaseAsync();
        var providerId = "antigravity";

        await database.StoreHistoryAsync(new[]
        {
            CreateUsage(
                providerId,
                fetchedAtUtc: DateTime.UtcNow.AddDays(-8),
                new ProviderUsageDetail
                {
                    Name = "GPT OSS",
                    DetailType = ProviderUsageDetailType.Model,
                    Description = "exhausted",
                }),
        });

        await database.StoreHistoryAsync(new[]
        {
            CreateUsage(
                providerId,
                fetchedAtUtc: DateTime.UtcNow,
                new ProviderUsageDetail
                {
                    Name = "Gemini 3 Flash",
                    DetailType = ProviderUsageDetailType.Model,
                    Description = "20% used",
                }),
        });

        var latest = await database.GetLatestHistoryAsync();
        var antigravity = Assert.Single(latest.Where(x => string.Equals(x.ProviderId, providerId, StringComparison.Ordinal)));
        Assert.NotNull(antigravity.Details);

        var names = antigravity.Details!.Select(d => d.Name).ToList();
        Assert.Contains("Gemini 3 Flash", names);
        Assert.DoesNotContain("GPT OSS", names);
    }

    [Fact]
    public async Task GetLatestHistoryAsync_DoesNotMergeStaleQuotaWindowDetails_IntoCurrentModelDetailsAsync()
    {
        var database = await this.CreateDatabaseAsync();
        var providerId = "gemini-cli";

        await database.StoreHistoryAsync(new[]
        {
            new ProviderUsage
            {
                ProviderId = providerId,
                ProviderName = "Google Gemini",
                RequestsUsed = 10,
                RequestsAvailable = 100,
                UsedPercent = 90,
                IsAvailable = true,
                Description = "ok",
                FetchedAt = DateTime.UtcNow.AddDays(-1),
                Details = new List<ProviderUsageDetail>
                {
                    new()
                    {
                        Name = "Quota Bucket (Primary)",
                        DetailType = ProviderUsageDetailType.QuotaWindow,
                        QuotaBucketKind = WindowKind.Burst,
                        Description = "65.0% used",
                    },
                },
            },
        });

        await database.StoreHistoryAsync(new[]
        {
            new ProviderUsage
            {
                ProviderId = providerId,
                ProviderName = "Google Gemini",
                RequestsUsed = 35,
                RequestsAvailable = 100,
                UsedPercent = 65,
                IsAvailable = true,
                Description = "ok",
                FetchedAt = DateTime.UtcNow,
                Details = new List<ProviderUsageDetail>
                {
                    new()
                    {
                        Name = "Gemini 3 Flash Preview",
                        ModelName = "gemini-3-flash-preview",
                        DetailType = ProviderUsageDetailType.Model,
                        QuotaBucketKind = WindowKind.None,
                        Description = "65.0% used",
                    },
                },
            },
        });

        var latest = await database.GetLatestHistoryAsync();
        var gemini = Assert.Single(latest, x => string.Equals(x.ProviderId, providerId, StringComparison.Ordinal));
        Assert.NotNull(gemini.Details);
        Assert.Contains(gemini.Details!, detail => detail.DetailType == ProviderUsageDetailType.Model);
        Assert.DoesNotContain(gemini.Details!, detail => detail.DetailType == ProviderUsageDetailType.QuotaWindow);
    }

    [Fact]
    public async Task StoreHistoryAsync_PersistsUnavailableUsage_WithNonPlaceholderDescriptionAndAccountNameAsync()
    {
        var database = await this.CreateDatabaseAsync();
        var providerId = "github-copilot";

        await database.StoreHistoryAsync(new[]
        {
            new ProviderUsage
            {
                ProviderId = providerId,
                ProviderName = "GitHub Copilot",
                RequestsUsed = 0,
                RequestsAvailable = 0,
                UsedPercent = 0,
                IsAvailable = false,
                Description = "Not authenticated. Please login in Settings.",
                AccountName = "rygel",
                FetchedAt = DateTime.UtcNow,
            },
        });

        var latest = await database.GetLatestHistoryAsync();
        var copilot = Assert.Single(latest.Where(x => string.Equals(x.ProviderId, providerId, StringComparison.Ordinal)));
        Assert.False(copilot.IsAvailable);
        Assert.Equal("rygel", copilot.AccountName);
        Assert.Contains("Not authenticated", copilot.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("github-copilot", "octocat")]
    [InlineData("antigravity", "user@example.com")]
    public async Task StoreProviderAsync_DoesNotClearExistingAccountNameAsync(
        string providerId,
        string accountName)
    {
        var database = await this.CreateDatabaseAsync();

        await database.StoreHistoryAsync(new[]
        {
            new ProviderUsage
            {
                ProviderId = providerId,
                ProviderName = providerId,
                RequestsUsed = 10,
                RequestsAvailable = 100,
                UsedPercent = 90,
                IsAvailable = true,
                Description = "ok",
                AccountName = accountName,
                FetchedAt = DateTime.UtcNow,
            },
        });

        await database.StoreProviderAsync(new ProviderConfig
        {
            ProviderId = providerId,
            AuthSource = "config",
        });

        var latest = await database.GetLatestHistoryAsync();
        var usage = Assert.Single(latest, x => string.Equals(x.ProviderId, providerId, StringComparison.Ordinal));
        Assert.Equal(accountName, usage.AccountName);
    }

    [Fact]
    public async Task StoreHistoryAsync_PersistsPlaceholderUnavailableUsage_WhenPassedByCallerAsync()
    {
        var database = await this.CreateDatabaseAsync();

        await database.StoreHistoryAsync(new[]
        {
            new ProviderUsage
            {
                ProviderId = "github-copilot",
                ProviderName = "GitHub Copilot",
                RequestsUsed = 0,
                RequestsAvailable = 0,
                UsedPercent = 0,
                IsAvailable = false,
                Description = "API Key missing",
                FetchedAt = DateTime.UtcNow,
            },
        });

        var latest = await database.GetLatestHistoryAsync();
        var copilot = Assert.Single(latest, x => string.Equals(x.ProviderId, "github-copilot", StringComparison.Ordinal));
        Assert.False(copilot.IsAvailable);
        Assert.Contains("API Key missing", copilot.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StoreHistoryAsync_PersistsUnknownProviderIds_WhenPassedByPipelineAsync()
    {
        var database = await this.CreateDatabaseAsync();

        await database.StoreHistoryAsync(new[]
        {
            new ProviderUsage
            {
                ProviderId = "custom-provider.experimental-model",
                ProviderName = "Custom Provider Experimental",
                RequestsUsed = 5,
                RequestsAvailable = 100,
                UsedPercent = 95,
                IsAvailable = true,
                Description = "Connected",
                FetchedAt = DateTime.UtcNow,
            },
        });

        var latest = await database.GetLatestHistoryAsync();
        var usage = Assert.Single(
            latest,
            item => string.Equals(item.ProviderId, "custom-provider.experimental-model", StringComparison.Ordinal));
        Assert.Equal("Custom Provider Experimental", usage.ProviderName);
    }

    [Fact]
    public async Task GetLatestHistoryAsync_ComputesUpstreamResponseValidity_FromHttpStatusAsync()
    {
        var database = await this.CreateDatabaseAsync();

        await database.StoreHistoryAsync(new[]
        {
            new ProviderUsage
            {
                ProviderId = "codex",
                ProviderName = "OpenAI Codex",
                RequestsUsed = 1,
                RequestsAvailable = 10,
                UsedPercent = 10,
                IsAvailable = true,
                Description = "Connected",
                HttpStatus = 200,
                FetchedAt = DateTime.UtcNow,
            },
        });

        var latest = await database.GetLatestHistoryAsync();
        var codex = Assert.Single(latest, x => string.Equals(x.ProviderId, "codex", StringComparison.Ordinal));
        Assert.Equal(UpstreamResponseValidity.Valid, codex.UpstreamResponseValidity);
        Assert.Equal("HTTP 200", codex.UpstreamResponseNote);
    }

    [Fact]
    public async Task GetLatestHistoryAsync_RestoresTopLevelResetTime_FromStaleDetailWhenCurrentIsMissingAsync()
    {
        var database = await this.CreateDatabaseAsync();
        var providerId = "github-copilot";
        var weeklyReset = DateTime.UtcNow.AddDays(3);

        await database.StoreHistoryAsync(new[]
        {
            new ProviderUsage
            {
                ProviderId = providerId,
                ProviderName = "GitHub Copilot",
                IsAvailable = true,
                Description = "Authenticated",
                NextResetTime = weeklyReset,
                FetchedAt = DateTime.UtcNow.AddHours(-2),
                Details = new List<ProviderUsageDetail>
                {
                    new()
                    {
                        Name = "Weekly Quota",
                        Description = "14% used",
                        DetailType = ProviderUsageDetailType.QuotaWindow,
                        QuotaBucketKind = WindowKind.Rolling,
                        NextResetTime = weeklyReset,
                    },
                },
            },
        });

        await database.StoreHistoryAsync(new[]
        {
            new ProviderUsage
            {
                ProviderId = providerId,
                ProviderName = "GitHub Copilot",
                IsAvailable = false,
                Description = "Not authenticated. Please login in Settings.",
                NextResetTime = null,
                FetchedAt = DateTime.UtcNow,
                Details = null,
            },
        });

        var latest = await database.GetLatestHistoryAsync();
        var copilot = Assert.Single(latest, x => string.Equals(x.ProviderId, providerId, StringComparison.Ordinal));
        Assert.NotNull(copilot.Details);
        Assert.NotNull(copilot.NextResetTime);
        Assert.Equal(weeklyReset.ToUniversalTime(), copilot.NextResetTime!.Value.ToUniversalTime(), TimeSpan.FromSeconds(1));
    }

    public void Dispose()
    {
        TestTempPaths.CleanupPath(this._dbPath);
    }

    private async Task<UsageDatabase> CreateDatabaseAsync()
    {
        var database = new UsageDatabase(NullLogger<UsageDatabase>.Instance, new TestDbPathProvider(this._dbPath));
        await database.InitializeAsync().ConfigureAwait(false);
        return database;
    }

    private static ProviderUsage CreateUsage(string providerId, DateTime fetchedAtUtc, ProviderUsageDetail detail)
    {
        return new ProviderUsage
        {
            ProviderId = providerId,
            ProviderName = "Google Antigravity",
            RequestsUsed = 10,
            RequestsAvailable = 100,
            UsedPercent = 90,
            IsAvailable = true,
            Description = "ok",
            FetchedAt = fetchedAtUtc,
            Details = new List<ProviderUsageDetail> { detail },
        };
    }

    private sealed class TestDbPathProvider : IAppPathProvider
    {
        private readonly string _dbPath;

        public TestDbPathProvider(string dbPath)
        {
            this._dbPath = dbPath;
        }

        public string GetAppDataRoot() => Path.GetDirectoryName(this._dbPath)!;

        public string GetDatabasePath() => this._dbPath;

        public string GetLogDirectory() => Path.Combine(this.GetAppDataRoot(), "logs");

        public string GetAuthFilePath() => Path.Combine(this.GetAppDataRoot(), "auth.json");

        public string GetPreferencesFilePath() => Path.Combine(this.GetAppDataRoot(), "preferences.json");

        public string GetProviderConfigFilePath() => Path.Combine(this.GetAppDataRoot(), "providers.json");

        public string GetUserProfileRoot() => this.GetAppDataRoot();

        public string GetMonitorInfoFilePath() => Path.Combine(this.GetAppDataRoot(), "monitor.json");
    }
}
