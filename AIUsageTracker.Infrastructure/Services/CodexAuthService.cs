// <copyright file="CodexAuthService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageTracker.Infrastructure.Configuration;
using AIUsageTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Services;

public class CodexAuthService
{
    private readonly ILogger<CodexAuthService> _logger;
    private readonly string? _authFilePath;

    public CodexAuthService(ILogger<CodexAuthService> logger, string? authFilePath = null)
    {
        this._logger = logger;
        this._authFilePath = authFilePath;
    }

    public string? GetAccessToken()
    {
        var auth = this.LoadAuth();
        return auth?.AccessToken;
    }

    public string? GetAccountId()
    {
        var auth = this.LoadAuth();
        return auth?.AccountId;
    }

    private static CodexAuth? TryReadAuth(JsonElement root)
    {
        var authData = ProviderAuthFileSchemaReader.Read(root, CodexProvider.StaticDefinition.SessionAuthFileSchemas);
        if (string.IsNullOrWhiteSpace(authData?.AccessToken))
        {
            return null;
        }

        return new CodexAuth
        {
            AccessToken = authData.AccessToken,
            AccountId = authData.AccountId,
        };
    }

    private CodexAuth? LoadAuth()
    {
        foreach (var path in this.GetAuthFileCandidates())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                var auth = TryReadAuth(doc.RootElement);
                if (auth != null)
                {
                    return auth;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                this._logger.LogDebug(ex, "Failed to read Codex auth file at {Path}", path);
            }
        }

        return null;
    }

    private IEnumerable<string> GetAuthFileCandidates()
    {
        if (!string.IsNullOrWhiteSpace(this._authFilePath))
        {
            yield return this._authFilePath;
            yield break;
        }

        var discoverySpec = CodexProvider.StaticDefinition.CreateAuthDiscoverySpec();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var path in ProviderAuthCandidatePathResolver.ResolvePaths(discoverySpec, home))
        {
            yield return path;
        }
    }

    private sealed class CodexAuth
    {
        public string? AccessToken { get; set; }

        public string? AccountId { get; set; }
    }
}
