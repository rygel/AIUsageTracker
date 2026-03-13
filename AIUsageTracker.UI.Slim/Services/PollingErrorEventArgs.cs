// <copyright file="PollingErrorEventArgs.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim.Services;

/// <summary>
/// Event arguments for polling errors.
/// </summary>
public class PollingErrorEventArgs : EventArgs
{
    public PollingErrorEventArgs(Exception exception, string message)
    {
        this.Exception = exception;
        this.Message = message;
    }

    public Exception Exception { get; }

    public string Message { get; }
}
