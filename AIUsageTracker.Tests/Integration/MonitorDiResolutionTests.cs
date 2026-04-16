// <copyright file="MonitorDiResolutionTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Extensions;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Infrastructure.Services;
using AIUsageTracker.Monitor.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AIUsageTracker.Tests.Integration;

/// <summary>
/// Mirrors the Monitor/Program.cs DI setup and verifies all critical services resolve.
/// Catches registration gaps that only manifest at runtime (missing bindings, circular deps, etc.).
/// </summary>
public class MonitorDiResolutionTests
{
    [Fact]
    public void AllCriticalMonitorServices_ResolveWithoutThrowing()
    {
        // Arrange — mirror Program.cs ConfigureServices section (lines 207-240)
        var services = new ServiceCollection();

        // Logging
        var loggerFactory = NullLoggerFactory.Instance;
        services.AddSingleton<ILoggerFactory>(loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        // IAppPathProvider — stub that returns temp paths
        var pathProvider = new StubAppPathProvider();
        services.AddSingleton<IAppPathProvider>(pathProvider);

        // Database
        services.AddSingleton<UsageDatabase>();
        services.AddSingleton<IUsageDatabase>(sp => sp.GetRequiredService<UsageDatabase>());
        services.AddSingleton<CachedGroupedUsageProjectionService>();

        // Notification — use NoOp
        services.AddSingleton<INotificationService, NoOpNotificationService>();

        // Config
        services.AddSingleton<IConfigService, ConfigService>();

        // GitHub auth
        services.AddSingleton<IGitHubAuthService, StubGitHubAuthService>();

        // Provider discovery
        services.AddSingleton<IProviderDiscoveryService, StubProviderDiscoveryService>();

        // Providers from assembly
        services.AddProvidersFromAssembly();

        // Alert + circuit breaker + pipeline
        services.AddSingleton<UsageAlertsService>();
        services.AddSingleton<ProviderRefreshCircuitBreakerService>();
        services.AddSingleton<IProviderUsageProcessingPipeline, ProviderUsageProcessingPipeline>();

        // Job scheduler
        services.AddSingleton<MonitorJobScheduler>();
        services.AddSingleton<IMonitorJobScheduler>(sp => sp.GetRequiredService<MonitorJobScheduler>());

        // Refresh sub-services (previously created by factory, now DI-registered)
        services.AddSingleton<ProviderRefreshConfigLoadingService>();
        services.AddSingleton<ProviderUsagePersistenceService>();
        services.AddSingleton<ProviderConnectivityCheckService>();
        services.AddSingleton<ProviderRefreshJobScheduler>();
        services.AddSingleton<ProviderManagerLifecycleService>();
        services.AddSingleton<ProviderRefreshNotificationService>();
        services.AddSingleton<StartupSequenceService>();

        // Refresh service
        services.AddSingleton<ProviderRefreshService>();

        // HTTP clients (same as Program.cs)
        services.AddHttpClient();
        services.AddConfiguredHttpClients();
        services.AddSingleton(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("PlainClient"));

        var provider = services.BuildServiceProvider();

        // Act & Assert — each critical service resolves without throwing
        var database = provider.GetRequiredService<IUsageDatabase>();
        Assert.NotNull(database);
        Assert.IsType<UsageDatabase>(database);

        var refreshService = provider.GetRequiredService<ProviderRefreshService>();
        Assert.NotNull(refreshService);

        var configService = provider.GetRequiredService<IConfigService>();
        Assert.NotNull(configService);
        Assert.IsType<ConfigService>(configService);

        var jobScheduler = provider.GetRequiredService<IMonitorJobScheduler>();
        Assert.NotNull(jobScheduler);
        Assert.IsType<MonitorJobScheduler>(jobScheduler);

        var alertsService = provider.GetRequiredService<UsageAlertsService>();
        Assert.NotNull(alertsService);

        var pipeline = provider.GetRequiredService<IProviderUsageProcessingPipeline>();
        Assert.NotNull(pipeline);
        Assert.IsType<ProviderUsageProcessingPipeline>(pipeline);

        var circuitBreaker = provider.GetRequiredService<ProviderRefreshCircuitBreakerService>();
        Assert.NotNull(circuitBreaker);
    }

    [Fact]
    public void AllProviderServices_ResolveFromFullMonitorDiGraph()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IAppPathProvider>(new StubAppPathProvider());
        services.AddSingleton<IProviderDiscoveryService, StubProviderDiscoveryService>();
        services.AddSingleton<IGitHubAuthService, StubGitHubAuthService>();
        services.AddHttpClient();
        services.AddConfiguredHttpClients();
        services.AddSingleton(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("PlainClient"));
        services.AddProvidersFromAssembly();

        var provider = services.BuildServiceProvider();
        var resolvedProviders = provider.GetServices<IProviderService>().ToList();

        // At minimum we expect ClaudeCodeProvider and CodexProvider
        Assert.Contains(resolvedProviders, p => p is ClaudeCodeProvider);
        Assert.Contains(resolvedProviders, p => p is CodexProvider);
        Assert.True(resolvedProviders.Count >= 2, $"Expected at least 2 providers, got {resolvedProviders.Count}");
    }

    [Fact]
    public void UsageDatabase_ResolvesAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IAppPathProvider>(new StubAppPathProvider());
        services.AddSingleton<UsageDatabase>();
        services.AddSingleton<IUsageDatabase>(sp => sp.GetRequiredService<UsageDatabase>());

        var provider = services.BuildServiceProvider();

        var db1 = provider.GetRequiredService<UsageDatabase>();
        var db2 = provider.GetRequiredService<IUsageDatabase>();

        Assert.Same(db1, db2);
    }

    private sealed class StubAppPathProvider : IAppPathProvider
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "aitracker-test-" + Guid.NewGuid().ToString("N").Substring(0, 8));

        public string GetAppDataRoot() => this._root;

        public string GetDatabasePath() => Path.Combine(this._root, "test.db");

        public string GetLogDirectory() => Path.Combine(this._root, "logs");

        public string GetAuthFilePath() => Path.Combine(this._root, "auth.json");

        public string GetPreferencesFilePath() => Path.Combine(this._root, "preferences.json");

        public string GetProviderConfigFilePath() => Path.Combine(this._root, "providers.json");

        public string GetUserProfileRoot() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        public string GetMonitorInfoFilePath() => Path.Combine(this.GetAppDataRoot(), "monitor.json");
    }

    private sealed class StubProviderDiscoveryService : IProviderDiscoveryService
    {
        public Task<ProviderAuthData?> DiscoverAuthAsync(ProviderAuthDiscoverySpec spec) => Task.FromResult<ProviderAuthData?>(null);

        public string? GetEnvironmentVariable(string name) => null;
    }

    private sealed class StubGitHubAuthService : IGitHubAuthService
    {
        public bool IsAuthenticated => false;

        public Task<(string DeviceCode, string UserCode, string VerificationUri, int ExpiresIn, int Interval)> InitiateDeviceFlowAsync()
            => Task.FromResult((string.Empty, string.Empty, string.Empty, 0, 0));

        public Task<string?> PollForTokenAsync(string deviceCode, int interval) => Task.FromResult<string?>(null);

        public Task<string?> RefreshTokenAsync(string refreshToken) => Task.FromResult<string?>(null);

        public string? GetCurrentToken() => null;

        public void Logout()
        {
        }

        public void InitializeToken(string token)
        {
        }

        public Task<string?> GetUsernameAsync() => Task.FromResult<string?>(null);
    }
}
