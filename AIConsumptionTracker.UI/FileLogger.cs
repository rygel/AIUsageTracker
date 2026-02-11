using System.IO;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace AIConsumptionTracker.UI;

public class FileLoggerProvider : ILoggerProvider
{
    private readonly string _filePath;
    private readonly string _appVersion;
    private readonly string _appDirectory;
    private readonly bool _isDebugMode;
    private static readonly HashSet<string> _initializedFiles = new();
    private static readonly object _initLock = new();

    public FileLoggerProvider(string filePath, bool isDebugMode = false)
    {
        _filePath = filePath;
        _isDebugMode = isDebugMode;
        _appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
        _appDirectory = AppContext.BaseDirectory;
        
        // Write header information when the file is first created
        InitializeLogFile();
    }

    private void InitializeLogFile()
    {
        lock (_initLock)
        {
            if (!_initializedFiles.Contains(_filePath))
            {
                var header = new System.Text.StringBuilder();
                header.AppendLine("================================================================================");
                header.AppendLine($"AI Consumption Tracker Log");
                header.AppendLine($"Version: {_appVersion}");
                header.AppendLine($"Application Directory: {_appDirectory}");
                header.AppendLine($"Log File: {_filePath}");
                header.AppendLine($"Debug Mode: {(_isDebugMode ? "ENABLED" : "Disabled")}");
                header.AppendLine($"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                header.AppendLine();
                header.AppendLine("--------------------------------------------------------------------------------");
                header.AppendLine("Application starting...");
                header.AppendLine("--------------------------------------------------------------------------------");
                header.AppendLine();
                
                File.WriteAllText(_filePath, header.ToString());
                _initializedFiles.Add(_filePath);
            }
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(_filePath);
    }

    public void Dispose()
    {
        WriteShutdownMessage();
    }

    private void WriteShutdownMessage()
    {
        lock (_initLock)
        {
            if (_initializedFiles.Contains(_filePath) && File.Exists(_filePath))
            {
                var shutdownMessage = new System.Text.StringBuilder();
                shutdownMessage.AppendLine();
                shutdownMessage.AppendLine("--------------------------------------------------------------------------------");
                shutdownMessage.AppendLine($"Application shutting down gracefully at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                shutdownMessage.AppendLine("--------------------------------------------------------------------------------");
                
                File.AppendAllText(_filePath, shutdownMessage.ToString());
            }
        }
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
