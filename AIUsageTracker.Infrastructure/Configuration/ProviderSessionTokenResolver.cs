// <copyright file="ProviderSessionTokenResolver.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Configuration;

internal sealed class ProviderSessionTokenResolver
{
    private readonly ProviderAuthDiscoverySpec _discoverySpec;
    private readonly string _description;
    private readonly string _sourcePrefix;
    private readonly ILogger _logger;
    private readonly IAppPathProvider _pathProvider;

    public ProviderSessionTokenResolver(
        ProviderAuthDiscoverySpec discoverySpec,
        string description,
        string sourcePrefix,
        ILogger logger,
        IAppPathProvider pathProvider)
    {
        this._discoverySpec = discoverySpec;
        this._description = description;
        this._sourcePrefix = sourcePrefix;
        this._logger = logger;
        this._pathProvider = pathProvider;
    }

    public async Task<DiscoveredSessionToken?> TryResolveAsync()
    {
        try
        {
            foreach (var path in this.GetCandidatePaths())
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var token = TryReadAccessToken(doc.RootElement, this._discoverySpec);
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                return new DiscoveredSessionToken(
                    this._discoverySpec.ProviderId,
                    token,
                    this._description,
                    $"{this._sourcePrefix}: {path}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            this._logger.LogDebug(ex, "Session token discovery failed for provider {ProviderId}", this._discoverySpec.ProviderId);
        }

        return null;
    }

    private static string? TryReadAccessToken(JsonElement root, ProviderAuthDiscoverySpec discoverySpec)
    {
        return ProviderAuthFileSchemaReader.Read(root, discoverySpec.SessionAuthFileSchemas)?.AccessToken;
    }

    private IEnumerable<string> GetCandidatePaths()
    {
        return ProviderAuthCandidatePathResolver.ResolvePaths(this._discoverySpec, this._pathProvider);
    }
}
