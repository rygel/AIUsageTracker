// <copyright file="ConfigServiceScanTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Monitor.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageTracker.Monitor.Tests;

/// <summary>
/// End-to-end tests for <see cref="ConfigService.ScanForKeysAsync"/> to verify
/// that keyless providers are never persisted as skeleton configs.
/// </summary>
public class ConfigServiceScanTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigService _service;

    public ConfigServiceScanTests()
    {
        this._tempDir = Path.Combine(Path.GetTempPath(), "config-scan-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this._tempDir);

        var pathProvider = new TestPathProvider(this._tempDir);
        this._service = new ConfigService(
            NullLogger<ConfigService>.Instance,
            NullLoggerFactory.Instance,
            pathProvider);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(this._tempDir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ScanForKeysAsync_NeverPersistsProvidersWithoutKeysAsync()
    {
        // Start with an empty providers.json
        await File.WriteAllTextAsync(
            Path.Combine(this._tempDir, "providers.json"),
            "{}");

        // Run scan — no env vars or auth files exist, so no keys will be found
        await this._service.ScanForKeysAsync();

        // Read back the persisted config
        var json = await File.ReadAllTextAsync(Path.Combine(this._tempDir, "providers.json"));
        var configs = await this._service.GetConfigsAsync();

        // Every persisted config must have an API key — no empty skeletons
        foreach (var config in configs)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(config.ApiKey),
                $"Provider '{config.ProviderId}' was persisted without an API key. " +
                "ScanForKeysAsync must not create skeleton configs for keyless providers.");
        }
    }

    [Fact]
    public async Task ScanForKeysAsync_DoesNotGrowConfigFileWithKeylessProvidersAsync()
    {
        // Seed with one existing provider (keys are discovered at runtime, not stored in JSON)
        var seed = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["claude-code"] = new { type = "quota-based", show_in_tray = true },
        };
        await File.WriteAllTextAsync(
            Path.Combine(this._tempDir, "providers.json"),
            JsonSerializer.Serialize(seed));

        await this._service.ScanForKeysAsync();

        // Read back the raw JSON to count provider entries
        var json = await File.ReadAllTextAsync(Path.Combine(this._tempDir, "providers.json"));
        var doc = JsonDocument.Parse(json);

        // The file should not have grown with skeleton entries for every known provider
        // (e.g. deepseek, antigravity, minimax etc. that have no keys)
        var providerCount = doc.RootElement.EnumerateObject().Count();
        Assert.True(
            providerCount <= 5,
            $"providers.json has {providerCount.ToString(CultureInfo.InvariantCulture)} entries after scan — expected only seeded + " +
            $"actually-discovered providers, not skeleton entries for all {ProviderMetadataCatalog.Definitions.Count.ToString(CultureInfo.InvariantCulture)} known providers.");
    }

    [Fact]
    public async Task ScanForKeysAsync_AllPersistedConfigsHaveKeysAsync()
    {
        // Start with empty config, no auth files, no env vars
        await File.WriteAllTextAsync(
            Path.Combine(this._tempDir, "providers.json"),
            "{}");

        await this._service.ScanForKeysAsync();

        // Force reload from disk
        var freshService = new ConfigService(
            NullLogger<ConfigService>.Instance,
            NullLoggerFactory.Instance,
            new TestPathProvider(this._tempDir));
        var configs = await freshService.GetConfigsAsync();

        // Every persisted config must have an API key
        var keyless = configs.Where(c => string.IsNullOrWhiteSpace(c.ApiKey)).ToList();
        Assert.Empty(keyless);
    }

    private sealed class TestPathProvider : IAppPathProvider
    {
        private readonly string _dir;

        public TestPathProvider(string dir)
        {
            this._dir = dir;
        }

        public string GetAppDataRoot() => this._dir;

        public string GetDatabasePath() => Path.Combine(this._dir, "usage.db");

        public string GetLogDirectory() => this._dir;

        public string GetAuthFilePath() => Path.Combine(this._dir, "auth.json");

        public string GetPreferencesFilePath() => Path.Combine(this._dir, "preferences.json");

        public string GetProviderConfigFilePath() => Path.Combine(this._dir, "providers.json");

        public string GetMonitorInfoFilePath() => Path.Combine(this._dir, "monitor-info.json");

        public string GetUserProfileRoot() => this._dir;
    }
}
