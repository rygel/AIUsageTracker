// <copyright file="Program.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Monitor
{
    using System.Diagnostics;
    using System.Net;
    using System.Net.Sockets;
    using System.Runtime.InteropServices;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using AIUsageTracker.Core.Interfaces;
    using AIUsageTracker.Core.Models;
    using AIUsageTracker.Core.MonitorClient;
    using AIUsageTracker.Infrastructure.Configuration;
    using AIUsageTracker.Infrastructure.Extensions;
    using AIUsageTracker.Infrastructure.Helpers;
    using AIUsageTracker.Infrastructure.Providers;
    using AIUsageTracker.Infrastructure.Services;
    using AIUsageTracker.Monitor.Endpoints;
    using AIUsageTracker.Monitor.Logging;
    using AIUsageTracker.Monitor.Services;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;

    public class Program
    {
        public static async Task Main(string[] args)
        {
            // Check for debug flag early
            bool isDebugMode = args.Contains("--debug");

            // Initialize path provider
            IAppPathProvider pathProvider = new DefaultAppPathProvider();

            // Set up file logging
            var logDir = pathProvider.GetLogDirectory();
            Directory.CreateDirectory(logDir);

            var logFile = Path.Combine(logDir, $"monitor_{DateTime.Now:yyyy-MM-dd}.log");

            // Create a simple logger factory that writes to both console (debug mode) and file
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .SetMinimumLevel(isDebugMode ? LogLevel.Debug : LogLevel.Information)
                    .AddProvider(new FileLoggerProvider(logFile));
                if (isDebugMode)
                {
                    builder.AddConsole();
                }
            });

            var logger = loggerFactory.CreateLogger("Monitor");

            // Rotate logs: keep only last 7 days
            try
            {
                var cutoffDate = DateTime.Now.AddDays(-7);
                foreach (var log in Directory.GetFiles(logDir, "monitor_*.log"))
                {
                    var fileInfo = new FileInfo(log);
                    if (fileInfo.LastWriteTime < cutoffDate)
                    {
                        fileInfo.Delete();
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Log rotation error");
            }

            logger.LogInformation("=== Monitor starting ===");

            // Machine-wide mutex to prevent concurrent launches
            string mutexName = @"Global\AIUsageTracker_Monitor_" + Environment.UserName;
            bool createdNew;
            using var startupMutex = new Mutex(true, mutexName, out createdNew);

            if (!createdNew)
            {
                logger.LogWarning("Another Monitor instance appears to be starting. Waiting for it to complete...");
                try
                {
                    if (!startupMutex.WaitOne(TimeSpan.FromSeconds(10)))
                    {
                        logger.LogError("Timeout waiting for other Monitor instance");
                        return;
                    }
                }
                catch (AbandonedMutexException)
                {
                    logger.LogWarning("Other Monitor instance exited unexpectedly. Proceeding.");
                }
            }

            try
            {
                if (isDebugMode)
                {
                    // Allocate a console window for debugging
                    if (OperatingSystem.IsWindows())
                    {
                        AllocConsole();
                    }

                    logger.LogInformation(string.Empty);
                    logger.LogInformation("═══════════════════════════════════════════════════════════════");
                    logger.LogInformation("  AIUsageTracker.Monitor - DEBUG MODE");
                    logger.LogInformation("═══════════════════════════════════════════════════════════════");
                    logger.LogInformation("  Started:    {StartedAt}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
                    logger.LogInformation("  Process ID: {ProcessId}", Environment.ProcessId);
                    logger.LogInformation("  Working Dir: {WorkingDir}", Directory.GetCurrentDirectory());
                    logger.LogInformation("  OS:         {Os}", Environment.OSVersion);
                    logger.LogInformation("  Runtime:    {Runtime}", Environment.Version);
                    logger.LogInformation("  Command Line: {CommandLine}", Environment.CommandLine);
                    logger.LogInformation("═══════════════════════════════════════════════════════════════");
                    logger.LogInformation(string.Empty);
                }

                // Reserve the canonical monitor port with retry for transient bind races.
                int port = ResolveCanonicalPort(preferredPort: 5000, debug: isDebugMode, logger: logger);

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
                        policy.AllowAnyOrigin()
                              .AllowAnyMethod()
                              .AllowAnyHeader();
                    });
                });

                // Configure JSON serialization with snake_case naming
                builder.Services.ConfigureHttpJsonOptions(options =>
                {
                    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
                    options.SerializerOptions.PropertyNameCaseInsensitive = true;
                    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
                });

                if (isDebugMode)
                {
                    logger.LogDebug("Registering services...");
                }

                builder.Services.AddSingleton(loggerFactory);
                builder.Services.AddSingleton(pathProvider);
                builder.Services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
                builder.Services.AddSingleton<UsageDatabase>();
                builder.Services.AddSingleton<IUsageDatabase>(sp => sp.GetRequiredService<UsageDatabase>());
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
                builder.Services.AddProvidersFromAssembly();
                builder.Services.AddSingleton<ProviderRefreshService>();
                builder.Services.AddHostedService(sp => sp.GetRequiredService<ProviderRefreshService>());

                // Configure HTTP clients with resilience policies
                builder.Services.AddHttpClient();
                builder.Services.AddResilientHttpClient();

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
                    await db.InitializeAsync();
                }

                app.UseCors();

                if (isDebugMode)
                {
                    logger.LogDebug("Registering API endpoints...");
                }

                const string apiContractVersion = "1";
                var agentVersion = typeof(UsageDatabase).Assembly.GetName().Version?.ToString() ?? "unknown";

                MonitorDiagnosticsEndpoints.Map(
                    app,
                    isDebugMode,
                    port,
                    agentVersion,
                    apiContractVersion,
                    args);
                MonitorUsageEndpoints.Map(app);
                MonitorConfigEndpoints.Map(app);
                MonitorHistoryEndpoints.Map(app);

                await app.StartAsync();

                if (isDebugMode)
                {
                    logger.LogInformation(string.Empty);
                    logger.LogInformation("═══════════════════════════════════════════════════════════════");
                    logger.LogInformation("  Agent ready! Listening on http://localhost:{Port}", port);
                    logger.LogInformation("═══════════════════════════════════════════════════════════════");
                    logger.LogInformation(string.Empty);
                    logger.LogInformation("  API Endpoints:");
                    logger.LogInformation("    GET  http://localhost:{Port}{Route}", port, MonitorApiRoutes.Health);
                    logger.LogInformation("    GET  http://localhost:{Port}{Route}", port, MonitorApiRoutes.Usage);
                    logger.LogInformation("    GET  http://localhost:{Port}{Route}", port, MonitorApiRoutes.Config);
                    logger.LogInformation("    POST http://localhost:{Port}{Route}", port, MonitorApiRoutes.Refresh);
                    logger.LogInformation(string.Empty);
                    logger.LogInformation("  Press Ctrl+C to stop");
                    logger.LogInformation("═══════════════════════════════════════════════════════════════");
                    logger.LogInformation(string.Empty);
                }

                // Update metadata only after successful bind/start.
                MonitorInfoPersistence.SaveMonitorInfo(port, isDebugMode, logger, pathProvider, startupStatus: "running");
                await app.WaitForShutdownAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Monitor startup failed");
                MonitorInfoPersistence.SaveMonitorInfo(0, isDebugMode, logger, pathProvider, startupStatus: $"failed: {ex.Message}");
                throw;
            }
            finally
            {
                // Release the startup mutex
                startupMutex?.Dispose();
            }
        }

        // Helper: Resolve canonical monitor port with bind retry (no alternate-port scanning).
        private static int ResolveCanonicalPort(int preferredPort, bool debug, ILogger logger)
        {
            var maxAttempts = 10;
            var attemptDelay = TimeSpan.FromMilliseconds(100);

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    // Try to actually bind to the port
                    using var listener = new TcpListener(IPAddress.Loopback, preferredPort);
                    listener.Start();
                    // Successfully bound - this is our port
                    listener.Stop();
                    if (debug)
                    {
                        logger.LogDebug("Port {Port} is available on attempt {Attempt}", preferredPort, attempt);
                    }

                    return preferredPort;
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
                {
                    if (attempt < maxAttempts)
                    {
                        if (debug)
                        {
                            logger.LogDebug("Port {Port} in use on attempt {Attempt}, retrying...", preferredPort, attempt);
                        }

                        Thread.Sleep(attemptDelay);
                        continue;
                    }

                    logger.LogWarning("Preferred port {Port} is unavailable after {Attempts} attempts.", preferredPort, maxAttempts);
                    break;
                }
            }

            logger.LogWarning("Preferred port {Port} was unavailable; selecting a random high port", preferredPort);
            return GetRandomHighPort(logger);
        }

        // Helper: Get a random high available port.
        private static int GetRandomHighPort(ILogger logger)
        {
            var random = new Random();
            const int minPort = 49152;
            const int maxPort = 65535;
            const int attempts = 200;

            for (int attempt = 0; attempt < attempts; attempt++)
            {
                var candidate = random.Next(minPort, maxPort + 1);
                try
                {
                    using var listener = new TcpListener(IPAddress.Loopback, candidate);
                    listener.Start();
                    listener.Stop();
                    logger.LogInformation("Using random high port {Port}", candidate);
                    return candidate;
                }
                catch (SocketException)
                {
                    // Keep searching.
                }
            }

            throw new InvalidOperationException($"No available high port found in range {minPort}-{maxPort}.");
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
}
