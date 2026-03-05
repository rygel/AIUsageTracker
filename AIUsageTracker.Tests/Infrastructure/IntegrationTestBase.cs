namespace AIUsageTracker.Tests.Infrastructure;

public abstract class IntegrationTestBase : IDisposable
{
    protected string TestRootPath { get; }

    protected IntegrationTestBase()
    {
        TestRootPath = Path.Combine(Path.GetTempPath(), "ai-tracker-int-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TestRootPath);
    }

    protected string CreateFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(TestRootPath, relativePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (directory != null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    public virtual void Dispose()
    {
        try
        {
            if (Directory.Exists(TestRootPath))
            {
                Directory.Delete(TestRootPath, true);
            }
        }
        catch
        {
            // Ignore cleanup errors in tests
        }
    }
}
