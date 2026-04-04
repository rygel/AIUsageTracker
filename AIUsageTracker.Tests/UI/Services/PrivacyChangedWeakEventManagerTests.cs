// <copyright file="PrivacyChangedWeakEventManagerTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Reflection;
using AIUsageTracker.UI.Slim;
using AIUsageTracker.UI.Slim.Services;

namespace AIUsageTracker.Tests.UI.Services;

/// <summary>
/// Tests for the PrivacyChangedWeakEventManager.
/// </summary>
public class PrivacyChangedWeakEventManagerTests
{
    [Fact]
    public void AddHandler_ThrowsArgumentNullException_WhenHandlerIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            PrivacyChangedWeakEventManager.AddHandler(null!));
    }

    [Fact]
    public void RemoveHandler_ThrowsArgumentNullException_WhenHandlerIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            PrivacyChangedWeakEventManager.RemoveHandler(null!));
    }

    [Fact]
    public void AddHandler_DoesNotThrow_WithValidHandler()
    {
        // Arrange
        EventHandler<PrivacyChangedEventArgs> handler = (s, e) => { };

        // Act & Assert - should not throw
        var exception = Record.Exception(() =>
            PrivacyChangedWeakEventManager.AddHandler(handler));

        Assert.Null(exception);

        // Cleanup
        PrivacyChangedWeakEventManager.RemoveHandler(handler);
    }

    [Fact]
    public void RemoveHandler_DoesNotThrow_WhenRemovingUnaddedHandler()
    {
        // Arrange
        EventHandler<PrivacyChangedEventArgs> handler = (s, e) => { };

        // Act & Assert - should not throw even if handler was never added
        var exception = Record.Exception(() =>
            PrivacyChangedWeakEventManager.RemoveHandler(handler));

        Assert.Null(exception);
    }

    [Fact]
    public void AddHandler_WhenPrivacyModeChanges_InvokesTypedHandlerWithoutException()
    {
        var originalPrivacyMode = App.IsPrivacyMode;
        var callCount = 0;
        EventHandler<PrivacyChangedEventArgs> handler = (_, _) => callCount++;

        PrivacyChangedWeakEventManager.AddHandler(handler);
        try
        {
            var exception = Record.Exception(() => App.SetPrivacyMode(!originalPrivacyMode));

            Assert.Null(exception);
            Assert.Equal(1, callCount);
        }
        finally
        {
            PrivacyChangedWeakEventManager.RemoveHandler(handler);
            App.SetPrivacyMode(originalPrivacyMode);
        }
    }

    /// <summary>
    /// Structural regression: every class that registers with PrivacyChangedWeakEventManager
    /// MUST store the delegate in a field to prevent GC from collecting the weak reference.
    /// This test scans the UI assembly for any class that has an OnPrivacyChanged method
    /// and verifies it also declares a _privacyChangedHandler field.
    /// </summary>
    [Fact]
    public void AllPrivacySubscribers_MustStoreHandlerInField_ToPreventGcCollection()
    {
        var uiAssembly = typeof(MainWindow).Assembly;
        var violations = new List<string>();

        foreach (var type in uiAssembly.GetTypes())
        {
            var hasOnPrivacyChanged = type.GetMethod(
                "OnPrivacyChanged",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) != null;

            if (!hasOnPrivacyChanged)
            {
                continue;
            }

            var hasHandlerField = type.GetField(
                "_privacyChangedHandler",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) != null;

            if (!hasHandlerField)
            {
                violations.Add(type.FullName ?? type.Name);
            }
        }

        Assert.True(
            violations.Count == 0,
            $"These classes have OnPrivacyChanged but no _privacyChangedHandler field " +
            $"(delegates passed to PrivacyChangedWeakEventManager.AddHandler will be GC'd): " +
            string.Join(", ", violations));
    }

    [Fact]
    public void AddHandler_WithStrongReference_InvokesHandlerAfterGcCycle()
    {
        // Regression: adding a temporary method-group delegate (no strong external reference)
        // means the GC can collect it, silently breaking the handler. Callers must keep a
        // strong reference (e.g. store in a field). This test verifies that a delegate held
        // via a strong reference survives a GC cycle and is still invoked.
        var originalPrivacyMode = App.IsPrivacyMode;
        var callCount = 0;

        // Simulate storing the delegate in a field — the pattern that prevents the bug.
        var handler = new EventHandler<PrivacyChangedEventArgs>((_, _) => callCount++);
        PrivacyChangedWeakEventManager.AddHandler(handler);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        try
        {
            App.SetPrivacyMode(!originalPrivacyMode);
            Assert.Equal(1, callCount);
        }
        finally
        {
            PrivacyChangedWeakEventManager.RemoveHandler(handler);
            App.SetPrivacyMode(originalPrivacyMode);
        }
    }
}
