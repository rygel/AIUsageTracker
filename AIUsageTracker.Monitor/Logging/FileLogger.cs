// <copyright file="FileLogger.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Globalization;

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
        ArgumentNullException.ThrowIfNull(formatter);

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
                var directory = Path.GetDirectoryName(this._logFile);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(this._logFile, logEntry + Environment.NewLine);
            }
            catch (Exception ex) when (
                ex is UnauthorizedAccessException or
                IOException or
                ArgumentException or
                NotSupportedException or
                PathTooLongException)
            {
                try
                {
                    Console.Error.WriteLine(
                        $"Monitor file logging failed for '{this._logFile}': {ex.Message}");
                }
                catch (Exception stderrEx) when (stderrEx is IOException or ObjectDisposedException)
                {
                    Debug.WriteLine(
                        $"Monitor stderr logging failed for '{this._logFile}': {stderrEx.Message}");
                }
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
