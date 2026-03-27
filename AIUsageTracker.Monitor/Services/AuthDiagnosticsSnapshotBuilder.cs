// <copyright file="AuthDiagnosticsSnapshotBuilder.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Monitor.Services;

internal static class AuthDiagnosticsSnapshotBuilder
{
    public static AuthDiagnosticsSnapshot Build(ProviderConfig config, DateTimeOffset nowUtc, ILogger? logger = null)
    {
        var rawAuthSource = string.IsNullOrWhiteSpace(config.AuthSource) ? AuthSource.None : config.AuthSource;
        var fallbackPath = ExtractFallbackPath(rawAuthSource);
        var authSource = SanitizeAuthSource(rawAuthSource, fallbackPath);
        var fallbackPathUsed = SanitizeFallbackPath(fallbackPath);

        return new AuthDiagnosticsSnapshot(
            ProviderId: config.ProviderId,
            Configured: !string.IsNullOrWhiteSpace(config.ApiKey),
            AuthSource: authSource,
            FallbackPathUsed: fallbackPathUsed,
            TokenAgeBucket: GetTokenAgeBucket(config.ProviderId, authSource, fallbackPath, nowUtc, logger),
            HasUserIdentity: HasUserIdentity(config.ProviderId, config.Description));
    }

    private static string ExtractFallbackPath(string authSource)
    {
        var separatorIndex = authSource.IndexOf(':');
        if (separatorIndex < 0 || separatorIndex == authSource.Length - 1)
        {
            return string.Empty;
        }

        var candidate = authSource[(separatorIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
        }

        return candidate.Contains('\\') || candidate.Contains('/')
            ? candidate
            : string.Empty;
    }

    private static string SanitizeAuthSource(string authSource, string fallbackPath)
    {
        if (string.Equals(authSource, AuthSource.None, StringComparison.OrdinalIgnoreCase))
        {
            return AuthSource.None;
        }

        if (AuthSource.IsEnvironment(authSource))
        {
            var variableSegment = authSource[4..].Trim();
            var equalIndex = variableSegment.IndexOf('=');
            if (equalIndex >= 0)
            {
                variableSegment = variableSegment[..equalIndex].Trim();
            }

            return AuthSource.FromEnvironmentVariable(variableSegment);
        }

        if (string.IsNullOrWhiteSpace(fallbackPath))
        {
            return authSource;
        }

        var fileName = Path.GetFileName(fallbackPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return authSource;
        }

        var separatorIndex = authSource.IndexOf(':');
        if (separatorIndex < 0)
        {
            return fileName;
        }

        return $"{authSource[..separatorIndex]}: {fileName}";
    }

    private static string SanitizeFallbackPath(string fallbackPath)
    {
        if (string.IsNullOrWhiteSpace(fallbackPath))
        {
            return "n/a";
        }

        var fileName = Path.GetFileName(fallbackPath);
        return string.IsNullOrWhiteSpace(fileName)
            ? "n/a"
            : fileName;
    }

    private static string GetTokenAgeBucket(
        string providerId,
        string authSource,
        string fallbackPath,
        DateTimeOffset nowUtc,
        ILogger? logger)
    {
        if (AuthSource.IsEnvironment(authSource))
        {
            return "runtime-env";
        }

        if (string.IsNullOrWhiteSpace(fallbackPath))
        {
            return "unknown";
        }

        try
        {
            if (!File.Exists(fallbackPath))
            {
                return "missing";
            }

            var lastWriteUtc = new DateTimeOffset(File.GetLastWriteTimeUtc(fallbackPath), TimeSpan.Zero);
            var age = nowUtc - lastWriteUtc;
            if (age <= TimeSpan.FromHours(1))
            {
                return "lt-1h";
            }

            if (age <= TimeSpan.FromHours(24))
            {
                return "lt-24h";
            }

            if (age <= TimeSpan.FromDays(7))
            {
                return "lt-7d";
            }

            return "gte-7d";
        }
        catch
        {
            logger?.LogDebug(
                "Auth diagnostics token age calculation failed for provider {ProviderId} and source {AuthSource}.",
                providerId,
                authSource);
            return "unknown";
        }
    }

    private static bool HasUserIdentity(string providerId, string? description)
    {
        if (!string.IsNullOrWhiteSpace(description) &&
            (description.Contains('@', StringComparison.Ordinal) ||
             description.Contains("user", StringComparison.OrdinalIgnoreCase) ||
             description.Contains("account", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return ProviderMetadataCatalog.Find(providerId)?.SupportsAccountIdentity ?? false;
    }
}
