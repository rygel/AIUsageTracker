// <copyright file="AtomicFileWriter.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Configuration;

internal static class AtomicFileWriter
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PathLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan[] RetryBackoff =
    [
        TimeSpan.FromMilliseconds(40),
        TimeSpan.FromMilliseconds(120),
        TimeSpan.FromMilliseconds(300),
    ];

    public static async Task WriteAllTextAtomicAsync(
        string path,
        string content,
        ILogger logger,
        string? backupPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(logger);

        var normalizedPath = Path.GetFullPath(path);
        var pathLock = PathLocks.GetOrAdd(
            normalizedPath,
            static _ => new SemaphoreSlim(1, 1));

        await pathLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(normalizedPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            for (var attempt = 1; attempt <= RetryBackoff.Length + 1; attempt++)
            {
                var tempPath = $"{normalizedPath}.{Guid.NewGuid():N}.tmp";
                try
                {
                    await File.WriteAllTextAsync(tempPath, content, Encoding.UTF8).ConfigureAwait(false);
                    ReplaceFile(tempPath, normalizedPath, backupPath);
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    TryDeleteTemp(tempPath, logger);
                    if (attempt > RetryBackoff.Length)
                    {
                        throw;
                    }

                    logger.LogDebug(
                        ex,
                        "Atomic write failed for {Path} on attempt {Attempt}/{TotalAttempts}; retrying.",
                        normalizedPath,
                        attempt,
                        RetryBackoff.Length + 1);
                    await Task.Delay(RetryBackoff[attempt - 1]).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            pathLock.Release();
        }
    }

    private static void ReplaceFile(string tempPath, string targetPath, string? backupPath)
    {
        if (File.Exists(targetPath))
        {
            try
            {
                File.Replace(tempPath, targetPath, backupPath, ignoreMetadataErrors: true);
                return;
            }
            catch (PlatformNotSupportedException)
            {
                // Fall through to move with overwrite.
            }
            catch (NotSupportedException)
            {
                // Fall through to move with overwrite.
            }
        }

        File.Move(tempPath, targetPath, overwrite: true);
    }

    private static void TryDeleteTemp(string tempPath, ILogger logger)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(cleanupEx, "Failed to clean up temporary file {TempPath}", tempPath);
        }
    }
}
