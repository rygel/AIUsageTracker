using System.IO.Compression;

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Paths;
using AIUsageTracker.Infrastructure.Helpers;
using AIUsageTracker.Infrastructure.Services;
using AIUsageTracker.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.FileProviders;
using Serilog;

var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
var appRoot = AppPathCatalog.GetCanonicalAppDataRoot(appData);
var logDir = AppPathCatalog.GetCanonicalLogDirectory(appData);
var dataProtectionKeyDirectory = Path.Combine(appRoot, "web-data-protection");

Directory.CreateDirectory(appRoot);
Directory.CreateDirectory(logDir);
Directory.CreateDirectory(dataProtectionKeyDirectory);

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
    var dataProtectionBuilder = builder.Services.AddDataProtection()
        .SetApplicationName("AIUsageTracker.Web")
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyDirectory));

    if (!builder.Environment.IsDevelopment())
    {
        dataProtectionBuilder.ProtectKeysWithDpapi();
    }

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
        options.Providers.Add<GzipCompressionProvider>();
        options.Providers.Add<BrotliCompressionProvider>();
        options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
        {
            "application/javascript",
            "application/json",
            "text/css",
            "text/html",
            "image/svg+xml",
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

    // Infrastructure & Domain Services
    builder.Services.AddSingleton<IAppPathProvider, DefaultAppPathProvider>();
    builder.Services.AddSingleton<WebDatabaseService>();
    builder.Services.AddSingleton<IWebDatabaseRepository>(sp => sp.GetRequiredService<WebDatabaseService>());
    builder.Services.AddSingleton<IUsageAnalyticsService, UsageAnalyticsService>();
    builder.Services.AddSingleton<IDataExportService>(sp =>
    {
        var repo = sp.GetRequiredService<IWebDatabaseRepository>();
        var logger = sp.GetRequiredService<ILogger<DataExportService>>();
        var dbPath = sp.GetRequiredService<WebDatabaseService>().GetDatabasePath();
        return new DataExportService(repo, logger, dbPath);
    });

    builder.Services.AddSingleton<MonitorProcessService>();
    builder.Services.AddSingleton<IConfigLoader, AIUsageTracker.Infrastructure.Configuration.JsonConfigLoader>();

    var app = builder.Build();

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error");
        app.UseHsts();
    }

    ApplyContentSecurityPolicy(app);

    app.UseHttpsRedirection();
    app.UseResponseCompression();
    app.UseOutputCache();
    ConfigureStaticFiles(app);

    app.UseRouting();

    MapMonitorRoutes(app, "/api/monitor");
    MapMonitorRoutes(app, "/api/agent");
    MapExportRoutes(app);

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

static void ApplyContentSecurityPolicy(WebApplication app)
{
    var contentSecurityPolicy = app.Environment.IsDevelopment()
        ? "default-src 'self'; " +
          "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://unpkg.com https://cdn.jsdelivr.net; " +
          "style-src 'self' 'unsafe-inline' https://unpkg.com; " +
          "img-src 'self' data:; " +
          "font-src 'self'; " +
          "connect-src 'self' ws: wss:;"
        : "default-src 'self'; " +
          "script-src 'self' 'unsafe-inline' https://unpkg.com https://cdn.jsdelivr.net; " +
          "style-src 'self' 'unsafe-inline' https://unpkg.com; " +
          "img-src 'self' data:; " +
          "font-src 'self'; " +
          "connect-src 'self';";

    app.Use(async (context, next) =>
    {
        context.Response.Headers.Append("Content-Security-Policy", contentSecurityPolicy);
        await next().ConfigureAwait(false);
    });
}

static void ConfigureStaticFiles(WebApplication app)
{
    var webRootCandidates = new[]
    {
        Path.Combine(app.Environment.ContentRootPath, "wwwroot"),
        Path.Combine(AppContext.BaseDirectory, "wwwroot"),
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
                if (ShouldCacheStaticAsset(extension))
                {
                    context.Context.Response.Headers.CacheControl = "public,max-age=604800";
                }
            },
        });
        Log.Information("Serving static assets from: {WebRootPath}", webRootPath);
        return;
    }

    Log.Warning("No wwwroot directory found; static assets may be unavailable.");
    app.UseStaticFiles();
}

static void MapMonitorRoutes(WebApplication app, string routePrefix)
{
    app.MapGet($"{routePrefix}/status", GetMonitorStatusAsync);
    app.MapPost($"{routePrefix}/start", StartMonitorAsync);
    app.MapPost($"{routePrefix}/stop", StopMonitorAsync);
}

static void MapExportRoutes(WebApplication app)
{
    app.MapGet("/api/export/csv", ExportCsvAsync);
    app.MapGet("/api/export/json", ExportJsonAsync);
    app.MapGet("/api/export/backup", ExportBackupAsync);
}

static bool ShouldCacheStaticAsset(string? extension)
{
    return string.Equals(extension, ".css", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(extension, ".js", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(extension, ".svg", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(extension, ".ico", StringComparison.OrdinalIgnoreCase);
}

static async Task<IResult> GetMonitorStatusAsync(MonitorProcessService agentService)
{
    var (isRunning, port, message, error) = await agentService.GetAgentStatusDetailedAsync().ConfigureAwait(false);
    return Results.Ok(new { isRunning, port, message, error });
}

static async Task<IResult> StartMonitorAsync(MonitorProcessService agentService)
{
    var (success, message) = await agentService.StartAgentDetailedAsync().ConfigureAwait(false);
    return CreateMessageResult(success, message);
}

static async Task<IResult> StopMonitorAsync(MonitorProcessService agentService)
{
    var (success, message) = await agentService.StopAgentDetailedAsync().ConfigureAwait(false);
    return CreateMessageResult(success, message);
}

static async Task<IResult> ExportCsvAsync(IDataExportService exportService)
{
    var csv = await exportService.ExportHistoryToCsvAsync().ConfigureAwait(false);
    if (string.IsNullOrEmpty(csv))
    {
        return Results.NotFound("No data to export");
    }

    return Results.Text(csv, "text/csv", System.Text.Encoding.UTF8);
}

static async Task<IResult> ExportJsonAsync(IDataExportService exportService)
{
    var json = await exportService.ExportHistoryToJsonAsync().ConfigureAwait(false);
    return Results.Text(json, "application/json", System.Text.Encoding.UTF8);
}

static async Task<IResult> ExportBackupAsync(IDataExportService exportService)
{
    var backup = await exportService.CreateDatabaseBackupAsync().ConfigureAwait(false);
    if (backup == null)
    {
        return Results.NotFound("No database to backup");
    }

    return Results.File(backup, "application/octet-stream", $"usage_backup_{DateTime.Now:yyyyMMdd_HHmmss}.db");
}

static IResult CreateMessageResult(bool success, string message)
{
    return success
        ? Results.Ok(new { message })
        : Results.BadRequest(new { message });
}

public partial class Program
{
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
    }
}
