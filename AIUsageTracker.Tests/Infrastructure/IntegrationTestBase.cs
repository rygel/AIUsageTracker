// <copyright file="IntegrationTestBase.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.Infrastructure;

public abstract class IntegrationTestBase : IDisposable
{
    protected string TestRootPath { get; }

    protected IntegrationTestBase()
    {
        this.TestRootPath = TestTempPaths.CreateDirectory("ai-tracker-int-tests");
    }

    protected string CreateFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(this.TestRootPath, relativePath);
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
        TestTempPaths.CleanupPath(this.TestRootPath);
    }
}
