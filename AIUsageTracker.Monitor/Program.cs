// <copyright file="Program.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Runtime.InteropServices;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Infrastructure.Extensions;
using AIUsageTracker.Infrastructure.Helpers;
using AIUsageTracker.Infrastructure.Services;
using AIUsageTracker.Monitor.Endpoints;
using AIUsageTracker.Monitor.Hubs;
using AIUsageTracker.Monitor.Logging;
using AIUsageTracker.Monitor.Services;

namespace AIUsageTracker.Monitor;

public partial class Program
{
    private const string DebugBannerSeparator = "═══════════════════════════════════════════════════════════════";

    protected Program()
    {
    }

    public static async Task Main(string[] args)
    {
        bool isDebugMode = args.Contains("--debug", StringComparer.Ordinal);
        IAppPathProvider pathProvider = new DefaultAppPathProvider();
        var holdsStartupMutex = false;

        var resolvedLogPath = MonitorLogPathResolver.Resolve(pathProvider, DateTime.Now);
        if (resolvedLogPath.UsedFallback)
        {
            await Console.Error.WriteLineAsync(
                $"Preferred monitor log directory '{resolvedLogPath.PreferredDirectory}' unavailable. Using fallback '{resolvedLogPath.LogDirectory}'.").ConfigureAwait(false);
        }

        using var loggerFactory = CreateLoggerFactory(isDebugMode, resolvedLogPath.LogFile);

        var logger = loggerFactory.CreateLogger("Monitor");

        if (resolvedLogPath.UsedFallback)
        {
            logger.LogWarning(
                "Preferred monitor log directory {PreferredLogDirectory} unavailable. Using fallback {FallbackLogDirectory}.",
                resolvedLogPath.PreferredDirectory,
                resolvedLogPath.LogDirectory);
        }

        RotateOldLogs(resolvedLogPath.LogDirectory, logger);

        var monitorVersion = System.Reflection.CustomAttributeExtensions
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(Program).Assembly)
            ?.InformationalVersion ?? "unknown";
        logger.LogInformation("=== Monitor starting === (v{Version})", monitorVersion);

        // Machine-wide mutex to prevent concurrent launches
        string mutexName = @"Global\AIUsageTracker_Monitor_" + Environment.UserName;
        bool createdNew;
        using var startupMutex = new Mutex(initiallyOwned: true, name: mutexName, createdNew: out createdNew);
        holdsStartupMutex = createdNew;

        try
        {
            var monitorLauncher = new MonitorLauncher(loggerFactory.CreateLogger<MonitorLauncher>());

            if (!createdNew)
            {
                var mutexAcquired = await TryAcquireStartupMutexAsync(monitorLauncher, startupMutex, logger).ConfigureAwait(false);
                if (!mutexAcquired)
                {
                    return;
                }

                holdsStartupMutex = true;
            }

            MonitorInfoPersistence.SaveMonitorInfo(0, isDebugMode, logger, pathProvider, startupStatus: "starting");

            if (isDebugMode)
            {
                LogDebugStartupBanner(logger, monitorVersion);
            }

            // Reserve the preferred monitor port with retry for transient bind races.
            int port = MonitorPortResolver.ResolveCanonicalPort(preferredPort: 5000, debug: isDebugMode, logger: logger);

            logger.LogDebug("Configuring web host on port {Port}...", port);
            logger.LogDebug("Base Directory: {BaseDir}", AppDomain.CurrentDomain.BaseDirectory);

            var app = await BuildAndStartWebAppAsync(args, port, loggerFactory, pathProvider, logger, isDebugMode).ConfigureAwait(false);

            if (isDebugMode)
            {
                LogDebugReadyBanner(logger, port);
            }

            // Update metadata only after successful bind/start.
            MonitorInfoPersistence.SaveMonitorInfo(port, isDebugMode, logger, pathProvider, startupStatus: "running");
            await app.WaitForShutdownAsync().ConfigureAwait(false);
            MonitorInfoPersistence.SaveMonitorInfo(0, isDebugMode, logger, pathProvider, startupStatus: "stopped");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Monitor startup failed");
            MonitorInfoPersistence.SaveMonitorInfo(0, isDebugMode, logger, pathProvider, startupStatus: $"failed: {ex.Message}");
            throw new InvalidOperationException("Monitor startup failed.", ex);
        }
        finally
        {
            ReleaseStartupMutex(startupMutex, holdsStartupMutex, logger);
        }
    }

    private static ILoggerFactory CreateLoggerFactory(bool isDebugMode, string logFilePath)
    {
        return LoggerFactory.Create(builder =>
        {
            builder
                .SetMinimumLevel(isDebugMode ? LogLevel.Debug : LogLevel.Information)
                .AddProvider(new FileLoggerProvider(logFilePath));
            if (isDebugMode)
            {
                builder.AddConsole();
            }
        });
    }

    private static void RotateOldLogs(string logDirectory, ILogger logger)
    {
        try
        {
            var cutoffDate = DateTime.Now.AddDays(-7);
            foreach (var fileInfo in Directory.GetFiles(logDirectory, "monitor_*.log")
                         .Select(log => new FileInfo(log))
                         .Where(fi => fi.LastWriteTime < cutoffDate))
            {
                fileInfo.Delete();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Log rotation error");
        }
    }

    private static async Task<WebApplication> BuildAndStartWebAppAsync(
        string[] args,
        int port,
        ILoggerFactory loggerFactory,
        IAppPathProvider pathProvider,
        ILogger logger,
        bool isDebugMode)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls($"http://localhost:{port}");

        if (isDebugMode)
        {
            builder.Logging.SetMinimumLevel(LogLevel.Information);
        }

        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.WithOrigins("http://localhost:5100", "http://localhost:5000")
                      .WithMethods("GET", "POST", "DELETE")
                      .WithHeaders("Content-Type", "Authorization", "X-Requested-With")
                      .AllowCredentials();
            });
        });

        builder.Services.AddSignalR();
        builder.Services.ConfigureHttpJsonOptions(options =>
            MonitorJsonSerializer.Configure(options.SerializerOptions));

        RegisterServices(builder, loggerFactory, pathProvider, logger, isDebugMode);

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<UsageDatabase>();
            await db.InitializeAsync().ConfigureAwait(false);
        }

        app.UseCors();
        app.MapHub<UsageHub>("/hubs/usage");

        if (isDebugMode)
        {
            logger.LogDebug("Registering API endpoints...");
        }

        const string contractVersion = MonitorApiContract.CurrentVersion;
        const string minClientContractVersion = MonitorApiContract.MinimumClientVersion;
        var agentVersion = typeof(UsageDatabase).Assembly.GetName().Version?.ToString() ?? "unknown";

        MonitorEndpointsRegistration.MapAll(
            app,
            isDebugMode,
            port,
            agentVersion,
            contractVersion,
            minClientContractVersion,
            args);

        await app.StartAsync().ConfigureAwait(false);
        return app;
    }

    private static void ReleaseStartupMutex(Mutex startupMutex, bool holdsMutex, ILogger logger)
    {
        if (!holdsMutex)
        {
            return;
        }

        try
        {
            startupMutex.ReleaseMutex();
        }
        catch (ApplicationException ex)
        {
            logger.LogDebug(ex, "Startup mutex ownership was lost before shutdown.");
        }
    }

    private static async Task<bool> TryAcquireStartupMutexAsync(MonitorLauncher monitorLauncher, Mutex startupMutex, ILogger logger)
    {
        var existingStatus = await monitorLauncher.GetAgentStatusInfoAsync().ConfigureAwait(false);
        if (existingStatus.IsRunning)
        {
            logger.LogWarning(
                "Monitor is already running on port {Port}. Skipping duplicate startup request.",
                existingStatus.Port);
            return false;
        }

        logger.LogWarning("Startup lock already held and no healthy monitor detected, waiting up to 10 seconds.");
        try
        {
            if (!startupMutex.WaitOne(TimeSpan.FromSeconds(10)))
            {
                var statusAfterWait = await monitorLauncher.GetAgentStatusInfoAsync().ConfigureAwait(false);
                if (statusAfterWait.IsRunning)
                {
                    logger.LogWarning(
                        "Monitor became healthy on port {Port} while waiting. Skipping duplicate startup request.",
                        statusAfterWait.Port);
                    return false;
                }

                logger.LogError(
                    "Timeout waiting for monitor startup lock and no healthy monitor detected. Aborting duplicate startup.");
                return false;
            }

            return true;
        }
        catch (AbandonedMutexException ex)
        {
            logger.LogWarning(ex, "Other Monitor instance exited unexpectedly. Proceeding.");
            return true;
        }
    }

    private static void LogDebugStartupBanner(ILogger logger, string monitorVersion)
    {
        if (OperatingSystem.IsWindows())
        {
            AllocConsole();
        }

        logger.LogInformation(DebugBannerSeparator);
        logger.LogInformation("  AIUsageTracker.Monitor - DEBUG MODE");
        logger.LogInformation(DebugBannerSeparator);
        logger.LogInformation("  Version: {Version} | PID: {ProcessId} | Started: {StartedAt}", monitorVersion, Environment.ProcessId, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
        logger.LogInformation("  OS: {Os} | Runtime: {Runtime}", Environment.OSVersion, Environment.Version);
        logger.LogInformation("  Working Dir: {WorkingDir}", Directory.GetCurrentDirectory());
        logger.LogInformation("  Command Line: {CommandLine}", Environment.CommandLine);
        logger.LogInformation(DebugBannerSeparator);
    }

    private static void LogDebugReadyBanner(ILogger logger, int port)
    {
        logger.LogInformation(DebugBannerSeparator);
        logger.LogInformation("  Agent ready! Listening on http://localhost:{Port}", port);
        logger.LogInformation(DebugBannerSeparator);
        logger.LogInformation(
            "  API Endpoints: GET http://localhost:{HealthPort}{Health} | GET http://localhost:{UsagePort}{Usage} | GET http://localhost:{ConfigPort}{Config} | POST http://localhost:{RefreshPort}{Refresh}",
            port, MonitorApiRoutes.Health,
            port, MonitorApiRoutes.Usage,
            port, MonitorApiRoutes.Config,
            port, MonitorApiRoutes.Refresh);
        logger.LogInformation("  Press Ctrl+C to stop");
        logger.LogInformation(DebugBannerSeparator);
    }

    private static void RegisterServices(WebApplicationBuilder builder, ILoggerFactory loggerFactory, IAppPathProvider pathProvider, ILogger logger, bool isDebugMode)
    {
        if (isDebugMode)
        {
            logger.LogDebug("Registering services...");
        }

        builder.Services.AddSingleton(loggerFactory);
        builder.Services.AddSingleton(pathProvider);
        builder.Services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        builder.Services.AddSingleton<UsageDatabase>();
        builder.Services.AddSingleton<IUsageDatabase>(sp => sp.GetRequiredService<UsageDatabase>());
        builder.Services.AddSingleton<CachedGroupedUsageProjectionService>();
        if (OperatingSystem.IsWindows())
        {
            builder.Services.AddSingleton<INotificationService, WindowsNotificationService>();
        }
        else
        {
            builder.Services.AddSingleton<INotificationService, NoOpNotificationService>();
        }

        builder.Services.AddSingleton<IConfigService, ConfigService>();
        builder.Services.AddSingleton<IGitHubAuthService, GitHubAuthService>();
        builder.Services.AddSingleton<IProviderDiscoveryService, ProviderDiscoveryService>();
        builder.Services.AddProvidersFromAssembly();
        builder.Services.AddSingleton<UsageAlertsService>();
        builder.Services.AddSingleton<ProviderRefreshCircuitBreakerService>();
        builder.Services.AddSingleton<IProviderUsageProcessingPipeline, ProviderUsageProcessingPipeline>();
        builder.Services.AddSingleton<MonitorJobScheduler>();
        builder.Services.AddSingleton<IMonitorJobScheduler>(sp => sp.GetRequiredService<MonitorJobScheduler>());
        builder.Services.AddHostedService(sp => sp.GetRequiredService<MonitorJobScheduler>());
        builder.Services.AddSingleton<ProviderRefreshConfigLoadingService>();
        builder.Services.AddSingleton<ProviderUsagePersistenceService>();
        builder.Services.AddSingleton<ProviderConnectivityCheckService>();
        builder.Services.AddSingleton<ProviderRefreshJobScheduler>();
        builder.Services.AddSingleton<ProviderManagerLifecycleService>();
        builder.Services.AddSingleton<ProviderRefreshNotificationService>();
        builder.Services.AddSingleton<StartupSequenceService>();
        builder.Services.AddSingleton<ProviderRefreshService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<ProviderRefreshService>());

        if (OperatingSystem.IsWindows())
        {
            builder.Services.AddSingleton<PowerStateListener>(sp =>
                new PowerStateListener(
                    sp.GetRequiredService<ILogger<PowerStateListener>>(),
                    sp.GetRequiredService<MonitorJobScheduler>(),
                    sp.GetRequiredService<IAppPathProvider>(),
                    onSuspend: () =>
                    {
                        sp.GetRequiredService<MonitorJobScheduler>().Pause();
                        sp.GetRequiredService<ProviderRefreshService>().CancelActiveRefresh();
                    },
                    onResume: () =>
                    {
                        var scheduler = sp.GetRequiredService<MonitorJobScheduler>();
                        scheduler.Resume();
                        sp.GetRequiredService<ProviderRefreshService>().QueueManualRefresh(forceAll: true);
                    }));
            builder.Services.AddHostedService(sp => sp.GetRequiredService<PowerStateListener>());
        }

        builder.Services.AddHttpClient();
        builder.Services.AddConfiguredHttpClients();
        builder.Services.AddSingleton(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("PlainClient"));
    }

    // P/Invoke to allocate console window
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllocConsole();

    // Compatibility wrapper kept for tests and external callers.
    public static void SaveMonitorInfo(int port, bool debug, ILogger logger, IAppPathProvider pathProvider, string? startupStatus = null)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);
        MonitorInfoPersistence.SaveMonitorInfo(port, debug, logger, pathProvider, startupStatus);
    }

    // Compatibility wrapper kept for tests and external callers.
    public static void ReportError(string message, IAppPathProvider pathProvider, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);
        MonitorInfoPersistence.ReportError(message, pathProvider, logger);
    }
}
