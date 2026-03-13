// <copyright file="ProviderRegistrationExtensions.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Reflection;

using AIUsageTracker.Core.Interfaces;

using Microsoft.Extensions.DependencyInjection;

namespace AIUsageTracker.Infrastructure.Extensions;

public static class ProviderRegistrationExtensions
{
    /// <summary>
    /// Automatically registers all IProviderService implementations from the Infrastructure assembly.
    /// </summary>
    /// <returns></returns>
    public static IServiceCollection AddProvidersFromAssembly(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var providerTypes = assembly.GetTypes()
            .Where(t => t.IsClass
                && !t.IsAbstract
                && typeof(IProviderService).IsAssignableFrom(t))
            .ToList();

        foreach (var providerType in providerTypes)
        {
            services.AddSingleton(typeof(IProviderService), providerType);
        }

        return services;
    }

    /// <summary>
    /// Registers a specific provider type.
    /// </summary>
    /// <returns></returns>
    public static IServiceCollection AddProvider<TProvider>(this IServiceCollection services)
        where TProvider : class, IProviderService
    {
        services.AddSingleton<IProviderService, TProvider>();
        return services;
    }
}
