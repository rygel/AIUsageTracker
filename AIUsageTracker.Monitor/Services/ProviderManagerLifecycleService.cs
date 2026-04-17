// <copyright file="ProviderManagerLifecycleService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Services;
using AIUsageTracker.Infrastructure.Configuration;

namespace AIUsageTracker.Monitor.Services;

public sealed class ProviderManagerLifecycleService : IDisposable
{
    private readonly ILogger<ProviderManagerLifecycleService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IConfigService _configService;
    private readonly IAppPathProvider _pathProvider;
    private readonly IReadOnlyList<IProviderService> _providers;
    private ProviderManager? _providerManager;

    public ProviderManagerLifecycleService(
        ILogger<ProviderManagerLifecycleService> logger,
        ILoggerFactory loggerFactory,
        IConfigService configService,
        IAppPathProvider pathProvider,
        IEnumerable<IProviderService> providers)
    {
        this._logger = logger;
        this._loggerFactory = loggerFactory;
        this._configService = configService;
        this._pathProvider = pathProvider;
        this._providers = providers.ToList();
    }

    public ProviderManager? CurrentManager => Volatile.Read(ref this._providerManager);

    public int CurrentMaxConcurrency { get; private set; } = ProviderManager.DefaultMaxConcurrentProviderRequests;

    public async Task<int> GetConfiguredMaxConcurrentProviderRequestsAsync()
    {
        var preferences = await this._configService.GetPreferencesAsync().ConfigureAwait(false);
        return ProviderManager.ClampMaxConcurrentProviderRequests(preferences.MaxConcurrentProviderRequests);
    }

    public async Task EnsureConcurrencyAsync()
    {
        var configuredConcurrency = await this.GetConfiguredMaxConcurrentProviderRequestsAsync().ConfigureAwait(false);
        if (configuredConcurrency == this.CurrentMaxConcurrency)
        {
            return;
        }

        this._logger.LogInformation(
            "Updating provider request concurrency limit from {Previous} to {Current}.",
            this.CurrentMaxConcurrency,
            configuredConcurrency);
        this.Initialize(configuredConcurrency);
    }

    public void Initialize(int maxConcurrentProviderRequests)
    {
        this._logger.LogDebug("Initializing providers...");

        var configLoader = new JsonConfigLoader(
            this._loggerFactory.CreateLogger<JsonConfigLoader>(),
            this._loggerFactory.CreateLogger<TokenDiscoveryService>(),
            this._pathProvider);

        var newProviderManager = new ProviderManager(
            this._providers,
            configLoader,
            this._loggerFactory.CreateLogger<ProviderManager>(),
            maxConcurrentProviderRequests);
        var previousProviderManager = Interlocked.Exchange(ref this._providerManager, newProviderManager);
        this.CurrentMaxConcurrency = maxConcurrentProviderRequests;
        previousProviderManager?.Dispose();

        this._logger.LogDebug(
            "Initialized {Count} providers at max concurrency {MaxConcurrency}: {Providers}",
            this._providers.Count,
            maxConcurrentProviderRequests,
            string.Join(", ", this._providers.Select(provider => provider.ProviderId)));

        this._logger.LogInformation("Loaded {Count} providers", this._providers.Count);
    }

    public void Dispose()
    {
        Interlocked.Exchange<ProviderManager?>(ref this._providerManager, null)?.Dispose();
    }
}
