using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageTracker.Tests.Core;

public sealed class MonitorProgramTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly TestAppPathProvider _pathProvider;

    public MonitorProgramTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "monitor-program-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _pathProvider = new TestAppPathProvider(_tempDirectory);
    }

    [Fact]
    public void SaveMonitorInfo_WritesStartupStatusToCandidatePaths()
    {
        InvokeMonitorProgramMethod(
            "SaveMonitorInfo",
            5123,
            true,
            NullLogger.Instance,
            _pathProvider,
            "running");

        foreach (var path in GetExpectedMonitorInfoPaths())
        {
            Assert.True(File.Exists(path), $"Expected monitor info file at {path}");

            var info = DeserializeMonitorInfo(path);
            Assert.Equal(5123, info.Port);
            Assert.True(info.DebugMode);
            Assert.Contains("Startup status: running", info.Errors ?? [], StringComparer.Ordinal);
        }
    }

    [Fact]
    public void WriteCandidatePaths_OnlyIncludeCanonicalPath()
    {
        var candidatePaths = MonitorInfoPathCatalog.GetWriteCandidatePaths(
            _pathProvider.GetAppDataRoot(),
            _pathProvider.GetUserProfileRoot());

        var expectedPath = Path.Combine(_pathProvider.GetAppDataRoot(), "AIUsageTracker", "monitor.json");

        var path = Assert.Single(candidatePaths);
        Assert.Equal(expectedPath, path);
    }

    [Fact]
    public void ReportError_AppendsMessageToExistingMonitorInfo()
    {
        var path = MonitorInfoPathCatalog.GetWriteCandidatePaths(
            _pathProvider.GetAppDataRoot(),
            _pathProvider.GetUserProfileRoot())[0];

        WriteMonitorInfoFile(path, "Startup status: running");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

        InvokeMonitorProgramMethod(
            "ReportError",
            "Refresh failed: boom",
            _pathProvider,
            NullLogger.Instance);

        var updated = DeserializeMonitorInfo(path);
        Assert.NotNull(updated.Errors);
        Assert.Contains("Startup status: running", updated.Errors!, StringComparer.Ordinal);
        Assert.Contains("Refresh failed: boom", updated.Errors!, StringComparer.Ordinal);
    }

    [Fact]
    public void ReportError_UpdatesNewestExistingMonitorInfoFile()
    {
        var candidatePaths = MonitorInfoPathCatalog.GetWriteCandidatePaths(
            _pathProvider.GetAppDataRoot(),
            _pathProvider.GetUserProfileRoot());
        var path = candidatePaths[0];

        WriteMonitorInfoFile(path, "existing");

        InvokeMonitorProgramMethod(
            "ReportError",
            "Refresh failed: newest",
            _pathProvider,
            NullLogger.Instance);

        var updated = DeserializeMonitorInfo(path);

        Assert.Contains("Refresh failed: newest", updated.Errors ?? [], StringComparer.Ordinal);
    }
`n
    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
`n
    private MonitorInfo DeserializeMonitorInfo(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<MonitorInfo>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;
    }
`n
    private IEnumerable<string> GetExpectedMonitorInfoPaths()
    {
        return MonitorInfoPathCatalog.GetWriteCandidatePaths(
            _pathProvider.GetAppDataRoot(),
            _pathProvider.GetUserProfileRoot());
    }
`n
    private static void InvokeMonitorProgramMethod(string methodName, params object?[] arguments)
    {
        var programType = typeof(ProviderRefreshService).Assembly.GetType("Program");
        Assert.NotNull(programType);

        var method = programType!.GetMethod(
            methodName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        method!.Invoke(null, arguments);
    }
`n
    private static void WriteMonitorInfoFile(string path, string error)
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
            StartedAt = "2026-03-06 10:00:00"
        };

        File.WriteAllText(path, JsonSerializer.Serialize(info));
    }
`n
    private sealed class TestAppPathProvider : IAppPathProvider
    {
        private readonly string _root;

        public TestAppPathProvider(string root)
        {
            _root = root;
        }
`n
        public string GetAppDataRoot() => Path.Combine(_root, "appdata");

        public string GetDatabasePath() => Path.Combine(GetAppDataRoot(), "usage.db");

        public string GetLogDirectory() => Path.Combine(GetAppDataRoot(), "logs");

        public string GetAuthFilePath() => Path.Combine(GetAppDataRoot(), "auth.json");

        public string GetPreferencesFilePath() => Path.Combine(GetAppDataRoot(), "preferences.json");

        public string GetProviderConfigFilePath() => Path.Combine(GetAppDataRoot(), "providers.json");

        public string GetUserProfileRoot() => Path.Combine(_root, "user-profile");
    }
}
