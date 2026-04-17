using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Services;

public sealed class ProviderDiscoveryService : IProviderDiscoveryService
{
    private readonly ILogger<ProviderDiscoveryService> _logger;
    private readonly IAppPathProvider _pathProvider;

    public ProviderDiscoveryService(
        ILogger<ProviderDiscoveryService> logger,
        IAppPathProvider pathProvider)
    {
        this._logger = logger;
        this._pathProvider = pathProvider;
    }

    public async Task<ProviderAuthData?> DiscoverAuthAsync(ProviderAuthDiscoverySpec discoverySpec)
    {
        ArgumentNullException.ThrowIfNull(discoverySpec);

        // 1. Check environment variables
        foreach (var envVar in discoverySpec.DiscoveryEnvironmentVariables)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrWhiteSpace(value))
            {
                this._logger.LogDebug("Discovered auth for {ProviderId} via environment variable {EnvVar}", discoverySpec.ProviderId, envVar);
                return new ProviderAuthData(value);
            }
        }

        // 2. Check auth file candidates
        foreach (var path in ProviderAuthCandidatePathResolver.ResolvePaths(discoverySpec, this._pathProvider))
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var authData = await LoadAuthFromFileAsync(path, discoverySpec.SessionAuthFileSchemas).ConfigureAwait(false);
                if (authData != null)
                {
                    this._logger.LogDebug("Discovered auth for {ProviderId} via file {Path}", discoverySpec.ProviderId, path);
                    return authData with { SourcePath = path };
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                this._logger.LogDebug(ex, "Failed to read auth file for {ProviderId} at {Path}", discoverySpec.ProviderId, path);
            }
        }

        return null;
    }

    public string? GetEnvironmentVariable(string name)
    {
        return Environment.GetEnvironmentVariable(name);
    }

    private static async Task<ProviderAuthData?> LoadAuthFromFileAsync(string path, IEnumerable<ProviderAuthFileSchema> schemas)
    {
        var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        return ProviderAuthFileSchemaReader.Read(doc.RootElement, schemas);
    }
}
