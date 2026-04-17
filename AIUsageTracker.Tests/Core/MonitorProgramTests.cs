// <copyright file="MonitorProgramTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Monitor.Services;
using AIUsageTracker.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageTracker.Tests.Core;

public sealed class MonitorProgramTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly TestAppPathProvider _pathProvider;

    public MonitorProgramTests()
    {
        this._tempDirectory = TestTempPaths.CreateDirectory("monitor-program-tests");
        this._pathProvider = new TestAppPathProvider(this._tempDirectory);
    }

    [Fact]
    public void SaveMonitorInfo_WritesStartupStatusToCandidatePaths()
    {
        this.InvokeMonitorProgramMethod(
            "SaveMonitorInfo",
            5123,
            true,
            NullLogger.Instance,
            this._pathProvider,
            "running");

        foreach (var path in this.GetExpectedMonitorInfoPaths())
        {
            Assert.True(File.Exists(path), $"Expected monitor info file at {path}");

            var info = this.DeserializeMonitorInfo(path);
            Assert.Equal(5123, info.Port);
            Assert.True(info.DebugMode);
            Assert.Contains("Startup status: running", info.Errors ?? [], StringComparer.Ordinal);
        }
    }

    [Fact]
    public void CanonicalMonitorInfoPath_IsUnderLocalAppData()
    {
        var path = MonitorLauncher.GetCanonicalMonitorInfoFilePath();

        Assert.EndsWith("AIUsageTracker" + Path.DirectorySeparatorChar + "monitor.json", path, StringComparison.Ordinal);
        Assert.DoesNotContain("AIUsageTracker" + Path.DirectorySeparatorChar + "AIUsageTracker", path, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportError_AppendsMessageToExistingMonitorInfo()
    {
        var path = this._pathProvider.GetMonitorInfoFilePath();

        this.WriteMonitorInfoFile(path, "Startup status: running");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

        this.InvokeMonitorProgramMethod(
            "ReportError",
            "Refresh failed: boom",
            this._pathProvider,
            NullLogger.Instance);

        var updated = this.DeserializeMonitorInfo(path);
        Assert.NotNull(updated.Errors);
        Assert.Contains("Startup status: running", updated.Errors!, StringComparer.Ordinal);
        Assert.Contains("Refresh failed: boom", updated.Errors!, StringComparer.Ordinal);
    }

    [Fact]
    public void ReportError_UpdatesExistingMonitorInfoFile()
    {
        var path = this._pathProvider.GetMonitorInfoFilePath();

        this.WriteMonitorInfoFile(path, "existing");

        this.InvokeMonitorProgramMethod(
            "ReportError",
            "Refresh failed: newest",
            this._pathProvider,
            NullLogger.Instance);

        var updated = this.DeserializeMonitorInfo(path);

        Assert.Contains("Refresh failed: newest", updated.Errors ?? [], StringComparer.Ordinal);
    }

    public void Dispose()
    {
        TestTempPaths.CleanupPath(this._tempDirectory);
    }

    private MonitorInfo DeserializeMonitorInfo(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<MonitorInfo>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        })!;
    }

    private string[] GetExpectedMonitorInfoPaths()
    {
        return new[] { this._pathProvider.GetMonitorInfoFilePath() };
    }

    private void InvokeMonitorProgramMethod(string methodName, params object?[] arguments)
    {
        var monitorAssembly = typeof(ProviderRefreshService).Assembly;
        var programType =
            monitorAssembly.GetType("AIUsageTracker.Monitor.Program") ??
            monitorAssembly.GetType("Program");
        Assert.NotNull(programType);

        var method = programType!.GetMethod(
            methodName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        method!.Invoke(null, arguments);
    }

    private void WriteMonitorInfoFile(string path, string error)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var info = new MonitorInfo
        {
            Port = 5000,
            ProcessId = 1234,
            DebugMode = false,
            Errors = new List<string> { error },
            MachineName = "test-machine",
            UserName = "test-user",
            StartedAt = "2026-03-06 10:00:00",
        };

        File.WriteAllText(path, JsonSerializer.Serialize(info));
    }

    private sealed class TestAppPathProvider : IAppPathProvider
    {
        private readonly string _root;

        public TestAppPathProvider(string root)
        {
            this._root = root;
        }

        public string GetAppDataRoot() => Path.Combine(this._root, "appdata");

        public string GetDatabasePath() => Path.Combine(this.GetAppDataRoot(), "usage.db");

        public string GetLogDirectory() => Path.Combine(this.GetAppDataRoot(), "logs");

        public string GetAuthFilePath() => Path.Combine(this.GetAppDataRoot(), "auth.json");

        public string GetPreferencesFilePath() => Path.Combine(this.GetAppDataRoot(), "preferences.json");

        public string GetProviderConfigFilePath() => Path.Combine(this.GetAppDataRoot(), "providers.json");

        public string GetUserProfileRoot() => Path.Combine(this._root, "user-profile");

        public string GetMonitorInfoFilePath() => Path.Combine(this.GetAppDataRoot(), "monitor.json");
    }
}
