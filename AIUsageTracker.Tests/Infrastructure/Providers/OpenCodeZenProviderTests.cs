// <copyright file="OpenCodeZenProviderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Tests.Infrastructure;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class OpenCodeZenProviderTests : HttpProviderTestBase<OpenCodeZenProvider>
{
    private readonly OpenCodeZenProvider _provider;

    public OpenCodeZenProviderTests()
    {
        this._provider = new OpenCodeZenProvider(this.Logger.Object);
        this.Config.ApiKey = "test-key";
    }

    [Fact]
    public async Task GetUsageAsync_CliNotFound_ReturnsUnavailableAsync()
    {
        // Arrange
        var provider = new OpenCodeZenProvider(this.Logger.Object, "non-existent-cli");

        // Act
        var result = await provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Equal(404, usage.HttpStatus);
        Assert.Contains("CLI not found", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_CliTimeout_ReturnsUnavailableWithTimeoutMessageAsync()
    {
        // Arrange
        var tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var scriptPath = OperatingSystem.IsWindows()
            ? Path.Combine(tempDirectory, "slow-opencode.cmd")
            : Path.Combine(tempDirectory, "slow-opencode.sh");

        if (OperatingSystem.IsWindows())
        {
            await File.WriteAllTextAsync(
                scriptPath,
                "@echo off\r\nping -n 30 127.0.0.1 >nul\r\necho delayed\r\n",
                CancellationToken.None);
        }
        else
        {
            await File.WriteAllTextAsync(
                scriptPath,
                "#!/usr/bin/env bash\nsleep 30\necho delayed\n",
                CancellationToken.None);
            File.SetUnixFileMode(
                scriptPath,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute |
                UnixFileMode.GroupRead |
                UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead |
                UnixFileMode.OtherExecute);
        }

        try
        {
            var provider = new OpenCodeZenProvider(this.Logger.Object, scriptPath, TimeSpan.FromMilliseconds(250));

            // Act
            var result = await provider.GetUsageAsync(this.Config);

            // Assert
            var usage = result.Single();
            Assert.False(usage.IsAvailable);
            Assert.Equal(500, usage.HttpStatus);
            Assert.Contains("timed out", usage.Description, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup for temporary test artifacts.
            }
        }
    }
}
