using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.FileProviders;
using Serilog;
using System.IO.Compression;
using AIUsageTracker.Web.Services;
using AIUsageTracker.Infrastructure.Services;
using AIUsageTracker.Infrastructure.Helpers;
using AIUsageTracker.Core.Interfaces;

namespace AIUsageTracker.Web
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console()
                .CreateLogger();

            try
            {
                var builder = WebApplication.CreateBuilder(args);
                
                var startup = new Startup(builder.Configuration);
                startup.ConfigureServices(builder.Services);
                
                builder.Host.UseSerilog((context, services, configuration) => configuration
                    .ReadFrom.Configuration(context.Configuration)
                    .ReadFrom.Services(services)
                    .Enrich.FromLogContext()
                    .WriteTo.Console());

                var app = builder.Build();
                startup.Configure(app, app.Environment);
                
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
        }
    }

    public class Startup
    {
        public IConfiguration Configuration { get; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddRazorPages();
            services.AddMemoryCache();
            services.AddOutputCache(options =>
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
            services.AddResponseCompression(options =>
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
                    "image/svg+xml"
                });
            });
            services.Configure<BrotliCompressionProviderOptions>(options =>
            {
                options.Level = CompressionLevel.Fastest;
            });
            services.Configure<GzipCompressionProviderOptions>(options =>
            {
                options.Level = CompressionLevel.Fastest;
            });

            // Infrastructure & Domain Services
            services.AddSingleton<AIUsageTracker.Core.Interfaces.IAppPathProvider, AIUsageTracker.Infrastructure.Helpers.DefaultAppPathProvider>();
            services.AddSingleton<AIUsageTracker.Web.Services.WebDatabaseService>();
            services.AddSingleton<AIUsageTracker.Core.Interfaces.IWebDatabaseRepository>(sp => sp.GetRequiredService<AIUsageTracker.Web.Services.WebDatabaseService>());
            services.AddSingleton<AIUsageTracker.Core.Interfaces.IUsageAnalyticsService, AIUsageTracker.Infrastructure.Services.UsageAnalyticsService>();
            services.AddSingleton<AIUsageTracker.Core.Interfaces.IDataExportService>(sp => 
            {
                var repo = sp.GetRequiredService<AIUsageTracker.Core.Interfaces.IWebDatabaseRepository>();
                var logger = sp.GetRequiredService<ILogger<AIUsageTracker.Infrastructure.Services.DataExportService>>();
                var dbPath = sp.GetRequiredService<AIUsageTracker.Web.Services.WebDatabaseService>().GetDatabasePath();
                return new AIUsageTracker.Infrastructure.Services.DataExportService(repo, logger, dbPath);
            });
            
            services.AddSingleton<AIUsageTracker.Web.Services.MonitorProcessService>();
            services.AddSingleton<AIUsageTracker.Core.Interfaces.IConfigLoader, AIUsageTracker.Infrastructure.Configuration.JsonConfigLoader>();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (!env.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            var isDevelopment = env.IsDevelopment();
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
                Path.Combine(env.ContentRootPath, "wwwroot"),
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
            }
            else
            {
                app.UseStaticFiles();
            }

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/api/monitor/status", async (AIUsageTracker.Web.Services.MonitorProcessService agentService) =>
                {
                    var (isRunning, port, message, error) = await agentService.GetAgentStatusDetailedAsync();
                    return Results.Ok(new { isRunning, port, message, error });
                });

                endpoints.MapGet("/api/agent/status", async (AIUsageTracker.Web.Services.MonitorProcessService agentService) =>
                {
                    var (isRunning, port, message, error) = await agentService.GetAgentStatusDetailedAsync();
                    return Results.Ok(new { isRunning, port, message, error });
                });

                endpoints.MapPost("/api/monitor/start", async (AIUsageTracker.Web.Services.MonitorProcessService agentService) =>
                {
                    var (success, message) = await agentService.StartAgentDetailedAsync();
                    return success
                        ? Results.Ok(new { message })
                        : Results.BadRequest(new { message });
                });

                endpoints.MapPost("/api/agent/start", async (AIUsageTracker.Web.Services.MonitorProcessService agentService) =>
                {
                    var (success, message) = await agentService.StartAgentDetailedAsync();
                    return success
                        ? Results.Ok(new { message })
                        : Results.BadRequest(new { message });
                });

                endpoints.MapPost("/api/monitor/stop", async (AIUsageTracker.Web.Services.MonitorProcessService agentService) =>
                {
                    var (success, message) = await agentService.StopAgentDetailedAsync();
                    return success
                        ? Results.Ok(new { message })
                        : Results.BadRequest(new { message });
                });

                endpoints.MapPost("/api/agent/stop", async (AIUsageTracker.Web.Services.MonitorProcessService agentService) =>
                {
                    var (success, message) = await agentService.StopAgentDetailedAsync();
                    return success
                        ? Results.Ok(new { message })
                        : Results.BadRequest(new { message });
                });

                // Data export endpoints
                endpoints.MapGet("/api/export/csv", async (AIUsageTracker.Core.Interfaces.IDataExportService exportService) =>
                {
                    var csv = await exportService.ExportHistoryToCsvAsync();
                    if (string.IsNullOrEmpty(csv))
                        return Results.NotFound("No data to export");

                    return Results.Text(csv, "text/csv", System.Text.Encoding.UTF8);
                });

                endpoints.MapGet("/api/export/json", async (AIUsageTracker.Core.Interfaces.IDataExportService exportService) =>
                {
                    var json = await exportService.ExportHistoryToJsonAsync();
                    return Results.Text(json, "application/json", System.Text.Encoding.UTF8);
                });

                endpoints.MapGet("/api/export/backup", async (AIUsageTracker.Core.Interfaces.IDataExportService exportService) =>
                {
                    var backup = await exportService.CreateDatabaseBackupAsync();
                    if (backup == null)
                        return Results.NotFound("No database to backup");

                    return Results.File(backup, "application/octet-stream", $"usage_backup_{DateTime.Now:yyyyMMdd_HHmmss}.db");
                });

                endpoints.MapRazorPages();
            });

            var dbService = app.ApplicationServices.GetRequiredService<AIUsageTracker.Web.Services.WebDatabaseService>();
            if (dbService.IsDatabaseAvailable())
            {
                Log.Information("Web UI connected to database: {ServiceName}", dbService.GetType().Name);
            }
            else
            {
                Log.Warning("Monitor database not found. Web UI will show empty data.");
            }
        }
    }
}
