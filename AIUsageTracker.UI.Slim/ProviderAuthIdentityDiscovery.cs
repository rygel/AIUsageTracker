using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim;

internal static class ProviderAuthIdentityDiscovery
{
    public static Task<string?> TryGetGitHubUsernameAsync(ILogger logger, IEnumerable<string>? candidatePaths = null)
    {
        return TryReadGitHubHostsUsernameAsync(candidatePaths ?? GetGitHubHostsCandidates(), logger);
    }

    public static Task<string?> TryGetOpenAiUsernameAsync(ILogger logger, IEnumerable<string>? candidatePaths = null)
    {
        return TryReadOpenAiUsernameAsync(candidatePaths ?? GetOpenAiAuthCandidates(), logger);
    }

    public static Task<string?> TryGetCodexUsernameAsync(ILogger logger, IEnumerable<string>? candidatePaths = null)
    {
        return TryReadCodexUsernameAsync(candidatePaths ?? GetCodexAuthCandidates(), logger);
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

    private static async Task<string?> TryReadOpenAiUsernameAsync(IEnumerable<string> candidatePaths, ILogger logger)
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
                if (!doc.RootElement.TryGetProperty("openai", out var openai) || openai.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var claim in new[] { "email", "upn" })
                {
                    if (openai.TryGetProperty(claim, out var emailElement) && emailElement.ValueKind == JsonValueKind.String)
                    {
                        var emailValue = emailElement.GetString();
                        if (IsEmailLike(emailValue))
                        {
                            return emailValue;
                        }
                    }
                }

                var explicitIdentity = FindIdentityInJson(openai);
                if (!string.IsNullOrWhiteSpace(explicitIdentity))
                {
                    return explicitIdentity;
                }

                if (openai.TryGetProperty("access", out var accessElement) && accessElement.ValueKind == JsonValueKind.String)
                {
                    var fromToken = TryGetUsernameFromJwt(accessElement.GetString());
                    if (!string.IsNullOrWhiteSpace(fromToken))
                    {
                        return fromToken;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to read OpenAI auth file at {Path}", path);
            }
        }

        return null;
    }

    private static async Task<string?> TryReadCodexUsernameAsync(IEnumerable<string> candidatePaths, ILogger logger)
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

                var directIdentity = FindIdentityInJson(doc.RootElement);
                if (!string.IsNullOrWhiteSpace(directIdentity))
                {
                    return directIdentity;
                }

                if (doc.RootElement.TryGetProperty("tokens", out var tokens) && tokens.ValueKind == JsonValueKind.Object)
                {
                    foreach (var claim in new[] { "id_token", "access_token" })
                    {
                        if (tokens.TryGetProperty(claim, out var tokenElement) && tokenElement.ValueKind == JsonValueKind.String)
                        {
                            var fromToken = TryGetUsernameFromJwt(tokenElement.GetString());
                            if (!string.IsNullOrWhiteSpace(fromToken))
                            {
                                return fromToken;
                            }
                        }
                    }
                }

                if (doc.RootElement.TryGetProperty("openai", out var openai) &&
                    openai.ValueKind == JsonValueKind.Object &&
                    openai.TryGetProperty("access", out var openAiAccessToken) &&
                    openAiAccessToken.ValueKind == JsonValueKind.String)
                {
                    var fromOpenAiToken = TryGetUsernameFromJwt(openAiAccessToken.GetString());
                    if (!string.IsNullOrWhiteSpace(fromOpenAiToken))
                    {
                        return fromOpenAiToken;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to read Codex auth file at {Path}", path);
            }
        }

        return null;
    }

    private static IEnumerable<string> GetGitHubHostsCandidates()
    {
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitHub CLI", "hosts.yml");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "gh", "hosts.yml");
    }

    private static IEnumerable<string> GetOpenAiAuthCandidates()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(userProfile, ".local", "share", "opencode", "auth.json");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "opencode", "auth.json");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "opencode", "auth.json");
        yield return Path.Combine(userProfile, ".opencode", "auth.json");
    }

    private static IEnumerable<string> GetCodexAuthCandidates()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(userProfile, ".codex", "auth.json");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "codex", "auth.json");
        yield return Path.Combine(userProfile, ".local", "share", "opencode", "auth.json");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "opencode", "auth.json");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "opencode", "auth.json");
        yield return Path.Combine(userProfile, ".opencode", "auth.json");
    }

    private static string? TryGetUsernameFromJwt(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var parts = token.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2:
                    payload += "==";
                    break;
                case 3:
                    payload += "=";
                    break;
            }

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = JsonDocument.Parse(json);

            foreach (var claim in new[] { "email", "upn", "preferred_username" })
            {
                if (doc.RootElement.TryGetProperty(claim, out var valueElement) && valueElement.ValueKind == JsonValueKind.String)
                {
                    var value = valueElement.GetString();
                    if (IsEmailLike(value))
                    {
                        return value;
                    }
                }
            }

            foreach (var claim in new[] { "username", "login", "name" })
            {
                if (doc.RootElement.TryGetProperty(claim, out var valueElement) && valueElement.ValueKind == JsonValueKind.String)
                {
                    var value = valueElement.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }

            var recursiveIdentity = FindIdentityInJson(doc.RootElement);
            if (!string.IsNullOrWhiteSpace(recursiveIdentity))
            {
                return recursiveIdentity;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string? FindIdentityInJson(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        var value = property.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            var key = property.Name.ToLowerInvariant();
                            if (key.Contains("email", StringComparison.Ordinal) ||
                                key.Contains("username", StringComparison.Ordinal) ||
                                key.Contains("login", StringComparison.Ordinal) ||
                                key.Contains("user", StringComparison.Ordinal))
                            {
                                return value;
                            }
                        }
                    }

                    var nested = FindIdentityInJson(property.Value);
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        return nested;
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var nested = FindIdentityInJson(item);
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        return nested;
                    }
                }

                break;
        }

        return null;
    }

    private static bool IsEmailLike(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && value.Contains('@');
    }
}
