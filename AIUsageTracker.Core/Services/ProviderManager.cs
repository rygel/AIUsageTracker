// <copyright file="ProviderManager.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Core.Services;

public class ProviderManager : IDisposable
{
    private static readonly TimeSpan ProviderRequestTimeout = TimeSpan.FromSeconds(25);
    public const int DefaultMaxConcurrentProviderRequests = 6;
    public const int MinMaxConcurrentProviderRequests = 1;
    public const int MaxMaxConcurrentProviderRequests = 32;

    private readonly IReadOnlyList<IProviderService> _providers;
    private readonly IConfigLoader _configLoader;
    private readonly ILogger<ProviderManager> _logger;
    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);
    private readonly SemaphoreSlim _configSemaphore = new(1, 1);
    private readonly SemaphoreSlim _httpSemaphore;
    private readonly TimeSpan _configCacheValidity = TimeSpan.FromSeconds(5);
    private List<ProviderUsage> _lastUsages = new();
    private List<ProviderConfig>? _lastConfigs;
    private DateTime _lastConfigLoadTime = DateTime.MinValue;
    private Task<IReadOnlyList<ProviderUsage>>? _refreshTask;

    public ProviderManager(
        IEnumerable<IProviderService> providers,
        IConfigLoader configLoader,
        ILogger<ProviderManager> logger,
        int maxConcurrentProviderRequests = DefaultMaxConcurrentProviderRequests)
    {
        this._providers = providers.ToList();
        this._configLoader = configLoader;
        this._logger = logger;

        this.MaxConcurrentProviderRequests = ClampMaxConcurrentProviderRequests(maxConcurrentProviderRequests);
        this._httpSemaphore = new SemaphoreSlim(this.MaxConcurrentProviderRequests);
    }

    public IReadOnlyList<ProviderUsage> LastUsages => this._lastUsages;

    public IReadOnlyList<ProviderConfig>? LastConfigs => this._lastConfigs;

    public int MaxConcurrentProviderRequests { get; }

    public static int ClampMaxConcurrentProviderRequests(int value)
    {
        return Math.Clamp(value, MinMaxConcurrentProviderRequests, MaxMaxConcurrentProviderRequests);
    }

    public async Task<IReadOnlyList<ProviderConfig>> GetConfigsAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && this.HasFreshConfigs())
        {
            this._logger.LogDebug("Using cached configs");
            return this._lastConfigs!;
        }

        await this._configSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!forceRefresh && this.HasFreshConfigs())
            {
                return this._lastConfigs!;
            }

            this._logger.LogDebug("Loading configs from file");
            var configs = (await this._configLoader.LoadConfigAsync().ConfigureAwait(false)).ToList();
            this._lastConfigs = configs;
            this._lastConfigLoadTime = DateTime.UtcNow;
            return configs;
        }
        finally
        {
            this._configSemaphore.Release();
        }
    }

    public async Task<IReadOnlyList<ProviderUsage>> GetAllUsageAsync(
        bool forceRefresh = true,
        Action<ProviderUsage>? progressCallback = null,
        IReadOnlyCollection<string>? includeProviderIds = null,
        IReadOnlyCollection<ProviderConfig>? overrideConfigs = null)
    {
        await this._refreshSemaphore.WaitAsync().ConfigureAwait(false);
        var semaphoreReleased = false;
        try
        {
            if (this._refreshTask != null && !this._refreshTask.IsCompleted)
            {
                this._logger.LogDebug("Joining existing refresh task...");
                var existingTask = this._refreshTask;
                this._refreshSemaphore.Release();
                semaphoreReleased = true;
                return await existingTask.ConfigureAwait(false);
            }

            if (!forceRefresh && this._lastUsages.Count > 0)
            {
                this._refreshSemaphore.Release();
                semaphoreReleased = true;
                return this._lastUsages;
            }

            this._refreshTask = this.FetchAllUsageInternalAsync(progressCallback, includeProviderIds, overrideConfigs);
            var currentTask = this._refreshTask;
            this._refreshSemaphore.Release();
            semaphoreReleased = true;
            return await currentTask.ConfigureAwait(false);
        }
        finally
        {
            if (!semaphoreReleased)
            {
                this._refreshSemaphore.Release();
            }
        }
    }

    public async Task<IReadOnlyList<ProviderUsage>> GetUsageAsync(string providerId)
    {
        var configs = await this.GetConfigsAsync(forceRefresh: false).ConfigureAwait(false);
        var config = configs.FirstOrDefault(c => c.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));

        if (config == null)
        {
            var definition = this._providers
                .Select(p => p.Definition)
                .FirstOrDefault(d => d.HandlesProviderId(providerId) && d.AutoIncludeWhenUnconfigured);
            if (definition == null)
            {
                throw new ArgumentException($"Provider '{providerId}' not found in configuration.", nameof(providerId));
            }

            config = definition.CreateDefaultConfig(providerId);
        }

        return await this.FetchSingleProviderUsageAsync(config, null).ConfigureAwait(false);
    }

    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            this._refreshSemaphore.Dispose();
            this._configSemaphore.Dispose();
            this._httpSemaphore.Dispose();
        }
    }

    private static string ResolveDisplayName(
        ProviderDefinition definition,
        string providerId,
        string? providerName)
    {
        var mapped = definition.ResolveDisplayName(providerId);
        if (!string.IsNullOrWhiteSpace(mapped))
        {
            return mapped;
        }

        if (!string.IsNullOrWhiteSpace(providerName))
        {
            return providerName;
        }

        return providerId;
    }

    private static ProviderConfig CloneConfig(ProviderConfig source)
    {
        return new ProviderConfig
        {
            ProviderId = source.ProviderId,
            ApiKey = source.ApiKey,
            Type = source.Type,
            BaseUrl = source.BaseUrl,
            ShowInTray = source.ShowInTray,
            EnableNotifications = source.EnableNotifications,
            EnabledSubTrays = source.EnabledSubTrays?.ToList() ?? new List<string>(),
            Models = source.Models,
            Description = source.Description,
            AuthSource = source.AuthSource,
            PlanType = source.PlanType,
        };
    }

    private static ProviderUsage CreateTimeoutUsage(
        ProviderConfig config,
        (bool IsQuotaBased, PlanType PlanType, string DisplayName) defaults,
        Stopwatch stopwatch)
    {
        return new ProviderUsage
        {
            ProviderId = config.ProviderId,
            ProviderName = defaults.DisplayName,
            Description = $"[Error] Timeout after {ProviderRequestTimeout.TotalSeconds:F0}s",
            RequestsPercentage = 0,
            IsAvailable = false,
            IsQuotaBased = defaults.IsQuotaBased,
            PlanType = defaults.PlanType,
            HttpStatus = 504,
            ResponseLatencyMs = stopwatch.Elapsed.TotalMilliseconds,
        };
    }

    private static ProviderUsage CreateArgumentErrorUsage(
        ProviderConfig config,
        (bool IsQuotaBased, PlanType PlanType, string DisplayName) defaults,
        string message,
        Stopwatch stopwatch)
    {
        return new ProviderUsage
        {
            ProviderId = config.ProviderId,
            ProviderName = defaults.DisplayName,
            Description = message,
            RequestsPercentage = 0,
            IsAvailable = false,
            IsQuotaBased = defaults.IsQuotaBased,
            PlanType = defaults.PlanType,
            ResponseLatencyMs = stopwatch.Elapsed.TotalMilliseconds,
        };
    }

    private static ProviderUsage CreateUnexpectedErrorUsage(
        ProviderConfig config,
        (bool IsQuotaBased, PlanType PlanType, string DisplayName) defaults,
        string message,
        Stopwatch stopwatch)
    {
        return new ProviderUsage
        {
            ProviderId = config.ProviderId,
            ProviderName = defaults.DisplayName,
            Description = $"[Error] {message}",
            RequestsPercentage = 0,
            IsAvailable = true,
            IsQuotaBased = defaults.IsQuotaBased,
            PlanType = defaults.PlanType,
            HttpStatus = 500,
            ResponseLatencyMs = stopwatch.Elapsed.TotalMilliseconds,
        };
    }

    private static ProviderUsage CreateUnknownProviderUsage(
        ProviderConfig config,
        (bool IsQuotaBased, PlanType PlanType, string DisplayName) defaults)
    {
        return new ProviderUsage
        {
            ProviderId = config.ProviderId,
            ProviderName = defaults.DisplayName,
            Description = "Usage unknown (provider integration missing)",
            RequestsPercentage = 0,
            IsAvailable = false,
            UsageUnit = "Status",
            IsQuotaBased = defaults.IsQuotaBased,
            PlanType = defaults.PlanType,
            ResponseLatencyMs = 0,
        };
    }

    private static List<ProviderUsage> CreateSingleUsageList(
        ProviderUsage usage,
        Action<ProviderUsage>? progressCallback)
    {
        progressCallback?.Invoke(usage);
        return new List<ProviderUsage> { usage };
    }

    private bool HasFreshConfigs()
    {
        return this._lastConfigs != null &&
            DateTime.UtcNow - this._lastConfigLoadTime < this._configCacheValidity;
    }

    private async Task<IReadOnlyList<ProviderUsage>> FetchAllUsageInternalAsync(
        Action<ProviderUsage>? progressCallback = null,
        IReadOnlyCollection<string>? includeProviderIds = null,
        IReadOnlyCollection<ProviderConfig>? overrideConfigs = null)
    {
        this._logger.LogDebug("Starting FetchAllUsageInternal...");
        var configs = overrideConfigs != null
            ? overrideConfigs.Select(CloneConfig).ToList()
            : (await this.GetConfigsAsync(forceRefresh: true).ConfigureAwait(false)).ToList();

        if (overrideConfigs == null)
        {
            foreach (var definition in this._providers
                         .Select(p => p.Definition)
                         .Where(d => d.AutoIncludeWhenUnconfigured))
            {
                if (configs.Any(c => c.ProviderId.Equals(definition.ProviderId, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                configs.Add(definition.CreateDefaultConfig());
            }
        }

        if (includeProviderIds != null && includeProviderIds.Count > 0)
        {
            var included = includeProviderIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            configs = configs
                .Where(c => included.Contains(c.ProviderId))
                .ToList();
        }

        var tasks = configs.Select(config => this.FetchSingleProviderUsageAsync(config, progressCallback));
        var nestedResults = await Task.WhenAll(tasks).ConfigureAwait(false);
        var results = nestedResults.SelectMany(x => x).ToList();
        this._lastUsages = results;
        return results;
    }

    private async Task<IReadOnlyList<ProviderUsage>> FetchSingleProviderUsageAsync(
        ProviderConfig config,
        Action<ProviderUsage>? progressCallback)
    {
        var provider = this._providers.FirstOrDefault(p => p.Definition.HandlesProviderId(config.ProviderId));
        var defaults = this.ResolveDefaults(config.ProviderId, provider);

        if (provider == null)
        {
            var unknownProviderUsage = CreateUnknownProviderUsage(config, defaults);
            return CreateSingleUsageList(unknownProviderUsage, progressCallback);
        }

        await this._httpSemaphore.WaitAsync().ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await this.FetchProviderUsagesAsync(
                    config,
                    provider,
                    defaults,
                    stopwatch,
                    progressCallback)
                .ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            this._logger.LogWarning("Skipping {ProviderId}: {Message}", config.ProviderId, ex.Message);
            var errorUsage = CreateArgumentErrorUsage(config, defaults, ex.Message, stopwatch);
            return CreateSingleUsageList(errorUsage, progressCallback);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to fetch usage for {ProviderId}", config.ProviderId);
            var errorUsage = CreateUnexpectedErrorUsage(config, defaults, ex.Message, stopwatch);
            var errorResults = CreateSingleUsageList(errorUsage, progressCallback);

            if (progressCallback == null)
            {
                throw;
            }

            return errorResults;
        }
        finally
        {
            this._httpSemaphore.Release();
        }
    }

    private async Task<IReadOnlyList<ProviderUsage>> FetchProviderUsagesAsync(
        ProviderConfig config,
        IProviderService provider,
        (bool IsQuotaBased, PlanType PlanType, string DisplayName) defaults,
        Stopwatch stopwatch,
        Action<ProviderUsage>? progressCallback)
    {
        this._logger.LogDebug("Fetching usage for provider: {ProviderId}", config.ProviderId);
        var usageTask = provider.GetUsageAsync(config, progressCallback);
        var timeoutTask = Task.Delay(ProviderRequestTimeout);
        var completedTask = await Task.WhenAny(usageTask, timeoutTask).ConfigureAwait(false);

        if (completedTask != usageTask)
        {
            stopwatch.Stop();
            this._logger.LogWarning(
                "Provider {ProviderId} timed out after {TimeoutSeconds}s",
                config.ProviderId,
                ProviderRequestTimeout.TotalSeconds);

            _ = usageTask.ContinueWith(
                static task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);

            var timeoutUsage = CreateTimeoutUsage(config, defaults, stopwatch);
            return CreateSingleUsageList(timeoutUsage, progressCallback);
        }

        var usages = (await usageTask.ConfigureAwait(false)).ToList();
        stopwatch.Stop();
        foreach (var usage in usages)
        {
            usage.ProviderName = ResolveDisplayName(provider.Definition, usage.ProviderId, usage.ProviderName);
            usage.AuthSource = config.AuthSource;
            usage.ResponseLatencyMs = stopwatch.Elapsed.TotalMilliseconds;
            progressCallback?.Invoke(usage);
        }

        this._logger.LogDebug("Success for {ProviderId}: {Count} items", config.ProviderId, usages.Count);
        return usages;
    }

    private (bool IsQuotaBased, PlanType PlanType, string DisplayName) ResolveDefaults(
        string providerId,
        IProviderService? provider = null)
    {
        var definition = provider?.Definition ??
            this._providers
                .Select(p => p.Definition)
                .FirstOrDefault(d => d.HandlesProviderId(providerId));

        if (definition != null)
        {
            return (
                definition.IsQuotaBased,
                definition.PlanType,
                definition.ResolveDisplayName(providerId) ?? definition.DisplayName);
        }

        this._logger.LogWarning(
            "Provider metadata missing for {ProviderId}. Identification defaults are disabled.",
            providerId);

        return (
            false,
            PlanType.Usage,
            string.IsNullOrWhiteSpace(providerId) ? string.Empty : providerId);
    }
}
