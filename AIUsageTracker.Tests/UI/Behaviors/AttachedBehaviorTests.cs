// <copyright file="AttachedBehaviorTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows;
using System.Windows.Controls;
using AIUsageTracker.UI.Slim.Behaviors;

namespace AIUsageTracker.Tests.UI.Behaviors;

/// <summary>
/// Tests for attached behaviors.
/// </summary>
public class AttachedBehaviorTests
{
    private static readonly TimeSpan StaTestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public Task WindowDragBehavior_GetEnableDrag_ReturnsFalseByDefaultAsync()
    {
        return RunInStaAsync(() =>
        {
            // Arrange
            var element = new Border();

            // Act
            var result = WindowDragBehavior.GetEnableDrag(element);

            // Assert
            Assert.False(result);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public Task WindowDragBehavior_SetEnableDrag_SetsValueAsync()
    {
        return RunInStaAsync(() =>
        {
            // Arrange
            var element = new Border();

            // Act
            WindowDragBehavior.SetEnableDrag(element, true);
            var result = WindowDragBehavior.GetEnableDrag(element);

            // Assert
            Assert.True(result);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public Task CloseWindowBehavior_GetCloseOnClick_ReturnsFalseByDefaultAsync()
    {
        return RunInStaAsync(() =>
        {
            // Arrange
            var button = new Button();

            // Act
            var result = CloseWindowBehavior.GetCloseOnClick(button);

            // Assert
            Assert.False(result);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public Task CloseWindowBehavior_SetCloseOnClick_SetsValueAsync()
    {
        return RunInStaAsync(() =>
        {
            // Arrange
            var button = new Button();

            // Act
            CloseWindowBehavior.SetCloseOnClick(button, true);
            var result = CloseWindowBehavior.GetCloseOnClick(button);

            // Assert
            Assert.True(result);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public Task CloseWindowBehavior_GetHideOnClick_ReturnsFalseByDefaultAsync()
    {
        return RunInStaAsync(() =>
        {
            // Arrange
            var button = new Button();

            // Act
            var result = CloseWindowBehavior.GetHideOnClick(button);

            // Assert
            Assert.False(result);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public Task CloseWindowBehavior_SetHideOnClick_SetsValueAsync()
    {
        return RunInStaAsync(() =>
        {
            // Arrange
            var button = new Button();

            // Act
            CloseWindowBehavior.SetHideOnClick(button, true);
            var result = CloseWindowBehavior.GetHideOnClick(button);

            // Assert
            Assert.True(result);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public Task OpenFolderBehavior_GetFolderPath_ReturnsNullByDefaultAsync()
    {
        return RunInStaAsync(() =>
        {
            // Arrange
            var element = new Border();

            // Act
            var result = OpenFolderBehavior.GetFolderPath(element);

            // Assert
            Assert.Null(result);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public Task OpenFolderBehavior_SetFolderPath_SetsValueAsync()
    {
        return RunInStaAsync(() =>
        {
            // Arrange
            var element = new Border();
            var path = @"C:\TestPath";

            // Act
            OpenFolderBehavior.SetFolderPath(element, path);
            var result = OpenFolderBehavior.GetFolderPath(element);

            // Assert
            Assert.Equal(path, result);
            return Task.CompletedTask;
        });
    }

    private static Task RunInStaAsync(Func<Task> testBody)
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                testBody().WaitAsync(StaTestTimeout).GetAwaiter().GetResult();
                tcs.SetResult(null);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return tcs.Task;
    }
}
