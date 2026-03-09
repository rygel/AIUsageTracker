// <copyright file="CodexAuthService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Infrastructure.Services
{
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using AIUsageTracker.Core.Interfaces;
    using AIUsageTracker.Core.Paths;
    using AIUsageTracker.Infrastructure.Providers;
    using Microsoft.Extensions.Logging;

    public class CodexAuthService : ICodexAuthService
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
                catch (Exception ex)
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

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var pathTemplate in CodexProvider.StaticDefinition.AuthIdentityCandidatePathTemplates)
            {
                var path = AuthPathTemplateResolver.Resolve(pathTemplate, home);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    yield return path;
                }
            }
        }

        private static CodexAuth? TryReadAuth(JsonElement root)
        {
            foreach (var schema in CodexProvider.StaticDefinition.SessionAuthFileSchemas)
            {
                if (!root.TryGetProperty(schema.RootProperty, out var element) || element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var accessToken = element.TryGetProperty(schema.AccessTokenProperty, out var accessTokenElement) &&
                                  accessTokenElement.ValueKind == JsonValueKind.String
                    ? accessTokenElement.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    continue;
                }

                var accountId = !string.IsNullOrWhiteSpace(schema.AccountIdProperty) &&
                                element.TryGetProperty(schema.AccountIdProperty, out var accountIdElement) &&
                                accountIdElement.ValueKind == JsonValueKind.String
                    ? accountIdElement.GetString()
                    : null;

                return new CodexAuth
                {
                    AccessToken = accessToken,
                    AccountId = accountId,
                };
            }

            return null;
        }

        private sealed class CodexAuth
        {
            public string? AccessToken { get; set; }

            public string? AccountId { get; set; }
        }
    }
}
