// <copyright file="FileLoggerProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Monitor.Logging;

public class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logFile;

    public FileLoggerProvider(string logFile)
    {
        this._logFile = logFile;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(this._logFile, categoryName);
    }

    public void Dispose()
    {
        this.Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }
}
