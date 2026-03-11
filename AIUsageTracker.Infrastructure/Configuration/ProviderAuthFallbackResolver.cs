// <copyright file="ProviderAuthFallbackResolver.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Infrastructure.Configuration;

internal sealed class ProviderAuthFallbackResolver : IProviderAuthFallbackResolver
{
    private readonly IReadOnlyList<string> _environmentVariableNames;

    public ProviderAuthFallbackResolver(string providerId, IReadOnlyList<string> environmentVariableNames)
    {
        this.ProviderId = providerId;
        this._environmentVariableNames = environmentVariableNames;
    }

    public string ProviderId { get; }

    public ProviderConfig? Resolve(
        IReadOnlyDictionary<string, string> environmentVariables,
        IReadOnlyList<ProviderConfig> discoveredConfigs)
    {
        foreach (var environmentVariableName in this._environmentVariableNames)
        {
            if (!environmentVariables.TryGetValue(environmentVariableName, out var value) ||
                string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (!ProviderMetadataCatalog.TryCreateDefaultConfig(
                    this.ProviderId,
                    out var envConfig,
                    apiKey: value,
                    authSource: AuthSource.FromEnvironmentVariable(environmentVariableName),
                    description: "Discovered via Environment Variable"))
            {
                return null;
            }

            return envConfig;
        }

        return discoveredConfigs.FirstOrDefault(config =>
            string.Equals(config.ProviderId, this.ProviderId, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(config.ApiKey) &&
            IsRooFallbackSource(config.AuthSource));
    }

    private static bool IsRooFallbackSource(string? authSource)
    {
        return AuthSource.IsRooOrKilo(authSource);
    }
}
