using System.IO;
using Microsoft.Extensions.Logging;

namespace AIConsumptionTracker.UI;

public class FileLoggerProvider : ILoggerProvider
{
    private readonly string _filePath;

    public FileLoggerProvider(string filePath)
    {
        _filePath = filePath;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(_filePath);
    }

    public void Dispose()
    {
    }
}

public class FileLogger : ILogger
{
    private readonly string _filePath;
    private static readonly object _lock = new();

    public FileLogger(string filePath)
    {
        _filePath = filePath;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{logLevel}] {message}";
        
        if (exception != null)
        {
            logEntry += $"\nException: {exception}";
        }
        
        lock (_lock)
        {
            File.AppendAllText(_filePath, logEntry + Environment.NewLine);
        }
    }
}
