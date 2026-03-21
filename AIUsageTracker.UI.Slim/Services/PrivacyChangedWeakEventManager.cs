// <copyright file="PrivacyChangedWeakEventManager.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim.Services;

/// <summary>
/// Maintains weak subscriptions for <see cref="App.PrivacyChanged"/> handlers.
/// </summary>
public static class PrivacyChangedWeakEventManager
{
    private static readonly object Sync = new();
    private static readonly List<WeakReference<EventHandler<PrivacyChangedEventArgs>>> Handlers = [];
    private static bool _isListening;

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

        lock (Sync)
        {
            PruneDeadHandlersNoLock();
            Handlers.Add(new WeakReference<EventHandler<PrivacyChangedEventArgs>>(handler));
            EnsureListeningNoLock();
        }
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

        lock (Sync)
        {
            for (var index = Handlers.Count - 1; index >= 0; index--)
            {
                if (!Handlers[index].TryGetTarget(out var existingHandler) || existingHandler == handler)
                {
                    Handlers.RemoveAt(index);
                }
            }

            StopListeningIfNoHandlersNoLock();
        }
    }

    private static void OnPrivacyChanged(object? sender, PrivacyChangedEventArgs e)
    {
        var liveHandlers = new List<EventHandler<PrivacyChangedEventArgs>>();
        lock (Sync)
        {
            for (var index = Handlers.Count - 1; index >= 0; index--)
            {
                if (Handlers[index].TryGetTarget(out var handler))
                {
                    liveHandlers.Add(handler);
                }
                else
                {
                    Handlers.RemoveAt(index);
                }
            }

            StopListeningIfNoHandlersNoLock();
        }

        liveHandlers.Reverse();

        foreach (var handler in liveHandlers)
        {
            handler(sender, e);
        }
    }

    private static void EnsureListeningNoLock()
    {
        if (_isListening)
        {
            return;
        }

        App.PrivacyChanged += OnPrivacyChanged;
        _isListening = true;
    }

    private static void StopListeningIfNoHandlersNoLock()
    {
        if (!_isListening || Handlers.Count != 0)
        {
            return;
        }

        App.PrivacyChanged -= OnPrivacyChanged;
        _isListening = false;
    }

    private static void PruneDeadHandlersNoLock()
    {
        for (var index = Handlers.Count - 1; index >= 0; index--)
        {
            if (!Handlers[index].TryGetTarget(out _))
            {
                Handlers.RemoveAt(index);
            }
        }
    }
}
