// <copyright file="IProviderAuthFallbackResolver.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Infrastructure.Configuration;

internal interface IProviderAuthFallbackResolver
{
    string ProviderId { get; }

    ProviderConfig? Resolve(
        IReadOnlyDictionary<string, string> environmentVariables,
        IReadOnlyList<ProviderConfig> discoveredConfigs);
}
