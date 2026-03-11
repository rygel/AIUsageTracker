// <copyright file="ProviderSessionTokenResolver.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Paths;
using AIUsageTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Configuration;

internal sealed class ProviderSessionTokenResolver
{
    private readonly ProviderDefinition _definition;
    private readonly string _description;
    private readonly string _sourcePrefix;
    private readonly ILogger<TokenDiscoveryService> _logger;
    private readonly IAppPathProvider _pathProvider;

    public ProviderSessionTokenResolver(
        ProviderDefinition definition,
        string description,
        string sourcePrefix,
        ILogger<TokenDiscoveryService> logger,
        IAppPathProvider pathProvider)
    {
        this._definition = definition;
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
                var token = TryReadAccessToken(doc.RootElement, this._definition);
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                return new DiscoveredSessionToken(
                    this._definition.ProviderId,
                    token,
                    this._description,
                    $"{this._sourcePrefix}: {path}");
            }
        }
        catch (Exception ex)
        {
            this._logger.LogDebug(ex, "Session token discovery failed for provider {ProviderId}", this._definition.ProviderId);
        }

        return null;
    }

    private IEnumerable<string> GetCandidatePaths()
    {
        var userProfilePath = this._pathProvider.GetUserProfileRoot();

        return this._definition.AuthIdentityCandidatePathTemplates
            .Select(pathTemplate => AuthPathTemplateResolver.Resolve(pathTemplate, userProfilePath))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)!;
    }

    private static string? TryReadAccessToken(JsonElement root, ProviderDefinition definition)
    {
        foreach (var schema in definition.SessionAuthFileSchemas)
        {
            if (!root.TryGetProperty(schema.RootProperty, out var element) || element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (element.TryGetProperty(schema.AccessTokenProperty, out var accessElement) &&
                accessElement.ValueKind == JsonValueKind.String)
            {
                return accessElement.GetString();
            }
        }

        return null;
    }
}
