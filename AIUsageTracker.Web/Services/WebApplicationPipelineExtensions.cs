// <copyright file="WebApplicationPipelineExtensions.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using Microsoft.Extensions.FileProviders;
using Serilog;

namespace AIUsageTracker.Web.Services;

internal static class WebApplicationPipelineExtensions
{
    public static void ConfigureWebUi(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        app.ApplyWebContentSecurityPolicy();
        app.UseHttpsRedirection();
        app.UseResponseCompression();
        app.UseOutputCache();
        app.ConfigureWebStaticFiles();
        app.UseRouting();

        WebApiEndpointMapper.MapMonitorRoutes(app, "/api/monitor");
        WebApiEndpointMapper.MapMonitorRoutes(app, "/api/agent");
        WebApiEndpointMapper.MapExportRoutes(app);
        app.MapRazorPages();
    }

    public static void LogDatabaseAvailability(this WebApplication app)
    {
        var dbService = app.Services.GetRequiredService<WebDatabaseService>();
        if (dbService.IsDatabaseAvailable())
        {
            Log.Information("Web UI connected to database: {ServiceName}", dbService.GetType().Name);
        }
        else
        {
            Log.Warning("Monitor database not found. Web UI will show empty data. Ensure the Monitor has run at least once to initialize the database.");
        }
    }

    public static void ApplyWebContentSecurityPolicy(this WebApplication app)
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

    public static void ConfigureWebStaticFiles(this WebApplication app)
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

    private static bool ShouldCacheStaticAsset(string? extension)
    {
        return string.Equals(extension, ".css", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".js", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".svg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".ico", StringComparison.OrdinalIgnoreCase);
    }
}
