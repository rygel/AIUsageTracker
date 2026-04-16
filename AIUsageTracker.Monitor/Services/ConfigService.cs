// <copyright file="ConfigService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Configuration;
using AIUsageTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageTracker.Monitor.Services;

public class ConfigService : IConfigService
{
    private readonly ILogger<ConfigService> _logger;
    private readonly JsonConfigLoader _configLoader;
    private readonly TokenDiscoveryService _tokenDiscovery;
    private readonly IAppPathProvider _pathProvider;
    private readonly SemaphoreSlim _configCacheLock = new(1, 1);
    private readonly SemaphoreSlim _prefsCacheLock = new(1, 1);
    private IReadOnlyList<ProviderConfig>? _cachedConfigs;
    private AppPreferences? _cachedPreferences;
    private int _startupAuthDiagnosticsLogged;

    public ConfigService(ILogger<ConfigService> logger, IAppPathProvider pathProvider)
        : this(logger, NullLoggerFactory.Instance, pathProvider)
    {
    }

    public ConfigService(ILogger<ConfigService> logger, ILoggerFactory loggerFactory, IAppPathProvider pathProvider)
    {
        this._logger = logger;
        this._pathProvider = pathProvider;
        var tokenDiscoveryLogger = loggerFactory.CreateLogger<TokenDiscoveryService>();
        this._configLoader = new JsonConfigLoader(
            logger: loggerFactory.CreateLogger<JsonConfigLoader>(),
            tokenDiscoveryLogger: tokenDiscoveryLogger,
            pathProvider: this._pathProvider);
        this._tokenDiscovery = new TokenDiscoveryService(tokenDiscoveryLogger, this._pathProvider);
    }

    public async Task<IReadOnlyList<ProviderConfig>> GetConfigsAsync()
    {
        var cached = Volatile.Read(ref this._cachedConfigs);
        if (cached != null)
        {
            return cached;
        }

        await this._configCacheLock.WaitAsync().ConfigureAwait(false);
        try
        {
            cached = Volatile.Read(ref this._cachedConfigs);
            if (cached != null)
            {
                return cached;
            }

            var configs = (await this._configLoader.LoadConfigAsync().ConfigureAwait(false)).ToList();
            this.LogAuthDiagnosticsSnapshotOnceOnStartup(configs);
            Volatile.Write(ref this._cachedConfigs, configs);
            return configs;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            this._logger.LogError(ex, "Failed to load configs: {Message}", ex.Message);
            MonitorInfoPersistence.ReportError($"Config load failed: {ex.Message}", this._pathProvider, this._logger);
            return new List<ProviderConfig>();
        }
        finally
        {
            this._configCacheLock.Release();
        }
    }

    public async Task SaveConfigAsync(ProviderConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.ProviderId))
        {
            throw new ArgumentException("Provider ID must not be null or whitespace.", nameof(config));
        }

        if (!ProviderMetadataCatalog.TryGet(config.ProviderId, out _))
        {
            throw new ArgumentException(
                $"Unknown provider ID '{config.ProviderId}'. Only catalog-registered providers may be saved.",
                nameof(config));
        }

        try
        {
            var configs = (await this._configLoader.LoadConfigAsync().ConfigureAwait(false)).ToList();

            // Update or add
            var existing = configs.FirstOrDefault(c =>
                c.ProviderId.Equals(config.ProviderId, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                var index = configs.IndexOf(existing);
                configs[index] = config;
            }
            else
            {
                configs.Add(config);
            }

            await this._configLoader.SaveConfigAsync(configs).ConfigureAwait(false);
            Volatile.Write(ref this._cachedConfigs, null);
            this._logger.LogInformation("Saved: {ProviderId}", config.ProviderId);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to save config for {ProviderId}: {Message}", config.ProviderId, ex.Message);
            throw;
        }
    }

    public async Task RemoveConfigAsync(string providerId)
    {
        try
        {
            var configs = (await this._configLoader.LoadConfigAsync().ConfigureAwait(false)).ToList();
            configs.RemoveAll(c => c.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));
            await this._configLoader.SaveConfigAsync(configs).ConfigureAwait(false);
            Volatile.Write(ref this._cachedConfigs, null);
            Volatile.Write(ref this._cachedPreferences, null); // force ScanForKeysAsync to reload suppressed list from disk
            this._logger.LogInformation("Removed: {ProviderId}", providerId);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to remove config for {ProviderId}: {Message}", providerId, ex.Message);
            throw;
        }
    }

    public async Task<AppPreferences> GetPreferencesAsync()
    {
        var cached = Volatile.Read(ref this._cachedPreferences);
        if (cached != null)
        {
            return cached;
        }

        await this._prefsCacheLock.WaitAsync().ConfigureAwait(false);
        try
        {
            cached = Volatile.Read(ref this._cachedPreferences);
            if (cached != null)
            {
                return cached;
            }

            var prefs = await this._configLoader.LoadPreferencesAsync().ConfigureAwait(false);
            Volatile.Write(ref this._cachedPreferences, prefs);
            return prefs;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            this._logger.LogError(ex, "Failed to load preferences: {Message}", ex.Message);
            return new AppPreferences();
        }
        finally
        {
            this._prefsCacheLock.Release();
        }
    }

    public async Task SavePreferencesAsync(AppPreferences preferences)
    {
        try
        {
            await this._configLoader.SavePreferencesAsync(preferences).ConfigureAwait(false);
            Volatile.Write(ref this._cachedPreferences, null);
            this._logger.LogInformation("Prefs saved");
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to save preferences: {Message}", ex.Message);
            throw;
        }
    }

    public async Task<IReadOnlyList<ProviderConfig>> ScanForKeysAsync()
    {
        try
        {
            var discovered = await this._tokenDiscovery.DiscoverTokensAsync().ConfigureAwait(false);
            var existing = (await this._configLoader.LoadConfigAsync().ConfigureAwait(false)).ToList();
            var prefs = await this.GetPreferencesAsync().ConfigureAwait(false);
            var suppressed = new HashSet<string>(prefs.SuppressedProviderIds, StringComparer.OrdinalIgnoreCase);
            var discoveredWithKeys = discovered
                .Where(config => !string.IsNullOrWhiteSpace(config.ApiKey))
                .ToList();
            var addedWithKeys = new List<string>();
            var updatedWithKeys = new List<string>();
            var alreadyConfiguredWithKeys = new List<string>();

            this._logger.LogInformation(
                "Auth scan started: discovered {TotalDiscovered} providers ({ProvidersWithKeys} with keys).",
                discovered.Count,
                discoveredWithKeys.Count);

            // Merge discovered with existing — only add providers that actually have keys
            foreach (var newConfig in discovered)
            {
                // Skip providers the user deliberately removed via Settings.
                if (suppressed.Contains(newConfig.ProviderId))
                {
                    this._logger.LogDebug("Skipping suppressed provider: {ProviderId}", newConfig.ProviderId);
                    continue;
                }

                var existingConfig = existing.FirstOrDefault(c =>
                    c.ProviderId.Equals(newConfig.ProviderId, StringComparison.OrdinalIgnoreCase));

                if (existingConfig == null)
                {
                    // Never create empty skeleton configs for providers without keys
                    if (string.IsNullOrWhiteSpace(newConfig.ApiKey))
                    {
                        continue;
                    }

                    existing.Add(newConfig);
                    this._logger.LogInformation("Found: {ProviderId}", newConfig.ProviderId);
                    addedWithKeys.Add($"{newConfig.ProviderId} ({newConfig.AuthSource ?? "unknown"})");
                }
                else if (string.IsNullOrEmpty(existingConfig.ApiKey) && !string.IsNullOrEmpty(newConfig.ApiKey))
                {
                    // Update with discovered key
                    existingConfig.ApiKey = newConfig.ApiKey;
                    existingConfig.AuthSource = newConfig.AuthSource ?? string.Empty;
                    this._logger.LogInformation("Key updated: {ProviderId}", newConfig.ProviderId);
                    updatedWithKeys.Add($"{newConfig.ProviderId} ({newConfig.AuthSource ?? "unknown"})");
                }
                else if (!string.IsNullOrWhiteSpace(newConfig.ApiKey))
                {
                    alreadyConfiguredWithKeys.Add($"{newConfig.ProviderId} ({newConfig.AuthSource ?? "unknown"})");
                }
            }

            ProviderMetadataCatalog.NormalizeCanonicalConfigurations(existing);

            this.LogAuthDiagnosticsSnapshot(existing, "post-scan");

            this._logger.LogInformation(
                "Auth scan summary: added={Added}, updated={Updated}, alreadyConfigured={AlreadyConfigured}, discoveredWithKeys={DiscoveredWithKeys}.",
                addedWithKeys.Count,
                updatedWithKeys.Count,
                alreadyConfiguredWithKeys.Count,
                discoveredWithKeys.Count);

            if (addedWithKeys.Count > 0)
            {
                this._logger.LogInformation("Auth scan added providers: {Providers}", string.Join(", ", addedWithKeys));
            }

            if (updatedWithKeys.Count > 0)
            {
                this._logger.LogInformation("Auth scan updated providers: {Providers}", string.Join(", ", updatedWithKeys));
            }

            if (alreadyConfiguredWithKeys.Count > 0)
            {
                this._logger.LogInformation("Auth scan already-configured providers: {Providers}", string.Join(", ", alreadyConfiguredWithKeys));
            }

            await this._configLoader.SaveConfigAsync(existing).ConfigureAwait(false);
            Volatile.Write(ref this._cachedConfigs, null);
            return discovered.ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            this._logger.LogError(ex, "Failed to scan for keys: {Message}", ex.Message);
            return new List<ProviderConfig>();
        }
    }

    private void LogAuthDiagnosticsSnapshotOnceOnStartup(IReadOnlyList<ProviderConfig> configs)
    {
        if (Interlocked.Exchange(ref this._startupAuthDiagnosticsLogged, 1) == 1)
        {
            return;
        }

        this.LogAuthDiagnosticsSnapshot(configs, "startup-config-load");
    }

    private void LogAuthDiagnosticsSnapshot(IReadOnlyList<ProviderConfig> configs, string phase)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        foreach (var config in configs.OrderBy(item => item.ProviderId, StringComparer.OrdinalIgnoreCase))
        {
            var snapshot = AuthDiagnosticsSnapshotBuilder.Build(config, nowUtc, this._logger);
            this._logger.LogInformation(
                "Auth diagnostics [{Phase}] provider={ProviderId} configured={Configured} authSource={AuthSource} fallbackPathUsed={FallbackPathUsed} tokenAgeBucket={TokenAgeBucket} hasUserIdentity={HasUserIdentity}",
                phase,
                snapshot.ProviderId,
                snapshot.Configured,
                snapshot.AuthSource,
                snapshot.FallbackPathUsed,
                snapshot.TokenAgeBucket,
                snapshot.HasUserIdentity);
        }
    }
}
