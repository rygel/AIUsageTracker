using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIConsumptionTracker.Agent.Services;
using AIConsumptionTracker.Core.Models;
using System.Net;
using System.Net.Sockets;

// Check for debug flag early
bool isDebugMode = args.Contains("--debug");

if (isDebugMode)
{
    // Allocate a console window for debugging
    AllocConsole();
    Console.WriteLine("[DEBUG] AIConsumptionTracker.Agent started in debug mode");
    Console.WriteLine("[DEBUG] Press Ctrl+C to stop");
}

// Find available port (handle port conflicts)
int port = FindAvailablePort(5000);
if (port != 5000)
{
    Console.WriteLine($"[INFO] Port 5000 was in use, using port {port} instead");
}

// Save port info for UI to discover
SavePortInfo(port);

var builder = WebApplication.CreateBuilder(args);

// Configure URLs with the available port
builder.WebHost.UseUrls($"http://localhost:{port}");

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

builder.Services.AddSingleton<UsageDatabase>();
builder.Services.AddSingleton<ConfigService>();
builder.Services.AddHostedService<ProviderRefreshService>();
builder.Services.AddHttpClient();

var app = builder.Build();

app.UseCors();

// Health endpoint (check if agent is running)
app.MapGet("/api/health", () => Results.Ok(new { 
    status = "healthy", 
    timestamp = DateTime.UtcNow,
    port = port
}));

// Provider usage endpoints
app.MapGet("/api/usage", async (UsageDatabase db) =>
{
    var usage = await db.GetLatestHistoryAsync();
    // Filter out Antigravity completely
    var filtered = usage.Where(u => u.ProviderId != "antigravity").ToList();
    return Results.Ok(filtered);
});

app.MapGet("/api/usage/{providerId}", async (string providerId, UsageDatabase db) =>
{
    var usage = await db.GetHistoryByProviderAsync(providerId, 1);
    var result = usage.FirstOrDefault();
    return result != null ? Results.Ok(result) : Results.NotFound();
});

app.MapPost("/api/refresh", async (ProviderRefreshService refreshService) =>
{
    await refreshService.TriggerRefreshAsync();
    return Results.Ok(new { message = "Refresh triggered" });
});

// Config endpoints
app.MapGet("/api/config", async (ConfigService configService) =>
{
    var configs = await configService.GetConfigsAsync();
    return Results.Ok(configs);
});

app.MapPost("/api/config", async (ProviderConfig config, ConfigService configService) =>
{
    await configService.SaveConfigAsync(config);
    return Results.Ok(new { message = "Config saved" });
});

app.MapDelete("/api/config/{providerId}", async (string providerId, ConfigService configService) =>
{
    await configService.RemoveConfigAsync(providerId);
    return Results.Ok(new { message = "Config removed" });
});

// Preferences endpoints
app.MapGet("/api/preferences", async (ConfigService configService) =>
{
    var prefs = await configService.GetPreferencesAsync();
    return Results.Ok(prefs);
});

app.MapPost("/api/preferences", async (AppPreferences preferences, ConfigService configService) =>
{
    await configService.SavePreferencesAsync(preferences);
    return Results.Ok(new { message = "Preferences saved" });
});

// Scan for keys endpoint
app.MapPost("/api/scan-keys", async (ConfigService configService) =>
{
    var discovered = await configService.ScanForKeysAsync();
    return Results.Ok(new { discovered = discovered.Count, configs = discovered });
});

// History endpoints
app.MapGet("/api/history", async (UsageDatabase db, int? limit) =>
{
    var history = await db.GetHistoryAsync(limit ?? 100);
    return Results.Ok(history);
});

app.MapGet("/api/history/{providerId}", async (string providerId, UsageDatabase db, int? limit) =>
{
    var history = await db.GetHistoryByProviderAsync(providerId, limit ?? 100);
    return Results.Ok(history);
});

// Reset events endpoint
app.MapGet("/api/resets/{providerId}", async (string providerId, UsageDatabase db, int? limit) =>
{
    var resets = await db.GetResetEventsAsync(providerId, limit ?? 50);
    return Results.Ok(resets);
});

if (isDebugMode)
{
    Console.WriteLine("[DEBUG] Starting web server...");
}

app.Run();

// Helper: Find an available port starting from preferred port
static int FindAvailablePort(int preferredPort)
{
    // Try preferred port first
    if (IsPortAvailable(preferredPort))
    {
        return preferredPort;
    }
    
    // Try ports 5001-5010
    for (int port = 5001; port <= 5010; port++)
    {
        if (IsPortAvailable(port))
        {
            return port;
        }
    }
    
    // Fall back to random available port
    return GetRandomAvailablePort();
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

// Helper: Save port info for UI discovery
static void SavePortInfo(int port)
{
    try
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var agentDir = Path.Combine(appData, "AIConsumptionTracker", "Agent");
        Directory.CreateDirectory(agentDir);
        
        var portFile = Path.Combine(agentDir, "agent.port");
        File.WriteAllText(portFile, port.ToString());
        
        // Also save with timestamp for debugging
        var infoFile = Path.Combine(agentDir, "agent.info");
        var info = new
        {
            Port = port,
            StartedAt = DateTime.UtcNow.ToString("O"),
            ProcessId = Environment.ProcessId
        };
        File.WriteAllText(infoFile, JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[WARNING] Failed to save port info: {ex.Message}");
    }
}

// P/Invoke to allocate console window
[DllImport("kernel32.dll", SetLastError = true)]
static extern bool AllocConsole();
