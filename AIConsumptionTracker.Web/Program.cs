using AIConsumptionTracker.Web.Services;
using Serilog;

var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
var logDir = Path.Combine(appData, "AIConsumptionTracker", "Web", "logs");
Directory.CreateDirectory(logDir);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.File(
        Path.Combine(logDir, "web-.log"),
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.Console()
    .CreateLogger();

try
{
    Log.Information("Starting Web UI...");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    builder.Services.AddRazorPages();
    builder.Services.AddSingleton<WebDatabaseService>();
    builder.Services.AddSingleton<AgentProcessService>();
    builder.Services.AddSingleton<AIConsumptionTracker.Core.Interfaces.IConfigLoader, AIConsumptionTracker.Infrastructure.Configuration.JsonConfigLoader>();

    var app = builder.Build();

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error");
        app.UseHsts();
    }

    var isDevelopment = app.Environment.IsDevelopment();
    app.Use(async (context, next) =>
    {
        if (isDevelopment)
        {
            context.Response.Headers.Append("Content-Security-Policy",
                "default-src 'self'; " +
                "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://unpkg.com https://cdn.jsdelivr.net; " +
                "style-src 'self' 'unsafe-inline' https://unpkg.com; " +
                "img-src 'self' data:; " +
                "font-src 'self'; " +
                "connect-src 'self' ws: wss:;");
        }
        else
        {
            context.Response.Headers.Append("Content-Security-Policy",
                "default-src 'self'; " +
                "script-src 'self' 'unsafe-inline' https://unpkg.com https://cdn.jsdelivr.net; " +
                "style-src 'self' 'unsafe-inline' https://unpkg.com; " +
                "img-src 'self' data:; " +
                "font-src 'self'; " +
                "connect-src 'self';");
        }
        await next();
    });

    app.UseHttpsRedirection();
    app.UseStaticFiles();
    app.UseRouting();

    app.MapGet("/api/agent/status", async (AgentProcessService agentService) =>
    {
        var (isRunning, port) = await agentService.GetAgentStatusAsync();
        return Results.Ok(new { isRunning, port });
    });

    app.MapPost("/api/agent/start", async (AgentProcessService agentService) =>
    {
        var success = await agentService.StartAgentAsync();
        return success
            ? Results.Ok(new { message = "Agent started" })
            : Results.BadRequest(new { message = "Failed to start agent" });
    });

    app.MapPost("/api/agent/stop", async (AgentProcessService agentService) =>
    {
        var success = await agentService.StopAgentAsync();
        return success
            ? Results.Ok(new { message = "Agent stopped" })
            : Results.BadRequest(new { message = "Failed to stop agent" });
    });

    app.MapRazorPages();

    var dbService = app.Services.GetRequiredService<WebDatabaseService>();
    if (dbService.IsDatabaseAvailable())
    {
        Log.Information("Web UI connected to database: {ServiceName}", dbService.GetType().Name);
    }
    else
    {
        Log.Warning("Agent database not found. Web UI will show empty data.");
        Log.Warning("Ensure the Agent has run at least once to initialize the database.");
    }

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
