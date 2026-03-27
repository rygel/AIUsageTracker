// <copyright file="MonitorLogPathResolverTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Monitor.Logging;
using AIUsageTracker.Tests.Infrastructure;

namespace AIUsageTracker.Monitor.Tests;

public class MonitorLogPathResolverTests : IDisposable
{
    private readonly string _tempDirectory = TestTempPaths.CreateDirectory("monitor-log-path-resolver");

    [Fact]
    public void Resolve_UsesPreferredDirectory_WhenItIsWritable()
    {
        var preferredDirectory = Path.Combine(this._tempDirectory, "logs");
        var pathProvider = new TestAppPathProvider(preferredDirectory);

        var resolved = MonitorLogPathResolver.Resolve(pathProvider, new DateTime(2026, 03, 13));

        Assert.False(resolved.UsedFallback);
        Assert.Equal(preferredDirectory, resolved.LogDirectory);
        Assert.Equal(Path.Combine(preferredDirectory, "monitor_2026-03-13.log"), resolved.LogFile);
        Assert.True(Directory.Exists(preferredDirectory));
    }

    [Fact]
    public void Resolve_FallsBackToTempDirectory_WhenPreferredDirectoryIsNotRooted()
    {
        var pathProvider = new TestAppPathProvider(Path.Combine("AIUsageTracker", "logs"));

        var resolved = MonitorLogPathResolver.Resolve(pathProvider, new DateTime(2026, 03, 13));

        var expectedFallbackDirectory = Path.Combine(Path.GetTempPath(), "AIUsageTracker", "logs");
        Assert.True(resolved.UsedFallback);
        Assert.Equal(expectedFallbackDirectory, resolved.LogDirectory);
        Assert.Equal(Path.Combine(expectedFallbackDirectory, "monitor_2026-03-13.log"), resolved.LogFile);
    }

    [Fact]
    public void Resolve_FallsBackToTempDirectory_WhenPreferredPathIsAFile()
    {
        var blockedPath = Path.Combine(this._tempDirectory, "blocked-path");
        File.WriteAllText(blockedPath, "not-a-directory");
        var pathProvider = new TestAppPathProvider(blockedPath);

        var resolved = MonitorLogPathResolver.Resolve(pathProvider, new DateTime(2026, 03, 13));

        Assert.True(resolved.UsedFallback);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "AIUsageTracker", "logs"), resolved.LogDirectory);
    }

    public void Dispose()
    {
        TestTempPaths.CleanupPath(this._tempDirectory);
    }

    private sealed class TestAppPathProvider : IAppPathProvider
    {
        private readonly string _logDirectory;

        public TestAppPathProvider(string logDirectory)
        {
            this._logDirectory = logDirectory;
        }

        public string GetAppDataRoot() => this._logDirectory;

        public string GetDatabasePath() => Path.Combine(this._logDirectory, "usage.db");

        public string GetLogDirectory() => this._logDirectory;

        public string GetAuthFilePath() => Path.Combine(this._logDirectory, "auth.json");

        public string GetPreferencesFilePath() => Path.Combine(this._logDirectory, "preferences.json");

        public string GetProviderConfigFilePath() => Path.Combine(this._logDirectory, "providers.json");

        public string GetUserProfileRoot() => this._logDirectory;

        public string GetMonitorInfoFilePath() => Path.Combine(this.GetAppDataRoot(), "monitor.json");
    }
}
