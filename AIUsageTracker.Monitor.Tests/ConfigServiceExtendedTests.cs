// <copyright file="ConfigServiceExtendedTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Monitor.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageTracker.Monitor.Tests;

public sealed class ConfigServiceExtendedTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigService _service;

    public ConfigServiceExtendedTests()
    {
        this._tempDir = Path.Combine(Path.GetTempPath(), "config-extended-test-" + Guid.NewGuid().ToString("N"));
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
        }
    }

    [Fact]
    public async Task GetConfigsAsync_ReturnsEmpty_WhenNoFile()
    {
        var configs = await this._service.GetConfigsAsync();
        Assert.NotNull(configs);
    }

    [Fact]
    public async Task GetConfigsAsync_ReturnsCachedOnSecondCall()
    {
        await this.WriteProvidersJsonAsync(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["antigravity"] = new { key = string.Empty },
        });

        var first = await this._service.GetConfigsAsync();
        var second = await this._service.GetConfigsAsync();

        Assert.Equal(first.Count, second.Count);
    }

    [Fact]
    public async Task SaveConfigAsync_ThrowsOnNullConfig()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => this._service.SaveConfigAsync(null!));
    }

    [Fact]
    public async Task SaveConfigAsync_ThrowsOnEmptyProviderId()
    {
        var config = new ProviderConfig { ProviderId = string.Empty };
        await Assert.ThrowsAsync<ArgumentException>(
            () => this._service.SaveConfigAsync(config));
    }

    [Fact]
    public async Task SaveConfigAsync_ThrowsOnUnknownProviderId()
    {
        var config = new ProviderConfig { ProviderId = "nonexistent-xyz-abc" };
        await Assert.ThrowsAsync<ArgumentException>(
            () => this._service.SaveConfigAsync(config));
    }

    [Fact]
    public async Task SaveConfigAsync_AddsNewConfig()
    {
        await this.WriteProvidersJsonAsync("{}");

        var config = new ProviderConfig
        {
            ProviderId = "antigravity",
            ApiKey = "test-key-123",
            AuthSource = "manual",
        };

        await this._service.SaveConfigAsync(config);

        var authPath = Path.Combine(this._tempDir, "auth.json");
        Assert.True(File.Exists(authPath));
        var content = await File.ReadAllTextAsync(authPath);
        Assert.Contains("test-key-123", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveConfigAsync_UpdatesExistingConfig()
    {
        await this.WriteProvidersJsonAsync(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["antigravity"] = new { key = "old-key" },
        });

        var config = new ProviderConfig
        {
            ProviderId = "antigravity",
            ApiKey = "new-key",
            AuthSource = "updated",
        };

        await this._service.SaveConfigAsync(config);

        var authPath = Path.Combine(this._tempDir, "auth.json");
        var content = await File.ReadAllTextAsync(authPath);
        Assert.Contains("new-key", content, StringComparison.Ordinal);
        Assert.DoesNotContain("old-key", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoveConfigAsync_RemovesProvider()
    {
        await this.WriteProvidersJsonAsync(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["antigravity"] = new { key = "key1" },
            ["gemini-cli"] = new { key = "key2" },
        });

        await this._service.RemoveConfigAsync("antigravity");

        var authPath = Path.Combine(this._tempDir, "auth.json");
        if (File.Exists(authPath))
        {
            var content = await File.ReadAllTextAsync(authPath);
            Assert.DoesNotContain("key1", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task GetPreferencesAsync_ReturnsDefault_WhenNoFile()
    {
        var prefs = await this._service.GetPreferencesAsync();
        Assert.NotNull(prefs);
    }

    [Fact]
    public async Task GetPreferencesAsync_ReturnsCachedOnSecondCall()
    {
        var first = await this._service.GetPreferencesAsync();
        var second = await this._service.GetPreferencesAsync();

        Assert.Equal(first.SuppressedProviderIds.Count, second.SuppressedProviderIds.Count);
    }

    [Fact]
    public async Task SavePreferencesAsync_PersistsAndInvalidates()
    {
        var prefs = new AppPreferences
        {
            SuppressedProviderIds = new List<string> { "antigravity" },
        };

        await this._service.SavePreferencesAsync(prefs);

        var loaded = await this._service.GetPreferencesAsync();
        Assert.Contains("antigravity", loaded.SuppressedProviderIds);
    }

    private async Task WriteProvidersJsonAsync(object data)
    {
        var json = data is string s ? s : JsonSerializer.Serialize(data);
#pragma warning disable MA0004
        await File.WriteAllTextAsync(
            Path.Combine(this._tempDir, "providers.json"),
            json);
#pragma warning restore MA0004
    }

    private sealed class TestPathProvider : IAppPathProvider
    {
        private readonly string _root;

        public TestPathProvider(string root) => this._root = root;

        public string GetAppDataRoot() => this._root;

        public string GetDatabasePath() => Path.Combine(this._root, "monitor.db");

        public string GetLogDirectory() => Path.Combine(this._root, "logs");

        public string GetAuthFilePath() => Path.Combine(this._root, "auth.json");

        public string GetPreferencesFilePath() => Path.Combine(this._root, "prefs.json");

        public string GetProviderConfigFilePath() => Path.Combine(this._root, "providers.json");

        public string GetMonitorInfoFilePath() => Path.Combine(this._root, "monitor.json");

        public string GetUserProfileRoot() => Path.Combine(this._root, "userprofile");
    }
}
