using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageTracker.Monitor.Services;
using AIUsageTracker.Core.Models;
using System.Net;
using System.Net.Sockets;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Infrastructure.Services;
using AIUsageTracker.Infrastructure.Extensions;
using AIUsageTracker.Infrastructure.Helpers;
using AIUsageTracker.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

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
            Program.AllocConsole();
        }
        logger.LogInformation("");
        logger.LogInformation("═══════════════════════════════════════════════════════════════");
        logger.LogInformation("  AIUsageTracker.Monitor - DEBUG MODE");
        logger.LogInformation("═══════════════════════════════════════════════════════════════");
        logger.LogInformation("  Started:    {StartedAt}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        logger.LogInformation("  Process ID: {ProcessId}", Environment.ProcessId);
        logger.LogInformation("  Working Dir: {WorkingDir}", Directory.GetCurrentDirectory());
        logger.LogInformation("  OS:         {Os}", Environment.OSVersion);
        logger.LogInformation("  Runtime:    {Runtime}", Environment.Version);
        logger.LogInformation("  Command Line: {CommandLine}", Environment.CommandLine);
        logger.LogInformation("═══════════════════════════════════════════════════════════════");
        logger.LogInformation("");
    }

    // Find available port with retry logic for bind races
    int port = FindAvailablePortWithRetry(preferredPort: 5000, debug: isDebugMode, logger: logger);
    if (port != 5000)
    {
        logger.LogInformation("Port 5000 was in use, using port {Port} instead", port);
    }

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

if (isDebugMode) logger.LogDebug("Registering services...");
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

// Health endpoint (check if agent is running)
app.MapGet("/api/health", (ILogger<Program> logger) => 
{
    if (isDebugMode) logger.LogDebug("GET /api/health");
    return Results.Ok(new { 
        status = "healthy", 
        timestamp = DateTime.UtcNow,
        port = port,
        processId = Environment.ProcessId,
        agentVersion = agentVersion,
        apiContractVersion = apiContractVersion
    });
});

// Diagnostics endpoint
app.MapGet("/api/diagnostics", (EndpointDataSource endpointDataSource, ProviderRefreshService refreshService, ILogger<Program> logger) => 
{
    if (isDebugMode) logger.LogDebug("GET /api/diagnostics");

    var apiEndpoints = endpointDataSource.Endpoints
        .OfType<RouteEndpoint>()
        .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) == true)
        .GroupBy(endpoint => endpoint.RoutePattern.RawText!, StringComparer.OrdinalIgnoreCase)
        .Select(group => new
        {
            route = group.Key,
            methods = group
                .SelectMany(endpoint => endpoint.Metadata
                    .OfType<HttpMethodMetadata>()
                    .SelectMany(metadata => metadata.HttpMethods))
                .Where(method => !string.IsNullOrWhiteSpace(method))
                .Select(method => method.ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(method => method)
                .ToArray()
        })
        .OrderBy(endpoint => endpoint.route)
        .ToList();

    return Results.Ok(new {
        port = port,
        processId = Environment.ProcessId,
        workingDir = Directory.GetCurrentDirectory(),
        baseDir = AppDomain.CurrentDomain.BaseDirectory,
        startedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        os = Environment.OSVersion.ToString(),
        runtime = Environment.Version.ToString(),
        args = args,
        endpoints = apiEndpoints,
        refreshTelemetry = refreshService.GetRefreshTelemetrySnapshot()
    });
});

// Provider usage endpoints
app.MapGet("/api/usage", async (UsageDatabase db, IConfigService configService, ILogger<Program> logger) =>
{
    var usage = await db.GetLatestHistoryAsync();

    var configs = await configService.GetConfigsAsync();
    var hasCodexSession = configs.Any(c =>
        c.ProviderId.Equals("codex", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(c.ApiKey));
    var hasExplicitOpenAiApiKey = configs.Any(c =>
        c.ProviderId.Equals("openai", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(c.ApiKey) &&
        c.ApiKey.StartsWith("sk-", StringComparison.OrdinalIgnoreCase));

    if (hasCodexSession && !hasExplicitOpenAiApiKey)
    {
        usage = usage
            .Where(u => !u.ProviderId.Equals("openai", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    logger.LogDebug("GET /api/usage returning {Count} providers: {Providers}", 
        usage.Count, string.Join(", ", usage.Select(u => u.ProviderId)));
    
    return Results.Ok(usage);
});

app.MapGet("/api/usage/{providerId}", async (string providerId, UsageDatabase db, ILogger<Program> logger) =>
{
    logger.LogDebug("GET /api/usage/{ProviderId}", providerId);
    var usage = await db.GetHistoryByProviderAsync(providerId, 1);
    var result = usage.FirstOrDefault();
    return result != null ? Results.Ok(result) : Results.NotFound();
});

app.MapPost("/api/refresh", async ([FromServices] ProviderRefreshService refreshService, ILogger<Program> logger) =>
{
    logger.LogDebug("POST /api/refresh");
    await refreshService.TriggerRefreshAsync();
    return Results.Ok(new { message = "Refresh triggered" });
});

app.MapPost("/api/notifications/test", ([FromServices] INotificationService notificationService, ILogger<Program> logger) =>
{
    logger.LogDebug("POST /api/notifications/test");
    notificationService.ShowNotification(
        "AI Usage Tracker",
        "This is a test notification from Slim Settings.",
        "openSettings",
        "notifications");
    return Results.Ok(new { message = "Test notification sent" });
});

// Config endpoints
app.MapGet("/api/config", async (IConfigService configService, ILogger<Program> logger) =>
{
    logger.LogDebug("GET /api/config");
    var configs = await configService.GetConfigsAsync();
    return Results.Ok(configs);
});

app.MapPost("/api/config", async (ProviderConfig config, IConfigService configService, ILogger<Program> logger) =>
{
    logger.LogDebug("POST /api/config ({ProviderId})", config.ProviderId);
    await configService.SaveConfigAsync(config);
    return Results.Ok(new { message = "Config saved" });
});

app.MapDelete("/api/config/{providerId}", async (string providerId, IConfigService configService, ILogger<Program> logger) =>
{
    logger.LogDebug("DELETE /api/config/{ProviderId}", providerId);
    await configService.RemoveConfigAsync(providerId);
    return Results.Ok(new { message = "Config removed" });
});

// Preferences endpoints (deprecated: legacy compatibility only)
const string preferencesApiDeprecationMessage =
    "/api/preferences is deprecated and reserved for legacy clients; UI preferences must be managed locally by each UI.";
const string preferencesApiSunsetDate = "Wed, 31 Dec 2026 00:00:00 GMT";

app.MapGet("/api/preferences", async (HttpContext httpContext, IConfigService configService, ILogger<Program> logger) =>
{
    logger.LogDebug("GET /api/preferences");
    httpContext.Response.Headers.Append("Deprecation", "true");
    httpContext.Response.Headers.Append("Sunset", preferencesApiSunsetDate);
    var prefs = await configService.GetPreferencesAsync();
    return Results.Ok(prefs);
})
.WithMetadata(new ObsoleteAttribute(preferencesApiDeprecationMessage));

app.MapPost("/api/preferences", async (HttpContext httpContext, AppPreferences preferences, IConfigService configService, ILogger<Program> logger) =>
{
    logger.LogDebug("POST /api/preferences");
    httpContext.Response.Headers.Append("Deprecation", "true");
    httpContext.Response.Headers.Append("Sunset", preferencesApiSunsetDate);
    await configService.SavePreferencesAsync(preferences);
    return Results.Ok(new { message = "Preferences saved" });
})
.WithMetadata(new ObsoleteAttribute(preferencesApiDeprecationMessage));

// Scan for keys endpoint
app.MapPost("/api/scan-keys", async ([FromServices] IConfigService configService, [FromServices] ProviderRefreshService refreshService, ILogger<Program> logger) =>
{
    logger.LogDebug("POST /api/scan-keys");
    var discovered = await configService.ScanForKeysAsync();
    logger.LogDebug("Discovered {Count} keys", discovered.Count);

    // Immediately refresh so newly discovered keys appear in /api/usage within seconds
    _ = Task.Run(async () => await refreshService.TriggerRefreshAsync(forceAll: true));

    return Results.Ok(new { discovered = discovered.Count, configs = discovered });
});

// History endpoints
app.MapGet("/api/history", async (UsageDatabase db, int? limit, ILogger<Program> logger) =>
{
    logger.LogDebug("GET /api/history (limit={Limit})", limit ?? 100);
    var history = await db.GetHistoryAsync(limit ?? 100);
    return Results.Ok(history);
});

app.MapGet("/api/history/{providerId}", async (string providerId, UsageDatabase db, int? limit, ILogger<Program> logger) =>
{
    logger.LogDebug("GET /api/history/{ProviderId}", providerId);
    var history = await db.GetHistoryByProviderAsync(providerId, limit ?? 100);
    return Results.Ok(history);
});

// Reset events endpoint
app.MapGet("/api/resets/{providerId}", async (string providerId, UsageDatabase db, int? limit, ILogger<Program> logger) =>
{
    logger.LogDebug("GET /api/resets/{ProviderId}", providerId);
    var resets = await db.GetResetEventsAsync(providerId, limit ?? 50);
    return Results.Ok(resets);
});

    await app.StartAsync();

    if (isDebugMode)
    {
        logger.LogInformation("");
        logger.LogInformation("═══════════════════════════════════════════════════════════════");
        logger.LogInformation("  Agent ready! Listening on http://localhost:{Port}", port);
        logger.LogInformation("═══════════════════════════════════════════════════════════════");
        logger.LogInformation("");
        logger.LogInformation("  API Endpoints:");
        logger.LogInformation("    GET  http://localhost:{Port}/api/health", port);
        logger.LogInformation("    GET  http://localhost:{Port}/api/usage", port);
        logger.LogInformation("    GET  http://localhost:{Port}/api/config", port);
        logger.LogInformation("    POST http://localhost:{Port}/api/refresh", port);
        logger.LogInformation("");
        logger.LogInformation("  Press Ctrl+C to stop");
        logger.LogInformation("═══════════════════════════════════════════════════════════════");
        logger.LogInformation("");
    }

    // Update metadata only after successful bind/start.
    Program.SaveMonitorInfo(port, isDebugMode, logger, pathProvider, startupStatus: "running");
    await app.WaitForShutdownAsync();
}
catch (Exception ex)
{
    logger.LogError(ex, "Monitor startup failed");
    Program.SaveMonitorInfo(0, isDebugMode, logger, pathProvider, startupStatus: $"failed: {ex.Message}");
    throw;
}
finally
{
    // Release the startup mutex
    startupMutex?.Dispose();
}

// Helper: Find available port with actual bind retry (handles race conditions)
static int FindAvailablePortWithRetry(int preferredPort, bool debug, ILogger logger)
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
            if (debug) logger.LogDebug("Port {Port} is available on attempt {Attempt}", preferredPort, attempt);
            return preferredPort;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            if (attempt < maxAttempts)
            {
                if (debug) logger.LogDebug("Port {Port} in use on attempt {Attempt}, retrying...", preferredPort, attempt);
                Thread.Sleep(attemptDelay);
            }
            else
            {
                if (debug) logger.LogDebug("Port {Port} still in use after {MaxAttempts} attempts, trying alternatives", preferredPort, maxAttempts);
            }
        }
    }

    // Fall back to scanning available ports
    if (debug) logger.LogDebug("Port {Port} unavailable after retries, scanning for alternatives...", preferredPort);

    // Try ports 5001-5010 with actual bind
    for (int port = 5001; port <= 5010; port++)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            if (debug) logger.LogDebug("Found available port {Port}", port);
            return port;
        }
        catch (SocketException)
        {
            // Port not available, try next
        }
    }

    // Fall back to random available port
    var randomPort = GetRandomAvailablePort();
    if (debug) logger.LogDebug("Using random port {Port}", randomPort);
    return randomPort;
}

// Helper: Get random available port
static int GetRandomAvailablePort()
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
}

// Define partial Program class to hold static methods needed by other files
public partial class Program 
{
    // P/Invoke to allocate console window
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool AllocConsole();

    // Helper: Save monitor info for UI to discover
    public static void SaveMonitorInfo(int port, bool debug, ILogger logger, IAppPathProvider pathProvider, string? startupStatus = null)
    {
        try
        {
            var info = new MonitorInfo
            {
                Port = port,
                StartedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ProcessId = Environment.ProcessId,
                DebugMode = debug,
                Errors = new List<string>(),
                MachineName = Environment.MachineName,
                UserName = Environment.UserName
            };

            // Add startup status if provided
            if (!string.IsNullOrEmpty(startupStatus))
            {
                info.Errors ??= new List<string>();
                info.Errors.Add($"Startup status: {startupStatus}");
            }

            var json = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
            foreach (var infoPath in GetMonitorInfoCandidatePaths(pathProvider))
            {
                try
                {
                    var directory = Path.GetDirectoryName(infoPath);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.WriteAllText(infoPath, json);
                }
                catch (Exception ex)
                {
                    logger?.LogDebug(ex, "Failed to write monitor info to {MonitorInfoPath}", infoPath);
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to save agent info");
        }
    }

    // Helper: Report error to agent info
    public static void ReportError(string message, IAppPathProvider pathProvider, ILogger? logger = null)
    {
        try
        {
            var jsonFile = GetExistingAgentInfoPath(pathProvider);

            if (!string.IsNullOrWhiteSpace(jsonFile) && File.Exists(jsonFile))
            {
                var json = File.ReadAllText(jsonFile);
                var info = JsonSerializer.Deserialize<MonitorInfo>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (info != null)
                {
                    info.Errors ??= new List<string>();
                    info.Errors.Add(message);
                    var updatedJson = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(jsonFile, updatedJson);
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error reporting failed");
        }
    }

    private static string? GetExistingAgentInfoPath(IAppPathProvider pathProvider)
    {
        return GetMonitorInfoCandidatePaths(pathProvider)
            .Where(File.Exists)
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .FirstOrDefault();
    }

    private static IEnumerable<string> GetMonitorInfoCandidatePaths(IAppPathProvider pathProvider)
    {
        var appDataRoot = pathProvider.GetAppDataRoot();
        var userProfileRoot = pathProvider.GetUserProfileRoot();

        return new[]
        {
            Path.Combine(appDataRoot, "monitor.json"),
            Path.Combine(appDataRoot, "Monitor", "monitor.json"),
            Path.Combine(appDataRoot, "Agent", "monitor.json"),
            Path.Combine(userProfileRoot, ".ai-consumption-tracker", "monitor.json"),
            Path.Combine(userProfileRoot, ".opencode", "monitor.json")
        };
    }
}

// File logger provider for writing logs to file
public class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logFile;

    public FileLoggerProvider(string logFile)
    {
        _logFile = logFile;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(_logFile, categoryName);
    }

    public void Dispose() { }
}

public class FileLogger : ILogger
{
    private readonly string _logFile;
    private readonly string _categoryName;
    private static readonly object _lock = new();

    public FileLogger(string logFile, string categoryName)
    {
        _logFile = logFile;
        _categoryName = categoryName;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

    private static string GetLevelString(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRCE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFO ",
        LogLevel.Warning => "WARN ",
        LogLevel.Error => "ERROR",
        LogLevel.Critical => "CRIT ",
        LogLevel.None => "    ",
        _ => level.ToString().ToUpperInvariant().PadRight(5)
    };

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        var levelStr = GetLevelString(logLevel);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var categoryShort = _categoryName.Length > 30 
            ? _categoryName.Substring(_categoryName.Length - 30) 
            : _categoryName.PadRight(30);
        
        var logEntry = $"{timestamp} {levelStr} {categoryShort} | {message}";
        
        if (exception != null)
        {
            logEntry += Environment.NewLine + exception;
        }

        lock (_lock)
        {
            try
            {
                File.AppendAllText(_logFile, logEntry + Environment.NewLine);
            }
            catch (Exception)
            {
                // Intentionally suppressed: File logging failure in custom logger.
                // Cannot log this error anywhere since logging itself failed.
                // This prevents recursive logging attempts and ensures the application continues.
            }
        }
    }
}
