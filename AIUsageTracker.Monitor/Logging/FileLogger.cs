namespace AIUsageTracker.Monitor.Logging;

public class FileLogger : ILogger
{
    private readonly string _logFile;
    private readonly string _categoryName;
    private static readonly object _lock = new();

    public FileLogger(string logFile, string categoryName)
    {
        _logFile = logFile;
        _categoryName = categoryName;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

    private static string GetLevelString(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRCE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFO ",
        LogLevel.Warning => "WARN ",
        LogLevel.Error => "ERROR",
        LogLevel.Critical => "CRIT ",
        LogLevel.None => "    ",
        _ => level.ToString().ToUpperInvariant().PadRight(5)
    };

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        var levelStr = GetLevelString(logLevel);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
        var categoryShort = _categoryName.Length > 30
            ? _categoryName.Substring(_categoryName.Length - 30)
            : _categoryName.PadRight(30);

        var logEntry = $"{timestamp} {levelStr} {categoryShort} | {message}";

        if (exception != null)
        {
            logEntry += Environment.NewLine + exception;
        }

        lock (_lock)
        {
            try
            {
                File.AppendAllText(_logFile, logEntry + Environment.NewLine);
            }
            catch (Exception)
            {
                // Intentionally suppressed: File logging failure in custom logger.
                // Cannot log this error anywhere since logging itself failed.
                // This prevents recursive logging attempts and ensures the application continues.
            }
        }
    }
}
