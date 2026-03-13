// <copyright file="ProviderAuthCandidatePathResolver.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Paths;

namespace AIUsageTracker.Infrastructure.Configuration;

internal static class ProviderAuthCandidatePathResolver
{
    public static IReadOnlyList<string> ResolvePaths(
        ProviderAuthDiscoverySpec discoverySpec,
        IAppPathProvider pathProvider)
    {
        return ResolvePaths(discoverySpec, pathProvider.GetUserProfileRoot());
    }

    public static IReadOnlyList<string> ResolvePaths(
        ProviderAuthDiscoverySpec discoverySpec,
        string userProfilePath)
    {
        return discoverySpec.AuthIdentityCandidatePathTemplates
            .Select(pathTemplate => AuthPathTemplateResolver.Resolve(pathTemplate, userProfilePath))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()!;
    }
}
