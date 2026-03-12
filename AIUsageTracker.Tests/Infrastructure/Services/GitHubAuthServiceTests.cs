// <copyright file="GitHubAuthServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace AIUsageTracker.Tests.Infrastructure.Services;

public sealed class GitHubAuthServiceTests : IDisposable
{
    private readonly string? _originalAppData;
    private readonly string? _originalUserProfile;

    public GitHubAuthServiceTests()
    {
        this._originalAppData = Environment.GetEnvironmentVariable("APPDATA");
        this._originalUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");
    }

    [Fact]
    public void GetCurrentToken_LoadsTokenFromHostsYml_WhenMemoryTokenMissing()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "aiusage-gh-test-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture));
        var appData = Path.Combine(tempRoot, "AppData", "Roaming");
        var hostsPath = Path.Combine(appData, "GitHub CLI", "hosts.yml");
        Directory.CreateDirectory(Path.GetDirectoryName(hostsPath)!);
        File.WriteAllText(
            hostsPath,
            "github.com:\n    user: octocat\n    oauth_token: ghp_testtoken123\n");

        Environment.SetEnvironmentVariable("APPDATA", appData);
        Environment.SetEnvironmentVariable("USERPROFILE", Path.Combine(tempRoot, "User"));

        var service = new GitHubAuthService(new HttpClient(), Mock.Of<ILogger<GitHubAuthService>>());

        var token = service.GetCurrentToken();

        Assert.Equal("ghp_testtoken123", token);

        Directory.Delete(tempRoot, recursive: true);
    }

    [Fact]
    public async Task GetUsernameAsync_LoadsUsernameFromHostsYml_WhenNotAuthenticated()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "aiusage-gh-test-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture));
        var appData = Path.Combine(tempRoot, "AppData", "Roaming");
        var hostsPath = Path.Combine(appData, "GitHub CLI", "hosts.yml");
        Directory.CreateDirectory(Path.GetDirectoryName(hostsPath)!);
        File.WriteAllText(
            hostsPath,
            "github.com:\n    git_protocol: https\n    user: rygel\n");

        Environment.SetEnvironmentVariable("APPDATA", appData);
        Environment.SetEnvironmentVariable("USERPROFILE", Path.Combine(tempRoot, "User"));

        var service = new GitHubAuthService(new HttpClient(), Mock.Of<ILogger<GitHubAuthService>>());

        var username = await service.GetUsernameAsync();

        Assert.Equal("rygel", username);

        Directory.Delete(tempRoot, recursive: true);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("APPDATA", this._originalAppData);
        Environment.SetEnvironmentVariable("USERPROFILE", this._originalUserProfile);
    }
}
