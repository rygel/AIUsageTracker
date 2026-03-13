using System.Text.Json;

using Microsoft.Extensions.Logging;

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Paths;
using AIUsageTracker.Infrastructure.Configuration;

namespace AIUsageTracker.Infrastructure.Services;

public sealed class ProviderDiscoveryService : IProviderDiscoveryService
{
    private readonly ILogger<ProviderDiscoveryService> _logger;

    public ProviderDiscoveryService(ILogger<ProviderDiscoveryService> logger)
    {
        this._logger = logger;
    }

    public async Task<ProviderAuthData?> DiscoverAuthAsync(ProviderDefinition definition)
    {
        // 1. Check environment variables
        foreach (var envVar in definition.DiscoveryEnvironmentVariables)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrWhiteSpace(value))
            {
                this._logger.LogDebug("Discovered auth for {ProviderId} via environment variable {EnvVar}", definition.ProviderId, envVar);
                return new ProviderAuthData(value);
            }
        }

        // 2. Check auth file candidates
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var pathTemplate in definition.AuthIdentityCandidatePathTemplates)
        {
            var path = AuthPathTemplateResolver.Resolve(pathTemplate, userProfile);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                continue;
            }

            try
            {
                var authData = await LoadAuthFromFileAsync(path, definition.SessionAuthFileSchemas);
                if (authData != null)
                {
                    this._logger.LogDebug("Discovered auth for {ProviderId} via file {Path}", definition.ProviderId, path);
                    return authData with { SourcePath = path };
                }
            }
            catch (Exception ex)
            {
                this._logger.LogDebug(ex, "Failed to read auth file for {ProviderId} at {Path}", definition.ProviderId, path);
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
        var json = await File.ReadAllTextAsync(path);
        using var doc = JsonDocument.Parse(json);
        return ProviderAuthFileSchemaReader.Read(doc.RootElement, schemas);
    }
}
