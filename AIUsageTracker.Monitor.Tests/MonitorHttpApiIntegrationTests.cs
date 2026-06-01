// <copyright file="MonitorHttpApiIntegrationTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Monitor.Endpoints;
using AIUsageTracker.Monitor.Hubs;
using AIUsageTracker.Monitor.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AIUsageTracker.Monitor.Tests;

public sealed class MonitorHttpApiIntegrationTests : IAsyncLifetime
{
    private WebApplication? _app;
    private HttpClient? _client;
    private UsageDatabase? _database;
    private TestAppPathProvider? _pathProvider;

    [Fact]
    public async Task GetHealth_ReturnsHealthySnapshot()
    {
        var response = await this.Client.GetAsync(MonitorApiRoutes.Health);
        var payload = await response.Content.ReadFromJsonAsync<MonitorHealthSnapshot>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("healthy", payload.Status);
        Assert.False(string.IsNullOrWhiteSpace(payload.ContractVersion));
    }

    [Fact]
    public async Task GetUsage_ReturnsSeededProviderData()
    {
        await this.SeedUsageAsync("openai");

        var response = await this.Client.GetAsync(MonitorApiRoutes.Usage);
        var payload = await response.Content.ReadFromJsonAsync<List<ProviderUsage>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Contains(payload, usage => string.Equals(usage.ProviderId, "openai", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetUsageByProvider_ReturnsNotFound_WhenProviderMissing()
    {
        var response = await this.Client.GetAsync(MonitorApiRoutes.UsageByProvider("missing-provider"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostRefresh_ReturnsQueuedResponse()
    {
        var response = await this.Client.PostAsync(MonitorApiRoutes.Refresh, content: null);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(json.RootElement.TryGetProperty("queued", out _));
        Assert.True(json.RootElement.TryGetProperty("message", out _));
    }

    [Fact]
    public async Task PostConfig_ReturnsBadRequest_WhenProviderIdMissing()
    {
        var response = await this.Client.PostAsJsonAsync(
            MonitorApiRoutes.Config,
            new ProviderConfig
            {
                ProviderId = string.Empty,
                ApiKey = "x",
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostConfig_SavesAndReturnsOk()
    {
        var config = new ProviderConfig
        {
            ProviderId = "openai",
            ApiKey = "test-key",
        };

        var postResponse = await this.Client.PostAsJsonAsync(MonitorApiRoutes.Config, config);
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);

        var getResponse = await this.Client.GetAsync(MonitorApiRoutes.Config);
        var configs = await getResponse.Content.ReadFromJsonAsync<List<ProviderConfig>>();

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.NotNull(configs);
        Assert.Contains(configs, c =>
            string.Equals(c.ProviderId, "openai", StringComparison.Ordinal) &&
            string.Equals(c.ApiKey, "test-key", StringComparison.Ordinal));
    }

    public async Task InitializeAsync()
    {
        this._pathProvider = new TestAppPathProvider();
        this._database = new UsageDatabase(NullLogger<UsageDatabase>.Instance, this._pathProvider);
        await this._database.InitializeAsync();

        var configService = new InMemoryConfigService();
        var refreshService = CreateRefreshService(this._database, configService, this._pathProvider);
        var projectionService = new CachedGroupedUsageProjectionService(this._database, configService);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSignalR();

        builder.Services.AddSingleton(this._database);
        builder.Services.AddSingleton<IUsageDatabase>(this._database);
        builder.Services.AddSingleton<IConfigService>(configService);
        builder.Services.AddSingleton(refreshService);
        builder.Services.AddSingleton(new ProviderRefreshCircuitBreakerService(NullLogger<ProviderRefreshCircuitBreakerService>.Instance));
        builder.Services.AddSingleton(projectionService);
        builder.Services.AddSingleton(Mock.Of<INotificationService>());
        builder.Services.AddSingleton(Mock.Of<IMonitorJobScheduler>(scheduler =>
            scheduler.GetSnapshot() == new MonitorJobSchedulerSnapshot()));

        this._app = builder.Build();
        this._app.MapHub<UsageHub>("/hubs/usage");
        MonitorEndpointsRegistration.MapAll(
            this._app,
            isDebugMode: false,
            port: 5000,
            agentVersion: "test",
            contractVersion: MonitorApiContract.CurrentVersion,
            minClientContractVersion: MonitorApiContract.MinimumClientVersion,
            args: []);

        await this._app.StartAsync();
        this._client = this._app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        this._client?.Dispose();

        if (this._app != null)
        {
            await this._app.StopAsync();
            await this._app.DisposeAsync();
        }

        this._pathProvider?.Dispose();
    }

    private HttpClient Client => this._client ?? throw new InvalidOperationException("Client not initialized.");

    private async Task SeedUsageAsync(string providerId)
    {
        ArgumentNullException.ThrowIfNull(this._database);

        var config = new ProviderConfig
        {
            ProviderId = providerId,
            ApiKey = "seed-key",
        };

        await this._database.StoreProviderAsync(config, providerId);
        await this._database.StoreHistoryAsync(
        [
            new ProviderUsage
            {
                ProviderId = providerId,
                ProviderName = providerId,
                IsAvailable = true,
                RequestsUsed = 10,
                RequestsAvailable = 100,
                UsedPercent = 10,
                Description = "seed",
                FetchedAt = DateTime.UtcNow,
            },
        ]);
    }

    private static ProviderRefreshService CreateRefreshService(
        IUsageDatabase database,
        IConfigService configService,
        IAppPathProvider pathProvider)
    {
        var hubContext = new Mock<IHubContext<UsageHub>>();
        var hubClients = new Mock<IHubClients>();
        hubClients.Setup(c => c.All).Returns(Mock.Of<IClientProxy>());
        hubContext.Setup(c => c.Clients).Returns(hubClients.Object);

        var monitorJobScheduler = new Mock<IMonitorJobScheduler>();
        monitorJobScheduler
            .Setup(s => s.Enqueue(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<MonitorJobPriority>(),
                It.IsAny<string?>()))
            .Returns(true);
        monitorJobScheduler
            .Setup(s => s.GetSnapshot())
            .Returns(new MonitorJobSchedulerSnapshot());

        var refreshScheduler = new ProviderRefreshJobScheduler(
            monitorJobScheduler.Object,
            NullLogger<ProviderRefreshJobScheduler>.Instance);
        var circuitBreaker = new ProviderRefreshCircuitBreakerService(NullLogger<ProviderRefreshCircuitBreakerService>.Instance);
        var configLoadingService = new ProviderRefreshConfigLoadingService(
            configService,
            database,
            NullLogger<ProviderRefreshConfigLoadingService>.Instance);
        var persistenceService = new ProviderUsagePersistenceService(
            database,
            NullLogger<ProviderUsagePersistenceService>.Instance);
        var processingPipeline = new ProviderUsageProcessingPipeline(NullLogger<ProviderUsageProcessingPipeline>.Instance);
        var connectivityCheckService = new ProviderConnectivityCheckService(configService, processingPipeline);
        var lifecycleService = new ProviderManagerLifecycleService(
            NullLogger<ProviderManagerLifecycleService>.Instance,
            NullLoggerFactory.Instance,
            configService,
            pathProvider,
            Enumerable.Empty<IProviderService>());
        var alertsService = new UsageAlertsService(
            NullLogger<UsageAlertsService>.Instance,
            database,
            Mock.Of<INotificationService>(),
            configService);
        var notificationService = new ProviderRefreshNotificationService(alertsService, hubContext.Object);
        var startupSequenceService = new StartupSequenceService(
            refreshScheduler,
            configService,
            pathProvider,
            NullLogger<StartupSequenceService>.Instance);

        return new ProviderRefreshService(
            NullLogger<ProviderRefreshService>.Instance,
            database,
            Mock.Of<INotificationService>(),
            configService,
            pathProvider,
            circuitBreaker,
            configLoadingService,
            persistenceService,
            connectivityCheckService,
            refreshScheduler,
            lifecycleService,
            notificationService,
            startupSequenceService,
            processingPipeline);
    }

    private sealed class InMemoryConfigService : IConfigService
    {
        private readonly List<ProviderConfig> _configs = [];
        private AppPreferences _preferences = new();

        public Task<IReadOnlyList<ProviderConfig>> GetConfigsAsync()
        {
            return Task.FromResult<IReadOnlyList<ProviderConfig>>(this._configs.ToList());
        }

        public Task SaveConfigAsync(ProviderConfig config)
        {
            var index = this._configs.FindIndex(c =>
                string.Equals(c.ProviderId, config.ProviderId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                this._configs[index] = config;
            }
            else
            {
                this._configs.Add(config);
            }

            return Task.CompletedTask;
        }

        public Task RemoveConfigAsync(string providerId)
        {
            this._configs.RemoveAll(c => string.Equals(c.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
            return Task.CompletedTask;
        }

        public Task<AppPreferences> GetPreferencesAsync()
        {
            return Task.FromResult(this._preferences);
        }

        public Task SavePreferencesAsync(AppPreferences preferences)
        {
            this._preferences = preferences;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ProviderConfig>> ScanForKeysAsync()
        {
            return Task.FromResult<IReadOnlyList<ProviderConfig>>(Array.Empty<ProviderConfig>());
        }
    }

    private sealed class TestAppPathProvider : IAppPathProvider, IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "AIUsageTracker.Monitor.Tests", Guid.NewGuid().ToString("N"));

        public TestAppPathProvider()
        {
            Directory.CreateDirectory(this._root);
        }

        public string GetAppDataRoot() => this._root;

        public string GetDatabasePath() => Path.Combine(this._root, "usage.db");

        public string GetLogDirectory() => this._root;

        public string GetAuthFilePath() => Path.Combine(this._root, "auth.json");

        public string GetPreferencesFilePath() => Path.Combine(this._root, "preferences.json");

        public string GetProviderConfigFilePath() => Path.Combine(this._root, "providers.json");

        public string GetMonitorInfoFilePath() => Path.Combine(this._root, "monitor.json");

        public string GetUserProfileRoot() => this._root;

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(this._root))
                {
                    Directory.Delete(this._root, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
