// <copyright file="FileLogger.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;

using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Monitor.Logging;

public class FileLogger : ILogger
{
    private static readonly object _lock = new();
    private readonly string _logFile;
    private readonly string _categoryName;

    public FileLogger(string logFile, string categoryName)
    {
        this._logFile = logFile;
        this._categoryName = categoryName;
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!this.IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        var levelStr = GetLevelString(logLevel);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var categoryShort = this._categoryName.Length > 30
            ? this._categoryName.Substring(this._categoryName.Length - 30)
            : this._categoryName.PadRight(30);

        var logEntry = $"{timestamp} {levelStr} {categoryShort} | {message}";

        if (exception != null)
        {
            logEntry += Environment.NewLine + exception;
        }

        lock (_lock)
        {
            try
            {
                File.AppendAllText(this._logFile, logEntry + Environment.NewLine);
            }
            catch (Exception)
            {
                // Intentionally suppressed: File logging failure in custom logger.
                // Cannot log this error anywhere since logging itself failed.
                // This prevents recursive logging attempts and ensures the application continues.
            }
        }
    }

    private static string GetLevelString(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRCE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFO ",
        LogLevel.Warning => "WARN ",
        LogLevel.Error => "ERROR",
        LogLevel.Critical => "CRIT ",
        LogLevel.None => "    ",
        _ => level.ToString().ToUpperInvariant().PadRight(5),
    };
}
