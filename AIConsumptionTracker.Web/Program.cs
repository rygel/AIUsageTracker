using AIConsumptionTracker.Web.Services;
using Microsoft.Extensions.FileProviders;
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

    var webRootCandidates = new[]
    {
        Path.Combine(app.Environment.ContentRootPath, "wwwroot"),
        Path.Combine(AppContext.BaseDirectory, "wwwroot")
    };
    var webRootPath = webRootCandidates.FirstOrDefault(Directory.Exists);

    if (!string.IsNullOrWhiteSpace(webRootPath))
    {
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(webRootPath)
        });
        Log.Information("Serving static assets from: {WebRootPath}", webRootPath);
    }
    else
    {
        Log.Warning("No wwwroot directory found; static assets may be unavailable.");
        app.UseStaticFiles();
    }

    app.UseRouting();

    app.MapGet("/api/monitor/status", async (AgentProcessService agentService) =>
    {
        var (isRunning, port) = await agentService.GetAgentStatusAsync();
        return Results.Ok(new { isRunning, port });
    });

    app.MapGet("/api/agent/status", async (AgentProcessService agentService) =>
    {
        var (isRunning, port) = await agentService.GetAgentStatusAsync();
        return Results.Ok(new { isRunning, port });
    });

    app.MapPost("/api/monitor/start", async (AgentProcessService agentService) =>
    {
        var success = await agentService.StartAgentAsync();
        return success
            ? Results.Ok(new { message = "Monitor started" })
            : Results.BadRequest(new { message = "Failed to start monitor" });
    });

    app.MapPost("/api/agent/start", async (AgentProcessService agentService) =>
    {
        var success = await agentService.StartAgentAsync();
        return success
            ? Results.Ok(new { message = "Monitor started" })
            : Results.BadRequest(new { message = "Failed to start monitor" });
    });

    app.MapPost("/api/monitor/stop", async (AgentProcessService agentService) =>
    {
        var success = await agentService.StopAgentAsync();
        return success
            ? Results.Ok(new { message = "Monitor stopped" })
            : Results.BadRequest(new { message = "Failed to stop monitor" });
    });

    app.MapPost("/api/agent/stop", async (AgentProcessService agentService) =>
    {
        var success = await agentService.StopAgentAsync();
        return success
            ? Results.Ok(new { message = "Monitor stopped" })
            : Results.BadRequest(new { message = "Failed to stop monitor" });
    });

    app.MapRazorPages();

    var dbService = app.Services.GetRequiredService<WebDatabaseService>();
    if (dbService.IsDatabaseAvailable())
    {
        Log.Information("Web UI connected to database: {ServiceName}", dbService.GetType().Name);
    }
    else
    {
        Log.Warning("Monitor database not found. Web UI will show empty data.");
        Log.Warning("Ensure the Monitor has run at least once to initialize the database.");
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
