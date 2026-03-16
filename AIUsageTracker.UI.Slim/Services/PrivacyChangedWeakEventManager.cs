// <copyright file="PrivacyChangedWeakEventManager.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows;

namespace AIUsageTracker.UI.Slim.Services;

/// <summary>
/// WeakEventManager for the App.PrivacyChanged event to prevent memory leaks.
/// </summary>
/// <remarks>
/// Using WeakEventManager ensures that event listeners don't prevent garbage collection
/// of the subscribing objects when they're no longer needed.
/// </remarks>
public class PrivacyChangedWeakEventManager : WeakEventManager
{
    private PrivacyChangedWeakEventManager()
    {
    }

    /// <summary>
    /// Adds a handler for the PrivacyChanged event.
    /// </summary>
    /// <param name="handler">The handler to add.</param>
    public static void AddHandler(EventHandler<PrivacyChangedEventArgs> handler)
    {
        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        CurrentManager.ProtectedAddHandler(typeof(App), handler);
    }

    /// <summary>
    /// Removes a handler for the PrivacyChanged event.
    /// </summary>
    /// <param name="handler">The handler to remove.</param>
    public static void RemoveHandler(EventHandler<PrivacyChangedEventArgs> handler)
    {
        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        CurrentManager.ProtectedRemoveHandler(typeof(App), handler);
    }

    private static PrivacyChangedWeakEventManager CurrentManager
    {
        get
        {
            var managerType = typeof(PrivacyChangedWeakEventManager);
            var manager = (PrivacyChangedWeakEventManager?)GetCurrentManager(managerType);

            if (manager == null)
            {
                manager = new PrivacyChangedWeakEventManager();
                SetCurrentManager(managerType, manager);
            }

            return manager;
        }
    }

    /// <inheritdoc />
    protected override void StartListening(object source)
    {
        App.PrivacyChanged += this.OnPrivacyChanged;
    }

    /// <inheritdoc />
    protected override void StopListening(object source)
    {
        App.PrivacyChanged -= this.OnPrivacyChanged;
    }

    private void OnPrivacyChanged(object? sender, PrivacyChangedEventArgs e)
    {
        this.DeliverEvent(typeof(App), e);
    }
}
