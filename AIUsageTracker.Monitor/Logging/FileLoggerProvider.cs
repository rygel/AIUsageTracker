namespace AIUsageTracker.Monitor.Logging;

public class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logFile;

    public FileLoggerProvider(string logFile)
    {
        _logFile = logFile;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(_logFile, categoryName);
    }

    public void Dispose() { }
}
