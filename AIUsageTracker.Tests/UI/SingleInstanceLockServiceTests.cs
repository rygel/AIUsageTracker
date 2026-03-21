// <copyright file="SingleInstanceLockServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.UI.Slim.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageTracker.Tests.UI;

public class SingleInstanceLockServiceTests
{
    [Fact]
    public void TryAcquire_WhenLockAlreadyHeldByAnotherInstance_ReturnsFalse()
    {
        var mutexName = @"Local\AIUsageTracker_Test_" + Guid.NewGuid().ToString("N");
        var first = new SingleInstanceLockService(
            mutexName,
            NullLogger<SingleInstanceLockService>.Instance);
        var second = new SingleInstanceLockService(
            mutexName,
            NullLogger<SingleInstanceLockService>.Instance);

        try
        {
            Assert.True(first.TryAcquire());
            var secondAcquireResult = RunOnBackgroundThread(() => second.TryAcquire());
            Assert.False(secondAcquireResult);
        }
        finally
        {
            second.Release();
            first.Release();
        }
    }

    [Fact]
    public void Release_WhenCalled_AllowsAnotherInstanceToAcquireLock()
    {
        var mutexName = @"Local\AIUsageTracker_Test_" + Guid.NewGuid().ToString("N");
        var first = new SingleInstanceLockService(
            mutexName,
            NullLogger<SingleInstanceLockService>.Instance);
        var second = new SingleInstanceLockService(
            mutexName,
            NullLogger<SingleInstanceLockService>.Instance);

        try
        {
            Assert.True(first.TryAcquire());
            var blockedAcquireResult = RunOnBackgroundThread(() => second.TryAcquire());
            Assert.False(blockedAcquireResult);

            first.Release();

            var secondAcquireResult = RunOnBackgroundThread(() => second.TryAcquire());
            Assert.True(secondAcquireResult);
        }
        finally
        {
            second.Release();
            first.Release();
        }
    }

    private static bool RunOnBackgroundThread(Func<bool> action)
    {
        var completed = new ManualResetEventSlim(false);
        Exception? exception = null;
        var result = false;

        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                completed.Set();
            }
        });

        thread.IsBackground = true;
        thread.Start();
        completed.Wait();

        if (exception != null)
        {
            throw new InvalidOperationException("Background thread execution failed.", exception);
        }

        return result;
    }
}
