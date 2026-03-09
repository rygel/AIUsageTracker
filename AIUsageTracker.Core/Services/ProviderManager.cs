using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace AIUsageTracker.Core.Services;

public class ProviderManager : IDisposable
{
    private readonly IReadOnlyList<IProviderService> _providers;
    private readonly IConfigLoader _configLoader;
    private readonly Microsoft.Extensions.Logging.ILogger<ProviderManager> _logger;
    private List<ProviderUsage> _lastUsages = new();
    private List<ProviderConfig>? _lastConfigs;
    private DateTime _lastConfigLoadTime = DateTime.MinValue;
    private readonly TimeSpan _configCacheValidity = TimeSpan.FromSeconds(5);

    public IReadOnlyList<ProviderUsage> LastUsages => this._lastUsages;

    public IReadOnlyList<ProviderConfig>? LastConfigs => this._lastConfigs;

    public ProviderManager(IEnumerable<IProviderService> providers, IConfigLoader configLoader, Microsoft.Extensions.Logging.ILogger<ProviderManager> logger)
    {
        this._providers = providers.ToList();
        this._configLoader = configLoader;
        this._logger = logger;
    }

    private Task<IReadOnlyList<ProviderUsage>>? _refreshTask;
    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);
    private readonly SemaphoreSlim _configSemaphore = new(1, 1);
    private readonly SemaphoreSlim _httpSemaphore = new(6); // Limit parallel HTTP requests to avoid congestion
    private static readonly TimeSpan ProviderRequestTimeout = TimeSpan.FromSeconds(25);

    public async Task<IReadOnlyList<ProviderConfig>> GetConfigsAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && this._lastConfigs != null && DateTime.UtcNow - this._lastConfigLoadTime < this._configCacheValidity)
        {
            this._logger.LogDebug("Using cached configs");
            return this._lastConfigs;
        }

        await this._configSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!forceRefresh && this._lastConfigs != null && DateTime.UtcNow - this._lastConfigLoadTime < this._configCacheValidity)
            {
                return this._lastConfigs;
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
            // Release semaphore if it hasn't been released yet (handles exception cases)
            if (!semaphoreReleased)
            {
                this._refreshSemaphore.Release();
            }
        }
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
            // Auto-add providers that explicitly declare they should always be present.
            foreach (var definition in this._providers
                         .Select(p => p.Definition)
                         .Where(d => d.AutoIncludeWhenUnconfigured))
            {
                if (configs.Any(c => c.ProviderId.Equals(definition.ProviderId, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                configs.Add(new ProviderConfig
                {
                    ProviderId = definition.ProviderId,
                    ApiKey = string.Empty,
                    Type = definition.DefaultConfigType,
                    PlanType = definition.PlanType
                });
            }
        }
        if (includeProviderIds != null && includeProviderIds.Count > 0)
        {
            var included = includeProviderIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            configs = configs
                .Where(c => included.Contains(c.ProviderId))
                .ToList();
        }

        var results = new List<ProviderUsage>();

        var tasks = configs.Select(async config =>
        {
            return await this.FetchSingleProviderUsageAsync(config, progressCallback).ConfigureAwait(false);
        });

        var nestedResults = await Task.WhenAll(tasks).ConfigureAwait(false);
        results.AddRange(nestedResults.SelectMany(x => x));
        this._lastUsages = results;
        return results;
    }

    public async Task<IReadOnlyList<ProviderUsage>> GetUsageAsync(string providerId)
    {
        var configs = await this.GetConfigsAsync(forceRefresh: false).ConfigureAwait(false);
        var config = configs.FirstOrDefault(c => c.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));

        if (config == null)
        {
            var definition = this._providers
                .Select(p => p.Definition)
                .FirstOrDefault(d => d.HandlesProviderId(providerId) &&
                                     d.AutoIncludeWhenUnconfigured);
            if (definition == null)
            {
                throw new ArgumentException($"Provider '{providerId}' not found in configuration.", nameof(providerId));
            }

            config = new ProviderConfig
            {
                ProviderId = providerId,
                ApiKey = string.Empty,
                Type = definition.DefaultConfigType,
                PlanType = definition.PlanType,
            };
        }

        return await this.FetchSingleProviderUsageAsync(config, null).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ProviderUsage>> FetchSingleProviderUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback)
    {
        var provider = this._providers.FirstOrDefault(p => p.Definition.HandlesProviderId(config.ProviderId));
        var defaults = this.ResolveDefaults(config.ProviderId, provider);

        if (provider != null)
        {
            await this._httpSemaphore.WaitAsync().ConfigureAwait(false);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                this._logger.LogDebug($"Fetching usage for provider: {config.ProviderId}");
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
                        static task => { _ = task.Exception; },
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted,
                        TaskScheduler.Default);

                    var timeoutUsage = new ProviderUsage
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

                    progressCallback?.Invoke(timeoutUsage);
                    return new List<ProviderUsage> { timeoutUsage };
                }

                var usages = (await usageTask.ConfigureAwait(false)).ToList();
                stopwatch.Stop();
                foreach (var u in usages)
                {
                    u.ProviderName = ResolveDisplayName(provider.Definition, u.ProviderId, u.ProviderName);
                    u.AuthSource = config.AuthSource;
                    u.ResponseLatencyMs = stopwatch.Elapsed.TotalMilliseconds;
                    progressCallback?.Invoke(u);
                }

                this._logger.LogDebug($"Success for {config.ProviderId}: {usages.Count()} items");
                return usages;
            }
            catch (ArgumentException ex)
            {
                this._logger.LogWarning($"Skipping {config.ProviderId}: {ex.Message}");
                var errorUsage = new ProviderUsage
                {
                    ProviderId = config.ProviderId,
                    ProviderName = defaults.DisplayName,
                    Description = ex.Message,
                    RequestsPercentage = 0,
                    IsAvailable = false,
                    IsQuotaBased = defaults.IsQuotaBased,
                    PlanType = defaults.PlanType,
                    ResponseLatencyMs = stopwatch.Elapsed.TotalMilliseconds,
                };
                progressCallback?.Invoke(errorUsage);
                return new List<ProviderUsage> { errorUsage };
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, $"Failed to fetch usage for {config.ProviderId}");
                var errorUsage = new ProviderUsage
                {
                    ProviderId = config.ProviderId,
                    ProviderName = defaults.DisplayName,
                    Description = $"[Error] {ex.Message}",
                    RequestsPercentage = 0,
                    IsAvailable = true, // Still available, just failed to fetch
                    IsQuotaBased = defaults.IsQuotaBased,
                    PlanType = defaults.PlanType,
                    HttpStatus = 500, // Mark as error
                    ResponseLatencyMs = stopwatch.Elapsed.TotalMilliseconds,
                };
                progressCallback?.Invoke(errorUsage);
                // Throw exception here to let the caller know it failed (for Check command)
                if (progressCallback == null)
                {
                    throw;
                }

                return new List<ProviderUsage> { errorUsage };
            }
            finally
            {
                this._httpSemaphore.Release();
            }
        }

        var unknownProviderUsage = new ProviderUsage
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
        progressCallback?.Invoke(unknownProviderUsage);
        return new List<ProviderUsage> { unknownProviderUsage };
    }

    private static string ResolveDisplayName(ProviderDefinition definition, string providerId, string? providerName)
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
}
