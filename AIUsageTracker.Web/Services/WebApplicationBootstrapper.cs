using Serilog;

namespace AIUsageTracker.Web.Services;

internal static class WebApplicationBootstrapper
{
    public static WebApplication Build(string[] args, string localAppDataRoot)
    {
        var runtimePaths = WebRuntimePathResolver.Resolve(localAppDataRoot);
        ConfigureLogging(runtimePaths);

        Log.Information("Starting Web UI...");

        var builder = WebApplication.CreateBuilder(args);
        builder.Host.UseSerilog();
        builder.Services.AddWebUiDataProtection(builder.Environment, runtimePaths.DataProtectionKeyDirectory);
        builder.Services.AddWebUiInfrastructure();
        builder.Services.AddAIUsageTrackerWebServices(runtimePaths.DatabasePath);

        var app = builder.Build();
        app.ConfigureWebUi();
        app.LogDatabaseAvailability();
        return app;
    }

    private static void ConfigureLogging(WebRuntimePathResolver.WebRuntimePaths runtimePaths)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(runtimePaths.LogDirectory, "web-.log"),
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Console()
            .CreateLogger();
    }
}
