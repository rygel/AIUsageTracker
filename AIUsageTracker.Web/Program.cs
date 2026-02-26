using AIUsageTracker.Web.Services;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.FileProviders;
using Serilog;
using System.IO.Compression;

var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
var logDir = Path.Combine(appData, "AIUsageTracker", "logs");
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
    builder.Services.AddMemoryCache();
    builder.Services.AddOutputCache(options =>
    {
        options.AddPolicy("DashboardCache", policy =>
        {
            policy.Expire(TimeSpan.FromSeconds(15));
            policy.SetVaryByQuery("showUsed", "showInactive");
        });

        options.AddPolicy("ChartsCache", policy =>
        {
            policy.Expire(TimeSpan.FromSeconds(20));
            policy.SetVaryByQuery("hours");
        });
    });
    builder.Services.AddResponseCompression(options =>
    {
        options.EnableForHttps = true;
        options.Providers.Add<BrotliCompressionProvider>();
        options.Providers.Add<GzipCompressionProvider>();
        options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
        {
            "application/javascript",
            "application/json",
            "text/css",
            "text/html",
            "image/svg+xml"
        });
    });
    builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
    {
        options.Level = CompressionLevel.Fastest;
    });
    builder.Services.Configure<GzipCompressionProviderOptions>(options =>
    {
        options.Level = CompressionLevel.Fastest;
    });
    builder.Services.AddSingleton<WebDatabaseService>();
    builder.Services.AddSingleton<MonitorProcessService>();
    builder.Services.AddSingleton<AIUsageTracker.Core.Interfaces.IConfigLoader, AIUsageTracker.Infrastructure.Configuration.JsonConfigLoader>();

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
    app.UseResponseCompression();
    app.UseOutputCache();

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
            FileProvider = new PhysicalFileProvider(webRootPath),
            OnPrepareResponse = context =>
            {
                var extension = Path.GetExtension(context.File.Name);
                if (string.Equals(extension, ".css", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(extension, ".js", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(extension, ".svg", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(extension, ".ico", StringComparison.OrdinalIgnoreCase))
                {
                    context.Context.Response.Headers.CacheControl = "public,max-age=604800";
                }
            }
        });
        Log.Information("Serving static assets from: {WebRootPath}", webRootPath);
    }
    else
    {
        Log.Warning("No wwwroot directory found; static assets may be unavailable.");
        app.UseStaticFiles();
    }

    app.UseRouting();

    app.MapGet("/api/monitor/status", async (MonitorProcessService agentService) =>
    {
        var (isRunning, port, message, error) = await agentService.GetAgentStatusDetailedAsync();
        return Results.Ok(new { isRunning, port, message, error });
    });

    app.MapGet("/api/agent/status", async (MonitorProcessService agentService) =>
    {
        var (isRunning, port, message, error) = await agentService.GetAgentStatusDetailedAsync();
        return Results.Ok(new { isRunning, port, message, error });
    });

    app.MapPost("/api/monitor/start", async (MonitorProcessService agentService) =>
    {
        var (success, message) = await agentService.StartAgentDetailedAsync();
        return success
            ? Results.Ok(new { message })
            : Results.BadRequest(new { message });
    });

    app.MapPost("/api/agent/start", async (MonitorProcessService agentService) =>
    {
        var (success, message) = await agentService.StartAgentDetailedAsync();
        return success
            ? Results.Ok(new { message })
            : Results.BadRequest(new { message });
    });

    app.MapPost("/api/monitor/stop", async (MonitorProcessService agentService) =>
    {
        var (success, message) = await agentService.StopAgentDetailedAsync();
        return success
            ? Results.Ok(new { message })
            : Results.BadRequest(new { message });
    });

    app.MapPost("/api/agent/stop", async (MonitorProcessService agentService) =>
    {
        var (success, message) = await agentService.StopAgentDetailedAsync();
        return success
            ? Results.Ok(new { message })
            : Results.BadRequest(new { message });
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

