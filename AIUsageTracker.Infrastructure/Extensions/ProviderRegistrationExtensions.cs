using System.Reflection;
using AIUsageTracker.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Extensions;

public static class ProviderRegistrationExtensions
{
    /// <summary>
    /// Automatically registers all IProviderService implementations from the Infrastructure assembly
    /// </summary>
    public static IServiceCollection AddProvidersFromAssembly(this IServiceCollection services)
    {
        // Get the Infrastructure assembly (where providers are located)
        var assembly = Assembly.GetExecutingAssembly();

        // Find all types that implement IProviderService
        var providerTypes = assembly.GetTypes()
            .Where(t => t.IsClass
                && !t.IsAbstract
                && typeof(IProviderService).IsAssignableFrom(t))
            .ToList();

        foreach (var providerType in providerTypes)
        {
            // Register as singleton since providers are stateless and can be reused
            services.AddSingleton(typeof(IProviderService), providerType);
        }

        return services;
    }

    /// <summary>
    /// Registers a specific provider type
    /// </summary>
    public static IServiceCollection AddProvider<TProvider>(this IServiceCollection services)
        where TProvider : class, IProviderService
    {
        services.AddSingleton<IProviderService, TProvider>();
        return services;
    }
}
