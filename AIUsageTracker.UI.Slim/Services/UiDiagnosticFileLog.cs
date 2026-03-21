// <copyright file="UiDiagnosticFileLog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace AIUsageTracker.UI.Slim.Services;

internal static class UiDiagnosticFileLog
{
    private static readonly object Sync = new();
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(14);
    private static DateTime _lastPruneUtc = DateTime.MinValue;

    public static void Write(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        try
        {
            var now = DateTime.Now;
            var logLine = $"{now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}";

            lock (Sync)
            {
                var logDirectory = ResolveLogDirectory();
                Directory.CreateDirectory(logDirectory);
                PruneOldLogsIfNeeded(logDirectory, now);

                var logFile = Path.Combine(logDirectory, $"ui_{now:yyyy-MM-dd}.log");
                File.AppendAllText(logFile, logLine);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UiDiagnosticFileLog] Failed to write diagnostic entry: {ex.Message}");
        }
    }

    private static string ResolveLogDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            return Path.Combine(AppContext.BaseDirectory, "logs");
        }

        return Path.Combine(localAppData, "AIUsageTracker", "logs");
    }

    private static void PruneOldLogsIfNeeded(string logDirectory, DateTime now)
    {
        if (_lastPruneUtc != DateTime.MinValue && (now.ToUniversalTime() - _lastPruneUtc) < TimeSpan.FromHours(1))
        {
            return;
        }

        _lastPruneUtc = now.ToUniversalTime();
        var cutoff = now - RetentionPeriod;

        foreach (var file in Directory.GetFiles(logDirectory, "ui_*.log", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var lastWrite = File.GetLastWriteTime(file);
                if (lastWrite < cutoff)
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "[UiDiagnosticFileLog] Failed pruning '{0}': {1}",
                        file,
                        ex.Message));
            }
        }
    }
}
