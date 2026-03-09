// <copyright file="WebServiceCollectionExtensions.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Web.Services
{
    using AIUsageTracker.Core.Interfaces;
    using AIUsageTracker.Infrastructure.Configuration;
    using AIUsageTracker.Infrastructure.Helpers;
    using AIUsageTracker.Infrastructure.Services;
    using Microsoft.Extensions.Caching.Memory;

    internal static class WebServiceCollectionExtensions
    {
        public static IServiceCollection AddAIUsageTrackerWebServices(this IServiceCollection services, string databasePath)
        {
            services.AddSingleton<IAppPathProvider, DefaultAppPathProvider>();
            services.AddSingleton(_ => new WebDatabaseConnectionFactory(databasePath));
            services.AddSingleton(sp =>
            {
                var cache = sp.GetRequiredService<IMemoryCache>();
                var logger = sp.GetRequiredService<ILogger<WebDatabaseService>>();
                var pathProvider = sp.GetRequiredService<IAppPathProvider>();
                var connectionFactory = sp.GetRequiredService<WebDatabaseConnectionFactory>();
                return new WebDatabaseService(cache, logger, pathProvider, connectionFactory, databasePath);
            });
            services.AddSingleton<IWebDatabaseRepository>(sp => sp.GetRequiredService<WebDatabaseService>());
            services.AddSingleton<IUsageAnalyticsService, UsageAnalyticsService>();
            services.AddSingleton<IDataExportService>(sp =>
            {
                var repo = sp.GetRequiredService<IWebDatabaseRepository>();
                var logger = sp.GetRequiredService<ILogger<DataExportService>>();
                var dbPath = sp.GetRequiredService<WebDatabaseConnectionFactory>().GetDatabasePath();
                return new DataExportService(repo, logger, dbPath);
            });
            services.AddSingleton<MonitorProcessService>();
            services.AddSingleton<IConfigLoader, JsonConfigLoader>();
            return services;
        }
    }
}
