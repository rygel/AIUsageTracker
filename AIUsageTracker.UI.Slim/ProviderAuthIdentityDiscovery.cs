// <copyright file="ProviderAuthIdentityDiscovery.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.IO;
using System.Text.Json;

using AIUsageTracker.Core.Helpers;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim;

internal static class ProviderAuthIdentityDiscovery
{
    public static Task<string?> TryGetGitHubUsernameAsync(ILogger logger, IEnumerable<string>? candidatePaths = null)
    {
        return TryReadGitHubHostsUsernameAsync(
            candidatePaths ?? GetCandidatePaths(GitHubCopilotProvider.StaticDefinition),
            logger);
    }

    public static Task<string?> TryGetOpenAiUsernameAsync(ILogger logger, IEnumerable<string>? candidatePaths = null)
    {
        return TryReadOpenAiUsernameAsync(
            candidatePaths ?? GetCandidatePaths(OpenAIProvider.StaticDefinition),
            OpenAIProvider.StaticDefinition,
            logger);
    }

    public static Task<string?> TryGetCodexUsernameAsync(ILogger logger, IEnumerable<string>? candidatePaths = null)
    {
        return TryReadCodexUsernameAsync(
            candidatePaths ?? GetCandidatePaths(CodexProvider.StaticDefinition),
            CodexProvider.StaticDefinition,
            logger);
    }

    private static async Task<string?> TryReadGitHubHostsUsernameAsync(IEnumerable<string> candidatePaths, ILogger logger)
    {
        foreach (var path in candidatePaths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var lines = await File.ReadAllLinesAsync(path).ConfigureAwait(false);
                foreach (var rawLine in lines)
                {
                    var line = rawLine.Trim();
                    if (!line.StartsWith("user:", StringComparison.OrdinalIgnoreCase) &&
                        !line.StartsWith("login:", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var value = line[(line.IndexOf(':') + 1)..].Trim().Trim('\'', '"');
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to read GitHub auth hosts file at {Path}", path);
            }
        }

        return null;
    }

    private static async Task<string?> TryReadOpenAiUsernameAsync(
        IEnumerable<string> candidatePaths,
        ProviderDefinition definition,
        ILogger logger)
    {
        foreach (var path in candidatePaths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = FindFirstRootObject(doc.RootElement, definition);
                if (root == null)
                {
                    continue;
                }

                var explicitIdentity = SessionIdentityHelper.TryGetPreferredIdentity(root.Value);
                if (!string.IsNullOrWhiteSpace(explicitIdentity))
                {
                    return explicitIdentity;
                }

                if (root.Value.TryGetProperty("access", out var accessElement) &&
                    accessElement.ValueKind == JsonValueKind.String)
                {
                    var fromToken = SessionIdentityHelper.TryGetIdentityFromJwt(accessElement.GetString());
                    if (!string.IsNullOrWhiteSpace(fromToken))
                    {
                        return fromToken;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to read {Provider} auth file at {Path}", definition.DisplayName, path);
            }
        }

        return null;
    }

    private static async Task<string?> TryReadCodexUsernameAsync(
        IEnumerable<string> candidatePaths,
        ProviderDefinition definition,
        ILogger logger)
    {
        foreach (var path in candidatePaths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var directIdentity = SessionIdentityHelper.TryGetPreferredIdentity(doc.RootElement);
                if (!string.IsNullOrWhiteSpace(directIdentity))
                {
                    return directIdentity;
                }

                if (doc.RootElement.TryGetProperty("tokens", out var tokens) && tokens.ValueKind == JsonValueKind.Object)
                {
                    foreach (var claim in new[] { "id_token", "access_token" })
                    {
                        if (tokens.TryGetProperty(claim, out var tokenElement) &&
                            tokenElement.ValueKind == JsonValueKind.String)
                        {
                            var fromToken = SessionIdentityHelper.TryGetIdentityFromJwt(tokenElement.GetString());
                            if (!string.IsNullOrWhiteSpace(fromToken))
                            {
                                return fromToken;
                            }
                        }
                    }
                }

                var compatibilityRoot = FindFirstRootObject(doc.RootElement, definition);
                if (compatibilityRoot != null &&
                    compatibilityRoot.Value.TryGetProperty("access", out var accessToken) &&
                    accessToken.ValueKind == JsonValueKind.String)
                {
                    var fromCompatibilityToken = SessionIdentityHelper.TryGetIdentityFromJwt(accessToken.GetString());
                    if (!string.IsNullOrWhiteSpace(fromCompatibilityToken))
                    {
                        return fromCompatibilityToken;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to read {Provider} auth file at {Path}", definition.DisplayName, path);
            }
        }

        return null;
    }

    private static IEnumerable<string> GetCandidatePaths(ProviderDefinition definition)
    {
        return definition.AuthIdentityCandidatePathTemplates
            .Select(Environment.ExpandEnvironmentVariables)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static JsonElement? FindFirstRootObject(JsonElement root, ProviderDefinition definition)
    {
        foreach (var propertyName in definition.SessionAuthFileSchemas
                     .Select(schema => schema.RootProperty)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (root.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.Object)
            {
                return element;
            }
        }

        return null;
    }
}
