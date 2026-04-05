// <copyright file="Program.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Runtime.InteropServices;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Infrastructure.Configuration;
using AIUsageTracker.Infrastructure.Extensions;
using AIUsageTracker.Infrastructure.Helpers;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Infrastructure.Services;
using AIUsageTracker.Monitor.Endpoints;
using AIUsageTracker.Monitor.Hubs;
using AIUsageTracker.Monitor.Logging;
using AIUsageTracker.Monitor.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Monitor;

public class Program
{
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

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder
                .SetMinimumLevel(isDebugMode ? LogLevel.Debug : LogLevel.Information)
                .AddProvider(new FileLoggerProvider(resolvedLogPath.LogFile));
            if (isDebugMode)
            {
                builder.AddConsole();
            }
        });

        var logger = loggerFactory.CreateLogger("Monitor");

        if (resolvedLogPath.UsedFallback)
        {
            logger.LogWarning(
                "Preferred monitor log directory {PreferredLogDirectory} unavailable. Using fallback {FallbackLogDirectory}.",
                resolvedLogPath.PreferredDirectory,
                resolvedLogPath.LogDirectory);
        }

        // Rotate logs: keep only last 7 days
        try
        {
            var cutoffDate = DateTime.Now.AddDays(-7);
            foreach (var fileInfo in Directory.GetFiles(resolvedLogPath.LogDirectory, "monitor_*.log")
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

        var monitorVersion = System.Reflection.CustomAttributeExtensions
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(Program).Assembly)
            ?.InformationalVersion ?? "unknown";
        logger.LogInformation("=== Monitor starting === (v{Version})", monitorVersion);

        // Machine-wide mutex to prevent concurrent launches
        string mutexName = @"Global\AIUsageTracker_Monitor_" + Environment.UserName;
        bool createdNew;
        using var startupMutex = new Mutex(true, mutexName, out createdNew);
        holdsStartupMutex = createdNew;

        try
        {
            var monitorLauncher = new MonitorLauncher(loggerFactory.CreateLogger<MonitorLauncher>());

            if (!createdNew)
            {
                logger.LogWarning("Monitor startup lock is already held. Checking for existing healthy monitor instance.");
                var existingStatus = await monitorLauncher.GetAgentStatusInfoAsync().ConfigureAwait(false);
                if (existingStatus.IsRunning)
                {
                    logger.LogWarning(
                        "Monitor is already running on port {Port}. Skipping duplicate startup request.",
                        existingStatus.Port);
                    return;
                }

                logger.LogWarning("No healthy monitor instance detected yet. Waiting up to 10 seconds for startup lock.");
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
                            return;
                        }

                        logger.LogError(
                            "Timeout waiting for monitor startup lock and no healthy monitor detected. Aborting duplicate startup.");
                        return;
                    }

                    holdsStartupMutex = true;
                }
                catch (AbandonedMutexException)
                {
                    holdsStartupMutex = true;
                    logger.LogWarning("Other Monitor instance exited unexpectedly. Proceeding.");
                }
            }

            MonitorInfoPersistence.SaveMonitorInfo(0, isDebugMode, logger, pathProvider, startupStatus: "starting");

            if (isDebugMode)
            {
                // Allocate a console window for debugging
                if (OperatingSystem.IsWindows())
                {
                    AllocConsole();
                }

                logger.LogInformation("");
                logger.LogInformation("═══════════════════════════════════════════════════════════════");
                logger.LogInformation("  AIUsageTracker.Monitor - DEBUG MODE");
                logger.LogInformation("═══════════════════════════════════════════════════════════════");
                logger.LogInformation("  Version:    {Version}", monitorVersion);
                logger.LogInformation("  Started:    {StartedAt}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
                logger.LogInformation("  Process ID: {ProcessId}", Environment.ProcessId);
                logger.LogInformation("  Working Dir: {WorkingDir}", Directory.GetCurrentDirectory());
                logger.LogInformation("  OS:         {Os}", Environment.OSVersion);
                logger.LogInformation("  Runtime:    {Runtime}", Environment.Version);
                logger.LogInformation("  Command Line: {CommandLine}", Environment.CommandLine);
                logger.LogInformation("═══════════════════════════════════════════════════════════════");
                logger.LogInformation("");
            }

            // Reserve the canonical monitor port with retry for transient bind races.
            int port = MonitorPortResolver.ResolveCanonicalPort(preferredPort: 5000, debug: isDebugMode, logger: logger);

            logger.LogDebug("Configuring web host on port {Port}...", port);
            logger.LogDebug("Base Directory: {BaseDir}", AppDomain.CurrentDomain.BaseDirectory);

            var builder = WebApplication.CreateBuilder(args);

            // Configure URLs with the available port
            builder.WebHost.UseUrls($"http://localhost:{port}");

            // Suppress default console logging in debug mode (we handle our own)
            if (isDebugMode)
            {
                builder.Logging.SetMinimumLevel(LogLevel.Information);
            }

            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.WithOrigins("http://localhost:5100", "http://localhost:5000") // Explicit origins for SignalR/CORS safety
                          .AllowAnyMethod()
                          .AllowAnyHeader()
                          .AllowCredentials(); // Required for SignalR with WebSockets/Long Polling
                });
            });

            builder.Services.AddSignalR();

            // Configure JSON serialization — delegates to the Core canonical options
            builder.Services.ConfigureHttpJsonOptions(options =>
                MonitorJsonSerializer.Configure(options.SerializerOptions));

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
            builder.Services.AddSingleton<ProviderRefreshConfigSelector>();
            builder.Services.AddSingleton<ProviderRefreshConfigLoadingService>();
            builder.Services.AddSingleton<ProviderUsagePersistenceService>();
            builder.Services.AddSingleton<ProviderConnectivityCheckService>();
            builder.Services.AddSingleton<ProviderRefreshJobScheduler>();
            builder.Services.AddSingleton<ProviderManagerLifecycleService>();
            builder.Services.AddSingleton<ProviderRefreshNotificationService>();
            builder.Services.AddSingleton<StartupSequenceService>();
            builder.Services.AddSingleton<ProviderRefreshService>();
            builder.Services.AddHostedService(sp => sp.GetRequiredService<ProviderRefreshService>());

            // Configure HTTP clients
            builder.Services.AddHttpClient();
            builder.Services.AddConfiguredHttpClients();

            // Register plain HttpClient for providers that need it (e.g., ClaudeCodeProvider, KimiProvider).
            // Uses "PlainClient" — no Polly retry-on-429 policy, so providers control their own retry behavior.
            builder.Services.AddSingleton(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("PlainClient"));

            // Enable debug mode in refresh service
            if (isDebugMode)
            {
                ProviderRefreshService.SetDebugMode(true);
            }

            var app = builder.Build();

            // Async database initialization
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

            if (isDebugMode)
            {
                logger.LogInformation("");
                logger.LogInformation("═══════════════════════════════════════════════════════════════");
                logger.LogInformation("  Agent ready! Listening on http://localhost:{Port}", port);
                logger.LogInformation("═══════════════════════════════════════════════════════════════");
                logger.LogInformation("");
                logger.LogInformation("  API Endpoints:");
                logger.LogInformation("    GET  http://localhost:{Port}{Route}", port, MonitorApiRoutes.Health);
                logger.LogInformation("    GET  http://localhost:{Port}{Route}", port, MonitorApiRoutes.Usage);
                logger.LogInformation("    GET  http://localhost:{Port}{Route}", port, MonitorApiRoutes.Config);
                logger.LogInformation("    POST http://localhost:{Port}{Route}", port, MonitorApiRoutes.Refresh);
                logger.LogInformation("");
                logger.LogInformation("  Press Ctrl+C to stop");
                logger.LogInformation("═══════════════════════════════════════════════════════════════");
                logger.LogInformation("");
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
            throw;
        }
        finally
        {
            if (holdsStartupMutex)
            {
                try
                {
                    startupMutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    logger.LogDebug("Startup mutex ownership was lost before shutdown.");
                }
            }
        }
    }

    // P/Invoke to allocate console window
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool AllocConsole();

    // Compatibility wrapper kept for tests and external callers.
    public static void SaveMonitorInfo(int port, bool debug, ILogger logger, IAppPathProvider pathProvider, string? startupStatus = null)
    {
        MonitorInfoPersistence.SaveMonitorInfo(port, debug, logger, pathProvider, startupStatus);
    }

    // Compatibility wrapper kept for tests and external callers.
    public static void ReportError(string message, IAppPathProvider pathProvider, ILogger? logger = null)
    {
        MonitorInfoPersistence.ReportError(message, pathProvider, logger);
    }
}
