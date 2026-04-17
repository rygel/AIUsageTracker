// <copyright file="WebStartupServiceExtensions.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.IO.Compression;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.ResponseCompression;

namespace AIUsageTracker.Web.Services;

internal static class WebStartupServiceExtensions
{
    public static IServiceCollection AddWebUiInfrastructure(this IServiceCollection services)
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
                "image/svg+xml",
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

        return services;
    }

    public static IDataProtectionBuilder AddWebUiDataProtection(
        this IServiceCollection services,
        IWebHostEnvironment environment,
        string dataProtectionKeyDirectory)
    {
        var dataProtectionBuilder = services.AddDataProtection()
            .SetApplicationName("AIUsageTracker.Web")
            .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyDirectory));

        if (!environment.IsDevelopment() && OperatingSystem.IsWindows())
        {
            dataProtectionBuilder.ProtectKeysWithDpapi();
        }

        return dataProtectionBuilder;
    }
}
