// <copyright file="IntegrationTestBase.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.Infrastructure
{
    public abstract class IntegrationTestBase : IDisposable
    {
        protected string TestRootPath { get; }
    `n
        protected IntegrationTestBase()
        {
            this.TestRootPath = Path.Combine(Path.GetTempPath(), "ai-tracker-int-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.TestRootPath);
        }
    `n
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
    `n
        public virtual void Dispose()
        {
            try
            {
                if (Directory.Exists(this.TestRootPath))
                {
                    Directory.Delete(this.TestRootPath, true);
                }
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }
}
