// <copyright file="UsageDatabasePipelineTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;
using AIUsageTracker.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageTracker.Tests.Services;

/// <summary>
/// Full data-path integration tests: provider output →
/// <see cref="ProviderUsageProcessingPipeline"/> →
/// <see cref="UsageDatabase.StoreHistoryAsync"/> →
/// <see cref="UsageDatabase.GetLatestHistoryAsync"/>.
///
/// These tests verify that numeric values, booleans, and string fields survive
/// the complete pipeline without truncation, type mismatch, or silent data loss.
/// </summary>
public sealed class UsageDatabasePipelineTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ProviderUsageProcessingPipeline _pipeline;

    public UsageDatabasePipelineTests()
    {
        this._dbPath = TestTempPaths.CreateFilePath("usage-db-pipeline-tests", "usage.db");
        this._pipeline = new ProviderUsageProcessingPipeline(NullLogger<ProviderUsageProcessingPipeline>.Instance);
    }

    // -------------------------------------------------------------------------
    // Data preservation through the full pipeline
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Pipeline_ProviderData_IsStoredAndRetrievedFaithfullyAsync()
    {
        var db = await this.CreateDatabaseAsync();
        var fetchedAt = new DateTime(2026, 3, 19, 10, 0, 0, DateTimeKind.Utc);

        var originalUsage = new ProviderUsage
        {
            ProviderId = "codex",
            ProviderName = "OpenAI Codex",
            RequestsUsed = 123.5,
            RequestsAvailable = 876.5,
            UsedPercent = 12.35,
            IsAvailable = true,
            Description = "healthy",
            HttpStatus = 200,
            FetchedAt = fetchedAt,
        };

        var result = this._pipeline.Process([originalUsage], activeProviderIds: ["codex"], isPrivacyMode: false);
        await db.StoreHistoryAsync(result.Usages);

        var latest = await db.GetLatestHistoryAsync();

        var codex = Assert.Single(latest, u => string.Equals(u.ProviderId, "codex", StringComparison.Ordinal));
        Assert.Equal(123.5, codex.RequestsUsed, precision: 5);
        Assert.Equal(876.5, codex.RequestsAvailable, precision: 5);
        Assert.True(codex.IsAvailable);
        Assert.Equal(200, codex.HttpStatus);
    }

    [Fact]
    public async Task Pipeline_UnavailableProvider_IsStoredCorrectlyAsync()
    {
        var db = await this.CreateDatabaseAsync();

        var unavailable = new ProviderUsage
        {
            ProviderId = "codex",
            ProviderName = "OpenAI Codex",
            RequestsUsed = 0,
            RequestsAvailable = 0,
            IsAvailable = false,
            Description = "Auth token not found",
            HttpStatus = 401,
            FetchedAt = DateTime.UtcNow,
        };

        var result = this._pipeline.Process([unavailable], activeProviderIds: ["codex"], isPrivacyMode: false);
        await db.StoreHistoryAsync(result.Usages);

        var latest = await db.GetLatestHistoryAsync();

        var codex = Assert.Single(latest, u => string.Equals(u.ProviderId, "codex", StringComparison.Ordinal));
        Assert.False(codex.IsAvailable);
        Assert.Equal(401, codex.HttpStatus);
        Assert.Contains("Auth token", codex.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pipeline_InactiveProvider_IsFilteredAndNotStoredAsync()
    {
        var db = await this.CreateDatabaseAsync();

        var usage = new ProviderUsage
        {
            ProviderId = "inactive-provider",
            ProviderName = "Inactive",
            RequestsUsed = 50,
            RequestsAvailable = 950,
            IsAvailable = true,
            Description = "ok",
            HttpStatus = 200,
            FetchedAt = DateTime.UtcNow,
        };

        // "inactive-provider" is not in the active set
        var result = this._pipeline.Process([usage], activeProviderIds: ["codex"], isPrivacyMode: false);

        Assert.Empty(result.Usages);
        Assert.Equal(1, result.InactiveProviderFilteredCount);

        // Nothing stored in the DB
        await db.StoreHistoryAsync(result.Usages);
        var latest = await db.GetLatestHistoryAsync();
        Assert.Empty(latest);
    }

    [Fact]
    public async Task Pipeline_MixedBatch_ActiveStoredInactiveFilteredAsync()
    {
        var db = await this.CreateDatabaseAsync();
        var fetchedAt = DateTime.UtcNow.AddMinutes(-1);

        var usages = new[]
        {
            new ProviderUsage { ProviderId = "codex", ProviderName = "Codex", RequestsUsed = 50, RequestsAvailable = 950, IsAvailable = true, Description = "ok", HttpStatus = 200, FetchedAt = fetchedAt },
            new ProviderUsage { ProviderId = "suspended-provider", ProviderName = "Suspended", RequestsUsed = 10, RequestsAvailable = 90, IsAvailable = true, Description = "ok", HttpStatus = 200, FetchedAt = fetchedAt },
        };

        var result = this._pipeline.Process(usages, activeProviderIds: ["codex"], isPrivacyMode: false);
        await db.StoreHistoryAsync(result.Usages);

        var latest = await db.GetLatestHistoryAsync();
        Assert.Single(latest, u => string.Equals(u.ProviderId, "codex", StringComparison.Ordinal));
        Assert.DoesNotContain(latest, u => string.Equals(u.ProviderId, "suspended-provider", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Pipeline_FlatCardWithWindowKind_RoundTripsThroughDatabaseAsync()
    {
        var db = await this.CreateDatabaseAsync();

        // Flat burst card — replaces the old Details round-trip test
        var usage = new ProviderUsage
        {
            ProviderId = "codex",
            ProviderName = "OpenAI Codex",
            CardId = "burst",
            GroupId = "codex",
            Name = "5-hour quota",
            WindowKind = WindowKind.Burst,
            UsedPercent = 50.0,
            RequestsUsed = 50,
            RequestsAvailable = 950,
            IsAvailable = true,
            Description = "50% remaining",
            HttpStatus = 200,
            FetchedAt = DateTime.UtcNow.AddMinutes(-1),
        };

        var result = this._pipeline.Process([usage], activeProviderIds: ["codex"], isPrivacyMode: false);
        await db.StoreHistoryAsync(result.Usages);

        var latest = await db.GetLatestHistoryAsync();

        var codex = Assert.Single(latest, u => string.Equals(u.ProviderId, "codex", StringComparison.Ordinal));
        Assert.Equal(WindowKind.Burst, codex.WindowKind);
        Assert.Equal("burst", codex.CardId);
        Assert.Equal("5-hour quota", codex.Name);
        Assert.Equal(50.0, codex.UsedPercent, precision: 5);
    }

    [Fact]
    public async Task Pipeline_SequentialPolls_DedupGateSuppressesSecondInsertAsync()
    {
        var db = await this.CreateDatabaseAsync();
        var t1 = DateTime.UtcNow.AddMinutes(-5);
        var t2 = t1.AddMinutes(3);

        var usageT1 = new ProviderUsage { ProviderId = "codex", ProviderName = "Codex", RequestsUsed = 50, RequestsAvailable = 950, IsAvailable = true, Description = "ok", HttpStatus = 200, FetchedAt = t1 };
        var usageT2 = new ProviderUsage { ProviderId = "codex", ProviderName = "Codex", RequestsUsed = 50, RequestsAvailable = 950, IsAvailable = true, Description = "ok", HttpStatus = 200, FetchedAt = t2 };

        var r1 = this._pipeline.Process([usageT1], activeProviderIds: ["codex"], isPrivacyMode: false);
        await db.StoreHistoryAsync(r1.Usages);

        var r2 = this._pipeline.Process([usageT2], activeProviderIds: ["codex"], isPrivacyMode: false);
        await db.StoreHistoryAsync(r2.Usages);

        var history = await db.GetHistoryByProviderAsync("codex");
        Assert.Single(history); // dedup gate fired; no second row
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------
    public void Dispose() => TestTempPaths.CleanupPath(this._dbPath);

    private async Task<UsageDatabase> CreateDatabaseAsync()
    {
        var db = new UsageDatabase(NullLogger<UsageDatabase>.Instance, new TestDbPathProvider(this._dbPath));
        await db.InitializeAsync().ConfigureAwait(false);
        return db;
    }

    private sealed class TestDbPathProvider(string dbPath) : IAppPathProvider
    {
        public string GetAppDataRoot() => Path.GetDirectoryName(dbPath)!;

        public string GetDatabasePath() => dbPath;

        public string GetLogDirectory() => Path.Combine(this.GetAppDataRoot(), "logs");

        public string GetAuthFilePath() => Path.Combine(this.GetAppDataRoot(), "auth.json");

        public string GetPreferencesFilePath() => Path.Combine(this.GetAppDataRoot(), "preferences.json");

        public string GetProviderConfigFilePath() => Path.Combine(this.GetAppDataRoot(), "providers.json");

        public string GetUserProfileRoot() => this.GetAppDataRoot();

        public string GetMonitorInfoFilePath() => Path.Combine(this.GetAppDataRoot(), "monitor.json");
    }
}
