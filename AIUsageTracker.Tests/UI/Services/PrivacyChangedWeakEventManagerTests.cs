// <copyright file="PrivacyChangedWeakEventManagerTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

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
}
