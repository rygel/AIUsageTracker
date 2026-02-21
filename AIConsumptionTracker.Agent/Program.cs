using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIConsumptionTracker.Agent.Services;
using AIConsumptionTracker.Core.Models;
using System.Net;
using System.Net.Sockets;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

// Check for debug flag early
bool isDebugMode = args.Contains("--debug");

if (isDebugMode)
{
    // Allocate a console window for debugging
    Program.AllocConsole();
    Console.WriteLine("");
    Console.WriteLine("═══════════════════════════════════════════════════════════════");
    Console.WriteLine("  AIConsumptionTracker.Agent - DEBUG MODE");
    Console.WriteLine("═══════════════════════════════════════════════════════════════");
    Console.WriteLine($"  Started:    {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
    Console.WriteLine($"  Process ID: {Environment.ProcessId}");
    Console.WriteLine($"  Working Dir: {Directory.GetCurrentDirectory()}");
    Console.WriteLine($"  OS:         {Environment.OSVersion}");
    Console.WriteLine($"  Runtime:    {Environment.Version}");
    Console.WriteLine($"  Command Line: {Environment.CommandLine}");
    Console.WriteLine("═══════════════════════════════════════════════════════════════");
    Console.WriteLine("");
}

// Find available port (handle port conflicts)
int port = FindAvailablePort(5000, isDebugMode);
if (port != 5000)
{
    if (isDebugMode) Console.WriteLine($"[INFO] Port 5000 was in use, using port {port} instead");
}

// Save port info for UI to discover
Program.SaveAgentInfo(port, isDebugMode);

if (isDebugMode)
{
    Console.WriteLine($"[DEBUG] Configuring web host on port {port}...");
    Console.WriteLine($"[DEBUG] Base Directory: {AppDomain.CurrentDomain.BaseDirectory}");
}

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

if (isDebugMode) Console.WriteLine("[DEBUG] Registering services...");
builder.Services.AddSingleton<UsageDatabase>();
builder.Services.AddSingleton<IUsageDatabase>(sp => sp.GetRequiredService<UsageDatabase>());
builder.Services.AddSingleton<INotificationService, WindowsNotificationService>();
builder.Services.AddSingleton<ConfigService>();
builder.Services.AddSingleton<ProviderRefreshService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ProviderRefreshService>());
builder.Services.AddHttpClient();

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
    Console.WriteLine("[DEBUG] Registering API endpoints...");
}

const string apiContractVersion = "1";
var agentVersion = typeof(UsageDatabase).Assembly.GetName().Version?.ToString() ?? "unknown";

// Health endpoint (check if agent is running)
app.MapGet("/api/health", () => 
{
    if (isDebugMode) Console.WriteLine($"[API] GET /api/health - {DateTime.Now:HH:mm:ss}");
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
app.MapGet("/api/diagnostics", (EndpointDataSource endpointDataSource, ProviderRefreshService refreshService) => 
{
    if (isDebugMode) Console.WriteLine($"[API] GET /api/diagnostics - {DateTime.Now:HH:mm:ss}");

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
app.MapGet("/api/usage", async (UsageDatabase db, ConfigService configService) =>
{
    if (isDebugMode) Console.WriteLine($"[API] GET /api/usage - {DateTime.Now:HH:mm:ss}");
    var usage = await db.GetLatestHistoryAsync();
    var configs = await configService.GetConfigsAsync();
    var filteredUsage = UsageVisibilityFilter.FilterForConfiguredProviders(usage, configs);
    if (isDebugMode) Console.WriteLine($"[API] Returning {filteredUsage.Count} providers");
    return Results.Ok(filteredUsage);
});

app.MapGet("/api/usage/{providerId}", async (string providerId, UsageDatabase db) =>
{
    if (isDebugMode) Console.WriteLine($"[API] GET /api/usage/{providerId} - {DateTime.Now:HH:mm:ss}");
    var usage = await db.GetHistoryByProviderAsync(providerId, 1);
    var result = usage.FirstOrDefault();
    return result != null ? Results.Ok(result) : Results.NotFound();
});

app.MapPost("/api/refresh", async ([FromServices] ProviderRefreshService refreshService) =>
{
    if (isDebugMode) Console.WriteLine($"[API] POST /api/refresh - {DateTime.Now:HH:mm:ss}");
    await refreshService.TriggerRefreshAsync();
    return Results.Ok(new { message = "Refresh triggered" });
});

// Config endpoints
app.MapGet("/api/config", async (ConfigService configService) =>
{
    if (isDebugMode) Console.WriteLine($"[API] GET /api/config - {DateTime.Now:HH:mm:ss}");
    var configs = await configService.GetConfigsAsync();
    return Results.Ok(configs);
});

app.MapPost("/api/config", async (ProviderConfig config, ConfigService configService) =>
{
    if (isDebugMode) Console.WriteLine($"[API] POST /api/config ({config.ProviderId}) - {DateTime.Now:HH:mm:ss}");
    await configService.SaveConfigAsync(config);
    return Results.Ok(new { message = "Config saved" });
});

app.MapDelete("/api/config/{providerId}", async (string providerId, ConfigService configService) =>
{
    if (isDebugMode) Console.WriteLine($"[API] DELETE /api/config/{providerId} - {DateTime.Now:HH:mm:ss}");
    await configService.RemoveConfigAsync(providerId);
    return Results.Ok(new { message = "Config removed" });
});

// Preferences endpoints (deprecated: legacy compatibility only)
const string preferencesApiDeprecationMessage =
    "/api/preferences is deprecated and reserved for legacy clients; UI preferences must be managed locally by each UI.";
const string preferencesApiSunsetDate = "Wed, 31 Dec 2026 00:00:00 GMT";

app.MapGet("/api/preferences", async (HttpContext httpContext, ConfigService configService) =>
{
    if (isDebugMode) Console.WriteLine($"[API] GET /api/preferences - {DateTime.Now:HH:mm:ss}");
    httpContext.Response.Headers.Append("Deprecation", "true");
    httpContext.Response.Headers.Append("Sunset", preferencesApiSunsetDate);
    var prefs = await configService.GetPreferencesAsync();
    return Results.Ok(prefs);
})
.WithMetadata(new ObsoleteAttribute(preferencesApiDeprecationMessage));

app.MapPost("/api/preferences", async (HttpContext httpContext, AppPreferences preferences, ConfigService configService) =>
{
    if (isDebugMode) Console.WriteLine($"[API] POST /api/preferences - {DateTime.Now:HH:mm:ss}");
    httpContext.Response.Headers.Append("Deprecation", "true");
    httpContext.Response.Headers.Append("Sunset", preferencesApiSunsetDate);
    await configService.SavePreferencesAsync(preferences);
    return Results.Ok(new { message = "Preferences saved" });
})
.WithMetadata(new ObsoleteAttribute(preferencesApiDeprecationMessage));

// Scan for keys endpoint
app.MapPost("/api/scan-keys", async ([FromServices] ConfigService configService, [FromServices] ProviderRefreshService refreshService) =>
{
    if (isDebugMode) Console.WriteLine($"[API] POST /api/scan-keys - {DateTime.Now:HH:mm:ss}");
    var discovered = await configService.ScanForKeysAsync();
    if (isDebugMode) Console.WriteLine($"[API] Discovered {discovered.Count} keys");

    // Immediately refresh so newly discovered keys appear in /api/usage within seconds
    _ = Task.Run(async () => await refreshService.TriggerRefreshAsync(forceAll: true));

    return Results.Ok(new { discovered = discovered.Count, configs = discovered });
});

// History endpoints
app.MapGet("/api/history", async (UsageDatabase db, int? limit) =>
{
    if (isDebugMode) Console.WriteLine($"[API] GET /api/history (limit={limit ?? 100}) - {DateTime.Now:HH:mm:ss}");
    var history = await db.GetHistoryAsync(limit ?? 100);
    return Results.Ok(history);
});

app.MapGet("/api/history/{providerId}", async (string providerId, UsageDatabase db, int? limit) =>
{
    if (isDebugMode) Console.WriteLine($"[API] GET /api/history/{providerId} - {DateTime.Now:HH:mm:ss}");
    var history = await db.GetHistoryByProviderAsync(providerId, limit ?? 100);
    return Results.Ok(history);
});

// Reset events endpoint
app.MapGet("/api/resets/{providerId}", async (string providerId, UsageDatabase db, int? limit) =>
{
    if (isDebugMode) Console.WriteLine($"[API] GET /api/resets/{providerId} - {DateTime.Now:HH:mm:ss}");
    var resets = await db.GetResetEventsAsync(providerId, limit ?? 50);
    return Results.Ok(resets);
});

if (isDebugMode)
{
    Console.WriteLine("");
    Console.WriteLine("═══════════════════════════════════════════════════════════════");
    Console.WriteLine($"  Agent ready! Listening on http://localhost:{port}");
    Console.WriteLine("═══════════════════════════════════════════════════════════════");
    Console.WriteLine("");
    Console.WriteLine("  API Endpoints:");
    Console.WriteLine($"    GET  http://localhost:{port}/api/health");
    Console.WriteLine($"    GET  http://localhost:{port}/api/usage");
    Console.WriteLine($"    GET  http://localhost:{port}/api/config");
    Console.WriteLine($"    POST http://localhost:{port}/api/refresh");
    Console.WriteLine("");
    Console.WriteLine("  Press Ctrl+C to stop");
    Console.WriteLine("═══════════════════════════════════════════════════════════════");
    Console.WriteLine("");
}

app.Run();

// Helper: Find an available port starting from preferred port
static int FindAvailablePort(int preferredPort, bool debug)
{
    // Try preferred port first
    if (IsPortAvailable(preferredPort))
    {
        if (debug) Console.WriteLine($"[PORT] Port {preferredPort} is available");
        return preferredPort;
    }
    
    if (debug) Console.WriteLine($"[PORT] Port {preferredPort} is in use, trying alternatives...");
    
    // Try ports 5001-5010
    for (int port = 5001; port <= 5010; port++)
    {
        if (IsPortAvailable(port))
        {
            if (debug) Console.WriteLine($"[PORT] Port {port} is available");
            return port;
        }
    }
    
    // Fall back to random available port
    var randomPort = GetRandomAvailablePort();
    if (debug) Console.WriteLine($"[PORT] Using random port {randomPort}");
    return randomPort;
}

// Helper: Check if a port is available
static bool IsPortAvailable(int port)
{
    try
    {
        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        listener.Stop();
        return true;
    }
    catch
    {
        return false;
    }
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

    // Helper: Save agent info for UI to discover
    public static void SaveAgentInfo(int port, bool debug)
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var agentDir = Path.Combine(appData, "AIConsumptionTracker", "Agent");
            Directory.CreateDirectory(agentDir);
            
            var info = new AgentInfo
            {
                Port = port,
                StartedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ProcessId = Environment.ProcessId,
                DebugMode = debug,
                Errors = new List<string>(),
                MachineName = Environment.MachineName,
                UserName = Environment.UserName
            };

            var json = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(agentDir, "agent.json"), json);
        }
        catch (Exception ex)
        {
            if (debug) Console.WriteLine($"[ERROR] Failed to save agent info: {ex.Message}");
        }
    }

    // Helper: Report error to agent info
    public static void ReportError(string message)
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var agentDir = Path.Combine(appData, "AIConsumptionTracker", "Agent");
            var jsonFile = Path.Combine(agentDir, "agent.json");
            
            if (File.Exists(jsonFile))
            {
                var json = File.ReadAllText(jsonFile);
                var info = JsonSerializer.Deserialize<AgentInfo>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (info != null)
                {
                    info.Errors ??= new List<string>();
                    info.Errors.Add(message);
                    var updatedJson = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(jsonFile, updatedJson);
                }
            }
        }
        catch { /* Ignore errors during error reporting */ }
    }
}
