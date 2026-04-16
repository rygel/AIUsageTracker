// <copyright file="FileLoggerTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Monitor.Logging;
using AIUsageTracker.Tests.Infrastructure;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Monitor.Tests;

public class FileLoggerTests : IDisposable
{
    private readonly string _tempDirectory = TestTempPaths.CreateDirectory("file-logger-tests");

    [Fact]
    public void Log_CreatesMissingDirectoryAndAppendsMessage()
    {
        var logDirectory = Path.Combine(this._tempDirectory, "logs");
        var logFile = Path.Combine(logDirectory, "monitor_2026-03-13.log");
        var logger = new FileLogger(logFile, "Monitor");

        logger.LogInformation("hello from test");

        Assert.True(File.Exists(logFile));
        var content = File.ReadAllText(logFile);
        Assert.Contains("hello from test", content, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        TestTempPaths.CleanupPath(this._tempDirectory);
        GC.SuppressFinalize(this);
    }
}
