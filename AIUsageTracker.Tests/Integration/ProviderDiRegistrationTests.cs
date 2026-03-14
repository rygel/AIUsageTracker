// <copyright file="ProviderDiRegistrationTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Reflection;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Extensions;
using AIUsageTracker.Infrastructure.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AIUsageTracker.Tests.Integration;

/// <summary>
/// Tests that verify provider DI registration works correctly.
/// These tests would have caught the HttpClient registration issue in beta.33.
/// </summary>
public class ProviderDiRegistrationTests
{
    [Fact]
    public void AddProvidersFromAssembly_RegistersAllProviders()
    {
        // Arrange
        var services = new ServiceCollection();
        ConfigureRequiredServices(services);

        // Act
        services.AddProvidersFromAssembly();
        var provider = services.BuildServiceProvider();

        // Assert
        var providers = provider.GetServices<IProviderService>().ToList();
        Assert.NotEmpty(providers);

        // Verify we have the expected number of providers
        var expectedProviderTypes = GetExpectedProviderTypes();
        Assert.Equal(expectedProviderTypes.Count, providers.Count);
    }

    [Fact]
    public void AllProviders_CanBeResolved_FromDiContainer()
    {
        // Arrange
        var services = new ServiceCollection();
        ConfigureRequiredServices(services);
        services.AddProvidersFromAssembly();

        // Act
        var provider = services.BuildServiceProvider();

        // Assert - each provider should resolve without throwing
        var resolvedProviders = new List<IProviderService>();
        foreach (var service in provider.GetServices<IProviderService>())
        {
            resolvedProviders.Add(service);
        }

        // All providers should be resolved
        var expectedTypes = GetExpectedProviderTypes();
        Assert.Equal(expectedTypes.Count, resolvedProviders.Count);

        // Verify each expected provider type is present
        foreach (var expectedType in expectedTypes)
        {
            Assert.Contains(resolvedProviders, p => p.GetType() == expectedType);
        }
    }

    [Fact]
    public void ClaudeCodeProvider_RequiresHttpClient_CanBeResolved()
    {
        // Arrange
        var services = new ServiceCollection();
        ConfigureRequiredServices(services);
        services.AddProvidersFromAssembly();

        // Act
        var provider = services.BuildServiceProvider();
        var claudeCode = provider.GetServices<IProviderService>()
            .FirstOrDefault(p => p is ClaudeCodeProvider);

        // Assert
        Assert.NotNull(claudeCode);
        Assert.IsType<ClaudeCodeProvider>(claudeCode);
    }

    [Fact]
    public void KimiProvider_RequiresHttpClient_CanBeResolved()
    {
        // Arrange
        var services = new ServiceCollection();
        ConfigureRequiredServices(services);
        services.AddProvidersFromAssembly();

        // Act
        var provider = services.BuildServiceProvider();
        var kimi = provider.GetServices<IProviderService>()
            .FirstOrDefault(p => p is KimiProvider);

        // Assert
        Assert.NotNull(kimi);
        Assert.IsType<KimiProvider>(kimi);
    }

    [Fact]
    public void OpenAIProvider_RequiresResilientHttpClient_CanBeResolved()
    {
        // Arrange
        var services = new ServiceCollection();
        ConfigureRequiredServices(services);
        services.AddProvidersFromAssembly();

        // Act
        var provider = services.BuildServiceProvider();
        var openai = provider.GetServices<IProviderService>()
            .FirstOrDefault(p => p is OpenAIProvider);

        // Assert
        Assert.NotNull(openai);
        Assert.IsType<OpenAIProvider>(openai);
    }

    private static void ConfigureRequiredServices(IServiceCollection services)
    {
        // Logger
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        // HttpClient via factory (simulates AddHttpClient())
        services.AddHttpClient();

        // Plain HttpClient for providers that need it directly (no Polly retry-on-429)
        services.AddSingleton(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("PlainClient"));

        // Resilient HTTP client
        services.AddResilientHttpClient();

        // Provider discovery service (required by some providers)
        services.AddSingleton<IProviderDiscoveryService, NullProviderDiscoveryService>();

        // GitHub auth service (required by GitHubCopilotProvider)
        services.AddSingleton<IGitHubAuthService, NullGitHubAuthService>();
    }

    private static List<Type> GetExpectedProviderTypes()
    {
        var assembly = typeof(ClaudeCodeProvider).Assembly;
        return assembly.GetTypes()
            .Where(t => t.IsClass
                && !t.IsAbstract
                && typeof(IProviderService).IsAssignableFrom(t))
            .ToList();
    }

    /// <summary>
    /// Null implementation for testing.
    /// </summary>
    private class NullProviderDiscoveryService : IProviderDiscoveryService
    {
        public Task<ProviderAuthData?> DiscoverAuthAsync(ProviderAuthDiscoverySpec spec) => Task.FromResult<ProviderAuthData?>(null);

        public string? GetEnvironmentVariable(string name) => null;
    }

    /// <summary>
    /// Null implementation for testing.
    /// </summary>
    private class NullGitHubAuthService : IGitHubAuthService
    {
        public bool IsAuthenticated => false;

        public Task<(string DeviceCode, string UserCode, string VerificationUri, int ExpiresIn, int Interval)> InitiateDeviceFlowAsync()
            => Task.FromResult(("", "", "", 0, 0));

        public Task<string?> PollForTokenAsync(string deviceCode, int interval) => Task.FromResult<string?>(null);

        public Task<string?> RefreshTokenAsync(string refreshToken) => Task.FromResult<string?>(null);

        public string? GetCurrentToken() => null;

        public void Logout() { }

        public void InitializeToken(string token) { }

        public Task<string?> GetUsernameAsync() => Task.FromResult<string?>(null);
    }
}
