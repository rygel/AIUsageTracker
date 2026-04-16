// <copyright file="GitHubAuthService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.IO;
using System.Net.Http.Json;
using System.Security;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIUsageTracker.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Services;

public class GitHubAuthService : IGitHubAuthService
{
    // Using common Client ID for Copilot integrations (VS Code's ID) as this is required to get the 'copilot' scope permissions correctly.
    // In a real production app for general GitHub access, we would register our own.
    private const string CLIENTID = "Iv1.b507a08c87ecfe98";
    private const string AUTHURL = "https://github.com/login/device/code";
    private const string TOKENURL = "https://github.com/login/oauth/access_token";
    private const string SCOPE = "read:user copilot"; // Requesting copilot scope
    private const string USERURL = "https://api.github.com/user";

    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubAuthService> _logger;
    private string? _currentToken;
    private bool _cliTokenLookupAttempted;
    private string? _cachedUsername;

    public GitHubAuthService(HttpClient httpClient, ILogger<GitHubAuthService> logger)
    {
        this._httpClient = httpClient;
        this._logger = logger;
    }

    /// <inheritdoc/>
    public bool IsAuthenticated => !string.IsNullOrEmpty(this._currentToken);

    /// <inheritdoc/>
    public async Task<(string DeviceCode, string UserCode, string VerificationUri, int ExpiresIn, int Interval)> InitiateDeviceFlowAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, AUTHURL);
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", CLIENTID),
                new KeyValuePair<string, string>("scope", SCOPE),
            });
            request.Content = content;

            var response = await this._httpClient.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<DeviceFlowResponse>().ConfigureAwait(false);
            if (result == null)
            {
                throw new InvalidOperationException("Failed to parse device flow response.");
            }

            return (result.Device_code, result.User_code, result.Verification_uri, result.Expires_in, result.Interval);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Error initiating device flow");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<string?> PollForTokenAsync(string deviceCode, int interval)
    {
        // Polling logic would typically be handled by the caller or a loop here.
        // For this method, we make a SINGLE check. The caller (UI) should loop.
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, TOKENURL);
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", CLIENTID),
                new KeyValuePair<string, string>("device_code", deviceCode),
                new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:device_code"),
            });
            request.Content = content;

            var response = await this._httpClient.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var error))
            {
                var code = error.GetString();
                if (string.Equals(code, "authorization_pending", StringComparison.Ordinal))
                {
                    return null; // Keep polling
                }

                if (string.Equals(code, "slow_down", StringComparison.Ordinal))
                {
                    return "SLOW_DOWN"; // Signal to slow down
                }

                if (string.Equals(code, "expired_token", StringComparison.Ordinal))
                {
                    throw new SecurityException("Token expired");
                }

                if (string.Equals(code, "access_denied", StringComparison.Ordinal))
                {
                    throw new SecurityException("Access denied");
                }
            }

            if (root.TryGetProperty("access_token", out var tokenProp))
            {
                this._currentToken = tokenProp.GetString();
                return this._currentToken;
            }

            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            this._logger.LogError(ex, "Error polling for token");
            return null;
        }
    }

    /// <inheritdoc/>
    public Task<string?> RefreshTokenAsync(string refreshToken)
    {
        // Device flow tokens for apps like VS Code usually last a long time or don't use refresh tokens in the same way
        // as web apps (they use the access token until invalid).
        // Implementing placeholder.
        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc/>
    public string? GetCurrentToken()
    {
        if (!string.IsNullOrWhiteSpace(this._currentToken))
        {
            return this._currentToken;
        }

        this._currentToken = TryLoadTokenFromHostsFile();
        if (!string.IsNullOrWhiteSpace(this._currentToken))
        {
            return this._currentToken;
        }

        if (!this._cliTokenLookupAttempted)
        {
            this._cliTokenLookupAttempted = true;
            this._currentToken = TryLoadTokenFromGhCli(this._logger);
        }

        return this._currentToken;
    }

    /// <inheritdoc/>
    public void Logout()
    {
        this._currentToken = null;
    }

    /// <inheritdoc/>
    public async Task<string?> GetUsernameAsync()
    {
        if (this._cachedUsername != null)
        {
            return this._cachedUsername;
        }

        if (!this.IsAuthenticated)
        {
            this._cachedUsername = TryLoadUsernameFromHostsFile();
            return this._cachedUsername;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, USERURL);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", this._currentToken);
            request.Headers.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("AIUsageTracker", "1.0"));

            var response = await this._httpClient.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync().ConfigureAwait(false)).ConfigureAwait(false);
            if (doc.RootElement.TryGetProperty("login", out var loginProp))
            {
                this._cachedUsername = loginProp.GetString();
                return this._cachedUsername;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            this._logger.LogError(ex, "Error fetching GitHub username");
        }

        return null;
    }

    /// <inheritdoc/>
    public void InitializeToken(string token)
    {
        if (!string.Equals(this._currentToken, token, StringComparison.Ordinal))
        {
            this._currentToken = token;
            this._cachedUsername = null; // Reset cache if token changes
        }

        this._cliTokenLookupAttempted = false;
    }

    private static string? TryLoadTokenFromHostsFile()
    {
        foreach (var path in GetCandidateHostsPaths())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var content = File.ReadAllText(path);
                var token = TryExtractTokenFromHostsContent(content);
                if (!string.IsNullOrWhiteSpace(token))
                {
                    return token;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Keep auth loading resilient; provider handles auth failures.
            }
        }

        return null;
    }

    private static string? TryLoadTokenFromGhCli(ILogger<GitHubAuthService> logger)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "gh",
                    Arguments = "auth token",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };

            if (!process.Start())
            {
                logger.LogDebug("GitHub CLI token discovery failed: process did not start");
                return null;
            }

            const int timeoutMs = 4000;
            if (!process.WaitForExit(timeoutMs))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Ignore kill failures; token discovery is best-effort.
                }

                logger.LogDebug("GitHub CLI token discovery timed out after {TimeoutMs}ms", timeoutMs);
                return null;
            }

            if (process.ExitCode != 0)
            {
                var stderr = process.StandardError.ReadToEnd();
                logger.LogDebug(
                    "GitHub CLI token discovery failed with exit code {ExitCode}: {Message}",
                    process.ExitCode,
                    string.IsNullOrWhiteSpace(stderr) ? "no stderr" : stderr.Trim());
                return null;
            }

            var token = process.StandardOutput.ReadToEnd().Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                logger.LogDebug("GitHub CLI token discovery returned an empty token");
                return null;
            }

            return token;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            logger.LogDebug(ex, "GitHub CLI token discovery failed");
            return null;
        }
    }

    private static string? TryLoadUsernameFromHostsFile()
    {
        foreach (var path in GetCandidateHostsPaths())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var content = File.ReadAllText(path);
                var username = TryExtractUsernameFromHostsContent(content);
                if (!string.IsNullOrWhiteSpace(username))
                {
                    return username;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Keep auth loading resilient; provider handles auth failures.
            }
        }

        return null;
    }

    private static IEnumerable<string> GetCandidateHostsPaths()
    {
        var appData = Environment.GetEnvironmentVariable("APPDATA");
        if (!string.IsNullOrWhiteSpace(appData))
        {
            yield return Path.Combine(appData, "GitHub CLI", "hosts.yml");
        }

        var userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            yield return Path.Combine(userProfile, ".config", "gh", "hosts.yml");
        }
    }

    private static string? TryExtractTokenFromHostsContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var githubSection = Regex.Match(
            content,
            @"(?ms)^\s*github\.com:\s*(?<section>.*?)(?=^\S|\z)",
            RegexOptions.ExplicitCapture,
            TimeSpan.FromSeconds(1));

        var source = githubSection.Success
            ? githubSection.Groups["section"].Value
            : content;

        var tokenMatch = Regex.Match(
            source,
            @"(?m)^\s*oauth_token:\s*(?<token>\S+)\s*$",
            RegexOptions.ExplicitCapture,
            TimeSpan.FromSeconds(1));

        return tokenMatch.Success ? tokenMatch.Groups["token"].Value.Trim() : null;
    }

    private static string? TryExtractUsernameFromHostsContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var githubSection = Regex.Match(
            content,
            @"(?ms)^\s*github\.com:\s*(?<section>.*?)(?=^\S|\z)",
            RegexOptions.ExplicitCapture,
            TimeSpan.FromSeconds(1));

        var source = githubSection.Success
            ? githubSection.Groups["section"].Value
            : content;

        var userMatch = Regex.Match(
            source,
            @"(?m)^\s*user:\s*(?<user>\S+)\s*$",
            RegexOptions.ExplicitCapture,
            TimeSpan.FromSeconds(1));

        return userMatch.Success ? userMatch.Groups["user"].Value.Trim() : null;
    }

    // Helper class for JSON deserialization
    private sealed class DeviceFlowResponse
    {
        public string Device_code { get; set; } = string.Empty;

        public string User_code { get; set; } = string.Empty;

        public string Verification_uri { get; set; } = string.Empty;

        public int Expires_in { get; }

        public int Interval { get; }
    }
}
