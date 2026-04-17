// <copyright file="TestTempPaths.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.Infrastructure;

public static class TestTempPaths
{
    private static readonly TimeSpan StaleAge = TimeSpan.FromDays(1);
    private static readonly string ManagedRoot = Path.Combine(Path.GetTempPath(), "AIUsageTracker", "Tests");
    private static readonly string[] LegacyDirectoryPrefixes =
    [
        "AIUsageTracker_Test_",
        "ai-tracker-int-tests",
        "aiusagetracker-webtests",
        "codex-auth-service-tests",
        "codex-test-",
        "gemini-test-",
        "monitor-process-service-tests",
        "monitor-program-tests",
        "monitor-service-tests",
        "monitor-startup-tests",
        "provider-auth-tests",
        "provider-refresh-test-",
        "ProviderDiscoveryTests_",
        "token-discovery-",
        "update-channel-e2e-tests",
        "WebDatabaseServiceTests_",
    ];

    private static readonly string[] LegacyFilePrefixes =
    [
        "ai-migration-tests-",
        "ai-tracker-test-",
        "ai-usage-tracker-tests-",
    ];

    private static int _initialized;

    public static string CreateDirectory(string category)
    {
        EnsureInitialized();
        var directory = Path.Combine(ManagedRoot, Sanitize(category), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static string CreateFilePath(string category, string fileName)
    {
        var directory = CreateDirectory(category);
        return Path.Combine(directory, fileName);
    }

    public static void CleanupPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (Directory.Exists(path))
        {
            DeleteDirectoryWithRetry(path);
            return;
        }

        if (!File.Exists(path))
        {
            return;
        }

        var fullPath = Path.GetFullPath(path);
        if (fullPath.StartsWith(ManagedRoot, StringComparison.OrdinalIgnoreCase))
        {
            var parent = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
            {
                DeleteDirectoryWithRetry(parent);
                return;
            }
        }

        DeleteFileWithRetry(fullPath);
    }

    private static void EnsureInitialized()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
        {
            return;
        }

        Directory.CreateDirectory(ManagedRoot);
        CleanupStaleArtifacts();
    }

    private static void CleanupStaleArtifacts()
    {
        TryCleanupManagedRoot();
        TryCleanupLegacyArtifacts();
    }

    private static void TryCleanupManagedRoot()
    {
        foreach (var categoryDirectory in Directory.EnumerateDirectories(ManagedRoot))
        {
            foreach (var testDirectory in Directory.EnumerateDirectories(categoryDirectory))
            {
                if (IsStale(testDirectory))
                {
                    DeleteDirectoryWithRetry(testDirectory);
                }
            }
        }
    }

    private static void TryCleanupLegacyArtifacts()
    {
        var tempRoot = Path.GetTempPath();

        foreach (var directory in Directory.EnumerateDirectories(tempRoot))
        {
            var name = Path.GetFileName(directory);
            if (LegacyDirectoryPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) &&
                IsStale(directory))
            {
                DeleteDirectoryWithRetry(directory);
            }
        }

        foreach (var file in Directory.EnumerateFiles(tempRoot))
        {
            var name = Path.GetFileName(file);
            if (LegacyFilePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) &&
                IsStale(file))
            {
                DeleteFileWithRetry(file);
            }
        }
    }

    private static bool IsStale(string path)
    {
        var lastWriteUtc = File.GetLastWriteTimeUtc(path);
        return lastWriteUtc != DateTime.MinValue && (DateTime.UtcNow - lastWriteUtc) >= StaleAge;
    }

    private static void DeleteDirectoryWithRetry(string path)
    {
        const int maxAttempts = 5;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    return;
                }

                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(100 * attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < maxAttempts)
            {
                Thread.Sleep(100 * attempt);
            }
            catch (IOException ex)
            {
                Console.WriteLine($"[TEST CLEANUP] Failed to delete directory '{path}': {ex.Message}");
                return;
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"[TEST CLEANUP] Failed to delete directory '{path}': {ex.Message}");
                return;
            }
        }
    }

    private static void DeleteFileWithRetry(string path)
    {
        const int maxAttempts = 5;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return;
                }

                File.Delete(path);
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(100 * attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < maxAttempts)
            {
                Thread.Sleep(100 * attempt);
            }
            catch (IOException ex)
            {
                Console.WriteLine($"[TEST CLEANUP] Failed to delete file '{path}': {ex.Message}");
                return;
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"[TEST CLEANUP] Failed to delete file '{path}': {ex.Message}");
                return;
            }
        }
    }

    private static string Sanitize(string category)
    {
        return string.Concat(category.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-'));
    }
}
